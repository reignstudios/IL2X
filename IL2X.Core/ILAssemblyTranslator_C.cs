using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using IL2X.Core.EvaluationStack;

namespace IL2X.Core
{
	public sealed class ILAssemblyTranslator_C : ILAssemblyTranslator
	{
		public enum GC_Type
		{
			/// <summary>
			/// Modern platforms. Thread safe.
			/// </summary>
			Boehm,

			/// <summary>
			/// Legacy or unsupported Boehm platforms. Not thread safe.
			/// </summary>
			Portable,

			/// <summary>
			/// Super low memory or embedded devices. Not thread safe.
			/// </summary>
			Micro
		}

		public struct Options
		{
			public GC_Type gc;
			public string gcFolderPath;
		}

		public readonly Options options;

		private StreamWriter writer;
		private ILAssembly activeAssembly, activeCoreAssembly;// current assembly being translated
		private ILModule activeModule;// current module being translated
		private MethodDebugInformation activeMethodDebugInfo;// current method debug symbols
		private Dictionary<string, string> activeStringLiterals;// current module string literals
		private Dictionary<string, string> allStringLitterals;// multi-lib string literal values (active and dependancy lib string literals)

		#region Core dependency resolution
		public ILAssemblyTranslator_C(string binaryPath, bool loadReferences, in Options options, params string[] searchPaths)
		: base(binaryPath, loadReferences, searchPaths)
		{
			this.options = options;
			if (options.gcFolderPath == null) this.options.gcFolderPath = string.Empty;
		}

		public override void Translate(string outputPath)
		{
			allStringLitterals = new Dictionary<string, string>();
			TranslateAssembly(assembly, outputPath, new List<ILAssembly>());
		}

		private void TranslateAssembly(ILAssembly assembly, string outputPath, List<ILAssembly> translatedAssemblies)
		{
			activeAssembly = assembly;

			// validate assembly wasn't already translated
			if (translatedAssemblies.Exists(x => x.assemblyDefinition.FullName == assembly.assemblyDefinition.FullName)) return;
			translatedAssemblies.Add(assembly);

			// translate all modules into C assmebly files
			foreach (var module in assembly.modules)
			{
				// translate reference assemblies
				foreach (var reference in module.references) TranslateAssembly(reference, outputPath, translatedAssemblies);

				// create output folder
				if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

				// translate module
				TranslateModule(module, outputPath);
			}
		}

		private void TranslateModule(ILModule module, string outputPath)
		{
			activeModule = module;
			activeCoreAssembly = module.GetCoreLib();

			if (activeStringLiterals == null) activeStringLiterals = new Dictionary<string, string>();
			else activeStringLiterals.Clear();

			// get module filename
			string modulePath = Path.Combine(outputPath, module.moduleDefinition.Name.Replace('.', '_'));
			bool isExe = module.moduleDefinition.IsMain && (module.moduleDefinition.Kind == ModuleKind.Console || module.moduleDefinition.Kind == ModuleKind.Windows);
			if (isExe) modulePath += ".c";
			else modulePath += ".h";

			// get generated members filename
			string generatedMembersFilename = $"{module.moduleDefinition.Name.Replace('.', '_')}_GeneratedMembers.h";
			string generatedMembersInitMethod = "IL2X_Init_GeneratedMembers_" + module.moduleDefinition.Name.Replace('.', '_');
			string initModuleMethod = "IL2X_InitModule_" + module.moduleDefinition.Name.Replace('.', '_');

			// write module
			using (var stream = new FileStream(modulePath, FileMode.Create, FileAccess.Write, FileShare.Read))
			using (writer = new StreamWriter(stream))
			{
				// write generate info
				writer.WriteLine("// ###############################");
				writer.WriteLine($"// Generated with IL2X v{Utils.GetAssemblyInfoVersion()}");
				writer.WriteLine("// ###############################");
				if (!isExe) writer.WriteLine("#pragma once");

				// write includes of core lib
				if (module.assembly.isCoreLib)
				{
					// write include of gc to be used
					string gcFileName;
					switch (options.gc)
					{
						case GC_Type.Boehm: gcFileName = "IL2X.GC.Boehm"; break;
						case GC_Type.Portable: gcFileName = "IL2X.GC.Portable"; break;
						case GC_Type.Micro: gcFileName = "IL2X.GC.Micro"; break;
						default: throw new Exception("Unsupported GC option: " + options.gc);
					}
					
					gcFileName = Path.Combine(Path.GetFullPath(options.gcFolderPath), gcFileName);
					writer.WriteLine($"#include \"{gcFileName}.h\"");

					// include std libraries
					writer.WriteLine("#include <stdio.h>");
				}

				// write includes of dependencies
				foreach (var assemblyReference in module.references)
				foreach (var moduleReference in assemblyReference.modules)
				{
					writer.WriteLine($"#include \"{moduleReference.moduleDefinition.Name.Replace('.', '_')}.h\"");
				}
				writer.WriteLine();

				// write forward declare of types
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Type forward declares");
				writer.WriteLine("// ===============================");
				foreach (var type in module.typesDependencyOrdered) WriteTypeDefinition(type, false);
				writer.WriteLine();

				// write type definitions
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Type definitions");
				writer.WriteLine("// ===============================");
				foreach (var type in module.typesDependencyOrdered) WriteTypeDefinition(type, true);

				// write forward declare of type methods
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Method forward declares");
				writer.WriteLine("// ===============================");
				foreach (var type in module.moduleDefinition.Types)
				{
					foreach (var method in type.Methods) WriteMethodDefinition(method, false);
				}
				writer.WriteLine();

				// write include of IL instruction generated members
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Instruction generated members");
				writer.WriteLine("// ===============================");
				writer.WriteLine($"#include \"{generatedMembersFilename}\"");
				writer.WriteLine();

				// write method definitions
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Method definitions");
				writer.WriteLine("// ===============================");
				foreach (var type in module.moduleDefinition.Types)
				{
					foreach (var method in type.Methods) WriteMethodDefinition(method, true);
				}

				// write init module
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Init module");
				writer.WriteLine("// ===============================");
				writer.WriteLine($"void {initModuleMethod}()");
				writer.WriteLine('{');
				StreamWriterEx.AddTab();
				foreach (var reference in module.references)
				{
					foreach (var refModule in reference.modules)
					{
						writer.WriteLinePrefix($"IL2X_InitModule_{refModule.moduleDefinition.Name.Replace('.', '_')}();");
					}
				}
				writer.WriteLinePrefix($"{generatedMembersInitMethod}();");
				StreamWriterEx.RemoveTab();
				writer.WriteLine('}');
				writer.WriteLine();

				// write entry point
				if (isExe && module.moduleDefinition.EntryPoint != null)
				{
					writer.WriteLine("// ===============================");
					writer.WriteLine("// Entry Point");
					writer.WriteLine("// ===============================");
					writer.WriteLine("void main()");
					writer.WriteLine('{');
					StreamWriterEx.AddTab();
					writer.WriteLinePrefix("IL2X_GC_Init();");
					writer.WriteLinePrefix($"{initModuleMethod}();");
					writer.WriteLinePrefix($"{GetMethodDefinitionFullName(module.moduleDefinition.EntryPoint)}();");
					writer.WriteLinePrefix("IL2X_GC_Collect();");
					StreamWriterEx.RemoveTab();
					writer.WriteLine('}');
				}
			}

			// write IL instruction generated file
			using (var stream = new FileStream(Path.Combine(outputPath, generatedMembersFilename), FileMode.Create, FileAccess.Write, FileShare.Read))
			using (writer = new StreamWriter(stream))
			{
				// write generate info
				writer.WriteLine("// ###############################");
				writer.WriteLine($"// Generated with IL2X v{Utils.GetAssemblyInfoVersion()}");
				writer.WriteLine("// ###############################");
				writer.WriteLine("#pragma once");
				writer.WriteLine();

				var stringType = activeCoreAssembly.assemblyDefinition.MainModule.GetType("System.String");
				if (activeStringLiterals.Count != 0)
				{
					if (stringType == null) throw new Exception("Failed to get 'System.String' from CoreLib");
					string stringTypeName = GetTypeDefinitionFullName(stringType);

					writer.WriteLine("// ===============================");
					writer.WriteLine("// String literals");
					writer.WriteLine("// ===============================");
					foreach (var literal in activeStringLiterals)
					{
						string value = literal.Value;
						if (value.Contains('\n')) value = value.Replace("\n", "");
						if (value.Contains('\r')) value = value.Replace("\r", "");
						if (value.Length > 64) value = value.Substring(0, 64) + "...";
						writer.WriteLine($"// {value}");
						int stringMemSize = sizeof(int) + sizeof(char) + (literal.Value.Length * sizeof(char));// TODO: handle non-standard int & char sizes
						writer.Write($"char {literal.Key}[{stringMemSize}] = {{");
						foreach(byte b in BitConverter.GetBytes(literal.Value.Length))
						{
							writer.Write(b);
							writer.Write(',');
						}
						foreach(char c in literal.Value)
						{
							foreach (byte b in BitConverter.GetBytes(c))
							{
								writer.Write(b);
								writer.Write(',');
							}
						}
						writer.Write("0,0");// null-terminated char
						writer.WriteLine("};");
					}

					writer.WriteLine();
				}

				// write init module
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Init method");
				writer.WriteLine("// ===============================");
				writer.WriteLine($"void {generatedMembersInitMethod}()");
				writer.WriteLine('{');
				StreamWriterEx.AddTab();
				// TODO
				StreamWriterEx.RemoveTab();
				writer.WriteLine('}');
			}

			writer = null;
		}
		#endregion

		#region Type writers
		private void WriteTypeDefinition(TypeDefinition type, bool writeBody)
		{
			if (type.IsEnum) return;// enums are converted to numarics
			if (type.HasGenericParameters) return;// generics are generated per use
			if (type.IsPrimitive) return;// primitives

			if (!writeBody)
			{
				if (IsEmptyType(type))
				{
					if (type.IsValueType) writer.WriteLine(string.Format("typedef void* {0};", GetTypeDefinitionFullName(type)));// empty value types only function as pointers
					else writer.WriteLine(string.Format("typedef void {0};", GetTypeDefinitionFullName(type)));// empty reference types only function as 'void' as ptrs will be added
				}
				else writer.WriteLine(string.Format("typedef struct {0} {0};", GetTypeDefinitionFullName(type)));
			}
			else
			{
				if (IsEmptyType(type)) return;// empty types aren't allowed in C

				writer.WriteLine(string.Format("struct {0}", GetTypeDefinitionFullName(type)));
				writer.WriteLine('{');
				StreamWriterEx.AddTab();

				// get all types that should write fields
				var fieldTypeList = new List<TypeDefinition>();
				fieldTypeList.Add(type);
				var baseType = type.BaseType;
				while (baseType != null)
				{
					var baseTypeDef = GetTypeDefinition(baseType);
					fieldTypeList.Add(baseTypeDef);
					baseType = baseTypeDef.BaseType;
				}

				// write all fields starting from last base type
				for (int i = fieldTypeList.Count - 1; i != -1; --i)
				{
					foreach (var field in fieldTypeList[i].Fields) WriteFieldDefinition(field);
				}

				StreamWriterEx.RemoveTab();
				writer.WriteLine("};");
				writer.WriteLine();
			}
		}

		private void WriteFieldDefinition(FieldDefinition field)
		{
			if (field.FieldType.IsGenericInstance) throw new Exception("TODO: generate generic type");
			writer.WriteLinePrefix($"{GetTypeReferenceFullName(field.FieldType)} {GetFieldDefinitionName(field)};");
		}

		private void WriteMethodDefinition(MethodDefinition method, bool writeBody)
		{
			if (method.HasGenericParameters || method.DeclaringType.HasGenericParameters) return;// generics are generated per use
			if (method.HasCustomAttributes && method.CustomAttributes.Any(x => x.AttributeType.FullName == "IL2X.NativeExternCAttribute")) return;// skip native C methods

			if (method.IsConstructor)// force constructors to return a ref of their self
			{
				if (method.DeclaringType.IsPrimitive) throw new Exception("Constructors aren't supported on primitives");
				if (method.DeclaringType.IsValueType) writer.Write($"{GetTypeReferenceFullName(method.DeclaringType)}* {GetMethodDefinitionFullName(method)}(");
				else writer.Write($"{GetTypeReferenceFullName(method.DeclaringType)} {GetMethodDefinitionFullName(method)}(");
			}
			else
			{
				writer.Write($"{GetTypeReferenceFullName(method.ReturnType)} {GetMethodDefinitionFullName(method)}(");
			}

			if (!method.IsStatic)
			{
				writer.Write(GetTypeReferenceFullName(method.DeclaringType));
				if (method.DeclaringType.IsValueType) writer.Write('*');
				writer.Write(" self");
				if (method.HasParameters)
				{
					writer.Write(", ");
					WriteParameterDefinitions(method.Parameters);
				}
			}
			else if (method.HasParameters)
			{
				WriteParameterDefinitions(method.Parameters);
			}
			else
			{
				writer.Write("void");
			}
			writer.Write(')');
			
			if (!writeBody)
			{
				writer.WriteLine(';');
			}
			else
			{
				writer.WriteLine();
				writer.WriteLine('{');
				StreamWriterEx.AddTab();
				if (method.Body != null)
				{
					activeMethodDebugInfo = null;
					if (activeModule.symbolReader != null) activeMethodDebugInfo = activeModule.symbolReader.Read(method);
					WriteMethodBody(method.Body);
					activeMethodDebugInfo = null;
				}
				else if (method.IsAbstract)
				{
					// TODO: interface methods etc
				}
				else if (method.IsRuntime)
				{
					throw new NotImplementedException("TODO: handle delegates etc");
				}
				else if (method.ImplAttributes == MethodImplAttributes.InternalCall)
				{
					if (method.DeclaringType.FullName == "System.String")
					{
						if (method.Name == "FastAllocateString")
						{
							string lengthName = GetParameterDefinitionName(method.Parameters[0]);
							writer.WriteLinePrefix($"{GetTypeDefinitionFullName(method.DeclaringType)}* result = IL2X_Malloc(sizeof(int) + sizeof(wchar_t) + (sizeof(wchar_t) * {lengthName}));");
							writer.WriteLinePrefix($"result->{GetFieldDefinitionName(method.DeclaringType.Fields[0])} = {lengthName};");
							writer.WriteLinePrefix("return result;");
						}
						else if (method.Name == "get_Length")
						{
							writer.WriteLinePrefix($"return self->{GetFieldDefinitionName(method.DeclaringType.Fields[0])};");
						}
						else
						{
							throw new NotImplementedException("Unsupported internal runtime String method: " + method.Name);
						}
					}
					else if (method.DeclaringType.FullName == "System.Array")
					{
						if (method.Name == "get_Length")
						{
							writer.WriteLinePrefix($"return (int)(*(size_t*)self);");
						}
						else if (method.Name == "get_LongLength")
						{
							writer.WriteLinePrefix($"return (long)(*(size_t*)self);");
						}
						else
						{
							throw new NotImplementedException("Unsupported internal runtime Array method: " + method.Name);
						}
					}
					else
					{
						throw new NotImplementedException("Unsupported internal runtime method: " + method.Name);
					}
				}
				else
				{
					throw new NotImplementedException("Unsupported empty method type: " + method.Name);
				}
				StreamWriterEx.RemoveTab();
				writer.WriteLine('}');
				writer.WriteLine();
			}
		}

		private void WriteParameterDefinitions(IEnumerable<ParameterDefinition> parameters)
		{
			var last = parameters.LastOrDefault();
			foreach (var parameter in parameters)
			{
				if (parameter.ParameterType.IsGenericInstance) throw new Exception("TODO: generate generic type");
				WriteParameterDefinition(parameter);
				if (parameter != last) writer.Write(", ");
			}
		}

		private void WriteParameterDefinition(ParameterDefinition parameter)
		{
			writer.Write($"{GetTypeReferenceFullName(parameter.ParameterType)} {GetParameterDefinitionName(parameter)}");
		}
		#endregion

		#region Method Body / IL Instructions
		private void WriteMethodBody(MethodBody body)
		{
			// write local stack variables
			var variables = new List<LocalVariable>();
			if (body.HasVariables)
			{
				foreach (var variable in body.Variables)
				{
					variables.Add(WriteVariableDefinition(variable));
				}
			}

			// write instructions
			var stack = new Stack<IStack>();

			void Ldarg_X(int index)
			{
				if (index == 0 && body.Method.HasThis)
				{
					stack.Push(new Stack_ParameterVariable("self"));
				}
				else
				{
					if (body.Method.HasThis) --index;
					stack.Push(new Stack_ParameterVariable(GetParameterDefinitionName(body.Method.Parameters[index])));
				}
			}

			void Stloc_X(int index)
			{
				var item = stack.Pop();
				var variableLeft = variables[index];
				writer.WriteLinePrefix($"{variableLeft.name} = {item.GetValueName()};");
			}

			void Ldelem_X(Instruction instruction)
			{
				var index = stack.Pop();
				var array = (Stack_LocalVariable)stack.Pop();
				var arrayType = (ArrayType)array.variable.definition.VariableType;
				stack.Push(new Stack_ArrayElement($"(({GetTypeReferenceFullName(arrayType.ElementType)}*)((char*){array.GetValueName()} + sizeof(size_t)))[{index.GetValueName()}]"));
			}

			void Stelem_X(Instruction instruction)
			{
				var value = stack.Pop();
				var index = stack.Pop();
				var array = (Stack_LocalVariable)stack.Pop();
				var arrayType = (ArrayType)array.variable.definition.VariableType;
				writer.WriteLinePrefix($"(({GetTypeReferenceFullName(arrayType.ElementType)}*)((char*){array.GetValueName()} + sizeof(size_t)))[{index.GetValueName()}] = {value.GetValueName()};");
			}

			foreach (var instruction in body.Instructions)
			{
				// check if this instruction can be jumped to
				if (body.Instructions.Any(x => x.OpCode.Code == Code.Br_S && ((Instruction)x.Operand).Offset == instruction.Offset))
				{
					writer.WriteLinePrefix($"IL_{instruction.Offset.ToString("x4")}:");// write goto jump label short form
				}
				else if (body.Instructions.Any(x => x.OpCode.Code == Code.Br && ((Instruction)x.Operand).Offset == instruction.Offset))
				{
					writer.WriteLinePrefix($"IL_{instruction.Offset.ToString("x8")}:");// write goto jump label long form
				}

				// handle next instruction
				switch (instruction.OpCode.Code)
				{
					// evaluation operations
					case Code.Nop: break;

					// push to stack
					case Code.Ldnull: stack.Push(new Stack_Null("0")); break;

					case Code.Ldc_I4_0: stack.Push(new Stack_Int32(0)); break;
					case Code.Ldc_I4_1: stack.Push(new Stack_Int32(1)); break;
					case Code.Ldc_I4_2: stack.Push(new Stack_Int32(2)); break;
					case Code.Ldc_I4_3: stack.Push(new Stack_Int32(3)); break;
					case Code.Ldc_I4_4: stack.Push(new Stack_Int32(4)); break;
					case Code.Ldc_I4_5: stack.Push(new Stack_Int32(5)); break;
					case Code.Ldc_I4_6: stack.Push(new Stack_Int32(6)); break;
					case Code.Ldc_I4_7: stack.Push(new Stack_Int32(7)); break;
					case Code.Ldc_I4_8: stack.Push(new Stack_Int32(8)); break;
					case Code.Ldc_I4: stack.Push(new Stack_Int32((int)instruction.Operand)); break;
					case Code.Ldc_I4_S: stack.Push(new Stack_SByte((sbyte)instruction.Operand)); break;

					case Code.Ldarg_0: Ldarg_X(0); break;
					case Code.Ldarg_1: Ldarg_X(1); break;
					case Code.Ldarg_2: Ldarg_X(2); break;
					case Code.Ldarg_3: Ldarg_X(3); break;
					case Code.Ldarg:
					{
						Ldarg_X((int)instruction.Operand);
						break;
					}
					case Code.Ldarg_S:
					{
						Ldarg_X((short)instruction.Operand);
						break;
					}

					//case Code.Ldelem_I: Ldelem_X(instruction); break;// as void* (native int)
					case Code.Ldelem_I1: Ldelem_X(instruction); break;
					case Code.Ldelem_I2: Ldelem_X(instruction); break;
					case Code.Ldelem_I4: Ldelem_X(instruction); break;
					case Code.Ldelem_I8: Ldelem_X(instruction); break;
					case Code.Ldelem_U1: Ldelem_X(instruction); break;
					case Code.Ldelem_U2: Ldelem_X(instruction); break;
					case Code.Ldelem_U4: Ldelem_X(instruction); break;
					case Code.Ldelem_R4: Ldelem_X(instruction); break;
					case Code.Ldelem_R8: Ldelem_X(instruction); break;
					//case Code.Ldelem_Ref: Ldelem_X(instruction); break;// as System.Object

					case Code.Ldind_I4:
					{
						var item = stack.Pop();
						stack.Push(new Stack_Cast("(int)*" + item.GetValueName()));
						break;
					}

					case Code.Ldloc_0: stack.Push(new Stack_LocalVariable(variables[0], false)); break;
					case Code.Ldloc_1: stack.Push(new Stack_LocalVariable(variables[1], false)); break;
					case Code.Ldloc_2: stack.Push(new Stack_LocalVariable(variables[2], false)); break;
					case Code.Ldloc_3: stack.Push(new Stack_LocalVariable(variables[3], false)); break;
					case Code.Ldloc_S:
					{
						var operand = (VariableDefinition)instruction.Operand;
						stack.Push(new Stack_LocalVariable(variables[operand.Index], false));
						break;
					}

					case Code.Ldloca:
					{
						var operand = (VariableDefinition)instruction.Operand;
						stack.Push(new Stack_LocalVariable(variables[operand.Index], true));
						break;
					}
					case Code.Ldloca_S:
					{
						var operand = (VariableDefinition)instruction.Operand;
						stack.Push(new Stack_LocalVariable(variables[operand.Index], true));
						break;
					}

					case Code.Ldstr:
					{
						string value = (string)instruction.Operand;
						string valueFormated = $"StringLiteral_{allStringLitterals.Count}";
						stack.Push(new Stack_String(valueFormated));
						if (!allStringLitterals.ContainsKey(valueFormated))
						{
							allStringLitterals.Add(valueFormated, value);
							activeStringLiterals.Add(valueFormated, value);
						}
						break;
					}

					case Code.Ldfld:
					{
						var self = stack.Pop();
						var field = (FieldDefinition)instruction.Operand;
						stack.Push(new Stack_FieldVariable($"{self.GetValueName()}->" + GetFieldDefinitionName(field), false));
						break;
					}

					case Code.Ldflda:
					{
						var self = stack.Pop();
						var field = (FieldDefinition)instruction.Operand;
						stack.Push(new Stack_FieldVariable($"{self.GetValueName()}->" + GetFieldDefinitionName(field), true));
						break;
					}

					case Code.Conv_U:
					{
						var item = stack.Pop();
						stack.Push(new Stack_Cast($"(void*){item.GetValueName()}"));
						break;
					}
					case Code.Conv_U1:
					{
						var item = stack.Pop();
						stack.Push(new Stack_Cast($"(char){item.GetValueName()}"));
						break;
					}
					case Code.Conv_U2:
					{
						var item = stack.Pop();
						stack.Push(new Stack_Cast($"(short){item.GetValueName()}"));
						break;
					}
					case Code.Conv_U4:
					{
						var item = stack.Pop();
						stack.Push(new Stack_Cast($"(int){item.GetValueName()}"));
						break;
					}
					case Code.Conv_U8:
					{
						var item = stack.Pop();
						stack.Push(new Stack_Cast($"(long){item.GetValueName()}"));
						break;
					}

					case Code.Call:
					{
						var method = (MethodReference)instruction.Operand;

						string methodName = null;
						var methodDef = GetMemberDefinition(method) as MethodDefinition;
						var attr = methodDef.CustomAttributes.FirstOrDefault(x => x.AttributeType.FullName == "IL2X.NativeExternCAttribute");
						if (attr != null)
						{
							methodName = attr.ConstructorArguments[0].Value as string;
							if (methodName == null) methodName = method.Name;
						}
						if (methodName == null) methodName = GetMethodReferenceFullName(method);

						var methodInvoke = new StringBuilder(methodName + '(');
						var parameters = new StringBuilder();
						var lastParameter = method.Parameters.LastOrDefault();
						foreach (var p in method.Parameters)
						{
							var item = stack.Pop();
							parameters.Append(item.GetValueName());
							if (p != lastParameter) parameters.Append(", ");
						}
						if (method.HasThis)
						{
							var self = stack.Pop();
							methodInvoke.Append(self.GetValueName());
							if (method.HasParameters) methodInvoke.Append(", ");
						}
						if (method.HasParameters) methodInvoke.Append(parameters);
						methodInvoke.Append(')');
						stack.Push(new Stack_Call(methodInvoke.ToString()));
						break;
					}

					case Code.Newobj:
					{
						var method = (MethodReference)instruction.Operand;
						var methodInvoke = new StringBuilder(GetMethodReferenceFullName(method) + '(');
						if (IsAtomicType(method.DeclaringType)) methodInvoke.Append("IL2X_GC_NewAtomic");
						else methodInvoke.Append("IL2X_GC_New");
						methodInvoke.Append($"(sizeof({GetTypeReferenceFullName(method.DeclaringType, allowSymbols:false)}))");
						if (method.HasParameters) methodInvoke.Append(", ");
						var lastParameter = method.Parameters.LastOrDefault();
						foreach (var p in method.Parameters)
						{
							var item = stack.Pop();
							methodInvoke.Append(item.GetValueName());
							if (p != lastParameter) methodInvoke.Append(", ");
						}
						methodInvoke.Append(')');
						stack.Push(new Stack_Call(methodInvoke.ToString()));
						break;
					}

					case Code.Newarr:
					{
						var type = (TypeReference)instruction.Operand;
						var size = stack.Pop();
						if (type.IsValueType) stack.Push(new Stack_Call($"IL2X_GC_NewArrayAtomic(sizeof({GetTypeReferenceFullName(type)}) * {size.GetValueName()})"));
						else stack.Push(new Stack_Call($"IL2X_GC_NewArray(sizeof({GetTypeReferenceFullName(type)}) * {size.GetValueName()})"));
						break;
					}

					// pop from stack and write operation
					case Code.Stloc_0: Stloc_X(0); break;
					case Code.Stloc_1: Stloc_X(1); break;
					case Code.Stloc_2: Stloc_X(2); break;
					case Code.Stloc_3: Stloc_X(3); break;
					case Code.Stloc:
					{
						var variable = (VariableDefinition)instruction.Operand;
						Stloc_X(variable.Index);
						break;
					}
					case Code.Stloc_S:
					{
						var variable = (VariableDefinition)instruction.Operand;
						Stloc_X(variable.Index);
						break;
					}

					case Code.Stfld:
					{
						var itemRight = stack.Pop();
						var self = stack.Pop();
						var fieldLeft = (FieldDefinition)instruction.Operand;
						writer.WriteLinePrefix($"{self.GetValueName()}->{GetFieldDefinitionName(fieldLeft)} = {itemRight.GetValueName()};");
						break;
					}

					//case Code.Stelem_I: Stelem_X(instruction); break;// as void* (native int)
					case Code.Stelem_I1: Stelem_X(instruction); break;
					case Code.Stelem_I2: Stelem_X(instruction); break;
					case Code.Stelem_I4: Stelem_X(instruction); break;
					case Code.Stelem_I8: Stelem_X(instruction); break;
					case Code.Stelem_R4: Stelem_X(instruction); break;
					case Code.Stelem_R8: Stelem_X(instruction); break;

					case Code.Initobj:
					{
						var item = (Stack_LocalVariable)stack.Pop();
						if (!item.isAddress) throw new Exception("Init obj must be address");
						var variable = item.variable;
						var type = variable.definition.VariableType;
						writer.WriteLinePrefix($"memset({item.GetValueName()}, 0, sizeof({GetTypeReferenceFullName(type, true, false)}));");
						break;
					}

					case Code.Pop:
					{
						var item = stack.Pop();
						if (item is Stack_Call) writer.WriteLinePrefix(item.GetValueName() + ';');
						else throw new NotImplementedException("Unsupported 'pop' operation type: " + item);
						break;
					}

					// flow operations
					case Code.Br:
					{	
						var operand = (Instruction)instruction.Operand;
						writer.WriteLinePrefix($"goto IL_{operand.Offset.ToString("x8")};");
						break;
					}

					case Code.Br_S:
					{	
						var operand = (Instruction)instruction.Operand;
						writer.WriteLinePrefix($"goto IL_{operand.Offset.ToString("x4")};");
						break;
					}

					case Code.Ret:
					{
						if (body.Method.ReturnType.MetadataType == MetadataType.Void)
						{
							if (stack.Count != 0)
							{
								int stackCount = stack.Count;
								for (int i = 0; i != stackCount; ++i)
								{
									var item = stack.Pop();
									if (!(item is Stack_Call)) throw new NotImplementedException("'ret' instruction has unsupported remaining instruction: " + item); 
									writer.WriteLinePrefix($"{item.GetValueName()};");
								}
							}

							if (body.Method.IsConstructor) writer.WriteLinePrefix("return self;");// force constructors to return a ref of their self
							else writer.WriteLinePrefix("return;");
						}
						else
						{
							var item = stack.Pop();
							writer.WriteLinePrefix($"return {item.GetValueName()};");
						}
						break;
					}

					default: throw new Exception("Unsuported opcode type: " + instruction.OpCode.Code);
				}
			}

			if (stack.Count != 0)
			{
				string failedInstructions = string.Empty;
				foreach (var instruction in body.Instructions) failedInstructions += instruction.ToString() + Environment.NewLine;
				throw new Exception($"Instruction translation error! Evaluation stack for method {body.Method} didn't fully unwind: {stack.Count}{Environment.NewLine}{failedInstructions}");
			}
		}

		private LocalVariable WriteVariableDefinition(VariableDefinition variable)
		{
			var local = new LocalVariable();
			local.definition = variable;
			if (!activeMethodDebugInfo.TryGetName(variable, out local.name))
			{
				local.name = "var";
			}
			
			local.name = $"l_{local.name}_{variable.Index}";
			writer.WriteLinePrefix($"{GetTypeReferenceFullName(variable.VariableType)} {local.name};");
			return local;
		}
		#endregion

		#region Core name resolution
		private string AddModulePrefix(MemberReference member, in string value)
		{
			var memberDef = (MemberReference)GetMemberDefinition(member);
			return $"{memberDef.Module.Name.Replace(".", "")}_{value}";
		}

		protected override string GetTypeDefinitionName(TypeDefinition type)
		{
			string result = base.GetTypeDefinitionName(type);
			if (type.HasGenericParameters) result += '_' + GetGenericParameters(type);
			return result;
		}

		protected override string GetTypeReferenceName(TypeReference type)
		{
			string result = base.GetTypeReferenceName(type);
			if (type.IsGenericInstance) result += '_' + GetGenericArguments((IGenericInstance)type);
			return result;
		}
		
		protected override string GetTypeDefinitionFullName(TypeDefinition type, bool allowPrefix = true)
		{
			string result = base.GetTypeDefinitionFullName(type, allowPrefix);
			if (allowPrefix)
			{
				result = AddModulePrefix(type, result);
				result = "t_" + result;
			}
			return result;
		}

		private string GetPrimitiveName(TypeReference type)
		{
			switch (type.MetadataType)
			{
				case MetadataType.Boolean: return "char";
				case MetadataType.Char: return "wchar_t";
				case MetadataType.SByte: return "signed char";
				case MetadataType.Byte: return "unsigned char";
				case MetadataType.Int16: return "short";
				case MetadataType.UInt16: return "unsigned short";
				case MetadataType.Int32: return "int";
				case MetadataType.UInt32: return "unsigned int";
				case MetadataType.Int64: return "long";
				case MetadataType.UInt64: return "unsigned long";
				case MetadataType.Single: return "float";
				case MetadataType.Double: return "double";
				default: throw new NotImplementedException("Unsupported primitive: " + type.MetadataType);
			}
		}
		
		protected override string GetTypeReferenceFullName(TypeReference type, bool allowPrefix = true, bool allowSymbols = true)
		{
			string result;

			// resolve referencing
			string refSuffix = string.Empty;
			if (allowSymbols)
			{
				while (type.IsByReference || type.IsPointer || type.IsArray || type.IsPinned)
				{
					if (type.IsPointer)
					{
						refSuffix += '*';
						var ptrType = (PointerType)type;
						type = ptrType.ElementType;
					}
					else if (type.IsByReference)
					{
						refSuffix += '*';
						var refType = (ByReferenceType)type;
						type = refType.ElementType;
					}
					else if (type.IsArray)
					{
						refSuffix += '*';
						var arrayType = (ArrayType)type;
						type = arrayType.ElementType;
					}
					else if (type.IsPinned)
					{
						var pinType = (PinnedType)type;
						type = pinType.ElementType;
					}
				}

				if (!type.IsValueType) refSuffix += '*';
			}

			// resolve type name
			if (type.MetadataType == MetadataType.Void)
			{
				result = "void";
			}
			else if (type.IsPrimitive)
			{
				result = GetPrimitiveName(type);
			}
			else
			{
				result = base.GetTypeReferenceFullName(type, allowPrefix, allowSymbols);
				if (allowPrefix)
				{
					result = AddModulePrefix(type, result);
					result = "t_" + result;
				}
				if (allowSymbols)
				{
					var def = GetTypeDefinition(type);
					if (def.IsEnum)
					{
						if (!def.HasFields) throw new Exception("Enum has no fields: " + type.Name);
						result = GetTypeReferenceFullName(def.Fields[0].FieldType);
					}
					else
					{
						if (type.HasGenericParameters) result += '_' + GetGenericParameters(type);
					}
				}
			}
			
			return result + refSuffix;
		}

		protected override string GetMethodDefinitionName(MethodDefinition method)
		{
			string result = base.GetMethodDefinitionName(method);
			if (method.HasGenericParameters) result += '_' + GetGenericParameters(method);
			return result;
		}

		protected override string GetMethodReferenceName(MethodReference method)
		{
			string result = base.GetMethodReferenceName(method);
			if (method.HasGenericParameters) result += '_' + GetGenericParameters(method);
			return result;
		}

		protected override string GetMethodDefinitionFullName(MethodDefinition method)
		{
			int count = GetBaseTypeCount(method.DeclaringType);
			int methodIndex = GetMethodOverloadIndex(method);
			string result = base.GetMethodDefinitionFullName(method);
			result = AddModulePrefix(method, result);
			return $"m_{count}_{result}_{methodIndex}";
		}

		protected override string GetMethodReferenceFullName(MethodReference method)
		{
			int count = GetBaseTypeCount(GetTypeDefinition(method.DeclaringType));
			int methodIndex = GetMethodOverloadIndex(method);
			string result = base.GetMethodReferenceFullName(method);
			result = AddModulePrefix(method, result);
			return $"m_{count}_{result}_{methodIndex}";
		}

		protected override string GetGenericParameterName(GenericParameter parameter)
		{
			return "gp_" + base.GetGenericParameterName(parameter);
		}

		protected override string GetGenericArgumentTypeName(TypeReference type)
		{
			return GetTypeReferenceFullName(type);
		}

		protected override string GetParameterDefinitionName(ParameterDefinition parameter)
		{
			return "p_" + base.GetParameterDefinitionName(parameter);
		}

		protected override string GetParameterReferenceName(ParameterReference parameter)
		{
			return "p_" + base.GetParameterReferenceName(parameter);
		}

		protected override string GetFieldDefinitionName(FieldDefinition field)
		{
			int count = GetBaseTypeCount(field.DeclaringType);
			return $"f_{count}_{base.GetFieldDefinitionName(field)}";
		}

		protected override string GetFieldReferenceName(FieldReference field)
		{
			int count = GetBaseTypeCount(GetTypeDefinition(field.DeclaringType));
			return $"f_{count}_{base.GetFieldReferenceName(field)}";
		}

		protected override string GetTypeDefinitionDelimiter()
		{
			return "_";
		}

		protected override string GetTypeReferenceDelimiter()
		{
			return "_";
		}

		protected override string GetMethodDefinitionDelimiter()
		{
			return "_";
		}

		protected override string GetMethodReferenceDelimiter()
		{
			return "_";
		}

		protected override string GetMemberDefinitionNamespaceName(in string name)
		{
			return base.GetMemberDefinitionNamespaceName(name).Replace('.', '_');
		}

		protected override string GetMemberReferenceNamespaceName(in string name)
		{
			return base.GetMemberReferenceNamespaceName(name).Replace('.', '_');
		}

		protected override char GetGenericParameterOpenBracket()
		{
			return '_';
		}

		protected override char GetGenericParameterCloseBracket()
		{
			return '_';
		}

		protected override char GetGenericParameterDelimiter()
		{
			return '_';
		}

		protected override char GetGenericArgumentOpenBracket()
		{
			return '_';
		}

		protected override char GetGenericArgumentCloseBracket()
		{
			return '_';
		}

		protected override char GetGenericArgumentDelimiter()
		{
			return '_';
		}
		#endregion
	}
}

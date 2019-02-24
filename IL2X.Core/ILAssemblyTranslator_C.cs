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
		private ILAssembly activeAssembly;// current assembly being translated
		private ILModule activeModule;// current module being translated
		private MethodDebugInformation activeMethodDebugInfo;

		#region Core dependency resolution
		public ILAssemblyTranslator_C(string binaryPath, bool loadReferences, in Options options, params string[] searchPaths)
		: base(binaryPath, loadReferences, searchPaths)
		{
			this.options = options;
			if (options.gcFolderPath == null) this.options.gcFolderPath = string.Empty;
		}

		public override void Translate(string outputPath)
		{
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

			// get module filename
			string modulePath = Path.Combine(outputPath, module.moduleDefinition.Name.Replace('.', '_'));
			bool isExe = module.moduleDefinition.IsMain && (module.moduleDefinition.Kind == ModuleKind.Console || module.moduleDefinition.Kind == ModuleKind.Windows);
			if (isExe) modulePath += ".c";
			else modulePath += ".h";

			// write module
			using (var stream = new FileStream(modulePath, FileMode.Create, FileAccess.Write, FileShare.Read))
			using (writer = new StreamWriter(stream))
			{
				// write generate info
				writer.WriteLine("// ###############################");
				writer.WriteLine($"// Generated with IL2X v{Utils.GetAssemblyInfoVersion()}");
				writer.WriteLine("// ###############################");
				if (module.assembly.isCoreLib)
				{
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
				}
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
				//foreach (var type in module.moduleDefinition.Types) WriteTypeDefinition(type, false);
				foreach (var type in module.typesDependencyOrdered) WriteTypeDefinition(type, false);
				writer.WriteLine();

				// write type definitions
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Type definitions");
				writer.WriteLine("// ===============================");
				//foreach (var type in module.moduleDefinition.Types) WriteTypeDefinition(type, true);
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

				// write method definitions
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Method definitions");
				writer.WriteLine("// ===============================");
				foreach (var type in module.moduleDefinition.Types)
				{
					foreach (var method in type.Methods) WriteMethodDefinition(method, true);
				}

				// write entry point
				if (isExe && module.moduleDefinition.EntryPoint != null)
				{
					writer.WriteLine("// ===============================");
					writer.WriteLine("// Entry Point");
					writer.WriteLine("// ===============================");
					writer.WriteLine("void main()");
					writer.WriteLine('{');
					StreamWriterEx.AddTab();
					writer.WriteLinePrefix($"{GetMethodDefinitionFullName(module.moduleDefinition.EntryPoint)}();");
					StreamWriterEx.RemoveTab();
					writer.WriteLine('}');
				}
			}

			writer = null;
		}
		#endregion

		#region Type writers
		private void WriteTypeDefinition(TypeDefinition type, bool writeBody)
		{
			if (type.IsEnum) return;// enums are converted to numarics
			if (type.HasGenericParameters) return;// generics are generated per use

			if (!writeBody)
			{
				writer.WriteLine(string.Format("typedef struct {0} {0};", GetTypeDefinitionFullName(type)));
			}
			else
			{
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

			writer.Write($"{GetTypeReferenceFullName(method.ReturnType)} {GetMethodDefinitionFullName(method)}(");
			if (!method.IsStatic)
			{
				writer.Write($"{GetTypeReferenceFullName(method.DeclaringType)} self");
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
				if (method.Body != null)
				{
					StreamWriterEx.AddTab();
					activeMethodDebugInfo = null;
					if (activeModule.symbolReader != null) activeMethodDebugInfo = activeModule.symbolReader.Read(method);
					WriteMethodBody(method.Body);
					activeMethodDebugInfo = null;
					StreamWriterEx.RemoveTab();
				}
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
					case Code.Ldnull: stack.Push(new Stack_Null()); break;

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

					case Code.Ldloc_0: stack.Push(new Stack_LocalVariable(variables[0])); break;
					case Code.Ldloc_1: stack.Push(new Stack_LocalVariable(variables[1])); break;
					case Code.Ldloc_2: stack.Push(new Stack_LocalVariable(variables[2])); break;
					case Code.Ldloc_3: stack.Push(new Stack_LocalVariable(variables[3])); break;
					case Code.Ldloca_S:
					{
						var operand = (VariableDefinition)instruction.Operand;
						stack.Push(new Stack_LocalVariable(variables[operand.Index]));
						break;
					}

					case Code.Ldfld:
					{
						stack.Pop();
						var field = (FieldDefinition)instruction.Operand;
						stack.Push(new Stack_FieldVariable("self->" + GetFieldDefinitionName(field)));
						break;
					}

					case Code.Call:
					{
						var method = (MethodReference)instruction.Operand;
						var methodInvoke = new StringBuilder(GetMethodReferenceFullName(method) + '(');
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
							stack.Pop();// pop "self" ptr
							methodInvoke.Append("self");
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

					// pop from stack and write operation
					case Code.Stloc_0: Stloc_X(0); break;
					case Code.Stloc_1: Stloc_X(1); break;
					case Code.Stloc_2: Stloc_X(2); break;
					case Code.Stloc_3: Stloc_X(3); break;

					case Code.Stfld:
					{
						var itemRight = stack.Pop();
						stack.Pop();// pop "self" ptr
						var fieldLeft = (FieldDefinition)instruction.Operand;
						writer.WriteLinePrefix($"self->{GetFieldDefinitionName(fieldLeft)} = {itemRight.GetValueName()};");
						break;
					}

					case Code.Initobj:
					{
						var item = (Stack_LocalVariable)stack.Pop();
						var variable = item.variable;
						var type = variable.definition.VariableType;
						if (type.IsValueType) writer.WriteLinePrefix($"memset(&{variable.name}, 0, sizeof({GetTypeReferenceFullName(type, true, false)}));");
						else writer.WriteLinePrefix($"memset({variable.name}, 0, sizeof(0));");
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

							writer.WriteLinePrefix("return;");
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
		
		protected override string GetTypeReferenceFullName(TypeReference type, bool allowPrefix = true, bool allowSymbols = true)
		{
			string result;
			if (type.MetadataType == MetadataType.Void)
			{
				result = "void";
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
						return GetTypeReferenceFullName(def.Fields[0].FieldType);
					}

					if (type.HasGenericParameters) result += '_' + GetGenericParameters(type);
					if (!type.IsValueType || type.IsPointer) result += '*';
					if (type.IsByReference) result += '*';
				}
			}
			
			return result;
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
			string result = base.GetMethodDefinitionFullName(method);
			result = AddModulePrefix(method, result);
			return "m_" + result;
		}

		protected override string GetMethodReferenceFullName(MethodReference method)
		{
			string result = base.GetMethodReferenceFullName(method);
			result = AddModulePrefix(method, result);
			return "m_" + result;
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

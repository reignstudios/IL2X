﻿using System;
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

		private StreamWriterEx writer;
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

		public override void Translate(string outputPath, bool translateReferences)
		{
			allStringLitterals = new Dictionary<string, string>();
			TranslateAssembly(assembly, outputPath, translateReferences, new List<ILAssembly>());
		}

		private void TranslateAssembly(ILAssembly assembly, string outputPath, bool translateReferences, List<ILAssembly> translatedAssemblies)
		{
			activeAssembly = assembly;

			// validate assembly wasn't already translated
			if (translatedAssemblies.Exists(x => x.assemblyDefinition.FullName == assembly.assemblyDefinition.FullName)) return;
			translatedAssemblies.Add(assembly);

			// translate all modules into C assmebly files
			foreach (var module in assembly.modules)
			{
				// translate reference assemblies
				if (translateReferences)
				{
					foreach (var reference in module.references) TranslateAssembly(reference, outputPath, translateReferences, translatedAssemblies);
				}

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
			using (writer = new StreamWriterEx(stream))
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
					writer.WriteLine("#include <math.h>");
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
				foreach (var type in module.typesDependencyOrdered)
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
				foreach (var type in module.typesDependencyOrdered)
				{
					foreach (var method in type.Methods) WriteMethodDefinition(method, true);
				}

				// write init module
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Init module");
				writer.WriteLine("// ===============================");
				writer.WriteLine($"void {initModuleMethod}()");
				writer.WriteLine('{');
				writer.AddTab();
				writer.WriteLinePrefix("// Init references");
				foreach (var reference in module.references)
				{
					foreach (var refModule in reference.modules)
					{
						writer.WriteLinePrefix($"IL2X_InitModule_{refModule.moduleDefinition.Name.Replace('.', '_')}();");
					}
				}
				writer.WriteLine();
				writer.WriteLinePrefix("// Init generated members");
				writer.WriteLinePrefix($"{generatedMembersInitMethod}();");
				writer.WriteLine();
				writer.WriteLinePrefix("// Init static methods");
				foreach (var type in module.typesDependencyOrdered)
				{
					foreach (var method in type.Methods)
					{
						if (!method.IsConstructor || !method.IsStatic) continue;
						writer.WriteLinePrefix($"{GetMethodDefinitionFullName(method)}();");
					}
				}
				writer.RemoveTab();
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
					writer.AddTab();
					writer.WriteLinePrefix("IL2X_GC_Init();");
					writer.WriteLinePrefix($"{initModuleMethod}();");
					writer.WriteLinePrefix($"{GetMethodDefinitionFullName(module.moduleDefinition.EntryPoint)}();");
					writer.WriteLinePrefix("IL2X_GC_Collect();");
					writer.RemoveTab();
					writer.WriteLine('}');
				}
			}

			// write IL instruction generated file
			using (var stream = new FileStream(Path.Combine(outputPath, generatedMembersFilename), FileMode.Create, FileAccess.Write, FileShare.Read))
			using (writer = new StreamWriterEx(stream))
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
						//WriteStringLiteralValue(literal.Key, literal.Value);
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
				writer.AddTab();
				// TODO
				writer.RemoveTab();
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
				else
				{
					writer.WriteLine(string.Format("typedef struct {0} {0};", GetTypeDefinitionFullName(type)));
				}
			}
			else
			{
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

				// empty types aren't allowed in C (so only write if applicable)
				if (!IsEmptyType(type))
				{
					writer.WriteLine(string.Format("struct {0}", GetTypeDefinitionFullName(type)));
					writer.WriteLine('{');
					writer.AddTab();

					// write all non-static fields starting from last base type
					for (int i = fieldTypeList.Count - 1; i != -1; --i)
					{
						foreach (var field in fieldTypeList[i].Fields)
						{
							if (!field.IsStatic) WriteFieldDefinition(field);
						}
					}

					writer.RemoveTab();
					writer.WriteLine("};");
				}

				// write all statics
				for (int i = fieldTypeList.Count - 1; i != -1; --i)
				{
					foreach (var field in fieldTypeList[i].Fields)
					{
						if (field.IsStatic && !field.HasConstant) WriteFieldDefinition(field);
					}
				}

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
				if (method.IsStatic) writer.Write($"void {GetMethodDefinitionFullName(method)}(");
				else writer.Write($"{GetTypeReferenceFullName(method.DeclaringType)} {GetMethodDefinitionFullName(method)}(");
			}
			else
			{
				writer.Write($"{GetTypeReferenceFullName(method.ReturnType)} {GetMethodDefinitionFullName(method)}(");
			}

			if (!method.IsStatic)
			{
				if (!method.IsConstructor || (method.IsConstructor && !method.DeclaringType.IsValueType))
				{
					writer.Write(GetTypeReferenceFullName(method.DeclaringType));
					if (method.DeclaringType.IsValueType) writer.Write('*');
					writer.Write(" self");
					if (method.HasParameters) writer.Write(", ");
				}
				if (method.HasParameters) WriteParameterDefinitions(method.Parameters);
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
				writer.AddTab();
				if (method.Body != null)
				{
					if (method.IsConstructor && method.DeclaringType.IsValueType) writer.WriteLinePrefix($"{GetTypeReferenceFullName(method.DeclaringType)} self;");
					activeMethodDebugInfo = null;
					if (activeModule.symbolReader != null) activeMethodDebugInfo = activeModule.symbolReader.Read(method);
					WriteMethodBody(method.Body);
					activeMethodDebugInfo = null;
					if (method.IsConstructor && !method.IsStatic) writer.WriteLinePrefix("return self;");
				}
				else if (method.IsAbstract)
				{
					// TODO: interface methods etc
				}
				else if (method.IsRuntime)
				{
					throw new NotImplementedException("TODO: handle delegates etc");
				}
				else if (method.ImplAttributes.HasFlag(MethodImplAttributes.InternalCall))
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
				writer.RemoveTab();
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
			var instructionJumpModify = new Dictionary<Instruction, BranchJumpModify>();

			void Ldarg_X(int index, bool isAddress, bool indexCanThisOffset)
			{
				if (index == 0 && body.Method.HasThis)
				{
					var selfParameter = new ParameterDefinition(body.Method.DeclaringType);
					stack.Push(new Stack_ParameterVariable(selfParameter, "self", true, isAddress, (body.Method.IsConstructor && body.Method.DeclaringType.IsValueType) ? "." : "->"));
				}
				else
				{
					if (indexCanThisOffset && body.Method.HasThis) --index;
					var p = body.Method.Parameters[index];
					stack.Push(new Stack_ParameterVariable(p, GetParameterDefinitionName(p), false, isAddress, null));
				}
			}

			void Ldarga_X(ParameterDefinition parameter)
			{
				Ldarg_X(parameter.Index, true, false);
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
				var array = (Stack_Typed)stack.Pop();
				var arrayType = (ArrayType)array.type;
				stack.Push(new Stack_ArrayElement(arrayType, $"(({GetTypeReferenceFullName(arrayType.ElementType)}*)((char*){array.GetValueName()} + sizeof(size_t)))[{index.GetValueName()}]"));
			}

			void Stelem_X(Instruction instruction)
			{
				var value = stack.Pop();
				var index = stack.Pop();
				var array = (Stack_Typed)stack.Pop();
				if (array.type is ArrayType)
				{
					var arrayType = (ArrayType)array.type;
					writer.WriteLinePrefix($"(({GetTypeReferenceFullName(arrayType.ElementType)}*)((char*){array.GetValueName()} + sizeof(size_t)))[{index.GetValueName()}] = {value.GetValueName()};");
				}
				else
				{
					writer.WriteLinePrefix($"(????)[{index.GetValueName()}] = {value.GetValueName()};");
				}
			}

			void Ldind_X(string nativeType, MetadataType primitiveType)
			{
				var item = stack.Pop();
				stack.Push(new Stack_Cast($"(*({nativeType}*){item.GetValueName()})", primitiveType));
			}

			void Conv_X(string nativeType, MetadataType primitiveType)
			{
				var item = stack.Pop();
				stack.Push(new Stack_Cast($"(({nativeType}){item.GetValueName()})", primitiveType));
			}

			void BranchUnconditional(Instruction instruction, string form)
			{
				var operand = (Instruction)instruction.Operand;
				int jmpOffset = Br_ForwardResolveStack(instruction, operand, false);
				writer.WriteLinePrefix($"goto IL_{jmpOffset.ToString(form)};");
			}

			void BranchBooleanCondition(Instruction instruction, bool trueCondition, string form)
			{
				var value = stack.Pop();
				var operand = (Instruction)instruction.Operand;
				writer.WritePrefix("if (");
				if (!trueCondition) writer.Write('!');
				writer.WriteLine($"{value.GetValueName()})");
				writer.WriteLinePrefix('{');
				writer.AddTab();
				int jmpOffset = Br_ForwardResolveStack(instruction, operand, true);
				writer.WriteLinePrefix($"goto IL_{jmpOffset.ToString(form)};");
				writer.RemoveTab();
				writer.WriteLinePrefix('}');
			}

			void BranchCompareCondition(Instruction instruction, string condition, string form, bool unsignedCmp)
			{
				void WriteValue(IStack value)
				{
					if (unsignedCmp)
					{
						void UnsignPrimitiveType(MetadataType type)
						{
							switch (type)
							{
								case MetadataType.SByte: writer.Write($"((unsigned char){value.GetValueName()})"); break;
								case MetadataType.Int16: writer.Write($"((unsigned short){value.GetValueName()})"); break;
								case MetadataType.Int32: writer.Write($"((unsigned int){value.GetValueName()})"); break;
								case MetadataType.Int64: writer.Write($"((unsigned long){value.GetValueName()})"); break;
								case MetadataType.Single:
								case MetadataType.Double:
									writer.Write(value.GetValueName());
									break;
								default: throw new NotImplementedException("Failed to unsign primitive type: " + type);
							}
						}

						if (value is Stack_SByte) writer.Write($"((unsigned char){value.GetValueName()})");
						else if (value is Stack_Int16) writer.Write($"((unsigned short){value.GetValueName()})");
						else if (value is Stack_Int32) writer.Write($"((unsigned int){value.GetValueName()})");
						else if (value is Stack_Int64) writer.Write($"((unsigned long){value.GetValueName()})");
						else if (value is Stack_Single || value is Stack_Double) writer.Write(value.GetValueName());
						else if (value is Stack_Call)
						{
							var call = (Stack_Call)value;
							if (call.method != null && call.method.ReturnType.IsPrimitive) UnsignPrimitiveType(call.method.ReturnType.MetadataType);
							else writer.Write(value.GetValueName());
						}
						else if (value is Stack_FieldVariable)
						{
							var field = (Stack_FieldVariable)value;
							if (field.field.FieldType.IsPrimitive) UnsignPrimitiveType(field.field.FieldType.MetadataType);
							else writer.Write(value.GetValueName());
						}
						else if (value is Stack_LocalVariable)
						{
							var local = (Stack_LocalVariable)value;
							UnsignPrimitiveType(local.variable.definition.VariableType.MetadataType);
						}
						else if (value is Stack_ParameterVariable)
						{
							var local = (Stack_ParameterVariable)value;
							UnsignPrimitiveType(local.definition.ParameterType.MetadataType);
						}
						else if (value is Stack_PrimitiveOperation)
						{
							var op = (Stack_PrimitiveOperation)value;
							UnsignPrimitiveType(op.primitiveType);
						}
						else if (value is Stack_ArrayElement)
						{
							var op = (Stack_ArrayElement)value;
							UnsignPrimitiveType(op.type.MetadataType);
						}
						else
						{
							throw new NotImplementedException("BranchCondition failed to unsign value: " + value.GetValueName());
						}
					}
					else
					{
						writer.Write(value.GetValueName());
					}
				}

				var value2 = stack.Pop();
				var value1 = stack.Pop();
				var operand = (Instruction)instruction.Operand;

				writer.WritePrefix("if (");
				WriteValue(value1);
				writer.Write($" {condition} ");
				WriteValue(value2);
				writer.WriteLine(')');
				writer.WriteLinePrefix('{');
				writer.AddTab();
				int jmpOffset = Br_ForwardResolveStack(instruction, operand, true);
				writer.WriteLinePrefix($"goto IL_{jmpOffset.ToString(form)};");
				writer.RemoveTab();
				writer.WriteLinePrefix('}');
			}

			MetadataType GetPrimitiveResult(IStack value)
			{
				if (value is Stack_SByte) return MetadataType.SByte;
				else if (value is Stack_Byte) return MetadataType.Byte;
				else if (value is Stack_Int16) return MetadataType.Int16;
				else if (value is Stack_UInt16) return MetadataType.UInt16;
				else if (value is Stack_Int32) return MetadataType.Int32;
				else if (value is Stack_UInt32) return MetadataType.UInt32;
				else if (value is Stack_Int64) return MetadataType.Int64;
				else if (value is Stack_UInt64) return MetadataType.UInt64;
				else if (value is Stack_Single) return MetadataType.Single;
				else if (value is Stack_Double) return MetadataType.Double;
				else if (value is Stack_Call)
				{
					var type = (Stack_Call)value;
					return type.method.ReturnType.MetadataType;
				}
				else if (value is Stack_Cast)
				{
					var type = (Stack_Cast)value;
					return type.type;
				}
				else if (value is Stack_FieldVariable)
				{
					var type = (Stack_FieldVariable)value;
					return type.field.FieldType.MetadataType;
				}
				else if (value is Stack_LocalVariable)
				{
					var type = (Stack_LocalVariable)value;
					return type.variable.definition.VariableType.MetadataType;
				}
				else if (value is Stack_ParameterVariable)
				{
					var type = (Stack_ParameterVariable)value;
					if (type.definition == null) return MetadataType.Object;
					return type.definition.ParameterType.MetadataType;
				}
				else if (value is Stack_PrimitiveOperation)
				{
					var type = (Stack_PrimitiveOperation)value;
					return type.primitiveType;
				}
				else if (value is Stack_ArrayElement)
				{
					var type = (Stack_ArrayElement)value;
					return type.type.MetadataType;
				}
				else throw new NotImplementedException("GetPrimitiveResult failed to result: " + value.GetType());
			}

			MetadataType GetPrimitiveOperationResultType(MetadataType value1, MetadataType value2)
			{
				var result = (value1 >= value2) ? value1 : value2;
				if (result == MetadataType.SByte || result == MetadataType.Byte || result == MetadataType.Int16 || result == MetadataType.UInt16)
				{
					result = MetadataType.Int32;
				}

				return result;
			}

			void PrimitiveOperator(string op)
			{
				var value2 = stack.Pop();
				var value1 = stack.Pop();
				var primitiveType1 = GetPrimitiveResult(value1);
				var primitiveType2 = GetPrimitiveResult(value2);
				stack.Push(new Stack_PrimitiveOperation($"({value1.GetValueName()} {op} {value2.GetValueName()})", GetPrimitiveOperationResultType(primitiveType1, primitiveType2)));
			}

			void ConditionalExpression(string condition)
			{
				var value2 = stack.Pop();
				var value1 = stack.Pop();
				stack.Push(new Stack_ConditionalExpression($"(({value1.GetValueName()} {condition} {value2.GetValueName()}) ? 1 : 0)"));
			}

			void BitwiseOperation(string op)
			{
				var value2 = stack.Pop();
				var value1 = stack.Pop();
				stack.Push(new Stack_BitwiseOperation($"({value1.GetValueName()} {op} {value2.GetValueName()})"));
			}

			int Br_ForwardResolveStack(Instruction brInstruction, Instruction jmpInstruction, bool keepExistingStack)
			{
				int existingStackCount = stack.Count;
				Stack<IStack> existingStack = null;
				if (keepExistingStack && stack.Count != 0) existingStack = new Stack<IStack>(stack);
				
				var origJmpInstruction = jmpInstruction;
				int jmpOffset = jmpInstruction.Offset;
				while (stack.Count != 0)
				{
					ProcessInstruction(jmpInstruction, false);
					writer.Flush();writer.BaseStream.Flush();// DEBUG
					jmpOffset = jmpInstruction.Offset + 1;// if next instruction is null make sure we jump after it
					jmpInstruction = jmpInstruction.Next;
					if (jmpInstruction != null) jmpOffset = jmpInstruction.Offset;
				}

				if (origJmpInstruction.Offset != jmpOffset) instructionJumpModify.Add(brInstruction, new BranchJumpModify(jmpOffset, existingStackCount));
				if (keepExistingStack && existingStack != null) stack = new Stack<IStack>(existingStack);
				return jmpOffset;
			}

			foreach (var instruction in body.Instructions)
			{
				ProcessInstruction(instruction, true);
				writer.Flush();writer.BaseStream.Flush();// DEBUG
			}

			void ProcessInstruction(Instruction instruction, bool writeBrJumps)
			{
				// check if this instruction can be jumped to
				if (writeBrJumps)
				{
					// validate instruction isnt jump to only handled
					foreach (var brModify in instructionJumpModify)
					{
						if (brModify.Value.stackCountBeforeJump == 0 && stack.Count == 0) continue;
						var jumpInstruction = (Instruction)brModify.Key.Operand;
						if (instruction.Offset >= jumpInstruction.Offset && instruction.Offset < brModify.Value.offset)
						{
							stack.Clear();// stack already forward processes, so remove
							return;
						}
					}

					// write jump name
					bool CanBeJumpedTo(params Code[] codes)
					{
						return body.Instructions.Any(x =>
						x.OpCode.HasAnyCodes(codes) &&// has branch code
						((((Instruction)x.Operand).Offset == instruction.Offset && !instructionJumpModify.ContainsKey(x)) ||// if instruction jumps to me and no branch overrides
						(instructionJumpModify.ContainsKey(x) && instructionJumpModify[x].offset == instruction.Offset)));// or instruction has branch override and jumps to me
					}

					if (CanBeJumpedTo
					(
						Code.Br_S, Code.Brfalse_S, Code.Brtrue_S,
						Code.Bge_Un_S,
						Code.Bge_S, Code.Bne_Un_S,
						Code.Bgt_S, Code.Bgt_Un_S,
						Code.Beq_S,
						Code.Blt_S, Code.Blt_Un_S,
						Code.Ble_S, Code.Ble_Un_S
					))
					{
						writer.WriteLinePrefix($"IL_{instruction.Offset.ToString("x4")}:;");// write goto jump label short form
					}
					else if (CanBeJumpedTo
					(
						Code.Br, Code.Brfalse, Code.Brtrue,
						Code.Bge, Code.Bge_Un,
						Code.Bgt, Code.Bgt_Un,
						Code.Bne_Un,
						Code.Beq,
						Code.Blt, Code.Blt_Un,
						Code.Ble, Code.Ble_Un
					))
					{
						writer.WriteLinePrefix($"IL_{instruction.Offset.ToString("x8")}:;");// write goto jump label long form
					}
				}

				// handle next instruction
				switch (instruction.OpCode.Code)
				{
					// evaluation operations
					case Code.Nop: break;

					// push to stack
					case Code.Ldnull: stack.Push(new Stack_Null("0")); break;

					case Code.Dup:
					{
						var item = stack.Pop();
						stack.Push(item);
						stack.Push(item);
						break;
					}

					case Code.Ldc_I4_M1: stack.Push(new Stack_Int32(-1)); break;
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
					case Code.Ldc_I4_S: stack.Push(new Stack_Int32((sbyte)instruction.Operand)); break;
					case Code.Ldc_I8: stack.Push(new Stack_Int64((long)instruction.Operand)); break;
					case Code.Ldc_R4: stack.Push(new Stack_Single((float)instruction.Operand)); break;
					case Code.Ldc_R8: stack.Push(new Stack_Double((double)instruction.Operand)); break;

					case Code.Ldarg_0: Ldarg_X(0, false, true); break;
					case Code.Ldarg_1: Ldarg_X(1, false, true); break;
					case Code.Ldarg_2: Ldarg_X(2, false, true); break;
					case Code.Ldarg_3: Ldarg_X(3, false, true); break;
					case Code.Ldarg:
					{
						if (instruction.Operand is int) Ldarg_X((int)instruction.Operand, false, true);
						else if (instruction.Operand is ParameterDefinition) Ldarg_X(((ParameterDefinition)instruction.Operand).Index, false, false);
						else throw new NotImplementedException("Ldarg unsupported operand: " + instruction.Operand.GetType());
						break;
					}
					case Code.Ldarg_S:
					{
						if (instruction.Operand is short) Ldarg_X((short)instruction.Operand, false, true);
						else if (instruction.Operand is int) Ldarg_X((int)instruction.Operand, false, true);
						else if (instruction.Operand is ParameterDefinition) Ldarg_X(((ParameterDefinition)instruction.Operand).Index, false, false);
						else throw new NotImplementedException("Ldarg_S unsupported operand: " + instruction.Operand.GetType());
						break;
					}

					case Code.Ldarga: Ldarga_X((ParameterDefinition)instruction.Operand); break;
					case Code.Ldarga_S: Ldarga_X((ParameterDefinition)instruction.Operand); break;

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
					case Code.Ldelem_Ref: Ldelem_X(instruction); break;// as System.Object

					case Code.Ldlen:
					{
						var array = stack.Pop();
						var arrayType = activeCoreAssembly.assemblyDefinition.MainModule.GetType("System.Array");
						var lengthMethod = arrayType.Methods.First(x => x.Name == "get_Length");
						stack.Push(new Stack_ArrayLength($"{GetMethodDefinitionFullName(lengthMethod)}({array.GetValueName()})"));
						break;
					}

					case Code.Ldind_I: Ldind_X("void*", MetadataType.IntPtr); break;
					case Code.Ldind_I2: Ldind_X("char", MetadataType.Byte); break;
					case Code.Ldind_I4: Ldind_X("short", MetadataType.Int16); break;
					case Code.Ldind_I8: Ldind_X("int", MetadataType.Int32); break;
					case Code.Ldind_R4: Ldind_X("float", MetadataType.Single); break;
					case Code.Ldind_R8: Ldind_X("double", MetadataType.Double); break;

					case Code.Ldloc_0: stack.Push(new Stack_LocalVariable(variables[0], false)); break;
					case Code.Ldloc_1: stack.Push(new Stack_LocalVariable(variables[1], false)); break;
					case Code.Ldloc_2: stack.Push(new Stack_LocalVariable(variables[2], false)); break;
					case Code.Ldloc_3: stack.Push(new Stack_LocalVariable(variables[3], false)); break;
					case Code.Ldloc:
					{
						var operand = (VariableDefinition)instruction.Operand;
						stack.Push(new Stack_LocalVariable(variables[operand.Index], false));
						break;
					}
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
						if (!allStringLitterals.ContainsValue(value))
						{
							allStringLitterals.Add(valueFormated, value);
							activeStringLiterals.Add(valueFormated, value);
						}
						break;
					}

					case Code.Ldsfld:
					{
						var field = (FieldDefinition)instruction.Operand;
						stack.Push(new Stack_FieldVariable(field, GetFieldDefinitionName(field), false));
						break;
					}

					case Code.Ldsflda:
					{
						var field = (FieldDefinition)instruction.Operand;
						stack.Push(new Stack_FieldVariable(field, GetFieldDefinitionName(field), true));
						break;
					}

					case Code.Ldfld:
					{
						var self = stack.Pop();
						var field = (FieldDefinition)instruction.Operand;
						stack.Push(new Stack_FieldVariable(field, self.GetValueName() + self.GetAccessToken() + GetFieldDefinitionName(field), false));
						break;
					}

					case Code.Ldflda:
					{
						var self = stack.Pop();
						var field = (FieldDefinition)instruction.Operand;
						stack.Push(new Stack_FieldVariable(field, self.GetValueName() + self.GetAccessToken() + GetFieldDefinitionName(field), true));
						break;
					}

					case Code.Conv_I: Conv_X("void*", MetadataType.IntPtr); break;
					case Code.Conv_I1: Conv_X("char", MetadataType.Byte); break;
					case Code.Conv_I2: Conv_X("short", MetadataType.Int16); break;
					case Code.Conv_I4: Conv_X("int", MetadataType.Int32); break;
					case Code.Conv_I8: Conv_X("long", MetadataType.Int64); break;

					case Code.Conv_U: Conv_X("void*", MetadataType.IntPtr); break;
					case Code.Conv_U1: Conv_X("unsigned char", MetadataType.Byte); break;
					case Code.Conv_U2: Conv_X("unsigned short", MetadataType.UInt16); break;
					case Code.Conv_U4: Conv_X("unsigned int", MetadataType.UInt32); break;
					case Code.Conv_U8: Conv_X("unsigned long", MetadataType.UInt64); break;
					case Code.Conv_R4: Conv_X("float", MetadataType.Single); break;
					case Code.Conv_R8: Conv_X("double", MetadataType.Double); break;

					case Code.Ceq: ConditionalExpression("=="); break;
					case Code.Cgt: ConditionalExpression(">"); break;
					case Code.Clt: ConditionalExpression("<"); break;

					case Code.And: BitwiseOperation("&"); break;
					case Code.Or: BitwiseOperation("|"); break;

					case Code.Shl: PrimitiveOperator("<<"); break;
					case Code.Shr: PrimitiveOperator(">>"); break;
					//case Code.Shr_Un: PrimitiveOperator(">>", true); break;

					case Code.Add: PrimitiveOperator("+"); break;
					case Code.Sub: PrimitiveOperator("-"); break;
					case Code.Mul: PrimitiveOperator("*"); break;
					case Code.Div: PrimitiveOperator("/"); break;

					case Code.Add_Ovf:
					case Code.Add_Ovf_Un:
					{
						// https://www.geeksforgeeks.org/check-for-integer-overflow/
						throw new NotImplementedException("TODO: need ability to throw Overflow exceptions");
					}

					case Code.Neg:
					{
						var value = stack.Pop();
						stack.Push(new Stack_Negate('!' + value.GetValueName()));
						break;
					}

					case Code.Call:
					case Code.Callvirt:
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
						var firstParameter = method.Parameters.FirstOrDefault();
						foreach (var p in method.Parameters)
						{
							var item = stack.Pop();
							if (p != firstParameter) parameters.Insert(0, ", ");
							parameters.Insert(0, item.GetValueName());
						}
						if (method.HasThis)
						{
							var self = stack.Pop();
							if (self.GetAccessToken() == ".") methodInvoke.Append('&');
							methodInvoke.Append(self.GetValueName());
							if (method.HasParameters) methodInvoke.Append(", ");
						}
						if (method.HasParameters) methodInvoke.Append(parameters);
						methodInvoke.Append(')');
						if (method.ReturnType.MetadataType == MetadataType.Void) writer.WriteLinePrefix(methodInvoke.Append(';').ToString());
						else stack.Push(new Stack_Call(method, methodInvoke.ToString()));
						break;
					}

					case Code.Newobj:
					{
						var method = (MethodReference)instruction.Operand;
						var methodInvoke = new StringBuilder(GetMethodReferenceFullName(method) + '(');
						if (!method.DeclaringType.IsValueType)
						{
							if (IsAtomicType(method.DeclaringType)) methodInvoke.Append("IL2X_GC_NewAtomic");
							else methodInvoke.Append("IL2X_GC_New");
							methodInvoke.Append($"(sizeof({GetTypeReferenceFullName(method.DeclaringType, allowSymbols:false)}))");
							if (method.HasParameters) methodInvoke.Append(", ");
						}
						var lastParameter = method.Parameters.LastOrDefault();
						foreach (var p in method.Parameters)
						{
							var item = stack.Pop();
							methodInvoke.Append(item.GetValueName());
							if (p != lastParameter) methodInvoke.Append(", ");
						}
						methodInvoke.Append(')');
						stack.Push(new Stack_Call(method, methodInvoke.ToString()));
						break;
					}

					case Code.Newarr:
					{
						var type = (TypeReference)instruction.Operand;
						var size = stack.Pop();
						if (type.IsValueType)
						{
							var allocMethod = new MethodDefinition("IL2X_GC_NewArrayAtomic", MethodAttributes.RTSpecialName, type);
							stack.Push(new Stack_Call(allocMethod, $"IL2X_GC_NewArrayAtomic(sizeof({GetTypeReferenceFullName(type)}) * {size.GetValueName()})"));
						}
						else
						{
							var allocMethod = new MethodDefinition("IL2X_GC_NewArray", MethodAttributes.RTSpecialName, type);
							stack.Push(new Stack_Call(allocMethod, $"IL2X_GC_NewArray(sizeof({GetTypeReferenceFullName(type)}) * {size.GetValueName()})"));
						}
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
						writer.WriteLinePrefix($"{self.GetValueName()}{self.GetAccessToken()}{GetFieldDefinitionName(fieldLeft)} = {itemRight.GetValueName()};");
						break;
					}

					case Code.Stsfld:
					{
						var item = stack.Pop();
						var field = (FieldDefinition)instruction.Operand;
						writer.WriteLinePrefix($"{GetFieldDefinitionName(field)} = {item.GetValueName()};");
						break;
					}

					case Code.Stind_R4:
					{
						var value = stack.Pop();
						var address = stack.Pop();
						writer.WriteLinePrefix($"(*{address.GetValueName()}) = {value.GetValueName()};");
						break;
					}

					//case Code.Stelem_I: Stelem_X(instruction); break;// as void* (native int)
					case Code.Stelem_I1: Stelem_X(instruction); break;
					case Code.Stelem_I2: Stelem_X(instruction); break;
					case Code.Stelem_I4: Stelem_X(instruction); break;
					case Code.Stelem_I8: Stelem_X(instruction); break;
					case Code.Stelem_R4: Stelem_X(instruction); break;
					case Code.Stelem_R8: Stelem_X(instruction); break;
					case Code.Stelem_Ref: Stelem_X(instruction); break;

					case Code.Initobj:
					{
						var item = stack.Pop();
						TypeReference type;
						if (item is Stack_LocalVariable)
						{
							var itemType = (Stack_LocalVariable)item;
							if (!itemType.isAddress) throw new Exception("Init obj must be address");
							type = itemType.variable.definition.VariableType;
						}
						else if (item is Stack_FieldVariable)
						{
							var itemType = (Stack_FieldVariable)item;
							if (!itemType.isAddress) throw new Exception("Init obj must be address");
							type = itemType.field.FieldType;
						}
						else if (item is Stack_ParameterVariable)
						{
							var itemType = (Stack_ParameterVariable)item;
							if (!itemType.isAddress) throw new Exception("Init obj must be address");
							if (itemType.definition == null) throw new Exception("Initobj parameter def is null (this shouldn't happen)");
							type = itemType.definition.ParameterType;
						}
						else
						{
							throw new NotImplementedException("Unsupported init obj type: " + item.GetType());
						}
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
					case Code.Br: BranchUnconditional(instruction, "x8"); break;
					case Code.Br_S: BranchUnconditional(instruction, "x4"); break;

					case Code.Brfalse: BranchBooleanCondition(instruction, false, "x8"); break;
					case Code.Brfalse_S: BranchBooleanCondition(instruction, false, "x4"); break;
					case Code.Brtrue: BranchBooleanCondition(instruction, true, "x8"); break;
					case Code.Brtrue_S: BranchBooleanCondition(instruction, true, "x4"); break;

					case Code.Beq: BranchCompareCondition(instruction, "==", "x8", false); break;
					case Code.Beq_S: BranchCompareCondition(instruction, "==", "x4", false); break;
					case Code.Bge: BranchCompareCondition(instruction, ">=", "x8", false); break;
					case Code.Bge_S: BranchCompareCondition(instruction, ">=", "x4", false); break;
					case Code.Bge_Un: BranchCompareCondition(instruction, ">=", "x8", true); break;
					case Code.Bge_Un_S: BranchCompareCondition(instruction, ">=", "x4", true); break;
					case Code.Bgt: BranchCompareCondition(instruction, ">", "x8", false); break;
					case Code.Bgt_S: BranchCompareCondition(instruction, ">", "x4", false); break;
					case Code.Bgt_Un: BranchCompareCondition(instruction, ">", "x8", true); break;
					case Code.Bgt_Un_S: BranchCompareCondition(instruction, ">", "x4", true); break;
					case Code.Ble: BranchCompareCondition(instruction, "<=", "x8", false); break;
					case Code.Ble_S: BranchCompareCondition(instruction, "<=", "x4", false); break;
					case Code.Ble_Un: BranchCompareCondition(instruction, "<=", "x8", true); break;
					case Code.Ble_Un_S: BranchCompareCondition(instruction, "<=", "x4", true); break;
					case Code.Blt: BranchCompareCondition(instruction, "<", "x8", false); break;
					case Code.Blt_S: BranchCompareCondition(instruction, "<", "x4", false); break;
					case Code.Blt_Un: BranchCompareCondition(instruction, "<", "x8", true); break;
					case Code.Blt_Un_S: BranchCompareCondition(instruction, "<", "x4", true); break;
					case Code.Bne_Un: BranchCompareCondition(instruction, "!=", "x8", true); break;
					case Code.Bne_Un_S: BranchCompareCondition(instruction, "!=", "x4", true); break;

					case Code.Ret:
					{
						if (body.Method.ReturnType.MetadataType != MetadataType.Void)
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
				if (allowSymbols) result = GetPrimitiveName(type);
				else result = base.GetTypeReferenceFullName(type, allowPrefix, allowSymbols);
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
			string typeFullName = string.Empty;
			if (field.IsStatic) typeFullName = GetTypeDefinitionFullName(field.DeclaringType) + '_';
			return $"f_{count}_{typeFullName}{base.GetFieldDefinitionName(field)}";
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

using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;
using IL2X.Core.Jit;

namespace IL2X.Core.Translators
{
	public sealed class Translator_C : Translator
	{
		private StreamWriter activeWriter;
		private string writerTab = string.Empty;
		private void AddTab() => writerTab = "\t" + writerTab;
		private void RemoveTab() => writerTab = writerTab.Substring(1, writerTab.Length - 1);
		private void Write(string value) => activeWriter.Write(value);
		private void WriteTab(string value) => activeWriter.Write(writerTab + value);
		private void WriteLine(string value) => activeWriter.WriteLine(value);
		private void WriteLineTab(string value) => activeWriter.WriteLine(writerTab + value);
		private void WriteLine() => activeWriter.WriteLine();

		private ModuleJit activeModule;
		private MethodDebugInformation activemethodDebugInfo;

		public Translator_C(Solution solution)
		: base(solution)
		{

		}

		protected override void TranslateModule(ModuleJit module, string outputDirectory)
		{
			activeModule = module;

			// write header file
			foreach (var type in module.allTypes)
			{
				string filename = FormatTypeFilename(type.type.FullName);
				using (var stream = new FileStream(Path.Combine(outputDirectory, filename) + ".h", FileMode.Create, FileAccess.Write, FileShare.Read))
				using (var writer = new StreamWriter(stream))
				{
					activeWriter = writer;

					// write standard header
					WriteLine("#pragma once");
					WriteHackDefines();// TODO: REMOVE: for testing only!!!
					WriteLine();

					// write type
					WriteTypeDefinition(type);
					WriteTypeMethodDefinition(type);
				}
			}

			// write code file
			foreach (var type in module.allTypes)
			{
				string filename = FormatTypeFilename(type.type.FullName);
				using (var stream = new FileStream(Path.Combine(outputDirectory, filename) + ".c", FileMode.Create, FileAccess.Write, FileShare.Read))
				using (var writer = new StreamWriter(stream))
				{
					activeWriter = writer;

					// write type field metadata
					WriteLine($"#include \"{filename}.h\"");

					// write type method metadata
					WriteTypeMethodImplementation(type);
				}
			}
		}

		private void WriteHackDefines()
		{
			WriteLine("#include <stdint.h>");
			WriteLine("#define t_System_Private_CoreLib_System_Void void");
			WriteLine("#define t_System_Private_CoreLib_System_Int32 int32_t");
			WriteLine("#define t_System_Private_CoreLib_System_Boolean int8_t");
		}

		private void WriteTypeDefinition(TypeJit type)
		{
			string typename = GetTypeFullFlatName(type.type);
			if (type.fields.Count != 0)
			{
				WriteLine($"typedef struct {typename}");
				WriteLine("{");
				WriteTypeFieldMetadata(type);
				Write($"}} {typename};");
			}
			else
			{
				WriteLine($"#define {typename} void");
			}
			WriteLine();
		}

		private void WriteTypeFieldMetadata(TypeJit type)
		{
			// TODO
		}

		private void WriteTypeMethodDefinition(TypeJit type)
		{
			foreach (var method in type.methods)
			{
				WriteMethodSignature(method);
				WriteLine(";");
			}
		}

		private void WriteMethodSignature(MethodJit method)
		{
			Write(GetTypeReferenceName(method.method.ReturnType));
			Write(" ");
			Write(GetMethodFullFlatName(method.method));
			Write("(");
			WriteMethodParameters(method);
			Write(")");
		}

		private void WriteMethodParameters(MethodJit method)
		{
			if (method.method.HasThis)
			{
				Write(GetTypeFullFlatName(method.type.type));
				Write("* self");
				if (method.asmParameters.Count != 0) Write(", ");
			}

			for (int i = 0; i != method.asmParameters.Count; ++i)
			{
				var p = method.asmParameters[i];
				Write(GetTypeReferenceName(p.parameter.ParameterType));
				Write(" ");
				Write(GetParameterName(p.parameter));
				if (i != method.asmParameters.Count - 1) Write(", ");
			}
		}

		private void WriteTypeMethodImplementation(TypeJit type)
		{
			foreach (var method in type.methods)
			{
				// load debug info if avaliable
				if (activeModule.module.symbolReader != null) activemethodDebugInfo = activeModule.module.symbolReader.Read(method.method);

				// write method
				WriteLine();
				WriteMethodSignature(method);
				WriteLine();
				WriteLine("{");
				if (method.asmOperations != null && method.asmOperations.Count != 0)
				{
					AddTab();
					WriteMethodLocals(method);
					WriteMethodInstructions(method);
					RemoveTab();
				}
				WriteLine("}");
			}
		}

		private void WriteMethodLocals(MethodJit method)
		{
			foreach (var local in method.asmLocals)
			{
				WriteTab($"{GetTypeReferenceName(local.type)} {GetLocalVariableName(local.variable)}");
				if (method.method.Body.InitLocals) Write(" = {0}");
				WriteLine(";");
			}
		}

		private void WriteMethodInstructions(MethodJit method)
		{
			// write jit-generated locals
			foreach (var local in method.asmEvalLocals)
			{
				WriteLineTab($"{GetTypeReferenceName(local.type)} {GetLocalEvalVariableName(local.index)};");
			}

			// write instructions
			foreach (var op in method.asmOperations)
			{
				var resultLocal = op.GetResultLocal();
				var resultField = op.GetResultField();
				if (resultLocal != null && !IsVoidType(resultLocal.type))
				{
					WriteTab($"{GetOperationValue(resultLocal)} = ");
					Write(GetOperationValue(op));
				}
				else if (resultField != null)
				{
					WriteTab($"{GetOperationValue(resultField)} = ");
					Write(GetOperationValue(op));
				}
				else
				{
					WriteTab(GetOperationValue(op));
				}
				WriteLine(";");
			}
		}

		private string GetOperationValue(ASMObject op)
		{
			switch (op.code)
			{
				// ===================================
				// variables
				// ===================================
				case ASMCode.Field:
				{
					var fieldOp = (ASMField)op;
					string result = string.Empty;
					ASMObject accessorOp = op;
					while (true)
					{
						var field = (ASMField)accessorOp;
						if (field.self is ASMThisPtr)
						{
							return "self->" + result;
						}
						else if (field.self is ASMField f)
						{
							result = GetFieldName(f.field) + GetTypeReferenceMemberAccessor(f.field.FieldType) + result;
							accessorOp = f;
							continue;
						}
						else if (field.self is ParameterReference p)
						{
							result = GetParameterName(p) + GetTypeReferenceMemberAccessor(p.ParameterType) + result;
						}
						else
						{
							throw new Exception("Unsupported field accesor: " + field.self.ToString());
						}

						return result + GetFieldName(fieldOp.field);
					}
				}

				case ASMCode.Local:
				{
					var local = (ASMLocal)op;
					return GetLocalVariableName(local.variable);
				}

				case ASMCode.EvalStackLocal:
				{
					var local = (ASMEvalStackLocal)op;
					return GetLocalEvalVariableName(local.index);
				}

				case ASMCode.ThisPtr:
				{
					return "self";
				}

				case ASMCode.Parameter:
				{
					var parameter = (ASMParameter)op;
					return GetParameterName(parameter.parameter);
				}

				case ASMCode.PrimitiveLiteral:
				{
					var primitive = (ASMPrimitiveLiteral)op;
					return primitive.value.ToString();
				}

				case ASMCode.StringLiteral:
				{
					var primitive = (ASMStringLiteral)op;
					return primitive.value;
				}

				case ASMCode.SizeOf:
				{
					var size = (ASMSizeOf)op;
					return $"sizeof({GetTypeReferenceName(size.type)})";
				}

				// ===================================
				// arithmatic
				// ===================================
				case ASMCode.Add:
				{
					var arithmaticOp = (ASMArithmatic)op;
					return $"{GetOperationValue(arithmaticOp.value1)} + {GetOperationValue(arithmaticOp.value2)}";
				}

				case ASMCode.Sub:
				{
					var arithmaticOp = (ASMArithmatic)op;
					return $"{GetOperationValue(arithmaticOp.value1)} - {GetOperationValue(arithmaticOp.value2)}";
				}

				case ASMCode.Mul:
				{
					var arithmaticOp = (ASMArithmatic)op;
					return $"{GetOperationValue(arithmaticOp.value1)} * {GetOperationValue(arithmaticOp.value2)}";
				}

				case ASMCode.Div:
				{
					var arithmaticOp = (ASMArithmatic)op;
					return $"{GetOperationValue(arithmaticOp.value1)} / {GetOperationValue(arithmaticOp.value2)}";
				}

				// ===================================
				// stores
				// ===================================
				case ASMCode.WriteLocal:
				{
					var writeOp = (ASMWriteLocal)op;
					return GetOperationValue(writeOp.value);
				}

				case ASMCode.WriteField:
				{
					var writeOp = (ASMWriteField)op;
					return GetOperationValue(writeOp.value);
				}

				// ===================================
				// branching
				// ===================================
				case ASMCode.ReturnVoid:
				{
					return "return";
				}

				case ASMCode.ReturnValue:
				{
					var returnOp = (ASMReturnValue)op;
					return $"return {GetOperationValue(returnOp.value)}";
				}

				case ASMCode.BranchMarker:
				{
					var branchOp = (ASMBranchMarker)op;
					return $"{GetJumpIndexName(branchOp.asmIndex)}:";
				}

				case ASMCode.Branch:
				{
					var branchOp = (ASMBranch)op;
					return $"goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
				}

				case ASMCode.BranchIfTrue:
				{
					var branchOp = (ASMBranchCondition)op;
					return $"if ({GetOperationValue(branchOp.values[0])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
				}

				case ASMCode.BranchIfFalse:
				{
					var branchOp = (ASMBranchCondition)op;
					return $"if (!{GetOperationValue(branchOp.values[0])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
				}

				case ASMCode.BranchIfEqual:
				{
					var branchOp = (ASMBranchCondition)op;
					return $"if ({GetOperationValue(branchOp.values[0])} == {GetOperationValue(branchOp.values[1])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
				}

				case ASMCode.BranchIfNotEqual:
				{
					var branchOp = (ASMBranchCondition)op;
					return $"if ({GetOperationValue(branchOp.values[0])} != {GetOperationValue(branchOp.values[1])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
				}

				case ASMCode.BranchIfGreater:
				{
					var branchOp = (ASMBranchCondition)op;
					return $"if ({GetOperationValue(branchOp.values[0])} > {GetOperationValue(branchOp.values[1])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
				}

				case ASMCode.BranchIfLess:
				{
					var branchOp = (ASMBranchCondition)op;
					return $"if ({GetOperationValue(branchOp.values[0])} < {GetOperationValue(branchOp.values[1])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
				}

				case ASMCode.BranchIfGreaterOrEqual:
				{
					var branchOp = (ASMBranchCondition)op;
					return $"if ({GetOperationValue(branchOp.values[0])} >= {GetOperationValue(branchOp.values[1])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
				}

				case ASMCode.BranchIfLessOrEqual:
				{
					var branchOp = (ASMBranchCondition)op;
					return $"if ({GetOperationValue(branchOp.values[0])} <= {GetOperationValue(branchOp.values[1])}) goto {GetJumpIndexName(branchOp.asmJumpToIndex)}";
				}

				case ASMCode.CmpEqual_1_0:
				{
					var branchOp = (ASMCmp)op;
					return $"({GetOperationValue(branchOp.value1)} == {GetOperationValue(branchOp.value2)}) ? 1 : 0";
				}

				case ASMCode.CmpNotEqual_1_0:
				{
					var branchOp = (ASMCmp)op;
					return $"({GetOperationValue(branchOp.value1)} != {GetOperationValue(branchOp.value2)}) ? 1 : 0";
				}

				case ASMCode.CmpGreater_1_0:
				{
					var branchOp = (ASMCmp)op;
					return $"({GetOperationValue(branchOp.value1)} > {GetOperationValue(branchOp.value2)}) ? 1 : 0";
				}

				case ASMCode.CmpLess_1_0:
				{
					var branchOp = (ASMCmp)op;
					return $"({GetOperationValue(branchOp.value1)} < {GetOperationValue(branchOp.value2)}) ? 1 : 0";
				}

				// ===================================
				// invoke
				// ===================================
				case ASMCode.CallMethod:
				{
					var invokeOp = (ASMCallMethod)op;
					var result = new StringBuilder();
					result.Append($"{GetMethodFullFlatName(invokeOp.method)}(");
					int count = 0;
					foreach (var p in invokeOp.parameters)
					{
						result.Append(GetOperationValue(p));
						if (count != invokeOp.parameters.Count - 1) result.Append(", ");
						++count;
					}
					result.Append(")");
					return result.ToString();
				}

				default: throw new NotImplementedException("Operation not implimented: " + op.code.ToString());
			}
		}

		private TypeSystem GetTypeSystem()
		{
			return activeModule.module.cecilModule.TypeSystem;
		}

		private bool IsVoidType(TypeReference type)
		{
			return type == GetTypeSystem().Void;
		}

		public static string GetTypeFlatName(TypeReference type)
		{
			return type.FullName.Replace('.', '_');
		}

		public static string GetTypeFullFlatName(TypeReference type)
		{
			return $"t_{AssemblyJit.GetFlatScopeName(type.Scope)}_{GetTypeFlatName(type)}";
		}

		private string GetTypeReferenceName(TypeReference type)
		{
			string result = GetTypeFullFlatName(type);
			if (!IsVoidType(type))
			{
				if (type.IsPointer || type.IsByReference || !type.IsValueType) result += "*";
			}
			return result;
		}

		private static string GetTypeReferenceMemberAccessor(TypeReference type)
		{
			if (type.IsPointer || type.IsByReference || !type.IsValueType) return "->";
			return ".";
		}

		private string GetLocalVariableName(VariableDefinition variable)
		{
			if (activemethodDebugInfo.TryGetName(variable, out string name)) return "l_" + name;
			return "l_" + variable.Index.ToString();
		}

		private string GetLocalEvalVariableName(int index)
		{
			return "le_" + index.ToString();
		}

		public static string GetFieldName(FieldReference field)
		{
			return "f_" + field.Name;
		}

		public static string GetFieldFullName(FieldReference field)
		{
			return $"f_{AssemblyJit.GetFlatScopeName(field.DeclaringType.Scope)}_{field.FullName}";
		}

		public static string GetParameterName(ParameterReference parameter)
		{
			return "p_" + parameter.Name;
		}

		public static string GetMethodFlatName(MethodReference method)
		{
			return method.Name.Replace('.', '_');
		}

		public static string GetMethodFullFlatName(MethodReference method)
		{
			return $"{GetTypeFullFlatName(method.DeclaringType)}_{method.Name.Replace('.', '_')}";
		}

		private string GetJumpIndexName(int jumpIndex)
		{
			return "JMP_" + jumpIndex.ToString("X4");
		}
	}
}

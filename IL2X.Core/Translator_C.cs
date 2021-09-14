using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;

namespace IL2X.Core
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

		private Module activeModule;
		private MethodDebugInformation activeMethodDebugInfo;

		public Translator_C(Solution solution)
		: base(solution)
		{

		}

		protected override void TranslateModule(Module module, string outputDirectory)
		{
			activeModule = module;

			// write header file
			foreach (var type in module.cecilModule.Types)
			{
				if (IsModuleType(type)) continue;
				string filename = FormatFilename(type.FullName);
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
			foreach (var type in module.cecilModule.Types)
			{
				if (IsModuleType(type)) continue;
				string filename = FormatFilename(type.FullName);
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
			WriteLine("#define mscorlib_System_Void void");
			WriteLine("#define mscorlib_System_Int32 int32_t");
			WriteLine("#define mscorlib_System_Boolean int8_t");
		}

		private void WriteTypeDefinition(TypeDefinition type)
		{
			string typename = GetTypeName(type);
			if (type.HasFields)
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

		private void WriteTypeFieldMetadata(TypeDefinition type)
		{
			// TODO
		}

		private void WriteMethodSignature(MethodDefinition method)
		{
			Write(GetTypeReferenceName(method.ReturnType));
			Write(" ");
			Write(GetMethodName(method));
			Write("(");
			WriteMethodParameters(method);
			Write(")");
		}

		private void WriteMethodParameters(MethodDefinition method)
		{
			if (method.HasThis)
			{
				Write(GetTypeName(method.DeclaringType));
				Write("* self");
				if (method.Parameters.Count != 0) Write(", ");
			}

			for (int i = 0; i != method.Parameters.Count; ++i)
			{
				var p = method.Parameters[i];
				Write(GetTypeReferenceName(p.ParameterType));
				Write(" ");
				Write(p.Name);
				if (i != method.Parameters.Count - 1) Write(", ");
			}
		}

		private void WriteTypeMethodDefinition(TypeDefinition type)
		{
			foreach (var method in type.Methods)
			{
				WriteMethodSignature(method);
				WriteLine(";");
			}
		}

		private void WriteTypeMethodImplementation(TypeDefinition type)
		{
			foreach (var method in type.Methods)
			{
				// load debug info if avaliable
				activeMethodDebugInfo = null;
				if (activeModule.symbolReader != null) activeMethodDebugInfo = activeModule.symbolReader.Read(method);

				// write method
				WriteLine();
				WriteMethodSignature(method);
				WriteLine();
				WriteLine("{");
				AddTab();
				WriteMethodLocals(method);
				WriteMethodInstructions(method);
				RemoveTab();
				WriteLine("}");
			}
		}

		private void WriteMethodLocals(MethodDefinition method)
		{
			foreach (var variable in method.Body.Variables)
			{
				WriteTab($"{GetTypeReferenceName(variable.VariableType)} {GetLocalVariableName(variable)}");
				if (method.Body.InitLocals) Write(" = {0}");
				WriteLine(";");
			}
		}

		private void WriteMethodInstructions(MethodDefinition method)
		{
			if (method.Body.Instructions.Count == 0) return;

			// jit instruction logic
			var methodJit = Jit(method, activeModule, false);

			// write jit-generated locals
			foreach (var local in methodJit.asmEvalLocals)
			{
				if (local.type == GetTypeSystem().Void) continue;
				WriteLineTab($"{GetTypeReferenceName(local.type)} {local.name};");
			}

			// write instructions
			foreach (var op in methodJit.asmOperations)
			{
				var resultLocal = op.GetResultLocal();
				if (resultLocal != null && !IsVoidType(resultLocal.type))
				{
					WriteTab($"{GetOperationValue(resultLocal)} = ");
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
				case ASMCode.Local:
				{
					var local = (ASMLocal)op;
					return local.name;
				}

				case ASMCode.EvalStackLocal:
				{
					var local = (ASMEvalStackLocal)op;
					return local.name;
				}

				case ASMCode.ThisPtr:
				{
					return "self";
				}

				case ASMCode.Parameter:
				{
					var parameter = (ASMParameter)op;
					return parameter.name;
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
					return $"{GetOperationValue(writeOp.value)}";
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
					result.Append($"{GetMethodName(invokeOp.method)}(");
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
			return activeModule.cecilModule.TypeSystem;
		}

		private bool IsVoidType(TypeReference type)
		{
			return type == GetTypeSystem().Void;
		}

		private static string GetScopeName(IMetadataScope scope)
		{
			return scope.Name.Replace('.', '_');
		}

		private static string GetTypeName(TypeReference type)
		{
			return $"{GetScopeName(type.Scope)}_{type.FullName.Replace('.', '_')}";
		}

		private static string GetTypeReferenceName(TypeReference type)
		{
			string result = GetTypeName(type);
			if (type.IsPointer) result += "*";
			return result;
		}

		private static string GetMethodName(MethodReference method)
		{
			return $"{GetTypeName(method.DeclaringType)}_{method.Name.Replace('.', '_')}";
		}

		private string GetLocalVariableName(VariableDefinition variable)
		{
			if (activeMethodDebugInfo.TryGetName(variable, out string name)) return $"l_{name}";
			return $"l_{variable.Index}";
		}

		private string GetJumpIndexName(int jumpIndex)
		{
			return "JMP_" + jumpIndex.ToString("X4");
		}
	}
}

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
				string filename = FormatTypeFilename(type.typeReference.FullName);
				using (var stream = new FileStream(Path.Combine(outputDirectory, filename) + ".h", FileMode.Create, FileAccess.Write, FileShare.Read))
				using (var writer = new StreamWriter(stream))
				{
					activeWriter = writer;

					// write standard header
					WriteLine("#pragma once");

					// write type
					WriteTypeDefinition(type);
					WriteLine();
					WriteTypeMethodDefinition(type);
				}
			}

			// write code file
			foreach (var type in module.allTypes)
			{
				string filename = FormatTypeFilename(type.typeReference.FullName);
				using (var stream = new FileStream(Path.Combine(outputDirectory, filename) + ".c", FileMode.Create, FileAccess.Write, FileShare.Read))
				using (var writer = new StreamWriter(stream))
				{
					activeWriter = writer;

					// write type field metadata
					WriteLine($"#include \"{filename}.h\"");
					IncludeSTD(type);

					// write type method metadata
					WriteTypeMethodImplementation(type);
				}
			}
		}

		private void IncludeSTD(TypeJit type)
		{
			foreach (var method in type.methods)
			{
				if (method.asmOperations == null) continue;
				foreach (var op in method.asmOperations)
				{
					if (op.code == ASMCode.InitObject)
					{
						WriteLine("#include <stdio.h>");
						break;
					}
				}
			}
		}

		private void WriteTypeDefinition(TypeJit type)
		{
			// include native dependencies or get native type name
			string nativeTypeName = null;
			if (type.typeDefinition.HasCustomAttributes)
			{
				foreach (var a in type.typeDefinition.CustomAttributes)
				{
					if (a.AttributeType.FullName == "IL2X.NativeTypeAttribute")
					{
						var args = a.ConstructorArguments;
						var arg1 = (NativeTarget)args[0].Value;
						if (arg1 == NativeTarget.C)
						{
							nativeTypeName = (string)args[1].Value;
							var arg3 = (CustomAttributeArgument[])args[2].Value;
							foreach (var p in arg3)
							{
								var pVal = (string)p.Value;
								WriteLine($"#include <{pVal}>");
							}
						}
					}
				}
			}

			// include dependencies
			foreach (var d in type.dependencies)
			{
				char s = Path.DirectorySeparatorChar;
				if (d.Scope != type.typeReference.Scope) WriteLine($"#include \"..{s}{GetScopeName(d.Scope)}{s}{FormatTypeFilename(d.FullName)}.h\"");
				else WriteLine($"#include \"{FormatTypeFilename(d.FullName)}.h\"");
			}

			// write type
			string typename = GetTypeFullName(type.typeReference);
			if (nativeTypeName != null)
			{
				WriteLine($"#define {typename} {nativeTypeName}");
			}
			else if (type.typeDefinition.IsEnum)
			{
				var field = type.typeDefinition.Fields[0];
				WriteLine($"#define {typename} {GetTypeFullName(field.FieldType)}");
			}
			else
			{
				if (type.fields.Count != 0)
				{
					WriteLine($"typedef struct {typename} {typename};");
					WriteLine($"struct {typename}");
					WriteLine("{");
					AddTab();
					WriteTypeNonStaticFieldDefinition(type);
					RemoveTab();
					Write("};");
				}
				else
				{
					WriteLine($"#define {typename} void");
				}

				WriteLine();
				WriteLine();
				WriteTypeStaticFieldDefinition(type);
			}
			WriteLine();
		}

		private void WriteTypeNonStaticFieldDefinition(TypeJit type)
		{
			foreach (var field in type.fields)
			{
				if (field.field.IsStatic) continue;
				WriteTab(GetTypeReferenceName(field.resolvedFieldType));
				WriteLine($" {GetFieldName(field.field)};");
			}
		}

		private void WriteTypeStaticFieldDefinition(TypeJit type)
		{
			foreach (var field in type.fields)
			{
				if (!field.field.IsStatic) continue;
				WriteLineTab($"{GetTypeReferenceName(field.resolvedFieldType)} {GetFieldFullName(field.field)};");
			}
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
			Write(GetMethodName(method.method));
			Write("(");
			WriteMethodParameters(method);
			Write(")");
		}

		private void WriteMethodParameters(MethodJit method)
		{
			if (method.method.HasThis)
			{
				Write(GetTypeFullName(method.type.typeReference));
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
					string result = GetFieldName(fieldOp.field);
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

						return result;
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

				case ASMCode.InitObject:
				{
					var writeOp = (ASMInitObject)op;
					var o = GetOperationValue(writeOp.obj);
					return $"memset({o}, 0, sizeof({o}))";
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
			return activeModule.module.cecilModule.TypeSystem;
		}

		private bool IsVoidType(TypeReference type)
		{
			return type == GetTypeSystem().Void || type.FullName == "System.Void";
		}

		public static string GetTypeName(TypeReference type)
		{
			string result = type.FullName.Replace('.', '_').Replace('`', '_').Replace('<', '_').Replace('>', '_').Replace('/', '_');
			if (type.IsArray) result = result.Replace("[]", "");
			if (type.IsGenericInstance) result = "g_" + result;
			return result;
		}

		public static string GetTypeFullName(TypeReference type)
		{
			return $"t_{GetScopeName(type.Scope)}_{GetTypeName(type)}";
		}

		private string GetTypeReferenceName(TypeReference type)
		{
			string result = GetTypeFullName(type);
			if (!IsVoidType(type))
			{
				while (!type.IsValueType)
				{
					result += "*";
					var lastType = type;
					if (type.IsGenericInstance) break;
					type = type.GetElementType();
					if (type == lastType) break;
				}
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
			return "l_" + variable.GetHashCode().ToString();//variable.Index.ToString();
		}

		private string GetLocalEvalVariableName(int index)
		{
			return "le_" + index.ToString();
		}

		private static string GetFieldName(FieldReference field)
		{
			return "f_" + field.Name.Replace('<', '_').Replace('>', '_');
		}

		private static string GetFieldFullName(FieldDefinition field)
		{
			return $"f_{GetScopeName(field.DeclaringType.Scope)}_{field.FullName}".Replace('.', '_').Replace(' ', '_').Replace("::", "_");
		}

		private static string GetParameterName(ParameterReference parameter)
		{
			return "p_" + parameter.Name;
		}

		private static string GetMethodName(MethodReference method)
		{
			int overload = GetMethodOverloadIndex(method);
			return $"{GetTypeFullName(method.DeclaringType)}_{method.Name.Replace('.', '_')}_{overload}";
		}

		private string GetJumpIndexName(int jumpIndex)
		{
			return "JMP_" + jumpIndex.ToString("X4");
		}
	}
}

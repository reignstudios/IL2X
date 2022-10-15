using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;
using IL2X.Core.Jit;

namespace IL2X.Core.Emitters
{
	public sealed class Emmiter_C : Emitter
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

		public Emmiter_C(Solution solution)
		: base(solution)
		{

		}

		protected override void TranslateModule(ModuleJit module, string outputDirectory)
		{
			activeModule = module;

			// write header forward-declare all types
			{
				using (var stream = new FileStream(Path.Combine(outputDirectory, "__ForwardDeclares.h"), FileMode.Create, FileAccess.Write, FileShare.Read))
				using (var writer = new StreamWriter(stream))
				{
					activeWriter = writer;

					// write standard header
					WriteLine("#pragma once");

					// write normal type declare
					WriteLine();
					WriteLine("/* === Normal Types === */");
					foreach (var type in module.allTypes)
					{
						if (type.typeDefinition.IsEnum) continue;
						if (GetNativeTypeAttributeInfo(NativeTarget.C, type.typeDefinition, out _, out _)) continue;

						string typename = GetTypeFullName(type.typeReference);
						WriteLine($"typedef struct {typename} {typename};");
					}

					// write native type declare
					bool nativeTypesExist = false;
					var nativeDefTypesSet = new HashSet<string>();
					var nativeHeadersSet = new HashSet<string>();
					foreach (var type in module.allTypes)
					{
						if (GetNativeTypeAttributeInfo(NativeTarget.C, type.typeDefinition, out string nativeType, out var nativeHeaders))
						{
							nativeTypesExist = true;
							string typename = GetTypeFullName(type.typeReference);
							nativeDefTypesSet.Add($"#define {typename} {nativeType}");
							foreach (string header in nativeHeaders) nativeHeadersSet.Add(header);
						}
					}

					if (nativeTypesExist)
					{
						WriteLine();
						WriteLine("/* === Native Types === */");

						foreach (string header in nativeHeadersSet)
						{
							WriteLine($"#include <{header}>");
						}

						foreach (string typeDef in nativeDefTypesSet)
						{
							WriteLine(typeDef);
						}
					}

					// write enum type declare
					if (module.enumTypes.Count != 0)
					{
						WriteLine();
						WriteLine("/* === Enums === */");
						foreach (var type in module.enumTypes)
						{
							string typename = GetTypeFullName(type.typeReference);
							var field = type.typeDefinition.Fields[0];
							WriteLine($"#define {typename} {GetTypeFullName(field.FieldType)}");
						}
					}

					// write runtime-type-base if core-lib
					if (module.assembly.assembly.isCoreLib)
                    {
						WriteLine();
						WriteLine("/* === RuntimeTypeBase === */");
						WriteLine("typedef struct IL2X_RuntimeTypeBase IL2X_RuntimeTypeBase;");
						WriteLine("struct IL2X_RuntimeTypeBase");
						WriteLine("{");
						AddTab();
						WriteLineTab($"{GetTypeFullName(typeJit.typeReference)}* Type;");
						WriteLineTab("// TODO: string ptr to Name");
						WriteLineTab("// TODO: string ptr to FullName");
						RemoveTab();
						WriteLine("};");
                    }
				}
			}

			// write header type-def-field file
			foreach (var type in module.allTypes)
			{
				string filename = FormatTypeFilename(type.typeReference.FullName);
				using (var stream = new FileStream(Path.Combine(outputDirectory, filename + ".h"), FileMode.Create, FileAccess.Write, FileShare.Read))
				using (var writer = new StreamWriter(stream))
				{
					activeWriter = writer;

					// write standard header
					WriteLine("#pragma once");
					WriteLine("#include \"__ForwardDeclares.h\"");

					// write type definition
					WriteTypeDefinition(type);
				}
			}

			// write header type-method file
			foreach (var type in module.allTypes)
			{
				string filename = FormatTypeFilename(type.typeReference.FullName);
				using (var stream = new FileStream(Path.Combine(outputDirectory, filename + "_Methods.h"), FileMode.Create, FileAccess.Write, FileShare.Read))
				using (var writer = new StreamWriter(stream))
				{
					activeWriter = writer;

					// write standard header
					WriteLine("#pragma once");
					WriteLine($"#include \"{filename}.h\"");
					WriteLine();

					// write method definitions
					WriteTypeMethodDefinition(type);

					// write runtime type
					WriteLine();
					WriteRuntimeType(type);
				}
			}

			// write code file
			foreach (var type in module.allTypes)
			{
				string filename = FormatTypeFilename(type.typeReference.FullName);
				using (var stream = new FileStream(Path.Combine(outputDirectory, filename + ".c"), FileMode.Create, FileAccess.Write, FileShare.Read))
				using (var writer = new StreamWriter(stream))
				{
					activeWriter = writer;

					// write type field metadata
					WriteLine($"#include \"{filename}_Methods.h\"");
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

		private void WriteRuntimeType(TypeJit type)
        {
			string typename = GetRuntimeTypeFullName(type.typeReference);
			WriteLine($"typedef struct {typename} {typename};");
			WriteLine($"struct {typename}");
			WriteLine("{");
			AddTab();
			WriteLineTab("IL2X_RuntimeTypeBase RuntimeTypeBase;");// TODO: write special value-type that contains BaseClass, Name & Fullname
			RemoveTab();
			WriteLine("};");
		}

        private void WriteTypeDefinition(TypeJit type)
		{
			// include value-type dependencies
			foreach (var d in type.dependencies)
			{
				if (d.IsValueType || type.typeDefinition.IsEnum)
				{
					char s = Path.DirectorySeparatorChar;
					if (d.Scope != type.typeReference.Scope) WriteLine($"#include \"..{s}{GetScopeName(d.Scope)}{s}{FormatTypeFilename(d.FullName)}.h\"");
					else WriteLine($"#include \"{FormatTypeFilename(d.FullName)}.h\"");
				}
			}

			// write type
			WriteLine();
			string typename = GetTypeFullName(type.typeReference);
			if (GetNativeTypeAttributeInfo(NativeTarget.C, type.typeDefinition, out _, out _))
			{
				WriteLine("/* Defined in '__ForwardDeclared.h' */");
			}
			else if (type.typeDefinition.IsEnum)
			{
				WriteLine("/* Defined in '__ForwardDeclared.h' */");
			}
			else
			{
				if (type.fields.Count != 0 || !type.isValueType)
				{
					WriteLine($"typedef struct {typename} {typename};");
					WriteLine($"struct {typename}");
					WriteLine("{");
					AddTab();
					if (!type.isValueType) WriteLineTab("void* RuntimeType;");
					WriteTypeNonStaticFieldDefinition(type);
					RemoveTab();
					Write("};");
				}
				else
				{
					WriteLine($"#define {typename} void");
				}

				WriteLine();
				WriteTypeStaticFieldDefinition(type);
			}
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
			// include all dependencies
			foreach (var d in type.dependencies)
			{
				char s = Path.DirectorySeparatorChar;
				if (d.Scope != type.typeReference.Scope) WriteLine($"#include \"..{s}{GetScopeName(d.Scope)}{s}{FormatTypeFilename(d.FullName)}.h\"");
				else WriteLine($"#include \"{FormatTypeFilename(d.FullName)}.h\"");
			}

			// write method signatures
			WriteLine();
			foreach (var method in type.methods)
			{
				WriteMethodSignature(method);
				WriteLine(";");
			}
		}

		private void WriteMethodSignature(MethodJit method)
		{
			Write(GetTypeReferenceName(method.methodReference.ReturnType));
			Write(" ");
			Write(GetMethodName(method.methodReference));
			Write("(");
			WriteMethodParameters(method);
			Write(")");
		}

		private void WriteMethodParameters(MethodJit method)
		{
			if (method.methodReference.HasThis)
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
				if (activeModule.module.symbolReader != null) activemethodDebugInfo = activeModule.module.symbolReader.Read(method.methodDefinition);

				// write method
				WriteLine();
				WriteMethodSignature(method);
				WriteLine();
				WriteLine("{");
				AddTab();
				if (method.asmOperations != null && method.asmOperations.Count != 0)
				{
					WriteMethodLocals(method);
					WriteMethodInstructions(method);
				}
				else
                {
					// write implementation detail
					if (method.methodDefinition.IsInternalCall) WriteMethodImplementationDetail(method);

				}
				RemoveTab();
				WriteLine("}");
			}
		}

		private void WriteMethodLocals(MethodJit method)
		{
			foreach (var local in method.asmLocals)
			{
				WriteTab($"{GetTypeReferenceName(local.type)} {GetLocalVariableName(local.variable)}");
				if (method.methodDefinition.Body.InitLocals) Write(" = {0}");
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

		private void WriteMethodImplementationDetail(MethodJit method)
        {
			if (method.methodDefinition.DeclaringType.FullName == "System.Object")
			{
				if (method.methodDefinition.FullName == "System.Type System.Object::GetType()")
				{
					WriteLineTab("return ((IL2X_RuntimeTypeBase*)self->RuntimeType)->Type;");
				}
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

				case ASMCode.BitwiseAnd: 
				{
					var arithmaticOp = (ASMArithmatic)op;
					return $"{GetOperationValue(arithmaticOp.value1)} & {GetOperationValue(arithmaticOp.value2)}";	
				}
				
				case ASMCode.BitwiseOr: 
				{
					var arithmaticOp = (ASMArithmatic)op;
					return $"{GetOperationValue(arithmaticOp.value1)} | {GetOperationValue(arithmaticOp.value2)}";	
				}
				
				case ASMCode.BitwiseXor: 
				{
					var arithmaticOp = (ASMArithmatic)op;
					return $"{GetOperationValue(arithmaticOp.value1)} ^ {GetOperationValue(arithmaticOp.value2)}";	
				}
				
				case ASMCode.BitwiseNot:
				{
					var arithmaticOp = (ASMArithmatic)op;
					return $"~{GetOperationValue(arithmaticOp.value1)}";
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

		private bool IsVoidType(TypeReference type)
		{
			return type.FullName == "System.Void";// TODO: don't just validate by name
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

		public static string GetRuntimeTypeFullName(TypeReference type)
		{
			return "r" + GetTypeFullName(type);
		}

		private static string GetTypeReferenceMemberAccessor(TypeReference type)
		{
			if (type.IsPointer || type.IsByReference || !type.IsValueType) return "->";
			return ".";
		}

		private string GetLocalVariableName(VariableDefinition variable)
		{
			if (activemethodDebugInfo.TryGetName(variable, out string name)) return "l_" + name;
			//return "l_" + variable.GetHashCode().ToString();
			return "l_" + variable.Index.ToString();
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
			string name = method.Name.Replace('.', '_');
			if (method.IsGenericInstance)
			{
				var genericMethod = (GenericInstanceMethod)method;
				name += "_";
				for (int i = 0; i != genericMethod.GenericArguments.Count; ++i)
                {
					name += GetTypeName(genericMethod.GenericArguments[i]);
					if (i != genericMethod.GenericArguments.Count - 1) name += "_";
                }
				name += "_";
			}

			int overload = GetMethodOverloadIndex(method);
			return $"{GetTypeFullName(method.DeclaringType)}_{name}_{overload}";
		}

		private string GetJumpIndexName(int jumpIndex)
		{
			return "JMP_" + jumpIndex.ToString("X4");
		}
	}
}

using Mono.Cecil;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Collections.Generic;
using Mono.Cecil.Cil;
using System.Text.RegularExpressions;
using System.Text;

namespace IL2X.Core
{
	public class ILAssemblyTranslator_Cpp : ILAssemblyTranslator
	{
		private const string allTypesHeader = "__ALL_TYPES.h";

		private StreamWriter writer;
		private readonly string precompiledHeader;
		
		private TypeReference activeType;

		public ILAssemblyTranslator_Cpp(string binaryPath, bool loadReferences, string precompiledHeader = null)
		: base(binaryPath, loadReferences)
		{
			this.precompiledHeader = precompiledHeader;
		}

		public override void Translate(string outputPath)
		{
			TranslateAssembly(assembly, outputPath);
		}

		private void TranslateAssembly(ILAssembly assembly, string outputPath)
		{
			// translate reference modules
			foreach (var reference in assembly.references)
			{
				TranslateAssembly(reference, outputPath);
			}

			// create assembly folder
			outputPath = Path.Combine(outputPath, GetAssemblyFolderName(assembly.assemblyDefinition.Name.Name));
			if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

			// translate main module
			WriteAllTypesHeader(assembly.assemblyDefinition.MainModule, outputPath);
			foreach (var type in assembly.assemblyDefinition.MainModule.GetTypes()) WriteType(type, outputPath);
		}

		private string GetTypeDeclarationKeyword(TypeDefinition type)
		{
			if (type.IsEnum) return "enum class";
			else if (type.IsInterface) return "class";
			else if (type.IsClass) return type.IsValueType ? "struct" : "class";
			else throw new Exception("Unsuported type kind: " + type.Name);
		}

		private bool IsValidFileType(TypeReference type)
		{
			if (type.Name == "<Module>") return false;
			if (type.Name == "<PrivateImplementationDetails>" || (type.DeclaringType != null && type.DeclaringType.Name == "<PrivateImplementationDetails>")) return false;
			if (IsFixedBufferType(type)) return false;
			return true;
		}

		private string GetAssemblyFolderName(string assemblyName)
		{
			return assemblyName.Replace('.', '_');
		}

		private string GetTypeFilename(TypeDefinition type)
		{
			return GetFullTypeName(type, "_", "_", '[', ']', ',', true, false);
		}

		private void WriteAllTypesHeader(ModuleDefinition module, string outputPath)
		{
			using (var stream = new FileStream(Path.Combine(outputPath, allTypesHeader), FileMode.Create, FileAccess.Write))
			using (writer = new StreamWriter(stream))
			{
				writer.WriteLine("#pragma once");
				writer.WriteLine("#include \"IL2X_Array.h\"");

				// include dependencies
				if (module.AssemblyReferences.Count != 0)
				{
					writer.WriteLine();
					writer.WriteLine("// DEPENDENCIES");
					foreach (var reference in module.AssemblyReferences)
					{
						writer.WriteLine($"#include \"../{GetAssemblyFolderName(reference.Name)}/{allTypesHeader}\"");
					}
				}

				// include types
				writer.WriteLine();
				writer.WriteLine("// TYPES");
				foreach (var type in module.GetTypes())
				{
					if (!IsValidFileType(type)) continue;

					activeType = type;
					writer.WriteLine($"#include \"{GetTypeFilename(type)}.h\"");
				}
			}
		}

		private void WriteType(TypeDefinition type, string outputPath)
		{
			if (!IsValidFileType(type)) return;

			activeType = type;
			string filename = GetTypeFilename(type);
			string filePath = Path.Combine(outputPath, filename);
			
			// write header
			using (var stream = new FileStream(filePath + ".h", FileMode.Create, FileAccess.Write))
			using (writer = new StreamWriter(stream))
			{
				writer.WriteLine("#pragma once");
				var processedTypes = new List<TypeReference>();

				// include base type headers
				if (type.BaseType != null)
				{
					var baseTypeDefinition = type.BaseType.Resolve();
					writer.WriteLine($"#include \"{GetTypeFilename(baseTypeDefinition)}.h\"");
					processedTypes.Add(baseTypeDefinition);
				}

				foreach (var i in type.Interfaces)
				{
					var baseTypeDefinition = i.InterfaceType.Resolve();
					writer.WriteLine($"#include \"{GetTypeFilename(baseTypeDefinition)}.h\"");
					processedTypes.Add(baseTypeDefinition);
				}

				// predefine used types
				var usedTyped = GetAllUsedPublicTypes(type);
				if (usedTyped.Count != 0)
				{
					// write value type field headers (as full type info is required)
					foreach (var field in type.Fields)
					{
						var elementType = field.FieldType;
						if (elementType.MetadataType == MetadataType.Void) continue;
						if (IsFixedBufferType(elementType))
						{
							elementType = GetFixedBufferType((TypeDefinition)elementType, out _);
						}
						if (elementType.IsRequiredModifier)
						{
							var specialType = (RequiredModifierType)elementType;
							elementType = specialType.ElementType;
						}
						if (!elementType.IsValueType) continue;
						if (elementType.IsGenericParameter) continue;
						if (field.IsStatic) continue;

						var resolvedType = elementType.Resolve();
						if (resolvedType == null) throw new Exception("Failed to result type: " + field.FieldType);
						if (resolvedType.IsEnum) continue;// enums get forward declared
						if (processedTypes.Exists(x => x.FullName == resolvedType.FullName)) continue;// type already processed so skip
						writer.WriteLine($"#include \"{GetTypeFilename(resolvedType)}.h\"");
						processedTypes.Add(resolvedType);
					}

					writer.WriteLine();

					// write ref or non-field value types (no full type info is needed)
					foreach (var usedType in usedTyped)
					{
						if (usedType.MetadataType == MetadataType.Void) continue;
						if (usedType.IsGenericParameter) continue;
						if (usedType.FullName == type.FullName) continue;

						var resolvedType = usedType.Resolve();
						if (resolvedType == null) throw new Exception("Failed to result type: " + usedType);
						if (processedTypes.Exists(x => x.FullName == resolvedType.FullName)) continue;// type already processed so skip
						int namespaceCount = WriteNamespaceStart(resolvedType, false);
						if (usedType.IsGenericInstance)
						{
							WriteGenericTemplateParameters(resolvedType);
							writer.Write(' ');
						}
						writer.Write($"{GetTypeDeclarationKeyword(resolvedType)} {GetNestedTypeName(resolvedType)}");
						if (resolvedType.IsEnum)
						{
							if (resolvedType.HasFields && resolvedType.Fields[0].IsRuntimeSpecialName) writer.Write($" : {GetNativePrimitiveTypeName(resolvedType.Fields[0].FieldType)}");
						}
						writer.Write(';');
						WriteNamespaceEnd(namespaceCount, true);

						processedTypes.Add(resolvedType);
					}

					writer.WriteLine();
				}
				else
				{
					writer.WriteLine();
				}

				WriteTypeHeader(type);
			}

			// write source
			if (!type.IsEnum && !type.HasGenericParameters)
			{
				using (var stream = new FileStream(filePath + ".cpp", FileMode.Create, FileAccess.Write))
				using (writer = new StreamWriter(stream))
				{
					if (!string.IsNullOrEmpty(precompiledHeader)) writer.WriteLine($"#include \"{precompiledHeader}\"");
					writer.WriteLine($"#include \"{filename}.h\"");
					writer.WriteLine($"#include \"{allTypesHeader}\"");
					writer.WriteLine();
					WriteTypeSource(type);
				}
			}
		}

		private int WriteNamespaceStart(TypeDefinition type, bool returnLine)
		{
			if (!string.IsNullOrEmpty(type.Namespace))
			{
				var namespaces = type.Namespace.Split('.');
				for (int i = 0; i != namespaces.Length; ++i)
				{
					writer.Write($"namespace {namespaces[i]}");
					if (i != namespaces.Length - 1 || !returnLine) writer.Write('{');
					else writer.WriteLine(Environment.NewLine + '{');
				}
				return namespaces.Length;
			}
			else if (type.DeclaringType != null)
			{
				return WriteNamespaceStart(type.DeclaringType, returnLine);
			}

			return 0;
		}

		private void WriteNamespaceEnd(int namespaceCount, bool returnLine)
		{
			for (int i = 0; i != namespaceCount; ++i) writer.Write('}');
			if (returnLine) writer.WriteLine();
		}

		private void WriteTypeHeader(TypeDefinition type)
		{
			// write namespace
			int namespaceCount = WriteNamespaceStart(type, true);
			if (namespaceCount != 0) StreamWriterEx.AddTab();

			// write generic parameters
			if (type.HasGenericParameters)
			{
				writer.WritePrefix();
				WriteGenericTemplateParameters(type);
				writer.WriteLine();
			}

			// write type name
			string typeKindKeyword = GetTypeDeclarationKeyword(type);
			writer.WritePrefix($"{typeKindKeyword} {GetNestedTypeName(type)}");

			// write inheritance
			if (type.IsEnum)
			{
				if (type.HasFields && type.Fields[0].IsRuntimeSpecialName) writer.Write($" : {GetNativePrimitiveTypeName(type.Fields[0].FieldType)}");
			}
			else
			{
				if (type.IsSealed) writer.Write(" final");
				if (type.BaseType != null) writer.Write($" : public {GetFullTypeName(type.BaseType, true)}");
				if (type.HasInterfaces)
				{
					if (type.BaseType == null) writer.Write(" : ");
					else writer.Write(", ");
					var lastInterface = type.Interfaces.LastOrDefault();
					foreach (var i in type.Interfaces)
					{
						writer.Write($"public {GetFullTypeName(i.InterfaceType, true)}");
						if (i != lastInterface) writer.Write(", ");
					}
				}
			}

			// finish write type name
			writer.WriteLine();
			writer.WriteLinePrefix('{');
			StreamWriterEx.AddTab();

			// write members
			if (type.IsEnum)
			{
				if (type.HasFields)
				{
					var lastField = type.Fields.LastOrDefault();
					foreach (var field in type.Fields)
					{
						if (field.IsRuntimeSpecialName) continue;
						writer.WritePrefix($"{GetMemberName(field)} = {field.Constant}");
						if (field != lastField) writer.WriteLine(',');
						else writer.WriteLine();
					}
				} 
			}
			else
			{
				bool membersWritten = false;
				if (type.HasFields)
				{
					if (membersWritten) writer.WriteLine();
					membersWritten = true;
					writer.WriteLinePrefix("// FIELDS");
					foreach (var field in type.Fields) WriteFieldHeader(field);
				}
			
				/*if (type.HasProperties)// TODO: is this needed or will methods do everything?
				{
					if (membersWritten) writer.WriteLine();
					membersWritten = true;
					writer.WriteLinePrefix("// Properties");
					foreach (var method in type.Properties) WriteMethodHeader(method);
				}*/

				if (type.HasMethods)
				{
					if (membersWritten) writer.WriteLine();
					membersWritten = true;
					writer.WriteLinePrefix("// METHODS");
					foreach (var method in type.Methods) WriteMethodHeader(method);
				}
			}

			StreamWriterEx.RemoveTab();
			writer.WriteLinePrefix("};");

			// close namespace
			if (namespaceCount != 0) StreamWriterEx.RemoveTab();
			WriteNamespaceEnd(namespaceCount, true);
		}

		private void WriteTypeSource(TypeDefinition type)
		{
			// write namespace
			int namespaceCount = WriteNamespaceStart(type, true);
			if (namespaceCount != 0) StreamWriterEx.AddTab();

			// write members
			bool membersWritten = false;
			if (type.HasFields)
			{
				if (membersWritten) writer.WriteLine();
				membersWritten = true;
				writer.WriteLinePrefix("// FIELDS");
				foreach (var field in type.Fields) WriteFieldSource(field);
			}

			if (type.HasMethods)
			{
				if (membersWritten) writer.WriteLine();
				membersWritten = true;
				writer.WriteLinePrefix("// METHODS");
				var lastMethod = type.Methods.LastOrDefault();
				foreach (var method in type.Methods)
				{
					if (!method.HasBody) continue;
					WriteMethodSource(method);
					if (method != lastMethod) writer.WriteLine();
				}
			}

			// close namespace
			if (namespaceCount != 0) StreamWriterEx.RemoveTab();
			WriteNamespaceEnd(namespaceCount, true);
		}

		private void WriteFieldHeader(FieldDefinition field)
		{
			if (field.IsSpecialName) return;
			TypeReference fieldType = field.FieldType;

			// if fixed type, convert to native
			bool isFixedType = false;
			int fixedTypeSize = 0;
			if (IsFixedBufferType(field.FieldType))
			{
				isFixedType = true;
				if (!field.FieldType.IsDefinition) throw new Exception("Cant get name for reference of FixedBuffer: " + field.FieldType);
				fieldType = GetFixedBufferType((TypeDefinition)field.FieldType, out fixedTypeSize);
			}

			// check if primitive backing type
			string fieldTypeName = GetFullTypeName(fieldType);
			if (fieldType.IsPrimitive && fieldType.IsValueType && fieldType.MetadataType == field.DeclaringType.MetadataType && field.Name == "m_value")
			{
				fieldTypeName = GetNativePrimitiveTypeName(fieldType);
			}

			// write
			WriteAccessModifier(field, field.IsPublic, field.IsAssembly, false, field.IsFamily, field.IsFamilyOrAssembly, field.IsFamilyAndAssembly, field.IsPrivate);
			if (field.IsStatic) writer.Write("static ");
			writer.Write($"{fieldTypeName} {GetMemberName(field)}");
			if (isFixedType) writer.WriteLine($"[{fixedTypeSize}];");
			else writer.WriteLine(';');
		}

		private void WriteFieldSource(FieldDefinition field)
		{
			if (field.IsSpecialName || !field.IsStatic) return;
			writer.WriteLinePrefix($"{GetFullTypeName(field.FieldType)} {GetFullTypeName(field.DeclaringType)}::{GetMemberName(field)};");
		}

		private void WriteMethodHeader(MethodDefinition method)
		{
			if (method.IsInternalCall) return;// TODO: handle internal calls
			if (method.IsPInvokeImpl) return;// TODO: handle pinvoke calls

			// write access modifier
			WriteAccessModifier(method, method.IsPublic, method.IsAssembly, method.IsVirtual, method.IsFamily, method.IsFamilyOrAssembly, method.IsFamilyAndAssembly, method.IsPrivate);

			// write generic parameters
			if (method.HasGenericParameters)
			{
				WriteGenericTemplateParameters(method);
				writer.Write(' ');
			}

			// write attributes
			if (method.IsStatic) writer.Write("static ");
			if (method.IsVirtual) writer.Write("virtual ");

			// write definition
			string name = method.IsConstructor ? GetNestedTypeName(method.DeclaringType) : GetMemberName(method);
			if (method.IsConstructor)
			{
				if (method.IsStatic) writer.Write("void STATIC_");
				writer.Write($"{name}(");
			}
			else
			{
				writer.Write($"{GetFullTypeName(method.ReturnType)} {name}(");
			}

			// write parameters
			WriteParameters(method.Parameters);

			// finish
			writer.Write(')');
			if (method.IsFinal) writer.Write(" final");
			if (method.HasBody) writer.WriteLine(';');
			else if (method.IsVirtual) writer.WriteLine(" = 0;");
			else if (method.IsConstructor) writer.WriteLine(';');// auto generated contructor TODO: auto generate source that calls initializer
			else throw new Exception("Unsuported method type: " + method.FullName);
		}
		
		private void WriteMethodSource(MethodDefinition method)
		{
			if (method.IsInternalCall) return;// TODO: handle internal calls
			if (method.IsPInvokeImpl) return;// TODO: handle pinvoke calls

			if (method.IsConstructor)
			{
				if (method.IsStatic) writer.Write("void STATIC_");
				writer.WritePrefix(string.Format("{0}::{0}(", GetNestedTypeName(method.DeclaringType)));
			}
			else
			{
				writer.WritePrefix($"{GetFullTypeName(method.ReturnType)} {GetNestedTypeName(method.DeclaringType)}::{GetMemberName(method)}(");
			}
			WriteParameters(method.Parameters);
			writer.WriteLine(')');
			writer.WriteLinePrefix('{');
			StreamWriterEx.AddTab();
			WriteMethodBody(method.Body);
			StreamWriterEx.RemoveTab();
			writer.WriteLinePrefix('}');
		}

		private void WriteGenericTemplateParameters(IGenericParameterProvider generic)
		{
			writer.Write("template<");
			var lastParameter = generic.GenericParameters.LastOrDefault();
			foreach (var parameter in generic.GenericParameters)
			{
				writer.Write($"typename {GetFullTypeName(parameter)}");
				if (parameter != lastParameter) writer.Write(',');
			}
			writer.Write('>');
		}

		private void WriteAccessModifier(MemberReference member, bool isPublic, bool isAssembly, bool isVirtual, bool isFamily, bool isFamilyOrAssembly, bool isFamilyAndAssembly, bool isPrivate)
		{
			if (isPublic || isAssembly || isVirtual || isFamilyOrAssembly || isFamilyAndAssembly) writer.WritePrefix("public: ");
			else if (isFamily) writer.WritePrefix("protected: ");
			else if (isPrivate) writer.WritePrefix("private: ");
			else throw new NotImplementedException("Unsuported access modifier state: " + member.FullName);
		}

		private void WriteParameters(Collection<ParameterDefinition> parameters)
		{
			var lastParameter = parameters.LastOrDefault();
			foreach (var parameter in parameters)
			{
				writer.Write($"{GetFullTypeName(parameter.ParameterType)} {parameter.Name}");
				if (parameter != lastParameter) writer.Write(", ");
			}
		}

		private void WriteMethodBody(MethodBody body)
		{
			// TODO: parse opcodes and evaluation stack
		}

		private string GetNativePrimitiveTypeName(TypeReference type)
		{
			switch (type.MetadataType)
			{
				case MetadataType.Void: return "void";
				case MetadataType.Boolean: return "bool";
				case MetadataType.Char: return "wchar_t";
				case MetadataType.SByte: return "__int8";
				case MetadataType.Byte: return "unsigned __int8";
				case MetadataType.Int16: return "__int16";
				case MetadataType.UInt16: return "unsigned __int16";
				case MetadataType.Int32: return "__int32";
				case MetadataType.UInt32: return "unsigned __int32";
				case MetadataType.Int64: return "__int64";
				case MetadataType.UInt64: return "unsigned __int64";
				case MetadataType.Single: return "float";
				case MetadataType.Double: return "double";
			}

			return null;
		}

		protected override string GetFullTypeName(TypeReference type, bool isBaseType = false)
		{
			// check if is by ref
			bool isByRef = type.IsByReference;
			if (isByRef)
			{
				var refType = (ByReferenceType)type;
				type = refType.ElementType;
			}

			// check if required modifier
			if (type.IsRequiredModifier)
			{
				var modType = (RequiredModifierType)type;
				type = modType.ElementType;
			}

			// check if is pointer
			bool isPtr = type.IsPointer;
			int ptrCount = 0;
			if (isPtr)
			{
				while (type.IsPointer)
				{
					var ptrType = (PointerType)type;
					type = ptrType.ElementType;
					++ptrCount;
				}
			}

			// check if type is void
			string name = type.MetadataType == MetadataType.Void ? "void" : null;

			// get non-primitive flattened name
			if (name == null)
			{
				if ((activeType.Namespace == type.Namespace && type.DeclaringType == null) || (activeType == type.DeclaringType && string.IsNullOrEmpty(type.Namespace)))
				{
					name = GetNestedTypeName(type);// remove verbosity if possible
				}
				else
				{
					if (!type.IsGenericParameter) name = "::";
					else name = string.Empty;
					name += GetFullTypeName(type, "::", "_", '<', '>', ',', !type.IsDefinition, true);
				}
			}
			
			// box if array type
			if (type.IsArray)
			{
				var arrayType = (ArrayType)type;
				string elementName = GetFullTypeName(arrayType.ElementType);
				name = $"IL2X_Array<{elementName}>";
			}
			
			// finish
			if (isPtr)
			{
				for (int i = 0; i != ptrCount; ++i) name += '*';
			}
			else if (!isBaseType && !type.IsValueType && !type.IsGenericParameter)
			{
				name += '*';
			}

			if (isByRef) name += '&';
			return name;
		}

		protected string GetNestedTypeName(TypeReference type)
		{
			return GetNestedTypeName(type, "_", '<', '>', ',', !type.IsDefinition, true);
		}

		protected string GetMemberName(MemberReference member)
		{
			return GetMemberName(member, "::", "_", '<', '>', ',', !member.IsDefinition, true);
		}
	}
}

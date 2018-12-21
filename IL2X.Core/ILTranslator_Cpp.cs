using Mono.Cecil;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Collections.Generic;
using Mono.Cecil.Cil;
using System.Text.RegularExpressions;

namespace IL2X.Core
{
	public class ILTranslator_Cpp : ILTranslator
	{
		private const string allTypesHeader = "__ALL_TYPES.h";

		private StreamWriter writer;
		private Stack<string> referencesParsed;
		private readonly string precompiledHeader;
		
		private TypeReference activeType;

		public ILTranslator_Cpp(string binaryPath, string precompiledHeader = null)
		: base(binaryPath)
		{
			this.precompiledHeader = precompiledHeader;
		}

		public override void Translate(string outputPath, bool translateReferences)
		{
			referencesParsed = new Stack<string>();
			TranslateModule(assemblyDefinition, outputPath, translateReferences);
		}

		private void TranslateModule(AssemblyDefinition assemblyDefinition, string outputPath, bool translateReferences)
		{
			var module = assemblyDefinition.MainModule;

			// translate references
			if (translateReferences)
			{
				foreach (var reference in module.AssemblyReferences)
				{
					using (var refAssemblyDefinition = assemblyResolver.Resolve(reference))
					{
						if (referencesParsed.Contains(refAssemblyDefinition.FullName)) continue;
						TranslateModule(refAssemblyDefinition, outputPath, translateReferences);
						referencesParsed.Push(refAssemblyDefinition.FullName);
					}
				}
			}

			// translate assembly
			outputPath = Path.Combine(outputPath, GetAssemblyFolderName(assemblyDefinition.Name.Name));
			if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);
			WriteAllTypesHeader(module, outputPath);
			foreach (var type in module.GetTypes()) WriteType(type, outputPath);
		}

		private string GetTypeDeclarationKeyword(TypeDefinition type)
		{
			if (type.IsEnum) return "enum";
			else if (type.IsInterface) return "class";
			else if (type.IsClass) return type.IsValueType ? "struct" : "class";
			else throw new Exception("Unsuported type kind: " + type.Name);
		}

		private bool IsValidType(TypeReference type)
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
			return GetFullTypeName(type, "_", "_", '[', ']', ',', true);
		}

		private void WriteAllTypesHeader(ModuleDefinition module, string outputPath)
		{
			using (var stream = new FileStream(Path.Combine(outputPath, allTypesHeader), FileMode.Create, FileAccess.Write))
			using (writer = new StreamWriter(stream))
			{
				writer.WriteLine("#pragma once");

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
					if (!IsValidType(type)) continue;

					activeType = type;
					writer.WriteLine($"#include \"{GetTypeFilename(type)}.h\";");
				}
			}
		}

		private void WriteType(TypeDefinition type, string outputPath)
		{
			if (!IsValidType(type)) return;

			activeType = type;
			string filename = GetTypeFilename(type);
			string filePath = Path.Combine(outputPath, filename);
			
			// write header
			using (var stream = new FileStream(filePath + ".h", FileMode.Create, FileAccess.Write))
			using (writer = new StreamWriter(stream))
			{
				writer.WriteLine("#pragma once");
				writer.WriteLine($"#include \"{allTypesHeader}\";");
				writer.WriteLine();
				WriteTypeHeader(type);
			}

			// write source
			if (!type.IsEnum && !type.HasGenericParameters)
			{
				using (var stream = new FileStream(filePath + ".cpp", FileMode.Create, FileAccess.Write))
				using (writer = new StreamWriter(stream))
				{
					writer.WriteLine($"#include \"{precompiledHeader}\";");
					writer.WriteLine($"#include \"{filename}.h\";");
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
				WriteGenericParameters(type);
				writer.WriteLine();
			}

			// write type name
			string typeKindKeyword = GetTypeDeclarationKeyword(type);
			writer.WritePrefix($"{typeKindKeyword} {GetNestedTypeName(type)}");
			if (type.IsSealed) writer.Write(" final");

			// write inheritance
			if (type.IsEnum)
			{
				if (type.HasFields && type.Fields[0].IsRuntimeSpecialName) writer.Write($" : {GetFullTypeName(type.Fields[0].FieldType)}");
			}
			else
			{
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

			// if fixed type, convert to native
			TypeReference fieldType = field.FieldType;
			bool isFixedType = false;
			int fixedTypeSize = 0;
			if (IsFixedBufferType(field.FieldType))
			{
				isFixedType = true;
				if (!field.FieldType.IsDefinition) throw new Exception("Cant get name for reference of FixedBuffer: " + field.FieldType);
				var typeDef = (TypeDefinition)field.FieldType;
				fieldType = typeDef.Fields[0].FieldType;
				fixedTypeSize = typeDef.ClassSize / GetPrimitiveSize(fieldType.MetadataType);
			}

			// write
			WriteAccessModifier(field, field.IsPublic, field.IsAssembly, false, field.IsFamily, field.IsFamilyOrAssembly, field.IsFamilyAndAssembly, field.IsPrivate);
			if (field.IsStatic) writer.Write("static ");
			writer.Write($"{GetFullTypeName(fieldType)} {GetMemberName(field)}");
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
				WriteGenericParameters(method);
				writer.Write(' ');
			}

			// write attributes
			if (method.IsStatic) writer.Write("static ");
			if (method.IsVirtual) writer.Write("virtual ");

			// write definition
			string name = method.IsConstructor ? GetNestedTypeName(method.DeclaringType) : GetMemberName(method);
			if (method.IsConstructor) writer.Write($"{name}(");
			else writer.Write($"{GetFullTypeName(method.ReturnType)} {name}(");

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

			if (method.IsConstructor) writer.WritePrefix(string.Format("{0}::{0}(", GetNestedTypeName(method.DeclaringType)));
			else writer.WritePrefix($"{GetFullTypeName(method.ReturnType)} {GetNestedTypeName(method.DeclaringType)}::{GetMemberName(method)}(");
			WriteParameters(method.Parameters);
			writer.WriteLine(')');
			writer.WriteLinePrefix('{');
			StreamWriterEx.AddTab();
			WriteMethodBody(method.Body);
			StreamWriterEx.RemoveTab();
			writer.WriteLinePrefix('}');
		}

		private void WriteGenericParameters(IGenericParameterProvider generic)
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

		private string GetNativeRuntimeTypeName(TypeReference type)
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
			bool isByRef = type.IsByReference;
			if (type.IsByReference)
			{
				var refType = (ByReferenceType)type;
				type = refType.ElementType;
			}

			string name = GetNativeRuntimeTypeName(type);
			if (name != null) return name;

			if (activeType.Namespace == type.Namespace || activeType == type.DeclaringType) name = GetNestedTypeName(type);// remove verbosity if possible
			else name = GetFullTypeName(type, "::", "_", '<', '>', ',', !type.IsDefinition);
			
			if (type.IsArray)
			{
				var arrayType = (ArrayType)type;
				string elementName = GetFullTypeName(arrayType.ElementType);
				name = $"IL2X_Array<{elementName}>";
			}
			
			if (!isBaseType && !type.IsValueType && !type.IsGenericParameter) name += '*';
			if (isByRef) name += '&';
			return name;
		}

		protected string GetNestedTypeName(TypeReference type)
		{
			return GetNestedTypeName(type, "_", '<', '>', ',', !type.IsDefinition);
		}

		protected string GetMemberName(MemberReference member)
		{
			return GetMemberName(member, "::", "_", '<', '>', ',', !member.IsDefinition);
		}
	}
}

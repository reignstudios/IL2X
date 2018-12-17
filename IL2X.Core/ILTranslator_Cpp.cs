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

			// translate references: TODO: make sure refs are only translated once <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
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
			outputPath = Path.Combine(outputPath, assemblyDefinition.Name.Name.Replace('.', '_'));
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
			if (type.HasGenericParameters) return false;// TODO: convert to C++ templates or C style option (future option: generate all opcode uses into new type and add compiler options to force specific ones)
			return true;
		}

		private void WriteAllTypesHeader(ModuleDefinition module, string outputPath)
		{
			using (var stream = new FileStream(Path.Combine(outputPath, "__ALL_TYPES.h"), FileMode.Create, FileAccess.Write))
			using (writer = new StreamWriter(stream))
			{
				writer.WriteLine("#pragma once");
				writer.WriteLine();
				foreach (var type in module.GetTypes())
				{
					if (!IsValidType(type)) continue;

					activeType = type;
					writer.WriteLine($"#include \"{GetFullNameFlat(type, "_", "_")}.h\";");
				}
			}
		}

		private void WriteType(TypeDefinition type, string outputPath)
		{
			if (!IsValidType(type)) return;

			activeType = type;
			string filename = GetFullNameFlat(type, "_", "_");
			string filePath = Path.Combine(outputPath, filename);
			
			// write header
			using (var stream = new FileStream(filePath + ".h", FileMode.Create, FileAccess.Write))
			using (writer = new StreamWriter(stream))
			{
				writer.WriteLine("#pragma once");
				writer.WriteLine("#include \"__ALL_TYPES.h\";");
				writer.WriteLine();
				WriteTypeHeader(type);
			}

			// write source
			if (!type.IsEnum)
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
			StreamWriterEx.AddTab();

			// write members
			string typeKindKeyword = GetTypeDeclarationKeyword(type);
			writer.WriteLinePrefix($"{typeKindKeyword} {GetNestedTypeNameFlat(type)}");
			writer.WriteLinePrefix('{');
			StreamWriterEx.AddTab();

			if (type.IsEnum)
			{
				if (type.HasFields)
				{
					var lastField = type.Fields.LastOrDefault();
					foreach (var field in type.Fields)
					{
						if (field.Attributes.HasFlag(FieldAttributes.RTSpecialName)) continue;
						writer.WritePrefix($"{GetmemberName(field)} = {field.Constant}");
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
			
				if (type.HasProperties)// TODO: is this needed or will methods do everything?
				{
					/*if (membersWritten) writer.WriteLine();
					membersWritten = true;
					writer.WriteLinePrefix("// Properties");
					foreach (var method in type.Properties) WriteMethodHeader(method);*/
				}

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
			StreamWriterEx.RemoveTab();
			WriteNamespaceEnd(namespaceCount, true);
		}

		private void WriteTypeSource(TypeDefinition type)
		{
			// write namespace
			int namespaceCount = WriteNamespaceStart(type, true);
			StreamWriterEx.AddTab();

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
			StreamWriterEx.RemoveTab();
			WriteNamespaceEnd(namespaceCount, true);
		}

		private void WriteFieldHeader(FieldDefinition field)
		{
			if (field.Attributes.HasFlag(FieldAttributes.RTSpecialName)) return;

			// generate access modifiers
			string accessModifier;
			if (field.IsPublic || field.IsAssembly) accessModifier = "public: ";
			else if (field.IsFamily) accessModifier = "protected: ";
			else if (field.IsPrivate) accessModifier = "private: ";
			else throw new NotImplementedException("Unsuported attributes: " + field.Attributes);
			if (field.IsStatic) accessModifier += "static ";

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
			writer.WritePrefix($"{accessModifier}{GetFullTypeNameFlat(fieldType)} {GetmemberName(field)}");
			if (isFixedType) writer.WriteLine($"[{fixedTypeSize}];");
			else writer.WriteLine(';');
		}

		private void WriteFieldSource(FieldDefinition field)
		{
			if (field.Attributes.HasFlag(FieldAttributes.RTSpecialName) || !field.IsStatic) return;
			writer.WriteLinePrefix($"{GetFullTypeNameFlat(field.FieldType)} {GetFullTypeNameFlat(field.DeclaringType)}::{GetmemberName(field)};");
		}

		private void WriteMethodHeader(MethodDefinition method)
		{
			string accessModifier = (method.IsPublic || method.IsAssembly) ? "public: " : "private: ";
			if (method.IsStatic) accessModifier += "static ";
			if (method.IsVirtual) accessModifier += "virtual ";

			string name = method.IsConstructor ? GetFullTypeNameFlat(method.DeclaringType) : GetmemberName(method);
			if (method.IsConstructor) writer.WritePrefix($"{name}(");
			else writer.WritePrefix($"{accessModifier}{GetFullTypeNameFlat(method.ReturnType)} {name}(");
			WriteParameters(method.Parameters);
			writer.Write(')');
			if (method.HasBody) writer.WriteLine(';');
			else writer.WriteLine(" = 0;");
		}
		
		private void WriteMethodSource(MethodDefinition method)
		{
			if (method.IsConstructor) writer.WritePrefix(string.Format("{0}::{0}(", GetFullTypeNameFlat(method.DeclaringType)));
			else writer.WritePrefix($"{GetFullTypeNameFlat(method.ReturnType)} {GetFullTypeNameFlat(method.DeclaringType)}::{GetmemberName(method)}(");
			WriteParameters(method.Parameters);
			writer.WriteLine(')');
			writer.WriteLinePrefix('{');
			StreamWriterEx.AddTab();
			WriteMethodBody(method.Body);
			StreamWriterEx.RemoveTab();
			writer.WriteLinePrefix('}');
		}

		private void WriteParameters(Collection<ParameterDefinition> parameters)
		{
			var lastParameter = parameters.LastOrDefault();
			foreach (var parameter in parameters)
			{
				writer.Write($"{GetFullTypeNameFlat(parameter.ParameterType)} {parameter.Name}");
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

		private string GetFullTypeNameFlat(TypeReference type)
		{
			string nativeTypeName = GetNativeRuntimeTypeName(type);
			if (nativeTypeName != null) return nativeTypeName;

			if (activeType.Namespace == type.Namespace || activeType == type.DeclaringType) return GetNestedNameFlat(type, "_");// remove verbosity if possible
			return GetFullNameFlat(type, "::", "_");
		}

		private string GetNestedTypeNameFlat(TypeReference type)
		{
			return GetNestedNameFlat(type, "_");
		}
	}
}

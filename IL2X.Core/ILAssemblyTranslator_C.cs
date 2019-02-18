using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Mono.Cecil;
using System.Linq;

namespace IL2X.Core
{
	public sealed class ILAssemblyTranslator_C : ILAssemblyTranslator
	{
		private StreamWriter writer;

		public ILAssemblyTranslator_C(string binaryPath, bool loadReferences)
		: base(binaryPath, loadReferences)
		{
			
		}

		public override void Translate(string outputPath)
		{
			TranslateAssembly(assembly, outputPath);
		}

		private void TranslateAssembly(ILAssembly assembly, string outputPath)
		{
			// translate all modules into C assmebly files
			foreach (var module in assembly.modules)
			{
				// translate reference assemblies
				foreach (var reference in module.references) TranslateAssembly(reference, outputPath);

				// create output folder
				if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

				// translate module
				TranslateModule(module, outputPath);
			}
		}

		private void TranslateModule(ILModule module, string outputPath)
		{
			// get module filename
			string modulePath = Path.Combine(outputPath, module.moduleDefinition.Name.Replace('.', '_'));
			if (module.moduleDefinition.IsMain && (module.moduleDefinition.Kind == ModuleKind.Console || module.moduleDefinition.Kind == ModuleKind.Windows)) modulePath += ".c";
			else modulePath += ".h";

			// write module
			using (var stream = new FileStream(modulePath, FileMode.Create, FileAccess.Write, FileShare.Read))
			using (writer = new StreamWriter(stream))
			{
				// write generate info
				writer.WriteLine("// ===============================");
				writer.WriteLine($"// Generated with IL2X v{Assembly.GetCallingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion}");
				writer.WriteLine("// ===============================");
				writer.WriteLine();

				// write forward declare of types
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Type forward declares");
				writer.WriteLine("// ===============================");
				foreach (var type in module.moduleDefinition.Types) WriteTypeDefinition(type, false);
				writer.WriteLine();

				// write forward declare of type methods
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Method forward declares");
				writer.WriteLine("// ===============================");
				foreach (var type in module.moduleDefinition.Types)
				{
					foreach (var method in type.Methods) WriteMethodDefinition(method, false);
				}
				writer.WriteLine();

				// write type definitions
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Type definitions");
				writer.WriteLine("// ===============================");
				foreach (var type in module.moduleDefinition.Types) WriteTypeDefinition(type, true);

				// write method definitions
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Method definitions");
				writer.WriteLine("// ===============================");
				foreach (var type in module.moduleDefinition.Types)
				{
					foreach (var method in type.Methods) WriteMethodDefinition(method, true);
				}
			}

			writer = null;
		}

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
				foreach (var field in type.Fields) WriteFieldDefinition(field);
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
			if (method.HasParameters) WriteParameterDefinitions(method.Parameters);
			else writer.Write("void");
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
				// TODO: write opcodes
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

		protected override string GetTypeDefinitionName(TypeDefinition type)
		{
			string result = base.GetTypeDefinitionName(type);
			if (type.HasGenericParameters) result += '_' + GetGenericParameters(type);
			return result;
		}

		protected override string GetTypeReferenceName(TypeReference type)
		{
			string result;

			var def = GetTypeDefinition(type);
			if (def.IsEnum)
			{
				if (!def.HasFields) throw new Exception("Enum has no fields: " + type.Name);
				return GetTypeReferenceName(def.Fields[0].FieldType);
			}
			else
			{
				result = base.GetTypeReferenceName(type);
			}

			if (type.HasGenericParameters) result += '_' + GetGenericParameters(type);
			if (!type.IsValueType) result += '*';
			return result;
		}

		private bool disableTypePrefix;
		protected override string GetTypeDefinitionFullName(TypeDefinition type)
		{
			string result = base.GetTypeDefinitionFullName(type);
			if (!disableTypePrefix) result = "t_" + result;
			return result;
		}

		protected override string GetTypeReferenceFullName(TypeReference type)
		{
			string result;
			if (type.MetadataType == MetadataType.Void)
			{
				result = "void";
			}
			else
			{
				result = base.GetTypeReferenceFullName(type);
				if (!disableTypePrefix) result = "t_" + result;
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
			disableTypePrefix = true;
			string result = "m_" + base.GetMethodDefinitionFullName(method);
			disableTypePrefix = false;
			return result;
		}

		protected override string GetMethodReferenceFullName(MethodReference method)
		{
			disableTypePrefix = true;
			string result = "m_" + base.GetMethodReferenceFullName(method);
			disableTypePrefix = false;
			return result;
		}

		protected override string GetGenericParameterName(GenericParameter parameter)
		{
			return "gp_" + base.GetGenericParameterName(parameter);
		}

		protected override string GetGenericArgumentTypeName(TypeReference type)
		{
			return base.GetTypeReferenceFullName(type);
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
			return "f_" + base.GetFieldDefinitionName(field);
		}

		protected override string GetFieldReferenceName(FieldReference field)
		{
			return "f_" + base.GetFieldReferenceName(field);
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
	}
}

using Mono.Cecil;
using System.IO;
using System;

namespace IL2X.Core
{
	public class ILTranslator_Cpp : ILTranslator
	{
		private StreamWriter writer;

		public ILTranslator_Cpp(string binaryPath)
		: base(binaryPath)
		{}

		public override void Translate(string outputPath)
		{
			TranslateModule(assemblyDefinition.MainModule, outputPath);
		}

		private void TranslateModule(ModuleDefinition module, string outputPath)
		{
			string ext = module.Kind == ModuleKind.Dll ? ".h" : ".cpp";
			using (var stream = new FileStream(Path.Combine(outputPath, module.Name + ext), FileMode.Create, FileAccess.Write))
			using (writer = new StreamWriter(stream))
			{
				writer.WriteLine(string.Format("{0}// ============={0}// Type forward declares{0}// =============", Environment.NewLine));
				foreach (var type in module.GetTypes()) WriteTypeDefinition(type, false);

				writer.WriteLine(string.Format("{0}// ============={0}// Type Definitions{0}// =============", Environment.NewLine));
				foreach (var type in module.GetTypes()) WriteTypeDefinition(type, true);
			}

			// translate references TODO: auto resolve all these first in the base class
			/*foreach (var reference in module.AssemblyReferences)
			{
				using (var refAssemblyDefinition = assemblyResolver.Resolve(reference))
				{
					TranslateModule(refAssemblyDefinition.MainModule, outputPath);
				}
			}*/
		}

		private void WriteTypeDefinition(TypeDefinition type, bool writeBody)
		{
			if (type.Name == "<Module>") return;

			string typeKindKeyword;
			if (type.IsEnum) typeKindKeyword = "enum";
			else if (type.IsInterface) typeKindKeyword = "class";
			else if (type.IsClass) typeKindKeyword = type.IsValueType ? "struct" : "class";
			else throw new Exception("Unsuported type kind: " + type.Name);

			writer.Write($"{typeKindKeyword} {type.Name}");
			if (writeBody)
			{
				writer.WriteLine(Environment.NewLine + '{');
				foreach (var field in type.Fields) WriteField(field);
				writer.WriteLine("};" + Environment.NewLine);
			}
			else
			{
				writer.WriteLine(';');
			}
		}

		private void WriteField(FieldDefinition field)
		{
			writer.WriteLine($"\t{field.FieldType} {field.Name};");
		}
	}
}

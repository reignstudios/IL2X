using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;
using System;
using System.IO;
using System.Text;

namespace IL2X.Core
{
	public abstract class ILTranslator : IDisposable
	{
		protected DefaultAssemblyResolver assemblyResolver;
		protected AssemblyDefinition assemblyDefinition;
		protected ISymbolReader symbolReader;

		public ILTranslator(string binaryPath, params string[] searchPaths)
		{
			// create assebly resolver object
			var readerParameters = new ReaderParameters();
			assemblyResolver = new DefaultAssemblyResolver();
			readerParameters.AssemblyResolver = assemblyResolver;

			// add resolver paths
			foreach (string path in searchPaths) assemblyResolver.AddSearchDirectory(path);

			// read debug symbol file
			string symbolsPath = Path.Combine(Path.GetDirectoryName(binaryPath), Path.GetFileNameWithoutExtension(binaryPath) + ".pdb");
			if (File.Exists(symbolsPath))
			{
				// load assembly
				var symbolReaderProvider = new PdbReaderProvider();
				readerParameters.SymbolReaderProvider = symbolReaderProvider;
				readerParameters.ReadSymbols = true;
				assemblyDefinition = AssemblyDefinition.ReadAssembly(binaryPath, readerParameters);

				// load symbols
				symbolReader = symbolReaderProvider.GetSymbolReader(assemblyDefinition.MainModule, symbolsPath);
			}
			else
			{
				assemblyDefinition = AssemblyDefinition.ReadAssembly(binaryPath, readerParameters);
			}
		}

		public void Dispose()
		{
			Utils.DisposeInstance(ref symbolReader);
			Utils.DisposeInstance(ref assemblyDefinition);
			Utils.DisposeInstance(ref assemblyResolver);
		}

		public abstract void Translate(string outputPath, bool translateReferences);

		protected string GetFullNameFlat(TypeReference type, string namespaceDelimiter, string nestedDelimiter)
		{
			var value = new StringBuilder();
			GetQualifiedNameFlat(type, ref namespaceDelimiter, ref nestedDelimiter, value, true);
			return value.ToString();
		}

		protected string GetNestedNameFlat(TypeReference type, string nestedDelimiter)
		{
			if (!type.IsNested) return type.Name;
			var value = new StringBuilder();
			GetQualifiedNameFlat(type, ref nestedDelimiter, ref nestedDelimiter, value, false);
			return value.ToString();
		}

		protected void GetQualifiedNameFlat(TypeReference type, ref string namespaceDelimiter, ref string nestedDelimiter, StringBuilder value, bool writeNamespace)
		{
			value.Insert(0, type.Name);
			if (type.DeclaringType != null)
			{
				value.Insert(0, nestedDelimiter);
				GetQualifiedNameFlat(type.DeclaringType, ref namespaceDelimiter, ref nestedDelimiter, value, writeNamespace);
			}
			else if (writeNamespace && !string.IsNullOrEmpty(type.Namespace))
			{
				value.Insert(0, type.Namespace.Replace(".", namespaceDelimiter) + namespaceDelimiter);
			}
		}
	}
}

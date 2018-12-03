using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;
using System;
using System.IO;

namespace IL2X.Core
{
	public abstract class ILTranslator : IDisposable
	{
		protected DefaultAssemblyResolver assemblyResolver;
		protected AssemblyDefinition assemblyDefinition;
		protected ISymbolReader symbolReader;

		public ILTranslator(string binaryPath)
		{
			// create assebly resolver object
			var readerParameters = new ReaderParameters();
			assemblyResolver = new DefaultAssemblyResolver();
			readerParameters.AssemblyResolver = assemblyResolver;

			// TODO: add resolver paths
			//assemblyResolver.AddSearchDirectory($@"D:\Dev\TEMP\IL2X\TestApp\bin\{bin}\netcoreapp2.1");
			
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

		public abstract void Translate(string outputPath);
	}
}

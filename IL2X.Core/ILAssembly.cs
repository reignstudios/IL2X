using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;
using System;
using System.Collections.Generic;
using System.IO;

namespace IL2X.Core
{
	public sealed class ILAssembly : IDisposable
	{
		// IL2X types
		public Stack<ILModule> modules { get; private set; }

		// Mono.Cecil types
		public AssemblyDefinition assemblyDefinition { get; private set; }

		public ILAssembly(Stack<ILAssembly> allAssemblies, string binaryPath, bool loadReferences, DefaultAssemblyResolver assemblyResolver)
		{
			allAssemblies.Push(this);

			// create reader parameters desc
			var readerParameters = new ReaderParameters();
			readerParameters.AssemblyResolver = assemblyResolver;

			// check if debug symbol file is avaliable
			var pdbReaderProvider = new PdbReaderProvider();
			string symbolsPath = Path.Combine(Path.GetDirectoryName(binaryPath), Path.GetFileNameWithoutExtension(binaryPath) + ".pdb");
			if (File.Exists(symbolsPath))
			{
				readerParameters.SymbolReaderProvider = pdbReaderProvider;
				readerParameters.ReadSymbols = true;
			}
				
			// read assembly file
			assemblyDefinition = AssemblyDefinition.ReadAssembly(binaryPath, readerParameters);

			// load assembly modules
			modules = new Stack<ILModule>();
			foreach (var moduleDef in assemblyDefinition.Modules)
			{
				// read debug symbols for module
				ISymbolReader symbolReader = null;
				if (readerParameters.ReadSymbols) symbolReader = pdbReaderProvider.GetSymbolReader(moduleDef, symbolsPath);

				// add module
				var module = new ILModule(allAssemblies, this, loadReferences, moduleDef, symbolReader, assemblyResolver);
				modules.Push(module);
			}
		}

		public void Dispose()
		{
			if (modules != null)
			{
				foreach (var module in modules)
				{
					module.Dispose();
					modules = null;
				}
			}
			
			if (assemblyDefinition != null)
			{
				assemblyDefinition.Dispose();
				assemblyDefinition = null;
			}
		}
	}
}

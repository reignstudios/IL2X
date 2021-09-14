using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;

namespace IL2X.Core
{
	public sealed class Library : IDisposable
	{
		public readonly Solution solution;
		public readonly string binaryPath;
		public List<Module> modules;
		public AssemblyDefinition cecilAssembly;

		public Library(Solution solution, string binaryPath)
		{
			this.solution = solution;
			this.binaryPath = binaryPath;
		}

		public void Dispose()
		{
			if (cecilAssembly != null)
			{
				cecilAssembly.Dispose();
				cecilAssembly = null;
			}
		}

		internal void Load(IAssemblyResolver assemblyResolver)
		{
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
			cecilAssembly = AssemblyDefinition.ReadAssembly(binaryPath, readerParameters);

			// load modules
			modules = new List<Module>();
			foreach (var cecilModule in cecilAssembly.Modules)
			{
				// read debug symbols for module
				ISymbolReader symbolReader = null;
				if (readerParameters.ReadSymbols) symbolReader = pdbReaderProvider.GetSymbolReader(cecilModule, symbolsPath);

				// create and load module
				var module = new Module(this, cecilModule, symbolReader);
				modules.Add(module);
				module.Load(assemblyResolver);
			}
		}
	}
}

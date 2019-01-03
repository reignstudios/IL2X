using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace IL2X.Core
{
	public class ILAssembly : IDisposable
	{
		internal ISymbolReader symbolReader;
		internal AssemblyDefinition assemblyDefinition;
		public Stack<ILAssembly> references { get; private set; }

		public ILAssembly(Stack<ILAssembly> allAssemblies, string binaryPath, bool loadReferences, string[] searchPaths)
		{
			// create assembly resolver
			using (var assemblyResolver = new DefaultAssemblyResolver())
			{
				foreach (string path in searchPaths) assemblyResolver.AddSearchDirectory(path);

				// create reader parameters desc
				var readerParameters = new ReaderParameters();
				readerParameters.AssemblyResolver = assemblyResolver;

				// load assembly with debug symbol file if possible
				var pdbReaderProvider = new PdbReaderProvider();
				string symbolsPath = Path.Combine(Path.GetDirectoryName(binaryPath), Path.GetFileNameWithoutExtension(binaryPath) + ".pdb");
				if (File.Exists(symbolsPath))
				{
					readerParameters.SymbolReaderProvider = pdbReaderProvider;
					readerParameters.ReadSymbols = true;
				}

				assemblyDefinition = AssemblyDefinition.ReadAssembly(binaryPath, readerParameters);
				if (readerParameters.ReadSymbols) symbolReader = pdbReaderProvider.GetSymbolReader(assemblyDefinition.MainModule, symbolsPath);
				allAssemblies.Push(this);

				// load references
				references = new Stack<ILAssembly>();
				if (loadReferences)
				{
					foreach (var reference in assemblyDefinition.MainModule.AssemblyReferences)
					{
						using (var assemblyDefinition = assemblyResolver.Resolve(reference))
						{
							if (allAssemblies.Any(x => x.assemblyDefinition.FullName == assemblyDefinition.FullName)) continue;
							references.Push(new ILAssembly(allAssemblies, assemblyDefinition.MainModule.FileName, loadReferences, searchPaths));
						}
					}
				}
			}
		}

		public void Dispose()
		{
			if (references != null)
			{
				foreach (var reference in references)
				{
					reference.Dispose();
					references = null;
				}
			}

			Utils.DisposeInstance(ref symbolReader);
			Utils.DisposeInstance(ref assemblyDefinition);
		}
	}
}

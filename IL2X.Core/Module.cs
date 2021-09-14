using System;
using Mono.Cecil;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;

namespace IL2X.Core
{
	public sealed class Module
	{
		public readonly Library library;
		public readonly ModuleDefinition cecilModule;
		public ISymbolReader symbolReader;
		public List<Library> libraryReferences;

		public Module(Library library, ModuleDefinition cecilModule, ISymbolReader symbolReader)
		{
			this.library = library;
			this.cecilModule = cecilModule;
			this.symbolReader = symbolReader;
		}

		internal void Load(IAssemblyResolver assemblyResolver)
		{
			// load library references
			libraryReferences = new List<Library>();
			foreach (var assemblyReference in cecilModule.AssemblyReferences)
			{
				using (var cecilAssembly = assemblyResolver.Resolve(assemblyReference))
				{
					Library l = library.solution.libraries.FirstOrDefault(x => x.cecilAssembly.FullName == cecilAssembly.FullName);
					if (l == null)
					{
						l = library.solution.AddLibrary(cecilAssembly.MainModule.FileName);
						l.Load(assemblyResolver);
					}
					libraryReferences.Add(l);
				}
			}
		}
	}
}

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
		public readonly Assembly assembly;
		public readonly ModuleDefinition cecilModule;
		public ISymbolReader symbolReader;
		public List<Assembly> assemblyReferences;

		public Module(Assembly assembly, ModuleDefinition cecilModule, ISymbolReader symbolReader)
		{
			this.assembly = assembly;
			this.cecilModule = cecilModule;
			this.symbolReader = symbolReader;
		}

		internal void Load(IAssemblyResolver assemblyResolver)
		{
			// load assembly references
			assemblyReferences = new List<Assembly>();
			foreach (var assemblyReference in cecilModule.AssemblyReferences)
			{
				using (var cecilAssembly = assemblyResolver.Resolve(assemblyReference))
				{
					Assembly l = assembly.solution.assemblies.FirstOrDefault(x => x.cecilAssembly.FullName == cecilAssembly.FullName);
					if (l == null)
					{
						l = assembly.solution.AddAssembly(cecilAssembly.MainModule.FileName);
						l.Load(assemblyResolver);
					}
					assemblyReferences.Add(l);
				}
			}
		}
	}
}

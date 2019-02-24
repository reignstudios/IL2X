using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IL2X.Core
{
	public sealed class ILModule : IDisposable
	{
		// IL2X types
		public ILAssembly assembly { get; private set; }
		public Stack<ILAssembly> references { get; private set; }

		// Mono.Cecil types
		public ModuleDefinition moduleDefinition { get; private set; }
		public ISymbolReader symbolReader { get; private set; }
		public List<TypeDefinition> typesDependencyOrdered { get; private set; }

		public ILModule(Stack<ILAssembly> allAssemblies, ILAssembly assembly, bool loadReferences, ModuleDefinition moduleDefinition, ISymbolReader symbolReader, DefaultAssemblyResolver assemblyResolver)
		{
			this.assembly = assembly;
			this.moduleDefinition = moduleDefinition;
			this.symbolReader = symbolReader;

			// create dependency ordered type list
			typesDependencyOrdered = moduleDefinition.Types.ToList();
			typesDependencyOrdered.Sort(delegate (TypeDefinition x, TypeDefinition y)
			{
				var baseType = x.BaseType;
				while (baseType != null)
				{
					if (!baseType.IsDefinition) return 0;
					if (baseType.FullName == y.FullName) return 1;

					var baseTypeDef = (TypeDefinition)baseType;
					baseType = baseTypeDef.BaseType;
				}

				return 0;
			});

			typesDependencyOrdered.Reverse();

			// load references
			references = new Stack<ILAssembly>();
			if (loadReferences)
			{
				foreach (var nameReference in moduleDefinition.AssemblyReferences)
				{
					using (var assemblyDefinition = assemblyResolver.Resolve(nameReference))
					{
						var ilAssembly = allAssemblies.FirstOrDefault(x => x.assemblyDefinition.FullName == assemblyDefinition.FullName);
						if (ilAssembly == null) ilAssembly = new ILAssembly(allAssemblies, assemblyDefinition.MainModule.FileName, loadReferences, assemblyResolver);
						references.Push(ilAssembly);
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

			if (symbolReader != null)
			{
				symbolReader.Dispose();
				symbolReader = null;
			}
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IL2X.Core.Jit
{
	public class ModuleJit
	{
		public AssemblyJit assembly;
		public Module module;
		public List<AssemblyJit> assemblyReferences;
		public List<TypeJit> allTypes, classTypes, structTypes, enumTypes;

		public ModuleJit(AssemblyJit assembly, Module module)
		{
			this.assembly = assembly;
			this.module = module;

			// jit dependencies first
			assemblyReferences = new List<AssemblyJit>();
			foreach (var a in module.assemblyReferences)
			{
				var existingProj = assembly.solution.projects.FirstOrDefault(x => x.assembly == a);
				if (existingProj != null)
				{
					assemblyReferences.Add(existingProj);
				}
				else
				{
					assemblyReferences.Add(new AssemblyJit(assembly.solution, a));
				}
			}
		}
	}
}

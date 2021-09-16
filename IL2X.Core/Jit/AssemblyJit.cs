using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IL2X.Core.Jit
{
	public class AssemblyJit
	{
		public Solution solution;
		public Assembly assembly;
		public List<ModuleJit> modules;

		public AssemblyJit(Solution solution, Assembly assembly)
		{
			this.solution = solution;
			this.assembly = assembly;
			solution.projects.Add(this);

			// jit modules
			modules = new List<ModuleJit>();
			foreach (var module in assembly.modules)
			{
				modules.Add(new ModuleJit(this, module));
			}
		}
	}
}

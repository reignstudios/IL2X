using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IL2X.Core.Jit
{
	public class AssemblyJit
	{
		public readonly Solution solution;
		public readonly Assembly assembly;
		public List<ModuleJit> modules;
		public bool optimized { get; internal set; }

		public AssemblyJit(Solution solution, Assembly assembly)
		{
			this.solution = solution;
			this.assembly = assembly;
			solution.assemblyJits.Add(this);
		}

		public void Jit()
		{
			modules = new List<ModuleJit>();
			foreach (var module in assembly.modules)
			{
				var m = new ModuleJit(this, module);
				m.Jit();
			}
		}

		public void Optimize()
		{
			if (optimized) return;
			optimized = true;
			foreach (var module in modules)
			{
				module.Optimize();
			}
		}

		public ModuleJit FindJitModuleRecursive(ModuleDefinition module)
		{
			if (modules != null)
			{
				foreach (var moduleJit in modules)
				{
					var result = moduleJit.FindJitModuleRecursive(module);
					if (result != null) return result;
				}
			}
			return null;
		}

		public TypeJit FindJitTypeRecursive(TypeReference type)
		{
			if (modules != null)
			{
				foreach (var module in modules)
				{
					var result = module.FindJitTypeRecursive(type);
					if (result != null) return result;
				}
			}
			return null;
		}

		public FieldJit FindJitFieldRecursive(FieldDefinition field)
		{
			if (modules != null)
			{
				foreach (var module in modules)
				{
					var result = module.FindJitFieldRecursive(field);
					if (result != null) return result;
				}
			}
			return null;
		}
	}
}

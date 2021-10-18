using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IL2X.Core.Jit
{
	public class ModuleJit
	{
		public readonly AssemblyJit assembly;
		public readonly Module module;
		public List<AssemblyJit> assemblyReferences;
		public List<TypeJit> allTypes, classTypes, structTypes, enumTypes;

		public ModuleJit(AssemblyJit assembly, Module module)
		{
			this.assembly = assembly;
			this.module = module;
			assembly.modules.Add(this);
		}

		internal void Jit()
		{
			// jit dependencies first
			assemblyReferences = new List<AssemblyJit>();
			foreach (var assemblyRef in module.assemblyReferences)
			{
				var existingProj = assembly.solution.assemblyJits.FirstOrDefault(x => x.assembly == assemblyRef);
				if (existingProj != null)
				{
					assemblyReferences.Add(existingProj);
				}
				else
				{
					var assemblyJit = new AssemblyJit(assembly.solution, assemblyRef);
					assemblyReferences.Add(assemblyJit);
					assemblyJit.Jit();
				}
			}

			// jit type
			allTypes = new List<TypeJit>();
			classTypes = new List<TypeJit>();
			structTypes = new List<TypeJit>();
			enumTypes = new List<TypeJit>();
			foreach (var type in module.cecilModule.Types)
			{
				if (IsModuleType(type)) continue;// skip auto-generated module type
				if (type.HasGenericParameters) continue;// don't JIT generic definition types
				var typeJit = new TypeJit(type, type, this);
				typeJit.Jit();
			}
		}

		internal void Optimize()
		{
			// search references first
			foreach (var assemblyRef in assemblyReferences)
			{
				if (!assemblyRef.optimized) assemblyRef.Optimize();
			}

			// search types
			foreach (var type in allTypes)
			{
				type.Optimize();
			}
		}

		public ModuleJit FindJitModuleRecursive(ModuleDefinition module)
		{
			// check if we match
			if (module == this.module.cecilModule) return this;

			// search references
			foreach (var assemblyRef in assemblyReferences)
			{
				var result = assemblyRef.FindJitModuleRecursive(module);
				if (result != null) return result;
			}

			return null;
		}

		public TypeJit FindJitTypeRecursive(TypeDefinition type)
		{
			// search references first
			foreach (var assemblyRef in assemblyReferences)
			{
				var result = assemblyRef.FindJitTypeRecursive(type);
				if (result != null) return result;
			}

			// search types
			foreach (var t in allTypes)
			{
				if (TypeJit.TypesEqual(t.typeDefinition, type)) return t;
			}

			return null;
		}

		public FieldJit FindJitFieldRecursive(FieldDefinition field)
		{
			// search references first
			foreach (var assemblyRef in assemblyReferences)
			{
				var result = assemblyRef.FindJitFieldRecursive(field);
				if (result != null) return result;
			}

			// search types
			foreach (var type in allTypes)
			{
				var result = type.FindJitFieldRecursive(field);
				if (result != null) return result;
			}

			return null;
		}

		private static bool IsModuleType(TypeDefinition type)
		{
			return type.FullName == "<Module>";
		}
	}
}

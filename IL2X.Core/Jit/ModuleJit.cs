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
		public List<ModuleJit> moduleReferences;
		public List<AssemblyJit> assemblyReferences;
		public List<TypeJit> allTypes, classTypes, structTypes, interfaceTypes, enumTypes;

		public ModuleJit(AssemblyJit assembly, Module module)
		{
			this.assembly = assembly;
			this.module = module;
			assembly.modules.Add(this);
		}

		internal void Jit()
		{
			// jit module dependencies
			moduleReferences = new List<ModuleJit>();
			foreach (var moduleRef in module.moduleReferences)
			{
				var existingModule = assembly.modules.FirstOrDefault(x => x.module == moduleRef);
				if (existingModule != null)
				{
					moduleReferences.Add(existingModule);
				}
				else
				{
					var moduleJit = new ModuleJit(assembly, moduleRef);
					moduleReferences.Add(moduleJit);
					moduleJit.Jit();
				}
			}

			// jit assembly dependencies
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
			interfaceTypes = new List<TypeJit>();
			enumTypes = new List<TypeJit>();
			foreach (var type in module.cecilModule.Types)
			{
				if (IsModuleType(type)) continue;// skip auto-generated module type
				if (type.HasGenericParameters) continue;// don't JIT generic definition types
				var typeJit = assembly.solution.FindJitTypeRecursive(type);
				if (typeJit == null)
				{
					typeJit = new TypeJit(type, type, this);
					typeJit.Jit();
				}
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
			if (assemblyReferences != null)
			{
				foreach (var assemblyRef in assemblyReferences)
				{
					var result = assemblyRef.FindJitModuleRecursive(module);
					if (result != null) return result;
				}
			}

			return null;
		}

		public TypeJit FindJitTypeRecursive(TypeReference type)
		{
			// search references first
			if (assemblyReferences != null)
			{
				foreach (var assemblyRef in assemblyReferences)
				{
					var result = assemblyRef.FindJitTypeRecursive(type);
					if (result != null) return result;
				}
			}

			// search types
			if (allTypes != null)
			{
				foreach (var t in allTypes)
				{
					if (TypeJit.TypesEqual(t.typeReference, type)) return t;
				}
			}

			return null;
		}

		public TypeJit FindJitTypeRecursive(string fullTypeName)
		{
			// search references first
			if (assemblyReferences != null)
			{
				foreach (var assemblyRef in assemblyReferences)
				{
					var result = assemblyRef.FindJitTypeRecursive(fullTypeName);
					if (result != null) return result;
				}
			}

			// search types
			if (allTypes != null)
			{
				foreach (var t in allTypes)
				{
					if (t.typeReference.FullName == fullTypeName) return t;
				}
			}

			return null;
		}

		public FieldJit FindJitFieldRecursive(FieldDefinition field)
		{
			// search references first
			if (assemblyReferences != null)
			{
				foreach (var assemblyRef in assemblyReferences)
				{
					var result = assemblyRef.FindJitFieldRecursive(field);
					if (result != null) return result;
				}
			}

			// search types
			if (allTypes != null)
			{
				foreach (var type in allTypes)
				{
					var result = type.FindJitFieldRecursive(field);
					if (result != null) return result;
				}
			}

			return null;
		}

		private static bool IsModuleType(TypeDefinition type)
		{
			return type.FullName == "<Module>";
		}
	}
}

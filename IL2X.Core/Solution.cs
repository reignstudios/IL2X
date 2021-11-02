using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Mono.Cecil;
using IL2X.Core.Jit;

namespace IL2X.Core
{
	public sealed class Solution : IDisposable
	{
		public enum Type
		{
			Executable,
			Library
		}

		public readonly Type type;
		public readonly string dllPath, dllFolderPath;

		public Assembly mainAssembly, coreAssembly;
		public List<Assembly> assemblies;

		public AssemblyJit mainAssemblyJit, coreAssemblyJit;
		public List<AssemblyJit> assemblyJits;

		public Solution(Type type, string dllPath)
		{
			TypeSystem.AddCustomCoreLib("IL2X.CoreLib");
			this.type = type;
			this.dllPath = dllPath;
			dllFolderPath = Path.GetDirectoryName(dllPath);
			string ext = Path.GetExtension(dllPath);
			if (ext != ".dll") throw new NotSupportedException("File must be '.dll'");
		}

		public void Dispose()
		{
			if (assemblies != null)
			{
				foreach (var assembly in assemblies) assembly.Dispose();
				assemblies = null;
			}
		}

		internal Assembly AddAssembly(string binaryPath)
		{
			var assembly = new Assembly(this, binaryPath);
			assemblies.Add(assembly);
			return assembly;
		}

		public void ReLoad()
		{
			assemblies = new List<Assembly>();
			using (var assemblyResolver = new DefaultAssemblyResolver())
			{
				assemblyResolver.AddSearchDirectory(dllFolderPath);
				mainAssembly = AddAssembly(dllPath);
				mainAssembly.Load(assemblyResolver);
			}
		}

		public void Jit()
		{
			assemblyJits = new List<AssemblyJit>();
			mainAssemblyJit = new AssemblyJit(this, mainAssembly);
			mainAssemblyJit.Jit();
		}

		public void Optimize()
		{
			if (!mainAssemblyJit.optimized) mainAssemblyJit.Optimize();
		}

		public ModuleJit FindJitModuleRecursive(ModuleDefinition module)
		{
			return mainAssemblyJit.FindJitModuleRecursive(module);
		}

		public TypeJit FindJitTypeRecursive(TypeReference type)
		{
			return mainAssemblyJit.FindJitTypeRecursive(type);
		}

		public FieldJit FindJitFieldRecursive(FieldDefinition field)
		{
			return mainAssemblyJit.FindJitFieldRecursive(field);
		}

		internal TypeReference ResolveType(TypeReference type, TypeReference usedInType)
		{
			// resolve generic instance
			if (type.IsGenericInstance)
			{
				var g = (IGenericInstance)type;
				if (g.GenericArguments.Any((x => x.IsGenericParameter)))
				{
					var element = type.GetElementType();
					var result = new GenericInstanceType(element);
					foreach (var arg in g.GenericArguments)
					{
						var argResolved = ResolveType(arg, usedInType);
						result.GenericArguments.Add(argResolved);
					}
					return result;
				}
			}

			// resolve generic parameter
			if (type.IsGenericParameter)
			{
				var genericParamArg = (GenericParameter)type;
				var g = (IGenericInstance)usedInType;
				type = g.GenericArguments[genericParamArg.Position];
			}

			return type;
		}

		private TypeJit ResolveElementType(TypeReference type, TypeJit usedInType)
		{
			var resolvedType = ResolveType(type, usedInType.typeReference);
			var resolvedTypeJit = FindJitTypeRecursive(resolvedType);
			if (resolvedTypeJit == null)
			{
				var moduleJit = FindJitModuleRecursive(type.Module);
				resolvedTypeJit = new TypeJit(null, resolvedType, moduleJit);
				resolvedTypeJit.Jit();
			}
			return resolvedTypeJit;
		}

		internal TypeJit ResolveType(TypeReference type, TypeJit usedInType)
		{
			// resolve all elements first
			if (type.IsArray || type.IsByReference || type.IsPointer)
			{
				var elementType = type.GetElementType();
				var elementTypeJit = ResolveType(elementType, usedInType);
				throw new NotImplementedException();// TODO: Add Mono.Cecil SetElementType method
			}

			// resolve main type
			return ResolveElementType(type, usedInType);
		}
	}
}
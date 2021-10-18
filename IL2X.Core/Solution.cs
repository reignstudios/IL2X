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

		public TypeJit FindJitTypeRecursive(TypeDefinition type)
		{
			return mainAssemblyJit.FindJitTypeRecursive(type);
		}

		public FieldJit FindJitFieldRecursive(FieldDefinition field)
		{
			return mainAssemblyJit.FindJitFieldRecursive(field);
		}

		internal TypeJit ResolveType(TypeReference type, TypeJit declaredInType)
		{
			// resolve generic instance
			if (type.IsGenericInstance)
			{
				var genericInstance = (IGenericInstance)type;
				var jitGenericArgs = new List<TypeJit>();
				foreach (var genericArg in genericInstance.GenericArguments)
				{
					/*if (arg.IsGenericParameter)
					{
						var genericParamArg = (GenericParameter)arg;
						int index = containingType.typeDefinition.GenericParameters.IndexOf(genericParamArg);
						var genericType = containingType.genericTypeReference.GenericArguments[index];
					}*/

					/*var genericArgDefinition = genericArg.Resolve();
					if (genericArgDefinition == null) throw new Exception("Failed to resolve Cecil generic argument type: " + genericArg.FullName);
					var argJitType = FindJitTypeRecursive(genericArgDefinition);
					if (argJitType == null)
					{
						argJitType = ResolveType(genericArg, containingType);
					}*/
					var argJitType = ResolveType(genericArg, declaredInType);
					jitGenericArgs.Add(argJitType);
				}

				//var genericJitType = new TypeJit(null, )
			}
			
			// resolve generic parameter
			if (type.IsGenericParameter)
			{
				var genericParamArg = (GenericParameter)type;
				int index = declaredInType.typeDefinition.GenericParameters.IndexOf(genericParamArg);
				var genericType = declaredInType.genericTypeReference.GenericArguments[index];
				var genericTypeDefinition = genericType.Resolve();
				if (genericTypeDefinition == null) throw new Exception("Failed to resolve Cecil generic parameter type: " + genericType.FullName);
				var genericJitType = FindJitTypeRecursive(genericTypeDefinition);
				if (genericJitType == null)// JIT type if not yet done
				{
					var declaringModule = declaredInType.module.assembly.solution.FindJitModuleRecursive(type.Module);
					if (declaringModule == null) throw new Exception("Failed to find declaring module for generic type: " + genericTypeDefinition.FullName);
					genericJitType = new TypeJit(genericTypeDefinition, type, declaringModule, null);
					genericJitType.Jit();
				}
				return genericJitType;
			}
			
			// resolve known type
			var typeDefinition = type.Resolve();
			if (typeDefinition == null) throw new Exception("Failed to resolve Cecil type: " + type.FullName);
			var knownJitType = FindJitTypeRecursive(typeDefinition);
			if (knownJitType == null)// JIT type if not yet done
			{
				var declaringModule = declaredInType.module.assembly.solution.FindJitModuleRecursive(type.Module);
				if (declaringModule == null) throw new Exception("Failed to find declaring module for known type: " + typeDefinition.FullName);
				knownJitType = new TypeJit(typeDefinition, type, declaringModule, null);
				knownJitType.Jit();
			}
			return knownJitType;
		}
	}
}

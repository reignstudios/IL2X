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

		internal TypeReference ResolveCecilType(TypeReference type, TypeReference usedInType, bool canModifyExistingType)
		{
			// resolve array
			if (type.IsArray)
			{
				var arrayType = (ArrayType)type;
				var elementType = ResolveCecilType(arrayType.ElementType, usedInType, canModifyExistingType);
				if (elementType != arrayType)
				{
					if (canModifyExistingType) arrayType.SetElementType(elementType);
					else arrayType = new ArrayType(elementType, arrayType.Rank);
				}
				return arrayType;
			}

			// resolve array
			if (type.IsPointer)
			{
				var pointerType = (PointerType)type;
				var elementType = ResolveCecilType(pointerType.ElementType, usedInType, canModifyExistingType);
				if (elementType != pointerType)
				{
					if (canModifyExistingType) pointerType.SetElementType(elementType);
					else pointerType = new PointerType(elementType);
				}
				return pointerType;
			}

			// resolve array
			if (type.IsByReference)
			{
				var byRefType = (ByReferenceType)type;
				var elementType = ResolveCecilType(byRefType.ElementType, usedInType, canModifyExistingType);
				if (elementType != byRefType)
				{
					if (canModifyExistingType) byRefType.SetElementType(elementType);
					else byRefType = new ByReferenceType(elementType);
				}
				return byRefType;
			}

			// resolve generic instance
			if (type.IsGenericInstance)
			{
				var g = (IGenericInstance)type;
				if (g.GenericArguments.Any((x => x.IsGenericParameter)))
				{
					var element = type.GetElementType();
					if (canModifyExistingType)
					{
						for (int i = 0; i != g.GenericArguments.Count; ++i)
						{
							var arg = g.GenericArguments[i];
							var argResolved = ResolveCecilType(arg, usedInType, canModifyExistingType);
							g.GenericArguments[i] = argResolved;
						}
					}
					else
					{
						var result = new GenericInstanceType(element);
						foreach (var arg in g.GenericArguments)
						{
							var argResolved = ResolveCecilType(arg, usedInType, canModifyExistingType);
							result.GenericArguments.Add(argResolved);
						}
						return result;
					}
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

		private TypeJit ResolveNormalType(TypeReference type)
		{
			var resolvedTypeJit = FindJitTypeRecursive(type);
			if (resolvedTypeJit == null)
			{
				var moduleJit = FindJitModuleRecursive(type.Module);
				if (moduleJit == null) throw new Exception("Failed to find jit module: " + type.Module.Name);
				resolvedTypeJit = new TypeJit(null, type, moduleJit);
				resolvedTypeJit.Jit();
			}
			return resolvedTypeJit;
		}

		internal TypeReference ResolveType(TypeReference type, TypeJit usedInType, MethodJit usedInMethod)
		{
			// resolve types with elements
			if (type.IsArray || type.IsByReference || type.IsPointer)
			{
				var elementType = ResolveType(type.GetElementType(), usedInType, usedInMethod);
				type.SetElementType(elementType);
				return type;
			}

			// resolve generic instance
			else if (type.IsGenericInstance)
			{
				var g = (IGenericInstance)type;
				if (g.GenericArguments.Any((x => x.IsGenericParameter)))
				{
					var element = type.GetElementType();
					for (int i = 0; i != g.GenericArguments.Count; ++i)
					{
						var arg = g.GenericArguments[i];
						var argResolved = ResolveType(arg, usedInType, usedInMethod);
						g.GenericArguments[i] = argResolved;
					}
				}
			}

			// resolve generic parameter
			else if (type.IsGenericParameter)
			{
				var genericParamArg = (GenericParameter)type;
				TypeReference argType;
				if (genericParamArg.Type == GenericParameterType.Type)
				{
					if (usedInType == null) throw new Exception("Trying to resolve generic type but the expected generic-parameter's type is null");
					var g = (IGenericInstance)usedInType.typeReference;
					argType = g.GenericArguments[genericParamArg.Position];
				}
				else if (genericParamArg.Type == GenericParameterType.Method)
                {
					if (usedInMethod == null) throw new Exception("Trying to resolve generic type but the expected generic-parameter's method is null");
					var g = (GenericInstanceMethod)usedInMethod.methodReference;
					argType = g.GenericArguments[genericParamArg.Position];
				}
				else
                {
					throw new NotImplementedException("Unsupported generic parameter type: " + genericParamArg.Type.ToString());
                }
				return ResolveType(argType, usedInType, usedInMethod);
			}

			// resolve normal type
			var resultJit = ResolveNormalType(type);
			return resultJit.typeReference;
		}
	}
}
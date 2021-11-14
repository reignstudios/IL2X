using IL2X.Core.Jit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IL2X.Core.Emitters
{
	public abstract class Emitter
	{
		public readonly Solution solution;

		public Emitter(Solution solution)
		{
			this.solution = solution;
		}

		public virtual void Translate(string outputDirectory)
		{
			if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);
			TranslateAssembly(solution.mainAssemblyJit, outputDirectory);
		}

		private void TranslateAssembly(AssemblyJit assembly, string outputDirectory)
		{
			foreach (var module in assembly.modules)
			{
				// translate references first
				foreach (var r in module.assemblyReferences)
				{
					TranslateAssembly(r, outputDirectory);
				}

				// translate this module
				outputDirectory = Path.Combine(outputDirectory, FormatTypeFilename(assembly.assembly.cecilAssembly.Name.Name));
				if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);
				TranslateModule(module, outputDirectory);
			}
		}

		protected abstract void TranslateModule(ModuleJit module, string outputDirectory);

		protected static string FormatTypeFilename(string filename)
		{
			return filename.Replace('.', '_').Replace('`', '_').Replace('<', '_').Replace('>', '_').Replace('/', '_');
		}

		protected static string GetScopeName(IMetadataScope scope)
		{
			return scope.Name.Replace(".dll", "").Replace('.', '_');
		}

		protected static TypeDefinition GetTypeDefinition(TypeReference type)
		{
			if (type.IsDefinition) return (TypeDefinition)type;
			var def = type.Resolve();
			if (def == null) throw new Exception("Failed to resolve type definition for reference: " + type.Name);
			return def;
		}

		protected static IMemberDefinition GetMemberDefinition(MemberReference member)
		{
			if (member.IsDefinition) return (IMemberDefinition)member;
			var def = member.Resolve();
			if (def == null) throw new Exception("Failed to resolve member definition for reference: " + member.Name);
			return def;
		}

		protected static int GetMethodOverloadIndex(MethodReference method)
		{
			int index = 0;
			var methodDef = GetMemberDefinition(method);
			foreach (var typeMethod in methodDef.DeclaringType.Methods)
			{
				if (typeMethod == methodDef) return index;
				if (typeMethod.Name == method.Name) ++index;
			}

			throw new Exception("Failed to find method index (this should never happen)");
		}

		protected static int GetVirtualMethodOverloadIndex(MethodDefinition method)
		{
			static int GetVirtualMethodOverloadIndex(TypeDefinition type, MethodDefinition methodSignature)
			{
				if (type.BaseType != null)
				{
					int index = GetVirtualMethodOverloadIndex(GetTypeDefinition(type.BaseType), methodSignature);
					if (index != -1) return index;
				}

				var paramaters = GetMethodParameterTypeReferences(methodSignature);
				foreach (var method in type.Methods)
				{
					if (!method.IsVirtual) continue;
					var foundMethod = FindMethodSignature(false, type, methodSignature.Name, paramaters);
					if (foundMethod != null) return GetMethodOverloadIndex(foundMethod);
				}

				return -1;
			}

			if (!method.IsVirtual) throw new Exception("Method must be virtual: " + method.FullName);
			int index = GetVirtualMethodOverloadIndex(method.DeclaringType, method);
			if (index != -1) return index;
			throw new Exception("Failed to find virtual method index (this should never happen)");
		}

		protected static MethodDefinition FindHighestVirtualMethodSlot(TypeDefinition type, MethodDefinition rootSlotMethodSignature)
		{
			MethodDefinition foundMethod;
			var paramaters = GetMethodParameterTypeReferences(rootSlotMethodSignature);
			var baseType = (TypeReference)type;
			do
			{
				var baseTypeDef = GetTypeDefinition(baseType);
				foundMethod = FindMethodSignature(false, baseTypeDef, rootSlotMethodSignature.Name, paramaters);
				if (foundMethod != null && foundMethod.IsVirtual) break;
				baseType = baseTypeDef.BaseType;
			} while (baseType != null);

			if (foundMethod == null) throw new Exception("Failed to find highest virtual method slot (this should never happen)");
			return foundMethod;
		}

		protected static TypeReference[] GetMethodParameterTypeReferences(MethodReference method)
		{
			var types = new TypeReference[method.Parameters.Count];
			for (int i = 0; i != types.Length; ++i) types[i] = method.Parameters[i].ParameterType;
			return types;
		}

		protected static MethodDefinition FindMethodSignature(bool constructor, TypeDefinition type, string methodName, params TypeReference[] paramaters)
		{
			foreach (var method in type.Methods)
			{
				if (method.IsConstructor != constructor || method.Name != methodName) continue;
				if (method.Parameters.Count != paramaters.Length) continue;
				bool found = true;
				for (int i = 0; i != paramaters.Length; ++i)
				{
					if (method.Parameters[i].ParameterType.FullName != paramaters[i].FullName)
					{
						found = false;
						break;
					}
				}

				if (found) return method;
			}

			return null;
		}

		protected static int GetBaseTypeCount(TypeDefinition type)
		{
			if (type.BaseType == null) return 0;
			return GetBaseTypeCount(GetTypeDefinition(type.BaseType)) + 1;
		}

		protected static bool HasBaseType(TypeDefinition type, TypeDefinition baseType)
		{
			var b = type.BaseType;
			while (b != null)
			{
				var bDef = GetTypeDefinition(b);
				if (bDef == baseType) return true;
				b = bDef.BaseType;
			}

			return false;
		}
	}
}

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;
using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace IL2X.Core
{
	public abstract class ILAssemblyTranslator : IDisposable
	{
		protected ILAssembly assembly;
		protected Stack<ILAssembly> allAssemblies;

		public ILAssemblyTranslator(string binaryPath, bool loadReferences, string[] searchPaths)
		{
			allAssemblies = new Stack<ILAssembly>();
			using (var assemblyResolver = new DefaultAssemblyResolver())
			{
				foreach (string path in searchPaths) assemblyResolver.AddSearchDirectory(path);
				assembly = new ILAssembly(allAssemblies, binaryPath, loadReferences, assemblyResolver);
			}
		}

		public void Dispose()
		{
			Utils.DisposeInstance(ref assembly);
		}

		public abstract void Translate(string outputPath, bool translateReferences);

		protected virtual string GetTypeDefinitionName(TypeDefinition type)
		{
			string result = type.Name;
			ParseMemberImplementationDetail(ref result);
			return result;
		}

		protected virtual string GetTypeReferenceName(TypeReference type)
		{
			string result = type.Name;
			ParseMemberImplementationDetail(ref result);
			return result;
		}

		protected virtual string GetTypeDefinitionFullName(TypeDefinition type, bool allowPrefix)
		{
			var result = new StringBuilder(type.Name);
			if (type.DeclaringType != null)
			{
				result.Insert(0, GetTypeDefinitionDelimiter());
				result.Insert(0, GetTypeDefinitionName(type.DeclaringType));
			}
			else if (!string.IsNullOrEmpty(type.Namespace))
			{
				result.Insert(0, GetTypeDefinitionDelimiter());
				result.Insert(0, GetMemberDefinitionNamespaceName(type.Namespace));
			}
			
			ParseMemberImplementationDetail(ref result);
			return result.ToString();
		}

		protected virtual string GetTypeReferenceFullName(TypeReference type, bool allowPrefix, bool allowSymbols)
		{
			var result = new StringBuilder(type.Name);
			if (type.DeclaringType != null)
			{
				result.Insert(0, GetTypeReferenceDelimiter());
				result.Insert(0, GetTypeReferenceName(type.DeclaringType));
			}
			else if (!string.IsNullOrEmpty(type.Namespace))
			{
				result.Insert(0, GetTypeReferenceDelimiter());
				result.Insert(0, GetMemberReferenceNamespaceName(type.Namespace));
			}
			
			ParseMemberImplementationDetail(ref result);
			return result.ToString();
		}

		protected virtual string GetTypeDefinitionDelimiter()
		{
			return ".";
		}

		protected virtual string GetTypeReferenceDelimiter()
		{
			return ".";
		}

		protected virtual string GetMethodDefinitionName(MethodDefinition method)
		{
			string result = method.Name;
			ParseMemberImplementationDetail(ref result);
			return result;
		}

		protected virtual string GetMethodReferenceName(MethodReference method)
		{
			string result = method.Name;
			ParseMemberImplementationDetail(ref result);
			return result;
		}

		protected virtual string GetMethodDefinitionFullName(MethodDefinition method)
		{
			var result = new StringBuilder(method.Name);
			if (method.DeclaringType != null)
			{
				result.Insert(0, GetMethodDefinitionDelimiter());
				result.Insert(0, GetTypeDefinitionFullName(method.DeclaringType, false));
			}

			ParseMemberImplementationDetail(ref result);
			return result.ToString();
		}

		protected virtual string GetMethodReferenceFullName(MethodReference method)
		{
			var result = new StringBuilder(method.Name);
			if (method.DeclaringType != null)
			{
				result.Insert(0, GetMethodReferenceDelimiter());
				result.Insert(0, GetTypeReferenceFullName(method.DeclaringType, false, false));
			}

			ParseMemberImplementationDetail(ref result);
			return result.ToString();
		}

		protected virtual string GetMethodDefinitionDelimiter()
		{
			return ".";
		}

		protected virtual string GetMethodReferenceDelimiter()
		{
			return ".";
		}

		protected virtual string GetMemberDefinitionNamespaceName(in string name)
		{
			return name;
		}

		protected virtual string GetMemberReferenceNamespaceName(in string name)
		{
			return name;
		}

		protected virtual string GetParameterDefinitionName(ParameterDefinition parameter)
		{
			string result = parameter.Name;
			ParseMemberImplementationDetail(ref result);
			return result;
		}

		protected virtual string GetParameterReferenceName(ParameterReference parameter)
		{
			string result = parameter.Name;
			ParseMemberImplementationDetail(ref result);
			return result;
		}

		protected virtual string GetFieldDefinitionName(FieldDefinition field)
		{
			string result = field.Name;
			ParseMemberImplementationDetail(ref result);
			return result;
		}

		protected virtual string GetFieldReferenceName(FieldReference field)
		{
			string result = field.Name;
			ParseMemberImplementationDetail(ref result);
			return result;
		}

		protected virtual string GetGenericParameterName(GenericParameter parameter)
		{
			return parameter.Name;
		}

		protected virtual string GetGenericParameters(IGenericParameterProvider generic)
		{
			var result = new StringBuilder(GetGenericParameterOpenBracket());
			var last = generic.GenericParameters.Last();
			foreach (var parameter in generic.GenericParameters)
			{
				result.Append(GetGenericParameterName(parameter));
				if (parameter != last) result.Append(GetGenericParameterDelimiter());
			}
			result.Append(GetGenericParameterCloseBracket());

			return result.ToString();
		}

		protected virtual char GetGenericParameterOpenBracket()
		{
			return '<';
		}

		protected virtual char GetGenericParameterCloseBracket()
		{
			return '>';
		}

		protected virtual char GetGenericParameterDelimiter()
		{
			return ',';
		}

		protected virtual string GetGenericArgumentTypeName(TypeReference type)
		{
			return GetTypeReferenceName(type);
		}

		protected virtual string GetGenericArguments(IGenericInstance generic)
		{
			var result = new StringBuilder(GetGenericArgumentOpenBracket());
			var last = generic.GenericArguments.Last();
			foreach (var argument in generic.GenericArguments)
			{
				result.Append(GetGenericArgumentTypeName(argument));
				if (argument != last) result.Append(GetGenericArgumentDelimiter());
			}
			result.Append(GetGenericArgumentCloseBracket());

			return result.ToString();
		}

		protected virtual char GetGenericArgumentOpenBracket()
		{
			return '<';
		}

		protected virtual char GetGenericArgumentCloseBracket()
		{
			return '>';
		}

		protected virtual char GetGenericArgumentDelimiter()
		{
			return ',';
		}

		protected void ParseMemberImplementationDetail(ref string elementName)
		{
			if (elementName.Contains('<')) elementName = elementName.Replace('<', '_');
			if (elementName.Contains('>')) elementName = elementName.Replace('>', '_');
			if (elementName.Contains('|')) elementName = elementName.Replace('|', '_');
			if (elementName.Contains('.')) elementName = elementName.Replace('.', '_');
			if (elementName.Contains('`')) elementName = elementName.Replace('`', '_');
		}

		protected void ParseMemberImplementationDetail(ref StringBuilder elementName)
		{
			if (elementName.Contains('<')) elementName = elementName.Replace('<', '_');
			if (elementName.Contains('>')) elementName = elementName.Replace('>', '_');
			if (elementName.Contains('|')) elementName = elementName.Replace('|', '_');
			if (elementName.Contains('.')) elementName = elementName.Replace('.', '_');
			if (elementName.Contains('`')) elementName = elementName.Replace('`', '_');
		}

		protected int GetMethodOverloadIndex(MethodReference method)
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

		private int GetVirtualMethodOverloadIndex(TypeDefinition type, MethodDefinition methodSignature)
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

		protected int GetVirtualMethodOverloadIndex(MethodDefinition method)
		{
			if (!method.IsVirtual) throw new Exception("Method must be virtual: " + method.FullName);
			int index = GetVirtualMethodOverloadIndex(method.DeclaringType, method);
			if (index != -1) return index;
			throw new Exception("Failed to find virtual method index (this should never happen)");
		}

		protected List<MethodDefinition> GetOrderedVirtualMethods(TypeDefinition type)
		{
			var virtualMethodList = new List<MethodDefinition>();
			var baseType = (TypeReference)type;
			do
			{
				var baseTypeDef = GetTypeDefinition(baseType);
				foreach (var method in baseTypeDef.Methods.Reverse())
				{
					if (!method.IsVirtual || method.IsReuseSlot) continue;
					virtualMethodList.Add(method);
				}
				baseType = baseTypeDef.BaseType;
			} while (baseType != null);

			return virtualMethodList;
		}

		protected MethodDefinition FindHighestVirtualMethodSlot(TypeDefinition type, MethodDefinition rootSlotMethodSignature)
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

		protected int GetBaseTypeCount(TypeDefinition type)
		{
			if (type.BaseType == null) return 0;
			return GetBaseTypeCount(GetTypeDefinition(type.BaseType)) + 1;
		}

		protected bool HasBaseType(TypeDefinition type, TypeDefinition baseType)
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

		protected TypeDefinition GetTypeDefinition(TypeReference type)
		{
			if (type.IsDefinition) return (TypeDefinition)type;
			var def = type.Resolve();
			if (def == null) throw new Exception("Failed to resolve type definition for reference: " + type.Name);
			return def;
		}

		protected MethodDefinition GetMethodDefinition(MethodReference method)
		{
			return (MethodDefinition)GetMemberDefinition(method);
		}

		protected IMemberDefinition GetMemberDefinition(MemberReference member)
		{
			if (member.IsDefinition) return (IMemberDefinition)member;
			var def = member.Resolve();
			if (def == null) throw new Exception("Failed to resolve member definition for reference: " + member.Name);
			return def;
		}

		protected bool IsFixedBufferType(TypeReference type)
		{
			return Regex.IsMatch(type.Name, @"<\w*>e__FixedBuffer");
		}

		protected TypeReference GetFixedBufferType(TypeDefinition type, out int fixedTypeSize)
		{
			var fixedType = type.Fields[0].FieldType;
			fixedTypeSize = type.ClassSize / GetPrimitiveSize(fixedType.MetadataType);
			return fixedType;
		}

		protected int GetPrimitiveSize(MetadataType type)
		{
			switch (type)
			{
				case MetadataType.Void: return 0;
				case MetadataType.Boolean: return sizeof(Boolean);
				case MetadataType.Char: return sizeof(Char);
				case MetadataType.SByte: return sizeof(SByte);
				case MetadataType.Byte: return sizeof(Byte);
				case MetadataType.Int16: return sizeof(Int16);
				case MetadataType.UInt16: return sizeof(UInt16);
				case MetadataType.Int32: return sizeof(Int32);
				case MetadataType.UInt32: return sizeof(UInt32);
				case MetadataType.Int64: return sizeof(Int64);
				case MetadataType.UInt64: return sizeof(UInt64);
				case MetadataType.Single: return sizeof(Single);
				case MetadataType.Double: return sizeof(Double);
			}

			throw new Exception("GetPrimitiveSize failed: Invalid primitive type: " + type);
		}

		protected bool IsAtomicType(TypeReference type)
		{
			var currentType = GetTypeDefinition(type);
			while (currentType != null)
			{
				foreach (var field in currentType.Fields)
				{
					if (!field.FieldType.IsValueType) return false;
				}
				if (currentType.BaseType != null) currentType = GetTypeDefinition(currentType.BaseType);
				else currentType = null;
			}

			return true;
		}

		protected bool IsEmptyType(TypeReference type, bool staticsDontCount = true)
		{
			var currentType = GetTypeDefinition(type);
			while (currentType != null)
			{
				if (currentType.HasFields)
				{
					if (staticsDontCount)
					{
						if (!currentType.Fields.All(x => x.IsStatic)) return false;
					}
					else
					{
						return false;
					}
				}
				if (currentType.BaseType != null) currentType = GetTypeDefinition(currentType.BaseType);
				else currentType = null;
			}

			return true;
		}

		protected TypeReference[] GetMethodParameterTypeReferences(MethodReference method)
		{
			var types = new TypeReference[method.Parameters.Count];
			for (int i = 0; i != types.Length; ++i) types[i] = method.Parameters[i].ParameterType;
			return types;
		}

		protected MethodDefinition FindMethodSignature(bool constructor, TypeDefinition type, string methodName, params TypeReference[] paramaters)
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
	}
}

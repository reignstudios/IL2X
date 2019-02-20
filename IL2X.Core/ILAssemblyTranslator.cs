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

		public abstract void Translate(string outputPath);

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

		private void ParseMemberImplementationDetail(ref string elementName)
		{
			if (elementName.Contains('<')) elementName = elementName.Replace('<', '_');
			if (elementName.Contains('>')) elementName = elementName.Replace('>', '_');
			if (elementName.Contains('|')) elementName = elementName.Replace('|', '_');
			if (elementName.Contains('.')) elementName = elementName.Replace('.', '_');
			if (elementName.Contains('`')) elementName = elementName.Replace('`', '_');
		}

		private void ParseMemberImplementationDetail(ref StringBuilder elementName)
		{
			if (elementName.Contains('<')) elementName = elementName.Replace('<', '_');
			if (elementName.Contains('>')) elementName = elementName.Replace('>', '_');
			if (elementName.Contains('|')) elementName = elementName.Replace('|', '_');
			if (elementName.Contains('.')) elementName = elementName.Replace('.', '_');
			if (elementName.Contains('`')) elementName = elementName.Replace('`', '_');
		}

		/*protected abstract string GetFullTypeName(TypeReference type, bool canWriteRefTypePtrSymbol);
		protected string GetFullTypeName(TypeReference type, string namespaceDelimiter, string nestedDelimiter, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts, bool writeGenericNameUnique)
		{
			var value = new StringBuilder();
			GetQualifiedTypeName(type, namespaceDelimiter, nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, writeGenericNameUnique, value, true);
			return value.ToString();
		}
		
		protected string GetNestedTypeName(TypeReference type, string nestedDelimiter, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts, bool writeGenericNameUnique)
		{
			if (!type.IsNested) return GetMemberName(type, nestedDelimiter, nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, writeGenericNameUnique);
			var value = new StringBuilder();
			GetQualifiedTypeName(type, nestedDelimiter, nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, writeGenericNameUnique, value, false);
			return value.ToString();
		}

		private void GetQualifiedTypeName(TypeReference type, in string namespaceDelimiter, in string nestedDelimiter, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts, bool writeGenericNameUnique, StringBuilder value, bool writeNamespace)
		{
			string name = GetMemberName(type, namespaceDelimiter, nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, writeGenericNameUnique);
			value.Insert(0, name);
			if (type.IsGenericParameter) return;
			if (type.DeclaringType != null)
			{
				value.Insert(0, nestedDelimiter);
				//genericOpenBracket = '_';
				//genericCloseBracket = '_';
				//genericDelimiter = '_';
				writeGenericParts = false;
				GetQualifiedTypeName(type.DeclaringType, namespaceDelimiter, nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, writeGenericNameUnique, value, writeNamespace);
			}
			else if (writeNamespace && !string.IsNullOrEmpty(type.Namespace))
			{
				value.Insert(0, type.Namespace.Replace(".", namespaceDelimiter) + namespaceDelimiter);
			}
		}
		
		protected string GetMemberName(MemberReference member, string namespaceDelimiter, string nestedDelimiter, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts, bool writeGenericNameUnique)
		{
			string memberName = member.Name;

			// strip method name mangle
			if (member is MethodDefinition)
			{
				var method = (MethodDefinition)member;
				if (method.IsFinal)
				{
					var parts = method.Name.Split('.');
					memberName = parts[parts.Length - 1];
				}
			}

			// handle generated implementation details
			ParseMemberImplementationDetail(ref memberName);

			// handle generic names
			if (member is IGenericInstance)
			{
				var generic = (IGenericInstance)member;
				if (generic.HasGenericArguments)// && memberName.Contains('`'))
				{
					return ResolveGenericName(member, memberName, generic.GenericArguments, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, writeGenericNameUnique && memberName.Contains('`'));
				}
			}
			else if (member is IGenericParameterProvider)
			{
				var generic = (IGenericParameterProvider)member;
				if (generic.HasGenericParameters && memberName.Contains('`'))
				{
					return ResolveGenericName(member, memberName, generic.GenericParameters, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, writeGenericNameUnique);
				}
			}

			return memberName;
		}

		protected virtual string GetParameterName(ParameterReference parameter)
		{
			string parameterName = parameter.Name;
			ParseMemberImplementationDetail(ref parameterName);
			return parameterName;
		}
		
		private string ResolveGenericName<T>(MemberReference member, string memberName, Mono.Collections.Generic.Collection<T> collection, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts, bool writeGenericNameUnique) where T : TypeReference
		{
			var match = Regex.Match(memberName, @"(\w*)`\d*");
			StringBuilder name;
			if (match.Success) name = new StringBuilder(match.Groups[1].Value);//throw new Exception("Failed to remove generic name tick: " + memberName);
			else name = new StringBuilder(memberName);

			// append generic name
			if (writeGenericNameUnique)
			{
				TypeDefinition type = null;
				if (member is TypeDefinition)
				{
					type = (TypeDefinition)member;
				}
				else if (member is TypeReference)
				{
					var typeRef = (TypeReference)member;
					if (!typeRef.IsDefinition)
					{
						type = typeRef.Resolve();
						if (type == null) throw new Exception("Unable to resolve generic type: " + member.Name);
					}
				}

				var lastParameter = type.GenericParameters.LastOrDefault();
				foreach (var argument in type.GenericParameters)
				{
					string argName = argument.Name;
					ParseMemberImplementationDetail(ref argName);
					name.Append($"_{argName}");
					if (argument == lastParameter) name.Append('_');
				}
			}

			// write generic parts
			if (writeGenericParts)
			{
				name.Append(genericOpenBracket);
				int i = 0;
				int count = collection.Count;
				foreach (var item in collection)
				{
					name.Append(GetFullTypeName(item, true));
					++i;
					if (i != count) name.Append(genericDelimiter);// must check via count as types could match
				}
				name.Append(genericCloseBracket);
			}

			return name.ToString();
		}*/

		protected int GetBaseTypeCount(TypeDefinition type)
		{
			if (type.BaseType == null) return 0;
			return GetBaseTypeCount(GetTypeDefinition(type.BaseType)) + 1;
		}

		protected TypeDefinition GetTypeDefinition(TypeReference type)
		{
			if (type.IsDefinition) return (TypeDefinition)type;
			var def = type.Resolve();
			if (def == null) throw new Exception("Failed to resolve type definition for reference: " + type.Name);
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

		protected List<TypeReference> GetAllElementTypes(TypeReference type)
		{
			var types = new List<TypeReference>();
			var elementType = type;
			while (true)
			{
				if (elementType.IsByReference)
				{
					var specialType = (ByReferenceType)elementType;
					elementType = specialType.ElementType;
					continue;
				}
				else if (elementType.IsArray)
				{
					var specialType = (ArrayType)elementType;
					elementType = specialType.ElementType;
					continue;
				}
				else if (elementType.IsPointer)
				{
					var specialType = (PointerType)elementType;
					elementType = specialType.ElementType;
					continue;
				}
				else if (elementType.IsRequiredModifier)
				{
					var specialType = (RequiredModifierType)elementType;
					elementType = specialType.ElementType;
					continue;
				}
				else if (IsFixedBufferType(elementType))
				{
					elementType = GetFixedBufferType((TypeDefinition)elementType, out _);
					continue;
				}
				else if (elementType.IsGenericInstance)
				{
					types.Add(elementType);
					var specialType = (GenericInstanceType)elementType;
					if (specialType.HasGenericArguments)
					{
						foreach (var argType in specialType.GenericArguments)
						{
							var argTypeElements = GetAllElementTypes(argType);
							types.AddRange(argTypeElements);
						}
					}
					elementType = specialType.ElementType;
					continue;
				}

				if (!elementType.HasGenericParameters) types.Add(elementType);
				break;
			}

			return types;
		}

		protected List<TypeReference> GetAllUsedPublicTypes(TypeDefinition type)
		{
			var types = new List<TypeReference>();
			void AddElementTypes(TypeReference elementTypeRoot)
			{
				var elementTypes = GetAllElementTypes(elementTypeRoot);
				foreach (var elementType in elementTypes)
				{
					if (!types.Exists(x => x.FullName == elementType.FullName)) types.Add(elementType);
				}
			}

			foreach (var field in type.Fields)
			{
				AddElementTypes(field.FieldType);
			}

			foreach (var method in type.Methods)
			{
				AddElementTypes(method.ReturnType);
				foreach (var parameter in method.Parameters)
				{
					AddElementTypes(parameter.ParameterType);
				}
			}

			return types;
		}

		protected bool HasInterfaceTypeRecursive(TypeDefinition type, TypeReference interfaceType)
		{
			foreach (var i in type.Interfaces)
			{
				if (i.InterfaceType.FullName == interfaceType.FullName) return true;
			}

			if (type.BaseType != null)
			{
				var resolvedType = type.BaseType;
				if (!resolvedType.IsDefinition) resolvedType = resolvedType.Resolve();
				return HasInterfaceTypeRecursive((TypeDefinition)resolvedType, interfaceType);
			}

			return false;
		}
	}
}

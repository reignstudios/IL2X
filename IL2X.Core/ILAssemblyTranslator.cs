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

		public ILAssemblyTranslator(string binaryPath, bool loadReferences, params string[] searchPaths)
		{
			allAssemblies = new Stack<ILAssembly>();
			assembly = new ILAssembly(allAssemblies, binaryPath, loadReferences, searchPaths);
		}

		public void Dispose()
		{
			Utils.DisposeInstance(ref assembly);
		}

		public abstract void Translate(string outputPath);

		protected abstract string GetFullTypeName(TypeReference type, bool isBaseType);
		protected string GetFullTypeName(TypeReference type, string namespaceDelimiter, string nestedDelimiter, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts, bool writeGenericNameUnique)
		{
			var value = new StringBuilder();
			GetQualifiedTypeName(type, ref namespaceDelimiter, ref nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, writeGenericNameUnique, value, true);
			return value.ToString();
		}
		
		protected string GetNestedTypeName(TypeReference type, string nestedDelimiter, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts, bool writeGenericNameUnique)
		{
			if (!type.IsNested) return GetMemberName(type, nestedDelimiter, nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, writeGenericNameUnique);
			var value = new StringBuilder();
			GetQualifiedTypeName(type, ref nestedDelimiter, ref nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, writeGenericNameUnique, value, false);
			return value.ToString();
		}

		private void GetQualifiedTypeName(TypeReference type, ref string namespaceDelimiter, ref string nestedDelimiter, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts, bool writeGenericNameUnique, StringBuilder value, bool writeNamespace)
		{
			string name = GetMemberName(type, namespaceDelimiter, nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, writeGenericNameUnique);
			value.Insert(0, name);
			if (type.IsGenericParameter) return;
			if (type.DeclaringType != null)
			{
				value.Insert(0, nestedDelimiter);
				GetQualifiedTypeName(type.DeclaringType, ref namespaceDelimiter, ref nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, writeGenericNameUnique, value, writeNamespace);
			}
			else if (writeNamespace && !string.IsNullOrEmpty(type.Namespace))
			{
				value.Insert(0, type.Namespace.Replace(".", namespaceDelimiter) + namespaceDelimiter);
			}
		}

		protected bool IsFixedBufferType(TypeReference type)
		{
			return Regex.IsMatch(type.Name, @"<\w*>e__FixedBuffer");
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
				if (generic.HasGenericArguments && memberName.Contains('`'))
				{
					return ResolveGenericName(member, memberName, generic.GenericArguments, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, writeGenericNameUnique);
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

		private void ParseMemberImplementationDetail(ref string memberName)
		{
			var match = Regex.Match(memberName, @"<(.*)>(.*)");
			if (match.Success)
			{
				if (string.IsNullOrEmpty(match.Groups[1].Value)) memberName = $"__{match.Groups[2].Value}";
				else memberName = $"__{match.Groups[1].Value}_{match.Groups[2].Value}";
				ParseMemberImplementationDetail(ref memberName);
				if (memberName.Contains('|')) memberName = memberName.Replace('|', '_');
			}
		}

		private string ResolveGenericName<T>(MemberReference member, string memberName, Mono.Collections.Generic.Collection<T> collection, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts, bool writeGenericNameUnique) where T : TypeReference
		{
			var match = Regex.Match(memberName, @"(\w*)`\d*");
			if (!match.Success) throw new Exception("Failed to remove generic name tick: " + memberName);
			var name = new StringBuilder(match.Groups[1].Value);

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
				var lastItem = collection.LastOrDefault();
				foreach (var item in collection)
				{
					name.Append(GetFullTypeName(item, false));
					if (item != lastItem) name.Append(genericDelimiter);
				}
				name.Append(genericCloseBracket);
			}

			return name.ToString();
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

		protected List<TypeReference> GetAllUsedTypeTypes(TypeDefinition type)
		{
			var types = new List<TypeReference>();
			foreach (var field in type.Fields)
			{
				if (!types.Exists(x => x.FullName == field.FieldType.FullName)) types.Add(field.FieldType);
			}

			foreach (var method in type.Methods)
			{
				if (!types.Exists(x => x.FullName == method.ReturnType.FullName)) types.Add(method.ReturnType);
				foreach (var parameter in method.Parameters)
				{
					if (!types.Exists(x => x.FullName == parameter.ParameterType.FullName)) types.Add(parameter.ParameterType);
				}
			}

			return types;
		}
	}
}

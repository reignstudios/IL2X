using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;
using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace IL2X.Core
{
	public abstract class ILTranslator : IDisposable
	{
		protected DefaultAssemblyResolver assemblyResolver;
		protected AssemblyDefinition assemblyDefinition;
		protected ISymbolReader symbolReader;

		public ILTranslator(string binaryPath, params string[] searchPaths)
		{
			// create assebly resolver object
			var readerParameters = new ReaderParameters();
			assemblyResolver = new DefaultAssemblyResolver();
			readerParameters.AssemblyResolver = assemblyResolver;

			// add resolver paths
			foreach (string path in searchPaths) assemblyResolver.AddSearchDirectory(path);

			// read debug symbol file
			string symbolsPath = Path.Combine(Path.GetDirectoryName(binaryPath), Path.GetFileNameWithoutExtension(binaryPath) + ".pdb");
			if (File.Exists(symbolsPath))
			{
				// load assembly
				var symbolReaderProvider = new PdbReaderProvider();
				readerParameters.SymbolReaderProvider = symbolReaderProvider;
				readerParameters.ReadSymbols = true;
				assemblyDefinition = AssemblyDefinition.ReadAssembly(binaryPath, readerParameters);

				// load symbols
				symbolReader = symbolReaderProvider.GetSymbolReader(assemblyDefinition.MainModule, symbolsPath);
			}
			else
			{
				assemblyDefinition = AssemblyDefinition.ReadAssembly(binaryPath, readerParameters);
			}
		}

		public void Dispose()
		{
			Utils.DisposeInstance(ref symbolReader);
			Utils.DisposeInstance(ref assemblyDefinition);
			Utils.DisposeInstance(ref assemblyResolver);
		}

		public abstract void Translate(string outputPath, bool translateReferences);

		protected abstract string GetFullTypeName(TypeReference type);
		protected string GetFullTypeName(TypeReference type, string namespaceDelimiter, string nestedDelimiter, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts)
		{
			var value = new StringBuilder();
			GetQualifiedTypeName(type, ref namespaceDelimiter, ref nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, value, true);
			return value.ToString();
		}

		protected abstract string GetNestedTypeName(TypeReference type);
		protected string GetNestedTypeName(TypeReference type, string nestedDelimiter, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts)
		{
			if (!type.IsNested) return GetMemberName(type, nestedDelimiter, nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts);
			var value = new StringBuilder();
			GetQualifiedTypeName(type, ref nestedDelimiter, ref nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, value, false);
			return value.ToString();
		}

		private void GetQualifiedTypeName(TypeReference type, ref string namespaceDelimiter, ref string nestedDelimiter, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts, StringBuilder value, bool writeNamespace)
		{
			string name = GetMemberName(type, namespaceDelimiter, nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts);
			value.Insert(0, name);
			if (type.IsGenericParameter) return;
			if (type.DeclaringType != null)
			{
				value.Insert(0, nestedDelimiter);
				GetQualifiedTypeName(type.DeclaringType, ref namespaceDelimiter, ref nestedDelimiter, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts, value, writeNamespace);
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

		protected abstract string GetMemberName(MemberReference member);
		protected string GetMemberName(MemberReference member, string namespaceDelimiter, string nestedDelimiter, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts)
		{
			string memberName = member.Name;
			ParseMemberImplementationDetail(ref memberName);

			if (member is IGenericInstance)
			{
				var generic = (IGenericInstance)member;
				if (generic.HasGenericArguments && memberName.Contains('`'))
				{
					return ResolveGenericName(memberName, generic.GenericArguments, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts);
				}
			}
			else if (member is IGenericParameterProvider)
			{
				var generic = (IGenericParameterProvider)member;
				if (generic.HasGenericParameters && memberName.Contains('`'))
				{
					return ResolveGenericName(memberName, generic.GenericParameters, genericOpenBracket, genericCloseBracket, genericDelimiter, writeGenericParts);
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

		private string ResolveGenericName<T>(string memberName, Mono.Collections.Generic.Collection<T> collection, char genericOpenBracket, char genericCloseBracket, char genericDelimiter, bool writeGenericParts) where T : TypeReference
		{
			var match = Regex.Match(memberName, @"(\w*)`\d*");
			if (!match.Success) throw new Exception("Failed to remove generic name tick: " + memberName);
			var name = new StringBuilder(match.Groups[1].Value);
			if (writeGenericParts)
			{
				name.Append(genericOpenBracket);
				var lastItem = collection.LastOrDefault();
				foreach (var item in collection)
				{
					name.Append(GetFullTypeName(item));
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
	}
}

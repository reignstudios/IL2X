using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;
using System;
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

		protected string GetFullNameFlat(TypeReference type, string namespaceDelimiter, string nestedDelimiter)
		{
			var value = new StringBuilder();
			GetQualifiedNameFlat(type, ref namespaceDelimiter, ref nestedDelimiter, value, true);
			return value.ToString();
		}

		protected string GetNestedNameFlat(TypeReference type, string nestedDelimiter)
		{
			if (!type.IsNested) return type.Name;
			var value = new StringBuilder();
			GetQualifiedNameFlat(type, ref nestedDelimiter, ref nestedDelimiter, value, false);
			return value.ToString();
		}

		private void GetQualifiedNameFlat(TypeReference type, ref string namespaceDelimiter, ref string nestedDelimiter, StringBuilder value, bool writeNamespace)
		{
			string name;
			if (ParseMemberImplementationDetail(type, out string fieldName, out string detailName))
			{
				if (string.IsNullOrEmpty(fieldName)) name = $"__{detailName}";
				else name = $"__{detailName}_{fieldName}";
			}
			else
			{
				name = type.Name;
			}

			value.Insert(0, name);
			if (type.DeclaringType != null)
			{
				value.Insert(0, nestedDelimiter);
				GetQualifiedNameFlat(type.DeclaringType, ref namespaceDelimiter, ref nestedDelimiter, value, writeNamespace);
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

		protected string GetmemberName(MemberReference member)
		{
			if (ParseMemberImplementationDetail(member, out string fieldName, out string detailName))
			{
				if (string.IsNullOrEmpty(fieldName)) return $"__{detailName}";
				else return $"__{detailName}_{fieldName}";
			}
			else
			{
				return member.Name;
			}
		}

		protected bool ParseMemberImplementationDetail(MemberReference member, out string fieldName, out string detailName)
		{
			var match = Regex.Match(member.Name, @"<(\w*)>(\w*)");
			if (match.Success)
			{
				fieldName = match.Groups[1].Value;
				detailName = match.Groups[2].Value;
				return true;
			}
			else
			{
				fieldName = null;
				detailName = null;
				return false;
			}
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

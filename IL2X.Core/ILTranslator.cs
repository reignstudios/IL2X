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

		protected void GetQualifiedNameFlat(TypeReference type, ref string namespaceDelimiter, ref string nestedDelimiter, StringBuilder value, bool writeNamespace)
		{
			string name;
			if (IsAnonymousType(type))
			{
				ParseAnonymousType(type, out int index);
				name = $"f__AnonymousType_{index}";
			}
			else if (IsGeneratedType(type))
			{
				ParseGeneratedType(type, out string methodName, out int index);
				name = $"d__{methodName}_{index}";
			}
			else if (IsDisplayClassType(type))
			{
				ParseDisplayClassType(type, out int index);
				name = $"c__DisplayClass_{index}";
			}
			else if (IsFinishReadAsyncType(type))
			{
				ParseFinishReadAsyncType(type, out string methodName, out int index);
				name = $"g__{methodName}_{index}";
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

		protected bool IsAnonymousType(TypeReference type)
		{
			return type.Name.StartsWith("<>f__AnonymousType");
		}

		protected void ParseAnonymousType(TypeReference type, out int index)
		{
			var match = Regex.Match(type.Name, @"<>f__AnonymousType(\d*)`(\d*)");
			if (match.Success)
			{
				if (int.TryParse(match.Groups[1].Value, out int parsedIndex)) index = parsedIndex;
				else index = 0;
			}
			else
			{
				throw new Exception("Failed to parse f__AnonymousType");
			}
		}

		protected bool IsGeneratedType(TypeReference type)
		{
			return Regex.IsMatch(type.Name, @"<.*>d__\d*");
		}

		protected void ParseGeneratedType(TypeReference type, out string methodName, out int index)
		{
			var match = Regex.Match(type.Name, @"<(.*)>d__(\d*)");
			if (match.Success)
			{
				methodName = match.Groups[1].Value;
				if (int.TryParse(match.Groups[2].Value, out int parsedIndex)) index = parsedIndex;
				else index = 0;
			}
			else
			{
				throw new Exception("Failed to parse d__");
			}
		}

		protected bool IsDisplayClassType(TypeReference type)
		{
			return Regex.IsMatch(type.Name, @"<>c__DisplayClass\d*");
		}

		protected void ParseDisplayClassType(TypeReference type, out int index)
		{
			var match = Regex.Match(type.Name, @"<>c__DisplayClass(\d*)");
			if (match.Success)
			{
				if (int.TryParse(match.Groups[1].Value, out int parsedIndex)) index = parsedIndex;
				else index = 0;
			}
			else
			{
				throw new Exception("Failed to parse c__DisplayClass");
			}
		}

		protected bool IsFinishReadAsyncType(TypeReference type)
		{
			return Regex.IsMatch(type.Name, @"<<.*>g__FinishReadAsync\|\d*_0>d");
		}

		protected void ParseFinishReadAsyncType(TypeReference type, out string methodName, out int index)
		{
			var match = Regex.Match(type.Name, @"<<(.*)>g__FinishReadAsync\|(\d*)_0>d");
			if (match.Success)
			{
				methodName = match.Groups[1].Value;
				if (int.TryParse(match.Groups[2].Value, out int parsedIndex)) index = parsedIndex;
				else index = 0;
			}
			else
			{
				throw new Exception("Failed to parse c__DisplayClass");
			}
		}

		protected bool IsFixedBufferType(TypeReference type)
		{
			return Regex.IsMatch(type.Name, "<.*>e__FixedBuffer");
		}

		protected void ParseFixedBufferType(TypeDefinition type, out FieldDefinition fieldType, out string fieldName)
		{
			if (!type.HasFields) throw new Exception("e__FixedBuffer has no fields");
			var match = Regex.Match(type.Name, @"<(.*)>e__FixedBuffer");
			if (match.Success)
			{
				fieldType = type.Fields[0];
				fieldName = match.Groups[1].Value;
			}
			else
			{
				throw new Exception("Failed to parse e__FixedBuffer");
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

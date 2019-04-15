using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using IL2X.Core.EvaluationStack;

namespace IL2X.Core
{
	public sealed class ILAssemblyTranslator_C : ILAssemblyTranslator
	{
		public enum GC_Type
		{
			/// <summary>
			/// Modern platforms. Thread safe.
			/// </summary>
			Boehm,

			/// <summary>
			/// Legacy or unsupported Boehm platforms. Not thread safe.
			/// </summary>
			Portable,

			/// <summary>
			/// Super low memory or embedded devices. Not thread safe.
			/// </summary>
			Micro
		}

		public enum Ptr_Size
		{
			Bit_8 = 1,
			Bit_16 = 2,
			Bit_32 = 4,
			Bit_64 = 8,
			Bit_128 = 16
		}

		public enum Endianness
		{
			Little,
			Big
		}

		public enum StringLiteralMemoryLocation
		{
			/// <summary>
			/// Stores string literals in shared global memory (RAM).
			/// This allows for runtime type info such as: "Abc".GetType()
			/// </summary>
			GlobalProgramMemory_RAM,

			/// <summary>
			/// Stores string literals in AVR program flash memory.
			/// Runtime type info will not work: "Abc".GetType() = null ref error
			/// </summary>
			ReadonlyProgramMemory_AVR
		}

		public struct Options
		{
			/// <summary>
			/// Target Garbage Collector
			/// This will generate the proper C #includes
			/// </summary>
			public GC_Type gc;

			/// <summary>
			/// Garbage Collector file path
			/// </summary>
			public string gcFolderPath;

			/// <summary>
			/// Native pointer size in bits (native int)
			/// </summary>
			public Ptr_Size ptrSize;

			/// <summary>
			/// CPU bit order
			/// </summary>
			public Endianness endianness;

			/// <summary>
			/// Whether or not to store runtime type string literal metadata.
			/// Metadata such as Type.FullName etc
			/// </summary>
			public bool storeRuntimeTypeStringLiteralMetadata;

			/// <summary>
			/// Memory location string literals are stored
			/// </summary>
			public StringLiteralMemoryLocation stringLiteralMemoryLocation;

			/// <summary>
			/// Ignore '.locals init' IL on methods in all libraries for better performance.
			/// This can be normally be done on projects compiled with C#
			/// </summary>
			public bool ignoreInitLocalsOnAllLibs;

			/// <summary>
			/// Ignore '.locals init' IL on methods in specific libraries for better performance.
			/// This can be normally be done on projects compiled with C#
			/// </summary>
			public string[] ignoreInitLocalsLibs;
		}

		public readonly Options options;

		private StreamWriterEx writer;
		private ILAssembly activeAssembly, activeCoreAssembly;// current assembly being translated
		private ILModule activeModule;// current module being translated
		private MethodDebugInformation activeMethodDebugInfo;// current method debug symbols
		private Dictionary<string, string> activeStringLiterals;// current module string literals
		private Dictionary<string, string> allStringLiterals;// multi-lib string literal values (active and dependancy lib string literals)

		#region Core dependency resolution
		public ILAssemblyTranslator_C(string binaryPath, bool loadReferences, in Options options, params string[] searchPaths)
		: base(binaryPath, loadReferences, searchPaths)
		{
			this.options = options;
			if (options.gcFolderPath == null) this.options.gcFolderPath = string.Empty;
		}

		public override void Translate(string outputPath, bool translateReferences)
		{
			allStringLiterals = new Dictionary<string, string>();
			TranslateAssembly(assembly, outputPath, translateReferences, new List<ILAssembly>());
		}

		private void TranslateAssembly(ILAssembly assembly, string outputPath, bool translateReferences, List<ILAssembly> translatedAssemblies)
		{
			// validate assembly wasn't already translated
			if (translatedAssemblies.Exists(x => x.assemblyDefinition.FullName == assembly.assemblyDefinition.FullName)) return;
			translatedAssemblies.Add(assembly);

			// translate all modules into C assmebly files
			foreach (var module in assembly.modules)
			{
				// translate reference assemblies
				if (translateReferences)
				{
					foreach (var reference in module.references) TranslateAssembly(reference, outputPath, translateReferences, translatedAssemblies);
				}

				// create output folder
				if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

				// translate module
				activeAssembly = assembly;
				TranslateModule(module, outputPath);
			}
		}

		private void TranslateModule(ILModule module, string outputPath)
		{
			activeModule = module;
			activeCoreAssembly = module.GetCoreLib();

			if (activeStringLiterals == null) activeStringLiterals = new Dictionary<string, string>();
			else activeStringLiterals.Clear();

			// get module filename
			string modulePath = Path.Combine(outputPath, module.moduleDefinition.Name.Replace('.', '_'));
			bool isExe = module.moduleDefinition.IsMain && (module.moduleDefinition.Kind == ModuleKind.Console || module.moduleDefinition.Kind == ModuleKind.Windows);
			if (isExe) modulePath += ".c";
			else modulePath += ".h";

			// get generated members filename
			string generatedMembersFilename = $"{module.moduleDefinition.Name.Replace('.', '_')}_GeneratedMembers.h";
			string generatedMembersInitMethod = "IL2X_Init_GeneratedMembers_" + module.moduleDefinition.Name.Replace('.', '_');
			string initModuleMethod = "IL2X_InitModule_" + module.moduleDefinition.Name.Replace('.', '_');

			// get string type
			var stringType = FindTypeDefinitionByFullName("System.String");
			if (stringType == null) throw new Exception("Failed to get 'System.String' from CoreLib");
			string stringTypeName = GetTypeDefinitionFullName(stringType);
			string stringTypeRuntimeTypeName = GetRuntimeTypeReferenceFullName(stringType);

			// get object type
			var objectType = FindTypeDefinitionByFullName("System.Object");

			// get runtime type
			var rtType = FindTypeDefinitionByFullName("System.RuntimeType");
			string rtTypeFullName = GetTypeDefinitionFullName(rtType);
			var rtTypeBaseTypeFieldName = GetFieldDefinitionName(rtType.Fields.First(x => x.Name == "_baseType"));
			var rtTypeFullNameFieldName = GetFieldDefinitionName(rtType.Fields.First(x => x.Name == "_name"));
			var rtTypeFullNameFieldFullName = GetFieldDefinitionName(rtType.Fields.First(x => x.Name == "_fullName"));

			// write module
			using (var stream = new FileStream(modulePath, FileMode.Create, FileAccess.Write, FileShare.Read))
			using (writer = new StreamWriterEx(stream))
			{
				// write generate info
				writer.WriteLine("/* ############################### */");
				writer.WriteLine($"/* Generated with IL2X v{Utils.GetAssemblyInfoVersion()} */");
				writer.WriteLine("/* ############################### */");
				if (!isExe) writer.WriteLine("#pragma once");

				// write includes of core lib
				if (module.assembly.isCoreLib)
				{
					// write include of gc to be used
					string gcFileName;
					switch (options.gc)
					{
						case GC_Type.Boehm: gcFileName = "IL2X.GC.Boehm"; break;
						case GC_Type.Portable: gcFileName = "IL2X.GC.Portable"; break;
						case GC_Type.Micro: gcFileName = "IL2X.GC.Micro"; break;
						default: throw new Exception("Unsupported GC option: " + options.gc);
					}
					
					gcFileName = Path.Combine(Path.GetFullPath(options.gcFolderPath), gcFileName);
					writer.WriteLine($"#include \"{gcFileName}.h\"");
					writer.WriteLine($"#include \"{Path.Combine(Path.GetFullPath(options.gcFolderPath), "IL2X.InstructionHelpers.h")}\"");

					// include std libraries
					writer.WriteLine("#include <stdio.h>");
					writer.WriteLine("#include <math.h>");
					writer.WriteLine("#include <stdint.h>");
					writer.WriteLine("#include <uchar.h>");
					writer.WriteLine("#include <locale.h>");
					if (options.stringLiteralMemoryLocation == StringLiteralMemoryLocation.ReadonlyProgramMemory_AVR) writer.WriteLine("#include <avr/pgmspace.h>");
				}

				// write includes of dependencies
				foreach (var assemblyReference in module.references)
				foreach (var moduleReference in assemblyReference.modules)
				{
					writer.WriteLine($"#include \"{moduleReference.moduleDefinition.Name.Replace('.', '_')}.h\"");
				}
				writer.WriteLine();

				// write forward declare of types
				writer.WriteLine("/* =============================== */");
				writer.WriteLine("/* Type forward declares */");
				writer.WriteLine("/* =============================== */");
				foreach (var type in module.typesDependencyOrdered) WriteTypeDefinition(type, false);
				writer.WriteLine();

				// write type definitions
				writer.WriteLine("/* =============================== */");
				writer.WriteLine("/* Type definitions */");
				writer.WriteLine("/* =============================== */");
				foreach (var type in module.typesDependencyOrdered) WriteTypeDefinition(type, true);

				// write type definitions
				writer.WriteLine("/* =============================== */");
				writer.WriteLine("/* Runtime Types */");
				writer.WriteLine("/* =============================== */");
				foreach (var type in module.typesDependencyOrdered) WriteRuntimeTypeDefinition(type);
				if (module.assembly.isCoreLib)
				{
					writer.WriteLine($"char IL2X_IsType({rtTypeFullName}* runtimeType, {rtTypeFullName}* isRuntimeType)");
					writer.WriteLine('{');
					writer.AddTab();
					writer.WriteLinePrefix($"{rtTypeFullName}* runtimeTypeBase = runtimeType;");
					writer.WriteLinePrefix("while (runtimeTypeBase != 0)");
					writer.WriteLinePrefix('{');
					writer.AddTab();
					writer.WriteLinePrefix("if (runtimeTypeBase == isRuntimeType) return 1;");
					writer.WriteLinePrefix($"runtimeTypeBase = runtimeTypeBase->{rtTypeBaseTypeFieldName};");
					writer.RemoveTab();
					writer.WriteLinePrefix('}');
					writer.WriteLinePrefix("return 0;");
					writer.RemoveTab();
					writer.WriteLine('}');
				}

				// write forward declare of type methods
				writer.WriteLine("/* =============================== */");
				writer.WriteLine("/* Method forward declares */");
				writer.WriteLine("/* =============================== */");
				foreach (var type in module.typesDependencyOrdered)
				{
					foreach (var method in type.Methods) WriteMethodDefinition(method, false);
				}
				writer.WriteLine();

				// write include of IL instruction generated members
				writer.WriteLine("/* =============================== */");
				writer.WriteLine("/* Instruction generated members */");
				writer.WriteLine("/* =============================== */");
				writer.WriteLine($"#include \"{generatedMembersFilename}\"");
				writer.WriteLine();

				// write method definitions
				writer.WriteLine("/* =============================== */");
				writer.WriteLine("/* Method definitions */");
				writer.WriteLine("/* =============================== */");
				foreach (var type in module.typesDependencyOrdered)
				{
					foreach (var method in type.Methods) WriteMethodDefinition(method, true);
				}

				// write init module
				writer.WriteLine("/* =============================== */");
				writer.WriteLine("/* Init module */");
				writer.WriteLine("/* =============================== */");
				writer.WriteLine($"void {initModuleMethod}()");
				writer.WriteLine('{');
				writer.AddTab();
				writer.WriteLinePrefix("/* Init references */");
				foreach (var reference in module.references)
				{
					foreach (var refModule in reference.modules)
					{
						writer.WriteLinePrefix($"IL2X_InitModule_{refModule.moduleDefinition.Name.Replace('.', '_')}();");
					}
				}
				writer.WriteLine();

				writer.WriteLinePrefix("/* Init runtime type metadata */");
				foreach (var type in module.typesDependencyOrdered)
				{
					if (type == rtType) continue;

					string runtimeTypeName = GetRuntimeTypeReferenceFullName(type);
					writer.WriteLinePrefix($"memset(&{runtimeTypeName}, 0, sizeof({GetRuntimeTypeDefinitionFullName(type)}));");
					writer.WriteLinePrefix($"{runtimeTypeName}.runtimeType.IL2X_RuntimeType = &{runtimeTypeName};");
					if (type.BaseType != null) writer.WriteLinePrefix($"{runtimeTypeName}.runtimeType.{rtTypeBaseTypeFieldName} = &{GetRuntimeTypeReferenceFullName(GetTypeDefinition(type.BaseType))};");

					string name = GetRuntimeTypeMetadataFullName(type) + "_Name";
					string fullname = GetRuntimeTypeMetadataFullName(type) + "_FullName";
					if (options.storeRuntimeTypeStringLiteralMetadata && options.stringLiteralMemoryLocation == StringLiteralMemoryLocation.GlobalProgramMemory_RAM)
					{
						writer.WriteLinePrefix($"(({stringTypeName}*){name})->IL2X_RuntimeType = &{stringTypeRuntimeTypeName};");
						writer.WriteLinePrefix($"(({stringTypeName}*){fullname})->IL2X_RuntimeType = &{stringTypeRuntimeTypeName};");
					}
					writer.WriteLinePrefix($"{runtimeTypeName}.runtimeType.{rtTypeFullNameFieldName} = {name};");
					writer.WriteLinePrefix($"{runtimeTypeName}.runtimeType.{rtTypeFullNameFieldFullName} = {fullname};");
				}
				writer.WriteLine();

				writer.WriteLinePrefix("/* Init runtime type vtabel */");
				var rtVirtMethods = GetOrderedVirtualMethods(rtType, false);
				var memberInfoDef = FindTypeDefinitionByFullName("System.Reflection.MemberInfo");
				foreach (var type in module.typesDependencyOrdered)
				{
					// write runtime type virtual methods
					bool isRuntimeTypeSet = type == memberInfoDef || HasBaseType(type, memberInfoDef);
					if (!isRuntimeTypeSet)
					{
						foreach (var method in rtVirtMethods)
						{
							var topMethod = FindHighestVirtualMethodSlot(rtType, method);
							if (!topMethod.IsAbstract) writer.WriteLinePrefix($"{GetRuntimeTypeReferenceFullName(type)}.{GetVirtualMethodVTabelName(method)} = {GetMethodDefinitionFullName(topMethod)};");
						}
					}

					// write type specific virtual methods
					foreach (var method in GetOrderedVirtualMethods(type, !isRuntimeTypeSet))
					{
						var topMethod = FindHighestVirtualMethodSlot(type, method);
						if (!topMethod.IsAbstract) writer.WriteLinePrefix($"{GetRuntimeTypeReferenceFullName(type)}.{GetVirtualMethodVTabelName(method)} = {GetMethodDefinitionFullName(topMethod)};");
					}
				}
				writer.WriteLine();

				writer.WriteLinePrefix("/* Init generated members (set string literal runtime-type ptrs) */");
				foreach (var literal in activeStringLiterals)
				{
					if (options.stringLiteralMemoryLocation == StringLiteralMemoryLocation.GlobalProgramMemory_RAM)
					{
						writer.WriteLinePrefix($"(({stringTypeName}*){literal.Key})->IL2X_RuntimeType = &{stringTypeRuntimeTypeName};");
					}
				}
				writer.WriteLine();

				writer.WriteLinePrefix("/* Init intrinsic fields */");
				foreach (var type in module.typesDependencyOrdered)
				{
					foreach (var field in type.Fields)
					{
						if (field.HasCustomAttributes && field.CustomAttributes.Any(x => x.AttributeType.FullName == "System.Runtime.CompilerServices.IntrinsicAttribute"))
						{
							if (!field.IsStatic) throw new NotImplementedException("Unsupported non-static field Intrinsic: " + field.Name);
							if (field.DeclaringType.FullName == "System.String")
							{
								if (field.Name == "Empty")
								{
									writer.WriteLinePrefix($"{GetFieldDefinitionName(field)} = IL2X_Malloc(sizeof({stringTypeName}));");
								}
								else
								{
									throw new NotImplementedException("Unsupported field Intrinsic: " + field.Name);
								}
							}
							else
							{
								throw new NotImplementedException("Unsupported Intrinsic type for field: " + field.Name);
							}
						}
					}
				}
				writer.WriteLine();

				writer.WriteLinePrefix("/* Init static methods */");
				foreach (var type in module.typesDependencyOrdered)
				{
					foreach (var method in type.Methods)
					{
						if (!method.IsConstructor || !method.IsStatic) continue;
						writer.WriteLinePrefix($"{GetMethodDefinitionFullName(method)}();");
					}
				}
				writer.RemoveTab();
				writer.WriteLine('}');
				writer.WriteLine();

				// write entry point
				if (isExe && module.moduleDefinition.EntryPoint != null)
				{
					writer.WriteLine("/* =============================== */");
					writer.WriteLine("/* Entry Point */");
					writer.WriteLine("/* =============================== */");
					
					writer.WriteLine("void InitConsole()");
					writer.WriteLine('{');
					writer.WriteLine("#ifdef _WIN32");
					writer.Write
(
@"	setlocale(LC_ALL, ""en_US.utf8"");
	SetConsoleOutputCP(CP_UTF8);
	SetConsoleCP(CP_UTF8);

	/*CONSOLE_FONT_INFOEX fontInfo;
	fontInfo.cbSize = sizeof(fontInfo);
	fontInfo.FontFamily = 54;
	fontInfo.FontWeight = 100;
	fontInfo.nFont = 0;
	const wchar_t fontName[] = L""KaiTi"";
	fontInfo.dwFontSize.Y = 18;
	memcpy(fontInfo.FaceName, fontName, (sizeof(fontName)));

	HANDLE stdOut = GetStdHandle(STD_OUTPUT_HANDLE);
	SetCurrentConsoleFontEx(stdOut, 0, &fontInfo);*/
"
);
					writer.WriteLine("#endif");
					writer.WriteLine('}');
					writer.WriteLine();

					writer.WriteLine("int main()");
					writer.WriteLine('{');
					writer.AddTab();
					writer.WriteLinePrefix("InitConsole();");
					writer.Write
(
@"
	jmp_buf IL2X_UnhandledThreadExceptionBuff;
	int result = setjmp(IL2X_UnhandledThreadExceptionBuff);
	if (result != 0)
	{
		wprintf(L""Unhandled Exception\n"");
		exit(-2);
	}
	else
	{
		memcpy(IL2X_ThreadExceptionJmpBuff, IL2X_UnhandledThreadExceptionBuff, sizeof(jmp_buf));
	}
"
);
					writer.WriteLine();
					writer.WriteLinePrefix("IL2X_GC_Init();");
					writer.WriteLinePrefix($"{initModuleMethod}();");
					writer.WriteLinePrefix($"{GetMethodDefinitionFullName(module.moduleDefinition.EntryPoint)}();");
					writer.WriteLinePrefix("IL2X_GC_Collect();");
					writer.WriteLinePrefix("return 0;");
					writer.RemoveTab();
					writer.WriteLine('}');
				}
			}

			// write IL instruction generated file
			using (var stream = new FileStream(Path.Combine(outputPath, generatedMembersFilename), FileMode.Create, FileAccess.Write, FileShare.Read))
			using (writer = new StreamWriterEx(stream))
			{
				// write generate info
				writer.WriteLine("/* ############################### */");
				writer.WriteLine($"/* Generated with IL2X v{Utils.GetAssemblyInfoVersion()} */");
				writer.WriteLine("/* ############################### */");
				writer.WriteLine("#pragma once");
				if (options.stringLiteralMemoryLocation == StringLiteralMemoryLocation.ReadonlyProgramMemory_AVR) writer.WriteLine("#include <avr/pgmspace.h>");
				writer.WriteLine();

				if (activeStringLiterals.Count != 0)
				{
					writer.WriteLine("/* =============================== */");
					writer.WriteLine("/* String literals */");
					writer.WriteLine("/* =============================== */");
					foreach (var literal in activeStringLiterals)
					{
						string value = literal.Value;
						if (value.Contains('\n')) value = value.Replace("\n", "");
						if (value.Contains('\r')) value = value.Replace("\r", "");
						if (value.Length > 64) value = value.Substring(0, 64) + "...";
						writer.WriteLine($"/* {value} */");
						if (options.stringLiteralMemoryLocation == StringLiteralMemoryLocation.ReadonlyProgramMemory_AVR) writer.Write("const PROGMEM ");
						writer.Write($"char {literal.Key}[{GetStringMemorySize(literal.Value)}] = ");
						writer.Write(StringToLiteral(literal.Value));
						writer.WriteLine(';');
					}
				}
			}

			writer = null;
		}

		private int GetStringMemorySize(string value)
		{
			return (int)options.ptrSize + sizeof(int) + sizeof(char) + (value.Length * sizeof(char));// TODO: handle non-standard int & char sizes
		}

		private string StringToLiteral(string value)
		{
			var result = new StringBuilder();
			result.Append('{');

			void WriteBinary(byte[] data)
			{
				foreach (byte b in data)
				{
					result.Append(b);
					result.Append(',');
				}
			}

			// write System.Object header
			WriteBinary(new byte[(int)options.ptrSize]);

			// write string length
			WriteBinary(BitConverter.GetBytes(value.Length));

			// write string unicode data
			byte[] binaryData;
			if (options.endianness == Endianness.Little) binaryData = Encoding.Unicode.GetBytes(value);
			else binaryData = Encoding.BigEndianUnicode.GetBytes(value);
			WriteBinary(binaryData);

			// null-terminated char
			result.Append("0,0}");

			return result.ToString();
		}

		private TypeDefinition GetMetadataTypeDefinition(MetadataType metadataType)
		{
			string fullname = "System." + metadataType.ToString();
			var result = FindTypeDefinitionByFullName(fullname);
			if (result == null) throw new Exception("Failed to find type definition for: " + metadataType);
			return result;
		}

		private TypeDefinition FindTypeDefinitionByFullName(string fullName)
		{
			var result = activeCoreAssembly.assemblyDefinition.MainModule.GetType(fullName);
			if (result == null) throw new Exception("Failed to find type definition: " + fullName);
			return result;
		}
		#endregion

		#region Type writers
		private void WriteRuntimeTypeDefinition(TypeDefinition type)
		{
			// get all virtual method slot roots
			var memberInfoDef = FindTypeDefinitionByFullName("System.Reflection.MemberInfo");
			bool isRuntimeTypeSet = type == memberInfoDef || HasBaseType(type, memberInfoDef);
			var virtualMethodList = GetOrderedVirtualMethods(type, !isRuntimeTypeSet);

			// add runtime type methods
			var rtType = FindTypeDefinitionByFullName("System.RuntimeType");
			if (!isRuntimeTypeSet)
			{
				var rtTypeVirtualMethodList = GetOrderedVirtualMethods(rtType, false);
				virtualMethodList.AddRange(rtTypeVirtualMethodList);
			}

			// write runtime type definition
			string rtTypeName = GetTypeDefinitionFullName(rtType);
			string defTypeName = GetRuntimeTypeDefinitionFullName(type);
			writer.WriteLine($"typedef struct {defTypeName}");
			writer.WriteLine('{');
			writer.AddTab();
			writer.WriteLinePrefix($"{rtTypeName} runtimeType;");// inherent base runtime type fields
			for (int i = virtualMethodList.Count - 1; i != -1; --i)
			{
				var method = virtualMethodList[i];
				writer.WritePrefix($"{GetTypeReferenceFullName(method.ReturnType)} (*{GetVirtualMethodVTabelName(method)})(");
				var lastParam = method.Parameters.LastOrDefault();
				foreach (var p in method.Parameters)
				{
					writer.Write(GetTypeReferenceFullName(p.ParameterType));
					if (p != lastParam) writer.Write(',');
				}
				writer.WriteLine(");");
			}
			writer.RemoveTab();
			writer.WriteLine($"}} {defTypeName};");

			// write runtime type reference
			writer.WriteLine($"{defTypeName} {GetRuntimeTypeReferenceFullName(type)};");

			// write runtime type metadata memory
			if (options.storeRuntimeTypeStringLiteralMetadata)
			{
				// TODO: allow metadata to be stored and loaded from external file to save memory / RAM

				if (options.stringLiteralMemoryLocation == StringLiteralMemoryLocation.ReadonlyProgramMemory_AVR) writer.Write("const PROGMEM ");
				writer.WriteLine($"char {GetRuntimeTypeMetadataFullName(type)}_Name[{GetStringMemorySize(type.Name)}] = {StringToLiteral(type.Name)};");

				if (options.stringLiteralMemoryLocation == StringLiteralMemoryLocation.ReadonlyProgramMemory_AVR) writer.Write("const PROGMEM ");
				writer.WriteLine($"char {GetRuntimeTypeMetadataFullName(type)}_FullName[{GetStringMemorySize(type.FullName)}] = {StringToLiteral(type.FullName)};");
			}

			writer.WriteLine();
		}

		private void WriteTypeDefinition(TypeDefinition type, bool writeBody)
		{
			if (type.IsEnum) return;// enums are converted to numarics
			if (type.HasGenericParameters) return;// generics are generated per use
			if (type.IsPrimitive) return;// primitives

			if (!writeBody)
			{
				if (type.IsValueType && IsEmptyType(type))
				{
					writer.WriteLine(string.Format("typedef void* {0};", GetTypeDefinitionFullName(type)));// empty value types only function as pointers
				}
				else
				{
					writer.WriteLine(string.Format("typedef struct {0} {0};", GetTypeDefinitionFullName(type)));
				}
			}
			else
			{
				// get all types that should write non-static fields
				var fieldTypeList = new List<TypeDefinition>();
				fieldTypeList.Add(type);
				var baseType = type.BaseType;
				while (baseType != null)
				{
					var baseTypeDef = GetTypeDefinition(baseType);
					fieldTypeList.Add(baseTypeDef);
					baseType = baseTypeDef.BaseType;
				}

				// empty types aren't allowed in C (so only write if applicable)
				bool didWriteType = false;
				if (!type.IsValueType || !IsEmptyType(type))
				{
					didWriteType = true;
					writer.WriteLine(string.Format("struct {0}", GetTypeDefinitionFullName(type)));
					writer.WriteLine('{');
					writer.AddTab();

					// writer runtime type header
					if (!type.IsValueType)
					{
						var runtimeType = FindTypeDefinitionByFullName("System.RuntimeType");
						writer.WriteLinePrefix($"{GetTypeDefinitionFullName(runtimeType)}* IL2X_RuntimeType;");
					}

					// write all non-static fields starting from last base type
					for (int i = fieldTypeList.Count - 1; i != -1; --i)
					{
						foreach (var field in fieldTypeList[i].Fields)
						{
							if (!field.IsStatic) WriteFieldDefinition(field);
						}
					}

					writer.RemoveTab();
					writer.WriteLine("};");
				}

				// write all statics
				for (int i = fieldTypeList.Count - 1; i != -1; --i)
				{
					foreach (var field in fieldTypeList[i].Fields)
					{
						if (field.IsStatic && !field.HasConstant) WriteFieldDefinition(field);
					}
				}

				if (didWriteType) writer.WriteLine();
			}
		}

		private void WriteFieldDefinition(FieldDefinition field)
		{
			if (field.FieldType.IsGenericInstance)
			{
				throw new Exception("TODO: generate generic type");
			}
			writer.WriteLinePrefix($"{GetTypeReferenceFullName(field.FieldType)} {GetFieldDefinitionName(field)};");
		}

		private void WriteMethodDefinition(MethodDefinition method, bool writeBody)
		{
			if (method.HasGenericParameters || method.DeclaringType.HasGenericParameters) return;// generics are generated per use
			if (method.HasCustomAttributes && method.CustomAttributes.Any(x => x.AttributeType.FullName == "IL2X.NativeExternCAttribute")) return;// skip native C methods
			if (method.IsAbstract) return;

			if (method.IsConstructor && method.DeclaringType.IsPrimitive) throw new Exception("Constructors aren't supported on primitives");
			writer.Write($"{GetTypeReferenceFullName(method.ReturnType)} {GetMethodDefinitionFullName(method)}(");

			if (!method.IsStatic)
			{
				writer.Write(GetTypeReferenceFullName(method.DeclaringType));
				if (method.DeclaringType.IsValueType) writer.Write('*');
				writer.Write(" self");
				if (method.HasParameters) 
				{
					writer.Write(", ");
					WriteParameterDefinitions(method.Parameters);
				}
			}
			else if (method.HasParameters)
			{
				WriteParameterDefinitions(method.Parameters);
			}
			else
			{
				writer.Write("void");
			}
			writer.Write(')');
			
			if (!writeBody)
			{
				writer.WriteLine(';');
			}
			else
			{
				writer.WriteLine();
				writer.WriteLine('{');
				writer.AddTab();
				if (method.Body != null)
				{
					activeMethodDebugInfo = null;
					if (activeModule.symbolReader != null) activeMethodDebugInfo = activeModule.symbolReader.Read(method);
					using (var streamCache = new MemoryStream())
					using (var writerCache = new StreamWriterEx(streamCache))
					{
						writerCache.prefix = writer.prefix;
						WriteMethodBody(method.Body, writerCache);
					}
					activeMethodDebugInfo = null;
				}
				else if (method.IsAbstract)
				{
					// TODO: interface methods etc
				}
				else if (method.IsRuntime)
				{
					throw new NotImplementedException("TODO: handle delegates etc");
				}
				else if (method.IsInternalCall)
				{
					if (method.DeclaringType.FullName == "System.String")
					{
						if (method.Name == "FastAllocateString")
						{
							string lengthName = GetParameterDefinitionName(method.Parameters[0]);
							writer.WriteLinePrefix($"{GetTypeDefinitionFullName(method.DeclaringType)}* result = IL2X_GC_NewAtomic(sizeof(intptr_t) + sizeof(int32_t) + sizeof(char16_t) + (sizeof(char16_t) * {lengthName}));");
							writer.WriteLinePrefix($"result->IL2X_RuntimeType = &{GetRuntimeTypeReferenceFullName(method.DeclaringType)};");
							writer.WriteLinePrefix($"result->{GetFieldDefinitionName(method.DeclaringType.Fields[0])} = {lengthName};");
							writer.WriteLinePrefix("return result;");
						}
						else if (method.Name == "get_Length")
						{
							writer.WriteLinePrefix($"return self->{GetFieldDefinitionName(method.DeclaringType.Fields[0])};");
						}
						else
						{
							throw new NotImplementedException("Unsupported internal runtime System.String method: " + method.Name);
						}
					}
					else if (method.DeclaringType.FullName == "System.Array")
					{
						if (method.Name == "get_Length")
						{
							writer.WriteLinePrefix($"return (int32_t)(*(size_t*)self);");
						}
						else if (method.Name == "get_LongLength")
						{
							writer.WriteLinePrefix($"return (int64_t)(*(size_t*)self);");
						}
						else
						{
							throw new NotImplementedException("Unsupported internal runtime System.Array method: " + method.Name);
						}
					}
					else if (method.DeclaringType.FullName == "System.Type")
					{
						if (method.Name == "GetTypeFromHandle")
						{
							var type = GetTypeDefinition(method.Parameters[0].ParameterType);
							writer.WriteLinePrefix($"return {GetParameterDefinitionName(method.Parameters[0])}.{GetFieldDefinitionName(type.Fields[0])};");
						}
						else
						{
							throw new NotImplementedException("Unsupported internal runtime System.Type method: " + method.Name);
						}
					}
					else if (method.DeclaringType.FullName == "System.Object")
					{
						if (method.Name == "GetType")
						{
							writer.WriteLinePrefix("return self->IL2X_RuntimeType;");
						}
						else
						{
							throw new NotImplementedException("Unsupported internal runtime System.Object method: " + method.Name);
						}
					}
					else
					{
						throw new NotImplementedException("Unsupported internal runtime method: " + method.Name);
					}
				}
				else if (method.HasCustomAttributes && method.CustomAttributes.Any(x => x.AttributeType.Name == "System.Runtime.CompilerServices.IntrinsicAttribute"))
				{
					throw new NotImplementedException("Unsupported method Intrinsic: " + method.Name);
				}
				else
				{
					throw new NotImplementedException("Unsupported empty method type: " + method.Name);
				}
				writer.RemoveTab();
				writer.WriteLine('}');
				writer.WriteLine();
			}
		}

		private void WriteParameterDefinitions(IEnumerable<ParameterDefinition> parameters)
		{
			var last = parameters.LastOrDefault();
			foreach (var parameter in parameters)
			{
				if (parameter.ParameterType.IsGenericInstance) throw new Exception("TODO: generate generic type");
				WriteParameterDefinition(parameter);
				if (parameter != last) writer.Write(", ");
			}
		}

		private void WriteParameterDefinition(ParameterDefinition parameter)
		{
			writer.Write($"{GetTypeReferenceFullName(parameter.ParameterType)} {GetParameterDefinitionName(parameter)}");
		}
		#endregion

		#region Method Body / IL Instructions
		private void WriteMethodBody(MethodBody body, StreamWriterEx writerCache)
		{
			var origWriter = writer;
			writer = writerCache;

			// write local stack variables
			var variables = new List<LocalVariable>();
			if (body.HasVariables)
			{
				// validate we can init locals
				bool initLocals = body.InitLocals && !options.ignoreInitLocalsOnAllLibs;
				if (initLocals && options.ignoreInitLocalsLibs != null)
				{
					foreach (string lib in options.ignoreInitLocalsLibs)
					{
						if (activeAssembly.assemblyDefinition.Name.Name == lib)
						{
							initLocals = false;
							break;
						}
					}
				}

				// write locals and back up their values
				List<LocalVariable> memsetLocals = null;
				if (initLocals) memsetLocals = new List<LocalVariable>();
				foreach (var variable in body.Variables)
				{
					bool initLocal = initLocals;
					var local = WriteVariableDefinition(variable, ref initLocal);
					variables.Add(local);
					if (initLocals && !initLocal) memsetLocals.Add(local);
				}

				// write init locals that require memset
				if (initLocals)
				{
					foreach (var variable in memsetLocals)
					{
						writer.WriteLinePrefix($"memset({variable.name}, 0, sizeof({GetTypeReferenceFullName(variable.definition.VariableType)}));");
					}
				}
			}

			// write exception handler local variable helpers
			Dictionary<int, ExceptionHandlerGroup> exceptionHandlerGroups = null;
			if (body.HasExceptionHandlers)
			{
				exceptionHandlerGroups = new Dictionary<int, ExceptionHandlerGroup>();
				foreach (var e in body.ExceptionHandlers)
				{
					int handlerID = e.TryEnd.Offset;

					// check if start handler already processed
					if (exceptionHandlerGroups.ContainsKey(handlerID)) continue;

					// write variable helpers
					writer.WriteLinePrefix(string.Format("jmp_buf IL2X_LOCAL_JMP_{0}, IL2X_LOCAL_JMP_LAST_{0};", handlerID));
					writer.WriteLinePrefix($"int IL2X_IS_JMP_{handlerID};");

					// add start handler
					var handler = new ExceptionHandlerGroup();
					handler.start = e;
					exceptionHandlerGroups.Add(handlerID, handler);
				}

				// set end handlers
				var last = body.ExceptionHandlers.Last();
				foreach (var group in exceptionHandlerGroups)
				{
					int handlerID = group.Key;
					var handler = group.Value;
					
					bool canCheck = false;
					ExceptionHandler lastE = null;
					foreach (var e2 in body.ExceptionHandlers)
					{
						if (handler.start == e2)
						{
							canCheck = true;
						}
						else if (canCheck && e2.TryEnd.Offset != handlerID)
						{
							handler.end = lastE;
							break;
						}

						lastE = e2;
					}

					if (handler.end == null) handler.end = last;
				}

				// set parent group
				foreach (var group in exceptionHandlerGroups)
				{
					foreach (var group2 in exceptionHandlerGroups)
					{
						if (group.Key == group2.Key) continue;
						if (group2.Value.start.TryStart.Offset <= group.Value.start.TryStart.Offset && group2.Value.start.TryEnd.Offset >= group.Value.start.TryEnd.Offset)
						{
							group.Value.parent = group2.Value;
							break;
						}
					}
				}

				// set finally handlers
				foreach (var group in exceptionHandlerGroups)
				{
					if (group.Value.end.HandlerType == ExceptionHandlerType.Finally)
					{
						group.Value._finally = group.Value.end;
					}
					else
					{
						var parent = group.Value.parent;
						while (parent != null)
						{
							if (parent.end.HandlerType == ExceptionHandlerType.Finally)
							{
								group.Value._finally = parent.end;
								break;
							}
							parent = parent.parent;
						}
					}
				}
			}

			// write instructions
			var stack = new Stack<EvaluationObject>();// objects currently on the stack
			var stackTypes = new Dictionary<int, List<TypeReference>>();// possible types a particular stack slot may represent
			var stackByRefTypes = new Dictionary<TypeReference, ByReferenceType>();// helper to keep evaluation stack "ByReferenceType" all using the same ptrs
			var instructionJumpModify = new Dictionary<Instruction, BranchJumpModify>();// helper to keep track of branch predicted jump modifications

			string FormatedEvalStackTypeName(int stackKey, int typeIndex)
			{
				return $"e_s{stackKey}_t{typeIndex}";
			}

			string StackPush(TypeReference type, string value, bool isAddress)
			{
				if (type == null) throw new Exception("Type cannot be null");

				// convert type to ref type if needed
				if (isAddress) type = GetTypeByReference(type);

				// validate stack key exists
				int stackKey = stack.Count;
				if (!stackTypes.ContainsKey(stackKey)) stackTypes.Add(stackKey, new List<TypeReference>());

				// get evaluation stack type index
				int typeIndex = stackTypes[stackKey].IndexOf(type);
				if (typeIndex == -1)
				{
					typeIndex = stackTypes[stackKey].Count;
					stackTypes[stackKey].Add(type);
				}

				// push and write evaluation stack object
				string evalStackValue = FormatedEvalStackTypeName(stackKey, typeIndex);
				stack.Push(new EvaluationObject(type, evalStackValue));
				if (!string.IsNullOrEmpty(value))
				{
					if (!isAddress) writer.WriteLinePrefix($"{evalStackValue} = {value};");
					else writer.WriteLinePrefix($"{evalStackValue} = &{value};");
				}

				return evalStackValue;
			}

			ByReferenceType GetTypeByReference(TypeReference type)
			{
				if (!stackByRefTypes.ContainsKey(type)) stackByRefTypes.Add(type, new ByReferenceType(type));
				return stackByRefTypes[type];
			}

			void Ldc_X(MetadataType type, ValueType value)
			{
				string result = value.ToString();//value.ToString("F5");
				var valueType = value.GetType();
				if (valueType == typeof(float))
				{
					if (!result.Contains('E') && !result.Contains('.')) result += ".0f";
					else result += 'f';
				}
				else if (valueType == typeof(double))
				{
					if (!result.Contains('E') && !result.Contains('.')) result += ".0";
				}
				StackPush(GetMetadataTypeDefinition(type), result, false);
			}

			void Ldarg_X(int index, bool isAddress, bool indexCanThisOffset)
			{
				if (index == 0 && body.Method.HasThis)
				{
					TypeReference type = body.Method.DeclaringType;
					if (type.IsValueType) type = GetTypeByReference(type);
					StackPush(type, "self", isAddress);
				}
				else
				{
					if (indexCanThisOffset && body.Method.HasThis) --index;
					var p = body.Method.Parameters[index];
					TypeReference type = p.ParameterType;
					StackPush(type, GetParameterDefinitionName(p), isAddress);
				}
			}

			void Ldarga_X(ParameterDefinition parameter)
			{
				Ldarg_X(parameter.Index, true, false);
			}

			void Stloc_X(int index)
			{
				var item = stack.Pop();
				var variableLeft = variables[index];
				writer.WriteLinePrefix($"{variableLeft.name} = {item.value};");
			}

			void Ldelem_X(Instruction instruction)
			{
				var index = stack.Pop();
				var array = stack.Pop();
				var arrayType = (ArrayType)array.type;
				StackPush(arrayType.ElementType, $"(({GetTypeReferenceFullName(arrayType.ElementType)}*)((char*){array.value} + sizeof(size_t)))[{index.value}]", false);
			}

			void Stelem_X(Instruction instruction)
			{
				var value = stack.Pop();
				var index = stack.Pop();
				var array = stack.Pop();
				var arrayType = (ArrayType)array.type;
				writer.WriteLinePrefix($"(({GetTypeReferenceFullName(arrayType.ElementType)}*)((char*){array.value} + sizeof(size_t)))[{index.value}] = {value.value};");
			}

			void Ldind_X(string nativePtrType, string nativeCastingTypeName, MetadataType castingType)
			{
				var item = stack.Pop();
				if (nativePtrType == nativeCastingTypeName) StackPush(GetMetadataTypeDefinition(castingType), $"(*(({nativePtrType}*){item.value}))", false);
				else StackPush(GetMetadataTypeDefinition(castingType), $"(({nativeCastingTypeName})*(({nativePtrType}*){item.value}))", false);
			}

			void Stind_X(string nativePtrType)
			{
				var value = stack.Pop();
				var address = stack.Pop();
				writer.WriteLinePrefix($"*(({nativePtrType}*){address.value}) = {value.value};");
			}

			void Ldloc_X(int variableIndex, bool isAddress)
			{
				var variable = variables[variableIndex];
				StackPush(variable.definition.VariableType, variable.name, isAddress);
			}

			void Ldsfld_X(FieldReference field, bool isAddress)
			{
				StackPush(field.FieldType, GetFieldReferenceName(field), isAddress);
			}

			void Ldfld_X(EvaluationObject self, FieldReference field, bool isAddress)
			{
				string accessToken = self.type.IsValueType ? "." : "->";
				StackPush(field.FieldType, $"{self.value}{accessToken}{GetFieldReferenceName(field)}", isAddress);
			}

			void Conv_X(string nativeCastingTypeName, MetadataType castingType)
			{
				var item = stack.Pop();
				StackPush(GetMetadataTypeDefinition(castingType), $"(({nativeCastingTypeName}){item.value})", false);
			}

			void Leave_X(Instruction instruction, string form)
			{
				if (stack.Count != 0) throw new Exception("Leave_X error: eval stack didn't fully unwind");

				// write finally instructions
				var handler = body.ExceptionHandlers.FirstOrDefault(x => instruction.Offset >= x.HandlerStart.Offset && instruction.Offset < x.HandlerEnd.Offset);// find the exception handler we are part of
				if (handler == null)
				{
					BranchUnconditional(instruction, form);
					return;
				}
				int handlerID = handler.TryEnd.Offset;
				var handlerGroup = exceptionHandlerGroups[handlerID];// get your handler group
				if (handlerGroup._finally != null)
				{
					writer.WriteLinePrefix("/* finally */");
					writer.WriteLinePrefix('{');
					writer.AddTab();

					var i = handlerGroup._finally.HandlerStart;
					while (i != handlerGroup._finally.HandlerEnd && i != null)
					{
						ProcessInstruction(i, true, true);
						i = i.Next;
						if (i.OpCode.Code == Code.Endfinally) break;
					}

					writer.RemoveTab();
					writer.WriteLinePrefix('}');
				}

				// branch predict
				BranchUnconditional(instruction, form);
			}

			void BranchUnconditional(Instruction instruction, string form)
			{
				var operand = (Instruction)instruction.Operand;
				int jmpOffset = Br_ForwardResolveStack(instruction, operand, false);
				writer.WriteLinePrefix($"goto IL_{jmpOffset.ToString(form)};");
			}

			void BranchBooleanCondition(Instruction instruction, bool trueCondition, string form)
			{
				var value = stack.Pop();
				var operand = (Instruction)instruction.Operand;
				writer.WritePrefix("if (");
				if (!trueCondition) writer.Write('!');
				writer.WriteLine($"{value.value})");
				writer.WriteLinePrefix('{');
				writer.AddTab();
				int jmpOffset = Br_ForwardResolveStack(instruction, operand, true);
				writer.WriteLinePrefix($"goto IL_{jmpOffset.ToString(form)};");
				writer.RemoveTab();
				writer.WriteLinePrefix('}');
			}

			string GetObjectValue(EvaluationObject value, bool asUnsigned)
			{
				if (asUnsigned)
				{
					if (!value.type.IsPrimitive) throw new Exception("Value is not primitive: " + value.value);
					var type = value.type.MetadataType;
					switch (type)
					{
						case MetadataType.SByte: return $"((uint8_t){value.value})";
						case MetadataType.Int16: return $"((uint16_t){value.value})";
						case MetadataType.Int32: return $"((uint32_t){value.value})";
						case MetadataType.Int64: return $"((uint64_t){value.value})";
						case MetadataType.Byte:
						case MetadataType.UInt16:
						case MetadataType.UInt32:
						case MetadataType.UInt64:
						case MetadataType.Single:
						case MetadataType.Double:
							return value.value;
						default: throw new NotImplementedException("Failed to unsign primitive type: " + type);
					}
				}
				else
				{
					return value.value;
				}
			}

			void BranchCompareCondition(Instruction instruction, string condition, string form, bool unsignedCmp)
			{
				var value2 = stack.Pop();
				var value1 = stack.Pop();
				var operand = (Instruction)instruction.Operand;

				writer.WritePrefix("if (");
				writer.Write(GetObjectValue(value1, unsignedCmp));
				writer.Write($" {condition} ");
				writer.Write(GetObjectValue(value2, unsignedCmp));
				writer.WriteLine(')');
				writer.WriteLinePrefix('{');
				writer.AddTab();
				int jmpOffset = Br_ForwardResolveStack(instruction, operand, true);
				writer.WriteLinePrefix($"goto IL_{jmpOffset.ToString(form)};");
				writer.RemoveTab();
				writer.WriteLinePrefix('}');
			}

			MetadataType GetPrimitiveOperationResultType(MetadataType value1, MetadataType value2)
			{
				var result = (value1 >= value2) ? value1 : value2;
				if (result == MetadataType.SByte || result == MetadataType.Byte || result == MetadataType.Int16 || result == MetadataType.UInt16)
				{
					result = MetadataType.Int32;
				}

				return result;
			}

			TypeReference GetPrimitiveOperationResultTypeRef(TypeReference type1, TypeReference type2)//(MetadataType value1, MetadataType value2)
			{
				var result = GetPrimitiveOperationResultType(type1.MetadataType, type2.MetadataType);
				if (result == MetadataType.Pointer)
				{
					if (type1.IsPointer) return type1;
					else if (type2.IsPointer) return type2;
					else throw new Exception("Primitive Operation Result Type failed");
				}
				return GetMetadataTypeDefinition(result);
			}

			void PrimitiveOperator(string op)
			{
				var value2 = stack.Pop();
				var value1 = stack.Pop();
				// pointer arithmetic is always done in steps of bytes and ignores its type (so make sure to cast to a byte*)
				if (value1.type.IsPointer) StackPush(GetPrimitiveOperationResultTypeRef(value1.type, value2.type), $"((char*){value1.value} {op} {value2.value})", false);
				else StackPush(GetPrimitiveOperationResultTypeRef(value1.type, value2.type), $"({value1.value} {op} {value2.value})", false);
			}

			void ConditionalExpression(string condition, bool unsignedCmp)
			{
				var value2 = stack.Pop();
				var value1 = stack.Pop();
				StackPush(GetMetadataTypeDefinition(MetadataType.Int32), $"(({GetObjectValue(value1, unsignedCmp)} {condition} {GetObjectValue(value2, unsignedCmp)}) ? 1 : 0)", false);
			}

			void BitwiseOperation(string op)
			{
				var value2 = stack.Pop();
				var value1 = stack.Pop();
				StackPush(GetPrimitiveOperationResultTypeRef(value1.type, value2.type), $"({value1.value} {op} {value2.value})", false);
			}

			int Br_ForwardResolveStack(Instruction brInstruction, Instruction jmpInstruction, bool keepExistingStack)
			{
				int existingStackCount = stack.Count;
				Stack<EvaluationObject> existingStack = null;
				if (keepExistingStack && stack.Count != 0) existingStack = new Stack<EvaluationObject>(stack);
				
				var origJmpInstruction = jmpInstruction;
				int jmpOffset = jmpInstruction.Offset;
				while (stack.Count != 0)
				{
					ProcessInstruction(jmpInstruction, false, false);
					jmpOffset = jmpInstruction.Offset + 1;// if next instruction is null make sure we jump after it
					jmpInstruction = jmpInstruction.Next;
					if (jmpInstruction != null) jmpOffset = jmpInstruction.Offset;
				}

				if (origJmpInstruction.Offset != jmpOffset) instructionJumpModify.Add(brInstruction, new BranchJumpModify(jmpOffset, existingStackCount));
				if (keepExistingStack && existingStack != null) stack = new Stack<EvaluationObject>(existingStack);
				return jmpOffset;
			}

			foreach (var instruction in body.Instructions)
			{
				ProcessInstruction(instruction, true, false);
			}

			void ProcessInstruction(Instruction instruction, bool writeBrJumps, bool canWriteFinallyBlock)
			{
				// if instruction within finally block skip as these are forward processed
				if (!canWriteFinallyBlock)
				{
					bool isFinallyBlock = false;
					foreach (var handler in body.ExceptionHandlers)
					{
						if (handler.HandlerType != ExceptionHandlerType.Finally) continue;
						if (instruction.Offset >= handler.HandlerStart.Offset && instruction.Offset < handler.HandlerEnd.Offset)
						{
							isFinallyBlock = true;
							break;
						}
					}
					if (isFinallyBlock) return;
				}

				// validate instruction isnt jump to only handled
				if (writeBrJumps)
				{
					foreach (var brModify in instructionJumpModify)
					{
						if (brModify.Value.stackCountBeforeJump == 0 && stack.Count == 0) continue;
						var jumpInstruction = (Instruction)brModify.Key.Operand;
						if (instruction.Offset > jumpInstruction.Offset && instruction.Offset < brModify.Value.offset)
						{
							stack.Clear();// stack already forward processes, so remove
							return;
						}
					}
				}

				// check for exception handlers
				if (body.HasExceptionHandlers && !canWriteFinallyBlock)
				{
					int nestedTry = 0;
					foreach (var e in body.ExceptionHandlers)
					{
						int handlerID = e.TryEnd.Offset;
						var group = exceptionHandlerGroups[handlerID];

						// check for try
						if (e.TryStart == instruction && e == group.start)
						{
							writer.WriteLinePrefix("/* .try */");
							writer.WriteLinePrefix($"memcpy(IL2X_LOCAL_JMP_LAST_{handlerID}, IL2X_ThreadExceptionJmpBuff, sizeof(jmp_buf));");
							writer.WriteLinePrefix($"IL2X_IS_JMP_{handlerID} = setjmp(IL2X_LOCAL_JMP_{handlerID});");
							writer.WriteLinePrefix($"if (IL2X_IS_JMP_{handlerID} == 0)");
							writer.WriteLinePrefix('{');
							writer.AddTab();
							writer.WriteLinePrefix($"memcpy(IL2X_ThreadExceptionJmpBuff, IL2X_LOCAL_JMP_{handlerID}, sizeof(jmp_buf));");
							++nestedTry;
						}

						// check for try end
						if (e.TryEnd == instruction && e == group.start)
						{
							writer.RemoveTab();
							writer.WriteLinePrefix("} /* end .try */");
							--nestedTry;
						}

						// detect catch start
						if (e.HandlerStart == instruction && e == group.start)
						{
							writer.WriteLinePrefix("else");
							writer.WriteLinePrefix('{');
							writer.AddTab();
							writer.WriteLinePrefix($"memcpy(IL2X_ThreadExceptionJmpBuff, IL2X_LOCAL_JMP_LAST_{handlerID}, sizeof(jmp_buf));");
						}

						// handler types
						if (e.HandlerType == ExceptionHandlerType.Catch)
						{
							if (e.HandlerStart == instruction)
							{
								writer.WriteLinePrefix($"/* catch {e.CatchType.FullName} */");
								var catchTypeDef = GetTypeDefinition(e.CatchType);
								writer.WriteLinePrefix($"if (IL2X_IsType(((t_IL2XPortableCoreLibdll_System_Object*)IL2X_ThreadExceptionObject)->IL2X_RuntimeType, &{GetRuntimeTypeReferenceFullName(catchTypeDef)}))");
								writer.WriteLinePrefix('{');
								writer.AddTab();

								var objectType = FindTypeDefinitionByFullName("System.Object");
								StackPush(objectType, "IL2X_ThreadExceptionObject", false);// pushed exception on eval stack
								writer.WriteLinePrefix("IL2X_ThreadExceptionObject = 0;");// null thread exception as its been handled
							}
						}
						else if (e.HandlerType == ExceptionHandlerType.Finally)
						{
							// 'finally' instructions are injected before try, filter, catch exits or unhandled exceptions
						}
						else
						{
							throw new NotImplementedException("Unsupported exception handler type: " + e.HandlerType);
						}

						// detect catch end
						if (e.HandlerEnd == instruction)
						{
							writer.RemoveTab();
							writer.WriteLinePrefix('}');
							if (e == group.end && e.HandlerType != ExceptionHandlerType.Finally)
							{
								writer.WriteLinePrefix("/* throw unhandled exception */");
								writer.WriteLinePrefix($"if (IL2X_ThreadExceptionObject != 0) longjmp(IL2X_ThreadExceptionJmpBuff, 1);");
								writer.RemoveTab();
								writer.WriteLinePrefix('}');
							}
						}
					}
				}

				// check if this instruction can be jumped to
				if (writeBrJumps)
				{
					bool CanBeJumpedTo(params Code[] codes)
					{
						return body.Instructions.Any(x =>
						x.OpCode.HasAnyCodes(codes) &&// has branch code
						((((Instruction)x.Operand).Offset == instruction.Offset && !instructionJumpModify.ContainsKey(x)) ||// if instruction jumps to me and no branch overrides
						(instructionJumpModify.ContainsKey(x) && instructionJumpModify[x].offset == instruction.Offset)));// or instruction has branch override and jumps to me
					}

					if (CanBeJumpedTo
					(
						Code.Br_S, Code.Brfalse_S, Code.Brtrue_S,
						Code.Bge_Un_S,
						Code.Bge_S, Code.Bne_Un_S,
						Code.Bgt_S, Code.Bgt_Un_S,
						Code.Beq_S,
						Code.Blt_S, Code.Blt_Un_S,
						Code.Ble_S, Code.Ble_Un_S,
						Code.Leave_S
					))
					{
						writer.WriteLinePrefix($"IL_{instruction.Offset.ToString("x4")}:;");// write goto jump label short form
					}

					if (CanBeJumpedTo
					(
						Code.Br, Code.Brfalse, Code.Brtrue,
						Code.Bge, Code.Bge_Un,
						Code.Bgt, Code.Bgt_Un,
						Code.Bne_Un,
						Code.Beq,
						Code.Blt, Code.Blt_Un,
						Code.Ble, Code.Ble_Un,
						Code.Leave
					))
					{
						writer.WriteLinePrefix($"IL_{instruction.Offset.ToString("x8")}:;");// write goto jump label long form
					}
				}

				// handle next instruction
				switch (instruction.OpCode.Code)
				{
					// evaluation operations
					case Code.Nop: break;
					case Code.Endfinally: throw new Exception("Endfinally should be processed in Leave_X");

					// push to stack
					case Code.Ldnull:
					{
						StackPush(GetMetadataTypeDefinition(MetadataType.Int32), "0", false);
						break;
					}

					case Code.Dup:
					{
						var item = stack.Pop();
						stack.Push(item);
						stack.Push(item);
						break;
					}

					case Code.Ldtoken:
					{
						var type = GetTypeDefinition((TypeReference)instruction.Operand);
						var rtTypeHandle = FindTypeDefinitionByFullName("System.RuntimeTypeHandle");
						var rtType = FindTypeDefinitionByFullName("System.RuntimeType");
						var thMethod = rtType.Methods.First(x => x.Name == "get_TypeHandle");
						StackPush(rtTypeHandle, $"{GetMethodDefinitionFullName(thMethod)}(&{GetRuntimeTypeReferenceFullName(type)})", false);
						break;
					}
					
					case Code.Ldc_I4_M1: Ldc_X(MetadataType.Int32, -1); break;
					case Code.Ldc_I4_0: Ldc_X(MetadataType.Int32, 0); break;
					case Code.Ldc_I4_1: Ldc_X(MetadataType.Int32, 1); break;
					case Code.Ldc_I4_2: Ldc_X(MetadataType.Int32, 2); break;
					case Code.Ldc_I4_3: Ldc_X(MetadataType.Int32, 3); break;
					case Code.Ldc_I4_4: Ldc_X(MetadataType.Int32, 4); break;
					case Code.Ldc_I4_5: Ldc_X(MetadataType.Int32, 5); break;
					case Code.Ldc_I4_6: Ldc_X(MetadataType.Int32, 6); break;
					case Code.Ldc_I4_7: Ldc_X(MetadataType.Int32, 7); break;
					case Code.Ldc_I4_8: Ldc_X(MetadataType.Int32, 8); break;
					case Code.Ldc_I4: Ldc_X(MetadataType.Int32, (int)instruction.Operand); break;
					case Code.Ldc_I4_S: Ldc_X(MetadataType.Int32, (sbyte)instruction.Operand); break;
					case Code.Ldc_I8: Ldc_X(MetadataType.Int64, (long)instruction.Operand); break;
					case Code.Ldc_R4: Ldc_X(MetadataType.Single, (float)instruction.Operand); break;
					case Code.Ldc_R8: Ldc_X(MetadataType.Double, (double)instruction.Operand); break;

					case Code.Ldarg_0: Ldarg_X(0, false, true); break;
					case Code.Ldarg_1: Ldarg_X(1, false, true); break;
					case Code.Ldarg_2: Ldarg_X(2, false, true); break;
					case Code.Ldarg_3: Ldarg_X(3, false, true); break;
					case Code.Ldarg:
					{
						if (instruction.Operand is int) Ldarg_X((int)instruction.Operand, false, true);
						else if (instruction.Operand is ParameterDefinition) Ldarg_X(((ParameterDefinition)instruction.Operand).Index, false, false);
						else throw new NotImplementedException("Ldarg unsupported operand: " + instruction.Operand.GetType());
						break;
					}
					case Code.Ldarg_S:
					{
						if (instruction.Operand is short) Ldarg_X((short)instruction.Operand, false, true);
						else if (instruction.Operand is int) Ldarg_X((int)instruction.Operand, false, true);
						else if (instruction.Operand is ParameterDefinition) Ldarg_X(((ParameterDefinition)instruction.Operand).Index, false, false);
						else throw new NotImplementedException("Ldarg_S unsupported operand: " + instruction.Operand.GetType());
						break;
					}

					case Code.Ldarga: Ldarga_X((ParameterDefinition)instruction.Operand); break;
					case Code.Ldarga_S: Ldarga_X((ParameterDefinition)instruction.Operand); break;

					//case Code.Ldelem_I: Ldelem_X(instruction); break;
					case Code.Ldelem_I1: Ldelem_X(instruction); break;
					case Code.Ldelem_I2: Ldelem_X(instruction); break;
					case Code.Ldelem_I4: Ldelem_X(instruction); break;
					case Code.Ldelem_I8: Ldelem_X(instruction); break;
					case Code.Ldelem_U1: Ldelem_X(instruction); break;
					case Code.Ldelem_U2: Ldelem_X(instruction); break;
					case Code.Ldelem_U4: Ldelem_X(instruction); break;
					case Code.Ldelem_R4: Ldelem_X(instruction); break;
					case Code.Ldelem_R8: Ldelem_X(instruction); break;
					case Code.Ldelem_Ref: Ldelem_X(instruction); break;

					case Code.Ldlen:
					{
						var array = stack.Pop();
						var arrayType = FindTypeDefinitionByFullName("System.Array");
						var lengthMethod = arrayType.Methods.First(x => x.Name == "get_Length");
						StackPush(GetMetadataTypeDefinition(MetadataType.UIntPtr), $"{GetMethodDefinitionFullName(lengthMethod)}({array.value})", false);
						break;
					}

					case Code.Ldind_I: Ldind_X("intptr_t", "intptr_t", MetadataType.IntPtr); break;
					case Code.Ldind_I1: Ldind_X("int8_t", "int32_t", MetadataType.Int32); break;
					case Code.Ldind_I2: Ldind_X("int16_t", "int32_t", MetadataType.Int32); break;
					case Code.Ldind_I4: Ldind_X("int32_t", "int32_t", MetadataType.Int32); break;
					case Code.Ldind_I8: Ldind_X("int64_t", "int64_t", MetadataType.Int64); break;
					case Code.Ldind_R4: Ldind_X("float", "float", MetadataType.Single); break;
					case Code.Ldind_R8: Ldind_X("double", "double", MetadataType.Double); break;
					
					case Code.Ldind_U1: Ldind_X("uint8_t", "int32_t", MetadataType.Int32); break;
					case Code.Ldind_U2: Ldind_X("uint16_t", "int32_t", MetadataType.Int32); break;
					case Code.Ldind_U4: Ldind_X("uint32_t", "int32_t", MetadataType.Int32); break;

					case Code.Ldloc_0: Ldloc_X(0, false); break;
					case Code.Ldloc_1: Ldloc_X(1, false); break;
					case Code.Ldloc_2: Ldloc_X(2, false); break;
					case Code.Ldloc_3: Ldloc_X(3, false); break;
					case Code.Ldloc:
					{
						var operand = (VariableDefinition)instruction.Operand;
						Ldloc_X(operand.Index, false);
						break;
					}
					case Code.Ldloc_S:
					{
						var operand = (VariableDefinition)instruction.Operand;
						Ldloc_X(operand.Index, false);
						break;
					}

					case Code.Ldloca:
					{
						var operand = (VariableDefinition)instruction.Operand;
						Ldloc_X(operand.Index, true);
						break;
					}
					case Code.Ldloca_S:
					{
						var operand = (VariableDefinition)instruction.Operand;
						Ldloc_X(operand.Index, true);
						break;
					}

					case Code.Ldstr:
					{
						string value = (string)instruction.Operand;
						string valueFormated;
						if (!allStringLiterals.ContainsValue(value))
						{
							valueFormated = $"StringLiteral_{allStringLiterals.Count}";
							allStringLiterals.Add(valueFormated, value);
							activeStringLiterals.Add(valueFormated, value);
						}
						else
						{
							valueFormated = allStringLiterals.First(x => x.Value == value).Key;
						}
						StackPush(FindTypeDefinitionByFullName("System.String"), valueFormated, false);
						break;
					}

					case Code.Ldsfld:
					{
						var field = (FieldReference)instruction.Operand;
						Ldsfld_X(field, false);
						break;
					}

					case Code.Ldsflda:
					{
						var field = (FieldReference)instruction.Operand;
						Ldsfld_X(field, true);
						break;
					}

					case Code.Ldfld:
					{
						var self = stack.Pop();
						var field = (FieldReference)instruction.Operand;
						Ldfld_X(self, field, false);
						break;
					}

					case Code.Ldflda:
					{
						var self = stack.Pop();
						var field = (FieldReference)instruction.Operand;
						Ldfld_X(self, field, true);
						break;
					}

					case Code.Conv_I: Conv_X("intptr_t", MetadataType.IntPtr); break;
					case Code.Conv_I1: Conv_X("int8_t", MetadataType.SByte); break;
					case Code.Conv_I2: Conv_X("int16_t", MetadataType.Int16); break;
					case Code.Conv_I4: Conv_X("int32_t", MetadataType.Int32); break;
					case Code.Conv_I8: Conv_X("int64_t", MetadataType.Int64); break;

					case Code.Conv_U: Conv_X("uintptr_t", MetadataType.UIntPtr); break;
					case Code.Conv_U1: Conv_X("uint8_t", MetadataType.Byte); break;
					case Code.Conv_U2: Conv_X("uint16_t", MetadataType.UInt16); break;
					case Code.Conv_U4: Conv_X("uint32_t", MetadataType.UInt32); break;
					case Code.Conv_U8: Conv_X("uint64_t", MetadataType.UInt64); break;
					case Code.Conv_R4: Conv_X("float", MetadataType.Single); break;
					case Code.Conv_R8: Conv_X("double", MetadataType.Double); break;

					case Code.Ceq: ConditionalExpression("==", false); break;
					case Code.Cgt: ConditionalExpression(">", false); break;
					case Code.Cgt_Un: ConditionalExpression(">", true); break;
					case Code.Clt: ConditionalExpression("<", false); break;

					case Code.And: BitwiseOperation("&"); break;
					case Code.Or: BitwiseOperation("|"); break;

					case Code.Shl: PrimitiveOperator("<<"); break;
					case Code.Shr: PrimitiveOperator(">>"); break;
					//case Code.Shr_Un: PrimitiveOperator(">>", true); break;

					case Code.Add: PrimitiveOperator("+"); break;
					case Code.Sub: PrimitiveOperator("-"); break;
					case Code.Mul: PrimitiveOperator("*"); break;
					case Code.Div: PrimitiveOperator("/"); break;

					case Code.Add_Ovf:
					case Code.Add_Ovf_Un:
					{
						// https://www.geeksforgeeks.org/check-for-integer-overflow/
						throw new NotImplementedException("TODO: need ability to throw Overflow exceptions");
					}

					case Code.Neg:
					{
						var value = stack.Pop();
						StackPush(value.type, '-' + value.value, false);
						break;
					}

					case Code.Call:
					case Code.Callvirt:
					{
						var method = (MethodReference)instruction.Operand;

						// get paramaters
						var methodInvoke = new StringBuilder("(");
						var parameters = new StringBuilder();
						var firstParameter = method.Parameters.FirstOrDefault();
						foreach (var p in method.Parameters)
						{
							var item = stack.Pop();
							if (p != firstParameter) parameters.Insert(0, ", ");
							parameters.Insert(0, item.value);
						}

						EvaluationObject self = null;
						if (method.HasThis)
						{
							self = stack.Pop();
							if (self.type.IsValueType) methodInvoke.Append('&');
							methodInvoke.Append(self.value);
							if (method.HasParameters) methodInvoke.Append(", ");
						}

						if (method.HasParameters) methodInvoke.Append(parameters);
						methodInvoke.Append(')');

						// insert / get method to invoke
						string methodName = null;
						var methodDef = GetMethodDefinition(method);
						var attr = methodDef.CustomAttributes.FirstOrDefault(x => x.AttributeType.FullName == "IL2X.NativeExternCAttribute");
						if (attr != null)
						{
							methodName = attr.ConstructorArguments[0].Value as string;
							if (methodName == null) methodName = method.Name;
						}
						else if (methodDef.IsVirtual && instruction.OpCode.Code == Code.Callvirt)
						{
							if (!method.HasThis) throw new NotImplementedException("Calling non-instance virtual method not supported");
							methodName = $"(({GetRuntimeTypeDefinitionFullName(methodDef.DeclaringType)}*){self.value}->IL2X_RuntimeType)->{GetVirtualMethodVTabelName(methodDef)}";
						}

						if (methodName == null) methodName = GetMethodReferenceFullName(method);
						methodInvoke.Insert(0, methodName);

						// finish
						if (method.ReturnType.MetadataType == MetadataType.Void) writer.WriteLinePrefix(methodInvoke.Append(';').ToString());
						else StackPush(method.ReturnType, methodInvoke.ToString(), false);
						break;
					}

					case Code.Newobj:
					{
						var method = (MethodReference)instruction.Operand;
						string allocMethod = null;

						// allocation memory
						if (!method.DeclaringType.IsValueType)
						{
							if (IsAtomicType(method.DeclaringType)) allocMethod = "IL2X_GC_NewAtomic";
							else allocMethod = "IL2X_GC_New";
							string typeName = GetTypeReferenceFullName(method.DeclaringType, allowSymbols: false);
							if (IsEmptyType(method.DeclaringType)) typeName += '*';
							allocMethod = $"{allocMethod}(sizeof({typeName}))";
						}

						// pop and backup parameters 
						var paramaterStack = new Stack<EvaluationObject>();
						foreach (var p in method.Parameters) paramaterStack.Push(stack.Pop());

						// push allocated memory to eval stack
						string self = StackPush(method.DeclaringType, allocMethod, false);

						// set runtime type pointer
						if (!method.DeclaringType.IsValueType)
						{
							var typeDef = GetTypeDefinition(method.DeclaringType);
							writer.WriteLinePrefix($"{self}->IL2X_RuntimeType = &{GetRuntimeTypeReferenceFullName(typeDef)};");
						}

						// construct memory
						if (method.DeclaringType.IsValueType) self = '&' + self;
						var methodInvoke = new StringBuilder($"{GetMethodReferenceFullName(method)}({self}");
						if (method.HasParameters) methodInvoke.Append(", ");
						var lastParameter = method.Parameters.LastOrDefault();
						foreach (var p in method.Parameters)
						{
							var item = paramaterStack.Pop();
							methodInvoke.Append(item.value);
							if (p != lastParameter) methodInvoke.Append(", ");
						}
						methodInvoke.Append(");");
						writer.WriteLinePrefix(methodInvoke.ToString());
						break;
					}

					case Code.Newarr:
					{
						var type = (TypeReference)instruction.Operand;
						var size = stack.Pop();
						string allocMethod;
						if (IsAtomicType(type)) allocMethod = "IL2X_GC_NewArrayAtomic";
						else allocMethod = "IL2X_GC_NewArray";
						StackPush(new ArrayType(type), $"{allocMethod}(sizeof({GetTypeReferenceFullName(type)}), {size.value})", false);
						break;
					}

					case Code.Localloc:
					{
						var size = stack.Pop();
						StackPush(GetMetadataTypeDefinition(MetadataType.IntPtr), $"alloca({size.value})", false);
						break;
					}

					// pop from stack and write operation
					case Code.Stloc_0: Stloc_X(0); break;
					case Code.Stloc_1: Stloc_X(1); break;
					case Code.Stloc_2: Stloc_X(2); break;
					case Code.Stloc_3: Stloc_X(3); break;
					case Code.Stloc:
					{
						var variable = (VariableDefinition)instruction.Operand;
						Stloc_X(variable.Index);
						break;
					}
					case Code.Stloc_S:
					{
						var variable = (VariableDefinition)instruction.Operand;
						Stloc_X(variable.Index);
						break;
					}

					case Code.Stfld:
					{
						var itemRight = stack.Pop();
						var self = stack.Pop();
						var fieldLeft = (FieldDefinition)instruction.Operand;
						string accessToken = self.type.IsValueType ? "." : "->";
						writer.WriteLinePrefix($"{self.value}{accessToken}{GetFieldDefinitionName(fieldLeft)} = {itemRight.value};");
						break;
					}

					case Code.Stsfld:
					{
						var item = stack.Pop();
						var field = (FieldDefinition)instruction.Operand;
						writer.WriteLinePrefix($"{GetFieldDefinitionName(field)} = {item.value};");
						break;
					}

					case Code.Stind_I1: Stind_X("int8_t"); break;
					case Code.Stind_I2: Stind_X("int16_t"); break;
					case Code.Stind_I4: Stind_X("int32_t"); break;
					case Code.Stind_I8: Stind_X("int64_t"); break;
					case Code.Stind_R4: Stind_X("float"); break;
					case Code.Stind_R8: Stind_X("double"); break;
					//case Code.Stind_Ref: Stind_X(); break;

					//case Code.Stelem_I: Stelem_X(instruction); break;// as void* (native int)
					case Code.Stelem_I1: Stelem_X(instruction); break;
					case Code.Stelem_I2: Stelem_X(instruction); break;
					case Code.Stelem_I4: Stelem_X(instruction); break;
					case Code.Stelem_I8: Stelem_X(instruction); break;
					case Code.Stelem_R4: Stelem_X(instruction); break;
					case Code.Stelem_R8: Stelem_X(instruction); break;
					case Code.Stelem_Ref: Stelem_X(instruction); break;

					case Code.Initobj:
					{
						var item = stack.Pop();
						var type = (ByReferenceType)item.type;
						writer.WriteLinePrefix($"memset({item.value}, 0, sizeof({GetTypeReferenceFullName(type.ElementType, true, false)}));");
						break;
					}

					case Code.Pop: stack.Pop(); break;

					// flow operations
					case Code.Br: BranchUnconditional(instruction, "x8"); break;
					case Code.Br_S: BranchUnconditional(instruction, "x4"); break;

					case Code.Brfalse: BranchBooleanCondition(instruction, false, "x8"); break;
					case Code.Brfalse_S: BranchBooleanCondition(instruction, false, "x4"); break;
					case Code.Brtrue: BranchBooleanCondition(instruction, true, "x8"); break;
					case Code.Brtrue_S: BranchBooleanCondition(instruction, true, "x4"); break;

					case Code.Beq: BranchCompareCondition(instruction, "==", "x8", false); break;
					case Code.Beq_S: BranchCompareCondition(instruction, "==", "x4", false); break;
					case Code.Bge: BranchCompareCondition(instruction, ">=", "x8", false); break;
					case Code.Bge_S: BranchCompareCondition(instruction, ">=", "x4", false); break;
					case Code.Bge_Un: BranchCompareCondition(instruction, ">=", "x8", true); break;
					case Code.Bge_Un_S: BranchCompareCondition(instruction, ">=", "x4", true); break;
					case Code.Bgt: BranchCompareCondition(instruction, ">", "x8", false); break;
					case Code.Bgt_S: BranchCompareCondition(instruction, ">", "x4", false); break;
					case Code.Bgt_Un: BranchCompareCondition(instruction, ">", "x8", true); break;
					case Code.Bgt_Un_S: BranchCompareCondition(instruction, ">", "x4", true); break;
					case Code.Ble: BranchCompareCondition(instruction, "<=", "x8", false); break;
					case Code.Ble_S: BranchCompareCondition(instruction, "<=", "x4", false); break;
					case Code.Ble_Un: BranchCompareCondition(instruction, "<=", "x8", true); break;
					case Code.Ble_Un_S: BranchCompareCondition(instruction, "<=", "x4", true); break;
					case Code.Blt: BranchCompareCondition(instruction, "<", "x8", false); break;
					case Code.Blt_S: BranchCompareCondition(instruction, "<", "x4", false); break;
					case Code.Blt_Un: BranchCompareCondition(instruction, "<", "x8", true); break;
					case Code.Blt_Un_S: BranchCompareCondition(instruction, "<", "x4", true); break;
					case Code.Bne_Un: BranchCompareCondition(instruction, "!=", "x8", true); break;
					case Code.Bne_Un_S: BranchCompareCondition(instruction, "!=", "x4", true); break;

					case Code.Ret:
					{
						if (body.Method.ReturnType.MetadataType != MetadataType.Void)
						{
							var item = stack.Pop();
							writer.WriteLinePrefix($"return {item.value};");
						}
						break;
					}

					case Code.Throw:
					{
						var e = stack.Pop();
						writer.WriteLinePrefix($"IL2X_ThreadExceptionObject = {e.value};");
						writer.WriteLinePrefix($"longjmp(IL2X_ThreadExceptionJmpBuff, 1); /* throw {e.type.FullName} */");
						break;
					}

					case Code.Leave: Leave_X(instruction, "x8"); break;
					case Code.Leave_S: Leave_X(instruction, "x4"); break;

					default: throw new Exception("Unsuported opcode type: " + instruction.OpCode.Code);
				}
			}
			
			if (stack.Count != 0)
			{
				string failedInstructions = string.Empty;
				foreach (var instruction in body.Instructions) failedInstructions += instruction.ToString() + Environment.NewLine;
				throw new Exception($"Instruction translation error! Evaluation stack for method {body.Method} didn't fully unwind: {stack.Count}{Environment.NewLine}{failedInstructions}");
			}

			// write evaluation stack locals
			foreach (var stackType in stackTypes)
			{
				for (int typeIndex = 0; typeIndex != stackType.Value.Count; ++typeIndex)
				{
					var type = stackType.Value[typeIndex];
					string localVarName = FormatedEvalStackTypeName(stackType.Key, typeIndex);
					origWriter.WriteLinePrefix($"{GetTypeReferenceFullName(type)} {localVarName};");
				}
			}

			// write cache to file stream
			writer = origWriter;
			writer.Flush();
			writer.BaseStream.Flush();
			writerCache.Flush();
			writerCache.BaseStream.Flush();
			writerCache.BaseStream.Position = 0;
			writerCache.BaseStream.CopyTo(writer.BaseStream);
		}

		private LocalVariable WriteVariableDefinition(VariableDefinition variable, ref bool initLocal)
		{
			var local = new LocalVariable();
			local.definition = variable;
			if (!activeMethodDebugInfo.TryGetName(variable, out local.name))
			{
				local.name = "var";
			}
			
			local.name = $"l_{local.name}_{variable.Index}";
			writer.WritePrefix($"{GetTypeReferenceFullName(variable.VariableType)} {local.name}");
			if (initLocal && (variable.VariableType.IsPrimitive || !variable.VariableType.IsValueType || variable.VariableType.IsPointer || variable.VariableType.IsByReference))
			{
				writer.Write(" = 0");
			}
			else
			{
				initLocal = false;
			}
			writer.WriteLine(';');
			return local;
		}
		#endregion

		#region Core name resolution
		private string AddModulePrefix(MemberReference member, in string value)
		{
			var memberDef = (MemberReference)GetMemberDefinition(member);
			return $"{memberDef.Module.Name.Replace(".", "")}_{value}";
		}

		private string GetRuntimeTypeDefinitionFullName(TypeDefinition type)
		{
			return GetTypeDefinitionFullName(type) + "_RTTYPE";
		}

		private string GetRuntimeTypeReferenceFullName(TypeDefinition type)
		{
			return GetTypeDefinitionFullName(type) + "_RTTYPE_OBJ";
		}

		private string GetRuntimeTypeMetadataFullName(TypeDefinition type)
		{
			return GetTypeDefinitionFullName(type) + "_RTTYPE_METADATA";
		}

		protected override string GetTypeDefinitionName(TypeDefinition type)
		{
			string result = base.GetTypeDefinitionName(type);
			if (type.HasGenericParameters) result += '_' + GetGenericParameters(type);
			return result;
		}

		protected override string GetTypeReferenceName(TypeReference type)
		{
			string result = base.GetTypeReferenceName(type);
			if (type.IsGenericInstance) result += '_' + GetGenericArguments((IGenericInstance)type);
			return result;
		}
		
		protected override string GetTypeDefinitionFullName(TypeDefinition type, bool allowPrefix = true)
		{
			string result = base.GetTypeDefinitionFullName(type, allowPrefix);
			if (allowPrefix)
			{
				result = AddModulePrefix(type, result);
				result = "t_" + result;
			}
			return result;
		}

		private string GetPrimitiveName(TypeReference type)
		{
			switch (type.MetadataType)
			{
				case MetadataType.Boolean: return "char";
				case MetadataType.Char: return "char16_t";
				case MetadataType.SByte: return "int8_t";
				case MetadataType.Byte: return "uint8_t";
				case MetadataType.Int16: return "int16_t";
				case MetadataType.UInt16: return "uint16_t";
				case MetadataType.Int32: return "int32_t";
				case MetadataType.UInt32: return "uint32_t";
				case MetadataType.Int64: return "int64_t";
				case MetadataType.UInt64: return "uint64_t";
				case MetadataType.Single: return "float";
				case MetadataType.Double: return "double";
				case MetadataType.IntPtr: return "intptr_t";
				case MetadataType.UIntPtr: return "uintptr_t";
				default: throw new NotImplementedException("Unsupported primitive: " + type.MetadataType);
			}
		}
		
		protected override string GetTypeReferenceFullName(TypeReference type, bool allowPrefix = true, bool allowSymbols = true)
		{
			string result;

			// resolve referencing
			string refSuffix = string.Empty;
			if (allowSymbols)
			{
				while (type.IsByReference || type.IsPointer || type.IsArray || type.IsPinned)
				{
					if (type.IsPointer)
					{
						refSuffix += '*';
						var ptrType = (PointerType)type;
						type = ptrType.ElementType;
					}
					else if (type.IsByReference)
					{
						refSuffix += '*';
						var refType = (ByReferenceType)type;
						type = refType.ElementType;
					}
					else if (type.IsArray)
					{
						refSuffix += '*';
						var arrayType = (ArrayType)type;
						type = arrayType.ElementType;
					}
					else if (type.IsPinned)
					{
						var pinType = (PinnedType)type;
						type = pinType.ElementType;
					}
				}

				if (!type.IsValueType && type.MetadataType != MetadataType.Void) refSuffix += '*';
			}

			// resolve type name
			if (type.MetadataType == MetadataType.Void)
			{
				result = "void";
			}
			else if (type.IsPrimitive)
			{
				if (allowSymbols) result = GetPrimitiveName(type);
				else result = base.GetTypeReferenceFullName(type, allowPrefix, allowSymbols);
			}
			else
			{
				result = base.GetTypeReferenceFullName(type, allowPrefix, allowSymbols);
				if (allowPrefix)
				{
					result = AddModulePrefix(type, result);
					result = "t_" + result;
				}
				if (allowSymbols)
				{
					var def = GetTypeDefinition(type);
					if (def.IsEnum)
					{
						if (!def.HasFields) throw new Exception("Enum has no fields: " + type.Name);
						result = GetTypeReferenceFullName(def.Fields[0].FieldType);
					}
					else
					{
						if (type.HasGenericParameters) result += '_' + GetGenericParameters(type);
					}
				}
			}
			
			return result + refSuffix;
		}

		protected override string GetMethodDefinitionName(MethodDefinition method)
		{
			string result = base.GetMethodDefinitionName(method);
			if (method.HasGenericParameters) result += '_' + GetGenericParameters(method);
			return result;
		}

		protected override string GetMethodReferenceName(MethodReference method)
		{
			string result = base.GetMethodReferenceName(method);
			if (method.HasGenericParameters) result += '_' + GetGenericParameters(method);
			return result;
		}

		protected override string GetMethodDefinitionFullName(MethodDefinition method)
		{
			int count = GetBaseTypeCount(method.DeclaringType);
			int methodIndex = GetMethodOverloadIndex(method);
			string result = base.GetMethodDefinitionFullName(method);
			result = AddModulePrefix(method, result);
			return $"m_{count}_{result}_{methodIndex}";
		}

		protected override string GetMethodReferenceFullName(MethodReference method)
		{
			int count = GetBaseTypeCount(GetTypeDefinition(method.DeclaringType));
			int methodIndex = GetMethodOverloadIndex(method);
			string result = base.GetMethodReferenceFullName(method);
			result = AddModulePrefix(method, result);
			return $"m_{count}_{result}_{methodIndex}";
		}

		private string GetVirtualMethodVTabelName(MethodReference method)
		{
			string methodName = method.Name;
			ParseMemberImplementationDetail(ref methodName);
			int overloadIndex = GetVirtualMethodOverloadIndex(GetMethodDefinition(method));
			return $"vTabel_{methodName}_{overloadIndex}";
		}

		protected override string GetGenericParameterName(GenericParameter parameter)
		{
			return "gp_" + base.GetGenericParameterName(parameter);
		}

		protected override string GetGenericArgumentTypeName(TypeReference type)
		{
			return GetTypeReferenceFullName(type);
		}

		protected override string GetParameterDefinitionName(ParameterDefinition parameter)
		{
			return "p_" + base.GetParameterDefinitionName(parameter);
		}

		protected override string GetParameterReferenceName(ParameterReference parameter)
		{
			return "p_" + base.GetParameterReferenceName(parameter);
		}

		protected override string GetFieldDefinitionName(FieldDefinition field)
		{
			int count = GetBaseTypeCount(field.DeclaringType);
			string typeFullName = string.Empty;
			if (field.IsStatic) typeFullName = GetTypeDefinitionFullName(field.DeclaringType) + '_';
			return $"f_{count}_{typeFullName}{base.GetFieldDefinitionName(field)}";
		}

		protected override string GetFieldReferenceName(FieldReference field)
		{
			var fieldDef = (FieldDefinition)GetMemberDefinition(field);
			return GetFieldDefinitionName(fieldDef);
		}

		protected override string GetTypeDefinitionDelimiter()
		{
			return "_";
		}

		protected override string GetTypeReferenceDelimiter()
		{
			return "_";
		}

		protected override string GetMethodDefinitionDelimiter()
		{
			return "_";
		}

		protected override string GetMethodReferenceDelimiter()
		{
			return "_";
		}

		protected override string GetMemberDefinitionNamespaceName(in string name)
		{
			return base.GetMemberDefinitionNamespaceName(name).Replace('.', '_');
		}

		protected override string GetMemberReferenceNamespaceName(in string name)
		{
			return base.GetMemberReferenceNamespaceName(name).Replace('.', '_');
		}

		protected override char GetGenericParameterOpenBracket()
		{
			return '_';
		}

		protected override char GetGenericParameterCloseBracket()
		{
			return '_';
		}

		protected override char GetGenericParameterDelimiter()
		{
			return '_';
		}

		protected override char GetGenericArgumentOpenBracket()
		{
			return '_';
		}

		protected override char GetGenericArgumentCloseBracket()
		{
			return '_';
		}

		protected override char GetGenericArgumentDelimiter()
		{
			return '_';
		}
		#endregion
	}
}

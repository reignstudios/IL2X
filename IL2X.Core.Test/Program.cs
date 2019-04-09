using System;
using System.IO;

namespace IL2X.Core.Test
{
	class Program
	{
		static void Main(string[] args)
		{
			MonoCecilUtils.SetCustomCoreLibName("IL2X.Portable.CoreLib");
			
			const string testName = "TestApp.Portable";
			#if DEBUG
			const string target = "Debug";
			#else
			const string target = "Release";
			#endif

			string binaryPathFolder = Path.Combine(Environment.CurrentDirectory, $@"..\..\..\..\{testName}\bin\{target}\netcoreapp2.2");
			string binaryPath = Path.Combine(binaryPathFolder, $"{testName}.dll");
			if (!File.Exists(binaryPath)) throw new Exception($"{testName} doesn't exist");

			var options = new ILAssemblyTranslator_C.Options()
			{
				gc = ILAssemblyTranslator_C.GC_Type.Boehm,
				gcFolderPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\IL2X.Native"),
				ptrSize = ILAssemblyTranslator_C.Ptr_Size.Bit_64,
				endianness = ILAssemblyTranslator_C.Endianness.Little,
				storeRuntimeTypeStringLiteralMetadata = true,
				stringLiteralMemoryLocation = ILAssemblyTranslator_C.StringLiteralMemoryLocation.GlobalProgramMemory_RAM,
				ignoreInitLocalsOnAllLibs = true,
				//ignoreInitLocalsLibs = new string[] {"TestApp.Portable"}
			};

			string outputPath = $@"..\..\..\..\{testName}\bin\{target}\netcoreapp2.2\TestOutput";
			if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);
			using (var translator = new ILAssemblyTranslator_C(binaryPath, true, options, Path.Combine(Environment.CurrentDirectory, $@"..\..\..\..\{testName}\bin\{target}\netcoreapp2.2")))
			{
				translator.Translate(outputPath, true);
			}
		}
	}
}

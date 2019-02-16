using System;
using System.IO;

namespace IL2X.Core.Test
{
	class Program
	{
		static void Main(string[] args)
		{
			string binaryPathFolder = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\TestApp\bin\Debug\netcoreapp2.2");
			string binaryPath = Path.Combine(binaryPathFolder, "TestApp.dll");
			if (!File.Exists(binaryPath)) throw new Exception("TestApp doesn't exist");

			const string outputPath = @"..\..\..\..\TestApp\bin\Debug\netcoreapp2.2\TestOutput";
			if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);
			using (var translator = new ILAssemblyTranslator_C(binaryPath, false))
			{
				translator.Translate(outputPath);
			}
			/*using (var translator = new ILAssemblyTranslator_Cpp(binaryPath, false))//, "stdafx.h"))// stdafx.h || pch.h
			{
				translator.Translate(outputPath);
			}*/
		}
	}
}

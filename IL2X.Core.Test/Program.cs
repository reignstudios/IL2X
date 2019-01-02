using System;
using System.IO;

namespace IL2X.Core.Test
{
	class Program
	{
		static void Main(string[] args)
		{
			string binaryPathFolder = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\TestApp\bin\Debug\netcoreapp2.1");
			string binaryPath = Path.Combine(binaryPathFolder, "TestApp.dll");
			if (!File.Exists(binaryPath)) throw new Exception("TestApp doesn't exist");

			const string outputPath = @"..\..\..\..\TestApp\bin\Debug\netcoreapp2.1\TestOutput";
			if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);
			using (var translator = new ILTranslator_Cpp(binaryPath, "stdafx.h"))// stdafx.h || pch.h
			{
				translator.Translate(outputPath, true);
			}
		}
	}
}

using IL2X.Core;
using System;
using System.IO;
using Xunit;

namespace IL2X.Test
{
	public class Test_TestApp
	{
		[Fact]
		public void Translate_CPP()
		{
			string binaryPathFolder = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\TestApp\bin\Debug\netcoreapp2.1");
			string binaryPath = Path.Combine(binaryPathFolder, "TestApp.dll");
			Assert.True(File.Exists(binaryPath));
			string outputPath = Path.Combine(binaryPathFolder, "Translated_CPP");
			if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);
			using (var translator  = new ILTranslator_Cpp(binaryPath))
			{
				translator.Translate(outputPath);
			}
		}
	}
}

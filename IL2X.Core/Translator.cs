using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IL2X.Core
{
	public abstract class Translator
	{
		public readonly Solution solution;
		protected bool writeDebugIL;

		public Translator(Solution solution)
		{
			this.solution = solution;
		}

		public virtual void Translate(string outputDirectory, bool writeDebugIL)
		{
			this.writeDebugIL = writeDebugIL;
			if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);
			TranslateLibrary(solution.mainLibrary, outputDirectory);
		}

		private void TranslateLibrary(Library library, string outputDirectory)
		{
			foreach (var module in library.modules)
			{
				TranslateModule(module, outputDirectory);
			}
		}

		protected abstract void TranslateModule(Module module, string outputDirectory);

		protected static bool IsModuleType(TypeDefinition type)
		{
			return type.FullName == "<Module>";
		}

		protected static string FormatFilename(string filename)
		{
			return filename.Replace('.', '_');
		}

		protected static MethodJit Jit(MethodDefinition method, Module module, bool optimize)
		{
			var result = new MethodJit(method, module);
			if (optimize) result.Optimize();
			return result;
		}
	}
}

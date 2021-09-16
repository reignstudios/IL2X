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

		public Translator(Solution solution)
		{
			this.solution = solution;
		}

		public virtual void Translate(string outputDirectory)
		{
			if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);
			TranslateAssembly(solution.mainAssembly, outputDirectory);
		}

		private void TranslateAssembly(Assembly assembly, string outputDirectory)
		{
			foreach (var module in assembly.modules)
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
	}
}

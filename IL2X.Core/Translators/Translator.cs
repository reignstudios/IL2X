using IL2X.Core.Jit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IL2X.Core.Translators
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
			TranslateAssembly(solution.mainAssemblyJit, outputDirectory);
		}

		private void TranslateAssembly(AssemblyJit assembly, string outputDirectory)
		{
			foreach (var module in assembly.modules)
			{
				// translate references first
				foreach (var r in module.assemblyReferences)
				{
					TranslateAssembly(r, outputDirectory);
				}

				// translate this module
				outputDirectory = Path.Combine(outputDirectory, FormatTypeFilename(assembly.assembly.cecilAssembly.Name.Name));
				if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);
				TranslateModule(module, outputDirectory);
			}
		}

		protected abstract void TranslateModule(ModuleJit module, string outputDirectory);

		public static string FormatTypeFilename(string filename)
		{
			return filename.Replace('.', '_');
		}
	}
}

using System;
using System.IO;
using IL2X.Core;
using IL2X.Core.Translators;

namespace IL2X.CLR
{
	class Program
	{
		static void Main(string[] args)
		{
			#if DEBUG
			string config = "Debug";
			#else
			string config = "Release";
			#endif

			string path = Path.Combine(Environment.CurrentDirectory, "../../../../RayTraceBenchmark/bin");
			using (var solution = new Solution(Solution.Type.Executable, Path.Combine(path, config, "net5.0/RayTraceBenchmark.dll").Replace('/', Path.DirectorySeparatorChar)))
			{
				solution.ReLoad();
				solution.Jit();
				solution.Optimize();

				var translator = new Translator_C(solution);
				translator.Translate(Path.Combine(path, "Debug/net5.0/IL2X_Output").Replace('/', Path.DirectorySeparatorChar));
			}
		}
	}
}

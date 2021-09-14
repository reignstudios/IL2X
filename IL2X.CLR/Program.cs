using System;
using IL2X.Core;

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
			using (var solution = new Solution(Solution.Type.Executable, @$"F:\Dev\Reign\IL2X\RayTraceBenchmark\bin\{config}\net5.0\RayTraceBenchmark.dll"))
			{
				solution.ReLoad();

				var translator = new Translator_C(solution);
				translator.Translate(@"F:\Dev\Reign\IL2X\RayTraceBenchmark\bin\Debug\net5.0\IL2X_Output", false);
			}
		}
	}
}

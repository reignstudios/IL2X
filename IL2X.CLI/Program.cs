﻿using System;
using System.IO;
using IL2X.Core;
using IL2X.Core.Emitters;

namespace IL2X.CLI
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

			string projPath = Environment.CurrentDirectory;
			for (int i = 0; i != 4; ++i) projPath = Path.GetDirectoryName(projPath);
			//path = Path.Combine(Environment.CurrentDirectory, "../../../../RayTraceBenchmark/bin");
			projPath = Path.Combine(projPath, "RayTraceBenchmark/bin");
			projPath = Path.Combine(projPath, config, "net8.0/RayTraceBenchmark.dll");
			projPath = projPath.Replace('/', Path.DirectorySeparatorChar);
			using (var solution = new Solution(Solution.Type.Executable, projPath))
			{
				solution.ReLoad();
				solution.Jit();
				//solution.Optimize();

				string outputPath = Path.GetDirectoryName(projPath);
				outputPath = Path.Combine(outputPath, "IL2X_Output");

				var emitter = new Emmiter_C(solution);
				emitter.Translate(outputPath);
			}
		}
	}
}

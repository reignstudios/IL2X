using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Mono.Cecil;

namespace IL2X.Core
{
	public sealed class Solution : IDisposable
	{
		public enum Type
		{
			Executable,
			Library
		}

		public readonly Type type;
		public readonly string dllPath, dllFolderPath;
		public Library mainLibrary, coreLibrary;
		public List<Library> libraries;

		public Solution(Type type, string dllPath)
		{
			this.type = type;
			this.dllPath = dllPath;
			dllFolderPath = Path.GetDirectoryName(dllPath);
			string ext = Path.GetExtension(dllPath);
			if (ext != ".dll") throw new NotSupportedException("File must be '.dll'");
		}

		public void Dispose()
		{
			if (libraries != null)
			{
				foreach (var library in libraries) library.Dispose();
				libraries = null;
			}
		}

		internal Library AddLibrary(string binaryPath)
		{
			var library = new Library(this, binaryPath);
			libraries.Add(library);
			return library;
		}

		public void ReLoad()
		{
			libraries = new List<Library>();

			// load assemblies
			using (var assemblyResolver = new DefaultAssemblyResolver())
			{
				assemblyResolver.AddSearchDirectory(dllFolderPath);
				mainLibrary = AddLibrary(dllPath);
				mainLibrary.Load(assemblyResolver);
			}
		}
	}
}

using System.IO;

namespace IL2X.Core
{
	public static class MonoCecilUtils
	{
		public static void SetCustomCoreLibName(string name)
		{
			Mono.Cecil.TypeSystem.CustomCoreLibName = name;
		}
	}
}

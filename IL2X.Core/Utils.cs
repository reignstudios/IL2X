using System;
using System.Collections.Generic;
using System.Text;

namespace IL2X.Core
{
	static class Utils
	{
		public static void DisposeInstance<T>(ref T instance) where T : class, IDisposable
		{
			if (instance != null)
			{
				instance.Dispose();
				instance = null;
			}
		}
	}
}

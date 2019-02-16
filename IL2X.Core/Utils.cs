using System;
using System.Collections.Generic;
using System.Text;

namespace IL2X.Core
{
	static class Utils
	{
		public static void DisposeInstances<T>(ref IEnumerable<T> instances) where T : class, IDisposable
		{
			if (instances != null)
			{
				foreach (var instance in instances)
				{
					instance.Dispose();
				}

				instances = null;
			}
		}

		public static void DisposeInstance<T>(ref T instance) where T : class, IDisposable
		{
			if (instance != null)
			{
				instance.Dispose();
				instance = null;
			}
		}

		public static bool Contains(this StringBuilder _this, char value)
		{
			for (int i = 0; i != _this.Length; ++i)
			{
				if (_this[i] == value) return true;
			}

			return false;
		}
	}
}

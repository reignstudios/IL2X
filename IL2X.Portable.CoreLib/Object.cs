using System.Runtime.InteropServices;

namespace System
{
	public class Object
	{
		private RuntimeType _runtimeType;
		public Type GetType()
		{
			return _runtimeType;
		}
	}
}

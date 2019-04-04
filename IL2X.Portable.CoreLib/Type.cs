using System.Runtime.CompilerServices;

namespace System
{
	public abstract class Type
	{
		public Type BaseType
		{
			get
			{
				return null;
			}
		}

		public string Name
		{
			get
			{
				return null;
			}
		}

		public string FullName
		{
			get
			{
				return null;
			}
		}

		public virtual RuntimeTypeHandle TypeHandle { get { throw new NotSupportedException(); } }

		[MethodImpl(MethodImplOptions.InternalCall)]
		public static extern Type GetTypeFromHandle(RuntimeTypeHandle handle);
	}
}

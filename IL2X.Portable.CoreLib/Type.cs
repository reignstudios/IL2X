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

		public abstract string FullName { get; }

		public virtual RuntimeTypeHandle TypeHandle { get { throw new NotSupportedException(); } }

		[MethodImpl(MethodImplOptions.InternalCall)]
		public static extern Type GetTypeFromHandle(RuntimeTypeHandle handle);
	}
}

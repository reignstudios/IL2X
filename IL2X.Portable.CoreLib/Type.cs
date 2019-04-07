using System.Runtime.CompilerServices;

namespace System
{
	public abstract class Type
	{
		public abstract Type BaseType { get; }
		public abstract string FullName { get; }

		public virtual RuntimeTypeHandle TypeHandle { get { throw new NotSupportedException(); } }

		[MethodImpl(MethodImplOptions.InternalCall)]
		public static extern Type GetTypeFromHandle(RuntimeTypeHandle handle);
	}
}

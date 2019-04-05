using System.Runtime.CompilerServices;

namespace System
{
	internal sealed class RuntimeType : TypeInfo//, ICloneable
	{
		public override RuntimeTypeHandle TypeHandle => new RuntimeTypeHandle(this);

		private string _FullName;
		public override string FullName => _FullName;
	}
}

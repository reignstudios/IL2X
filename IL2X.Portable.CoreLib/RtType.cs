using System.Runtime.CompilerServices;

namespace System
{
	internal sealed class RuntimeType : TypeInfo//, ICloneable
	{
		public override RuntimeTypeHandle TypeHandle => new RuntimeTypeHandle(this);

		private string _fullName;
		public override string FullName => _fullName;
	}
}

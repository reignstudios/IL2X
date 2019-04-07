using System.Runtime.CompilerServices;

namespace System
{
	internal sealed class RuntimeType : TypeInfo//, ICloneable
	{
		public override RuntimeTypeHandle TypeHandle => new RuntimeTypeHandle(this);

		private Type _baseType;
		public override Type BaseType => _baseType;

		private string _fullName;
		public override string FullName => _fullName;
	}
}

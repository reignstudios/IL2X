namespace System
{
	internal sealed class RuntimeType : TypeInfo//, ICloneable
	{
		public override RuntimeTypeHandle TypeHandle => new RuntimeTypeHandle(this);
	}
}

using System;

namespace IL2X
{
	[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
	public class NativeTypeAttribute : Attribute
	{
		public readonly NativeTarget target;
		public readonly string typeName;

		public NativeTypeAttribute(NativeTarget target, string typeName, params string[] dependencyIncludes)
		{
			this.target = target;
			this.typeName = typeName;
		}
	}
}

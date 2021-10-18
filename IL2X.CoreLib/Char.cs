using IL2X;

namespace System
{
	[NativeType(NativeTarget.C, "uint16_t", "stdint.h")]
	public readonly struct Char
	{
		/*public const Char MaxValue = '\uffff';
		public const Char MinValue = '\0';

		public unsafe string ToString()
		{
			return new string(this, 1);
		}*/
	}
}

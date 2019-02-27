using System.Runtime.CompilerServices;

namespace System
{
	public sealed class String
	{
		[NonSerialized]
		private int _stringLength;

		[NonSerialized]
		private char _firstChar;

		//[Intrinsic]
        //public static readonly string Empty;

		//[MethodImpl(MethodImplOptions.InternalCall)]
		//public extern String(char[] value);

		// Gets the character at a specified position.
        //
        /*[IndexerName("Chars")]
        public extern char this[int index]
        {
            [MethodImpl(MethodImplOptions.InternalCall)]
            get;
        }*/

		// Gets the length of this string
        //
        // This is a EE implemented function so that the JIT can recognise it specially
        // and eliminate checks on character fetches in a loop like:
        //        for(int i = 0; i < str.Length; i++) str[i]
        // The actual code generated for this will be one instruction and will be inlined.
        //
        /*public extern int Length
        {
            [MethodImplAttribute(MethodImplOptions.InternalCall)]
            get;
        }*/

		[MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern string FastAllocateString(int length);
	}
}

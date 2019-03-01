using System.Runtime.CompilerServices;

namespace System
{
	public sealed class String
	{
		[NonSerialized]
		private int _stringLength;

		[NonSerialized]
		internal char _firstChar;// TODO: change back to private when Console is implemented correctly

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
        public extern int Length
        {
            [MethodImpl(MethodImplOptions.InternalCall)]
            get;
        }

		[MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern string FastAllocateString(int length);
		
        public static bool IsNullOrEmpty(string value)
        {
            return (value == null || 0u >= (uint)value.Length) ? true : false;
        }

		public static string Concat(string str0, string str1)
        {
            /*if (IsNullOrEmpty(str0))
            {
                if (IsNullOrEmpty(str1))
                {
                    return string.Empty;
                }
                return str1;
            }

            if (IsNullOrEmpty(str1))
            {
                return str0;
            }

            int str0Length = str0.Length;

            string result = FastAllocateString(str0Length + str1.Length);

            FillStringChecked(result, 0, str0);
            FillStringChecked(result, str0Length, str1);

            return result;*/
			return null;
        }
	}
}

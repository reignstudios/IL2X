using IL2X;

namespace System
{
    public static class Console
    {
		[NativeExternC]
		private static unsafe extern int wprintf(char* text);
		
        public static unsafe void Write(string s)
        {
			fixed (char* ptr = &s._firstChar) wprintf(ptr);
        }

        public static void WriteLine(string s) => Write(s + Environment.NewLine);
        public static void WriteLine() => Write(Environment.NewLine);
    }
}

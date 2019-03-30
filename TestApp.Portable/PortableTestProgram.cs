using System;

namespace TestApp.Portable
{
	static class Program
	{
		static void Foo(int i)
		{
			i = checked(i + 22);
			Console.Write("Done!");
		}

		static void Main()
		{
			Foo(44);
		}
	}
}
using System;

class A
{
	public int i;
}

class B : A
{
	public int i2;
}

struct MyStruct
{
	public int i;
}

namespace TestApp.Portable
{
	static class Program
	{
		static void Main()//string[] args)
		{
			//Console.WriteLine("Hello World!");
			int i = 123;
			var b = new B();
			var m = new MyStruct();
			int i2 = i;
		}
	}
}

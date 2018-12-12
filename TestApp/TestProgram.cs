using System;

namespace TestApp
{
	class Program
	{
		static void Main(string[] args)
		{
			Console.WriteLine("Hello World!");
		}
	}

	namespace SubNamespace
	{
		class MyClass2
		{
			
		}

		abstract class MyClass
		{
			public struct MyStruct
			{
				unsafe fixed byte myFixedBuff[256];
			}

			static MyStruct i;
			public MyClass2 i2;
			internal MyClass2 i3;
			protected MyStruct i4;

			internal int Foo(int i, int i2)
			{
				return i + i2;
			}

			protected abstract void Boo();
		}
	}

	interface MyInterface
	{
		
	}

	enum MyEnum
	{
		A, B, C
	}
}

class A
{
	int i = 123;
}

class B : A
{

}
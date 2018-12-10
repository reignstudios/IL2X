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
		class MyClass
		{
			struct MyStruct
			{

			}

			MyStruct i;
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
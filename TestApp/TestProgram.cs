using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestApp
{
	interface MyInterface
	{
		void Foo();
	}

	class BaseProgram<T> : MyInterface
	{
		public List<List<T>> nestedT;
		public T myT;
		public virtual void Foo() { }
		public E MyTFoo<E>(E input)
		{
			return input;
		}

		public T MyTFoo(T input)
		{
			return input;
		}
	}

	class Program : BaseProgram<int>
	{
		int[] array = new int[123];
		Program[] array2 = new Program[123];
		List<int> list = new List<int>();

		static void Main(string[] args)
		{
			Console.WriteLine("Hello World!");
			var b = new BaseProgram<float>();
		}

		public void Resize(ref int[] array)
		{
			
		}

		public override void Foo()
		{
			base.Foo();
			var result = list.Exists(x => x == 0);
		}

		private async Task MyAsyncMethod()
		{
			await Task.Delay(100);
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
				unsafe fixed float myFixedBuff2[128];
				unsafe byte** myPtr;
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
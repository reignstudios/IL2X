using System;

namespace TestApp.Portable
{
	public class MyException : Exception
	{

	}

	/*class A
	{
		public virtual void Foo()
		{
			Console.WriteLine("Class A");
		}
	}

	class B : A
	{
		public override void Foo()
		{
			Console.WriteLine("Class B");
			base.Foo();
		}
	}

	struct TestStruct
	{
		
	}*/

	static class Program
	{
        //static void Foo(int i)
        //{
        //    //i = checked(i + 22);
        //    Console.WriteLine("Done: (你好 + こんにちは)" + string.Empty + "yahoo");
        //}

        static void Main()
		{
			/*A a = new B();
			a.Foo();
			Console.WriteLine("B Type: " + a.GetType().ToString());
			Console.WriteLine("B Type: " + a.GetType().Name);

			var type = typeof(object);
			Console.WriteLine("Type: " + type.FullName);
			Console.WriteLine("String Type: ".GetType().FullName);*/

			//Foo(44);
			Console.WriteLine("Starting");
			try
			{
				throw new Exception("Test exception!");
			}
			catch (MyException e)
			{
				Console.WriteLine("Caught my ex: " + e.Message);
			}
			catch (Exception e)
			{
				Console.WriteLine("Caught ex: " + e.Message);
			}
			Console.WriteLine("Should not hit");
		}
	}
}
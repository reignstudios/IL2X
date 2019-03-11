/*using System;

class A
{
	public int i;
}

class B : A
{
	public int i2;
}

struct Vec2
{
	public float x, y;

	public Vec2(float x, float y)
	{
		this.x = x;
		this.y = y;
	}

	public static Vec2 operator+(Vec2 p1, Vec2 p2)
	{
		return new Vec2(p1.x + p2.x, p1.y + p2.y);
	}

	public static bool Foo(Vec2 value)
	{
		return true;
	}
}

namespace TestApp.Portable
{
	static class Program
	{
		const int myInt = 55;
		static string myString = "Wow";

		static Program()
		{
			myString = "Wow2";
		}

		static void Main()//string[] args)
		{
			//Console.WriteLine("Hello World!");
			int i = 123;
			var b = new B();
			var x = new Vec2();
			var y = new Vec2();
			//var z = x + new Vec2(22, 33);
			//var z2 = x.x + y.y;
			var z = (x.x == (y.y + 5.0f * y.x) && Vec2.Foo(new Vec2(0, 0))) ? x : y;
			//var z = (Vec2.Foo(new Vec2(0, 0))) ? x : y;
			int i2 = i;
			var array = new int[128];
			array[0] = 33;
			i2 = array[0];

			string hi = "Hello .NET World!";
			hi = myString;
			while (true)
			{
				if (!string.IsNullOrEmpty(hi)) Console.Write(hi);
				//Console.Write("Hello");
			}
		}
	}
}
*/

/*namespace TestApp.Portable
{
	static class Program
	{
		static int i;

		static void Main()
		{
			START:;
			System.Console.Write("Start");
			if (i == 3)
			{
				goto END;
				//while (true){ }
			}
			else if (i == 2)
			{
				goto END;
			}
			goto START;
			END:;
		}
	}
}*/
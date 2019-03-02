using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Text;

namespace IL2X.Core.EvaluationStack
{
	interface IStack
	{
		string GetValueName();
	}

	sealed class LocalVariable
	{
		public VariableDefinition definition;
		public string name;
	}

	sealed class Stack_LocalVariable : IStack
	{
		public readonly LocalVariable variable;
		public readonly bool isAddress;

		public Stack_LocalVariable(LocalVariable variable, bool isAddress)
		{
			this.variable = variable;
			this.isAddress = isAddress;
		}

		public string GetValueName()
		{
			if (isAddress) return '&' + variable.name;
			return variable.name;
		}
	}

	sealed class Stack_ParameterVariable : IStack
	{
		public readonly string name;

		public Stack_ParameterVariable(string name)
		{
			this.name = name;
		}

		public string GetValueName()
		{
			return name;
		}
	}

	sealed class Stack_ArrayElement : IStack
	{
		public readonly string expression;

		public Stack_ArrayElement(string expression)
		{
			this.expression = expression;
		}

		public string GetValueName()
		{
			return expression;
		}
	}

	sealed class Stack_ConditionalExpression : IStack
	{
		public readonly string expression;

		public Stack_ConditionalExpression(string expression)
		{
			this.expression = expression;
		}

		public string GetValueName()
		{
			return expression;
		}
	}

	sealed class Stack_FieldVariable : IStack
	{
		public readonly string name;
		public readonly bool isAddress;

		public Stack_FieldVariable(string name, bool isAddress)
		{
			this.name = name;
			this.isAddress = isAddress;
		}

		public string GetValueName()
		{
			if (isAddress) return '&' + name;
			return name;
		}
	}

	sealed class Stack_Null : IStack
	{
		public readonly string value;

		public Stack_Null(string value)
		{
			this.value = value;
		}

		public string GetValueName()
		{
			return value;
		}
	}

	sealed class Stack_SByte : IStack
	{
		public readonly sbyte value;

		public Stack_SByte(sbyte value)
		{
			this.value = value;
		}

		public string GetValueName()
		{
			return value.ToString();
		}
	}

	sealed class Stack_Int32 : IStack
	{
		public readonly int value;

		public Stack_Int32(int value)
		{
			this.value = value;
		}

		public string GetValueName()
		{
			return value.ToString();
		}
	}

	sealed class Stack_String : IStack
	{
		public readonly string value;

		public Stack_String(string value)
		{
			this.value = value;
		}

		public string GetValueName()
		{
			return value;
		}
	}

	sealed class Stack_Cast : IStack
	{
		public readonly string value;

		public Stack_Cast(string value)
		{
			this.value = value;
		}

		public string GetValueName()
		{
			return value;
		}
	}

	sealed class Stack_Call : IStack
	{
		public readonly MethodReference method;
		public readonly string methodInvoke;

		public Stack_Call(MethodReference method, string methodInvoke)
		{
			this.method = method;
			this.methodInvoke = methodInvoke;
		}

		public string GetValueName()
		{
			return methodInvoke;
		}
	}
}

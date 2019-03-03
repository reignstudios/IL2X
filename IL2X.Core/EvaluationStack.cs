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
		string GetAccessToken();
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

		public string GetAccessToken()
		{
			return (variable.definition.VariableType.IsValueType && !isAddress) ? "." : "->";
		}
	}

	sealed class Stack_ParameterVariable : IStack
	{
		public readonly string name;
		public readonly bool isSelf;
		public readonly string accessToken;

		public Stack_ParameterVariable(string name, bool isSelf, string accessToken)
		{
			this.name = name;
			this.isSelf = isSelf;
			this.accessToken = accessToken;
		}

		public string GetValueName()
		{
			return name;
		}

		public string GetAccessToken()
		{
			return accessToken;
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

		public string GetAccessToken()
		{
			return "->";
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

		public string GetAccessToken()
		{
			return ".";
		}
	}

	sealed class Stack_PrimitiveOperation : IStack
	{
		public readonly string expression;

		public Stack_PrimitiveOperation(string expression)
		{
			this.expression = expression;
		}

		public string GetValueName()
		{
			return expression;
		}

		public string GetAccessToken()
		{
			return ".";
		}
	}

	sealed class Stack_FieldVariable : IStack
	{
		public readonly FieldDefinition field;
		public readonly string name;
		public readonly bool isAddress;

		public Stack_FieldVariable(FieldDefinition field, string name, bool isAddress)
		{
			this.field = field;
			this.name = name;
			this.isAddress = isAddress;
		}

		public string GetValueName()
		{
			if (isAddress) return '&' + name;
			return name;
		}

		public string GetAccessToken()
		{
			return (field.FieldType.IsValueType && !isAddress) ? "." : "->";
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

		public string GetAccessToken()
		{
			throw new NotImplementedException("Null access token not supported");
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

		public string GetAccessToken()
		{
			return ".";
		}
	}

	sealed class Stack_Int16 : IStack
	{
		public readonly short value;

		public Stack_Int16(short value)
		{
			this.value = value;
		}

		public string GetValueName()
		{
			return value.ToString();
		}

		public string GetAccessToken()
		{
			return ".";
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

		public string GetAccessToken()
		{
			return ".";
		}
	}

	sealed class Stack_Int64 : IStack
	{
		public readonly long value;

		public Stack_Int64(long value)
		{
			this.value = value;
		}

		public string GetValueName()
		{
			return value.ToString();
		}

		public string GetAccessToken()
		{
			return ".";
		}
	}

	sealed class Stack_Float : IStack
	{
		public readonly float value;

		public Stack_Float(float value)
		{
			this.value = value;
		}

		public string GetValueName()
		{
			string result = value.ToString();
			if (!result.Contains('.')) return result + ".0f";
			return result;
		}

		public string GetAccessToken()
		{
			return ".";
		}
	}

	sealed class Stack_Double : IStack
	{
		public readonly double value;

		public Stack_Double(double value)
		{
			this.value = value;
		}

		public string GetValueName()
		{
			if (value == 0.0) return value.ToString() + ".0";
			return value.ToString();
		}

		public string GetAccessToken()
		{
			return ".";
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

		public string GetAccessToken()
		{
			return "->";
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

		public string GetAccessToken()
		{
			return "->";
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

		public string GetAccessToken()
		{
			return method.ReturnType.IsValueType ? "." : "->";
		}
	}
}

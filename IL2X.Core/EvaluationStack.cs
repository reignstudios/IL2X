using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Text;

namespace IL2X.Core.EvaluationStack
{
	sealed class BranchJumpModify
	{
		public int offset, stackCountBeforeJump;
		public BranchJumpModify(int offset, int stackCountBeforeJump)
		{
			this.offset = offset;
			this.stackCountBeforeJump = stackCountBeforeJump;
		}
	}

	sealed class LocalVariable
	{
		public VariableDefinition definition;
		public string name;
	}

	interface IStack
	{
		string GetValueName();
		string GetAccessToken();
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
		public readonly ParameterDefinition definition;
		public readonly string name;
		public readonly bool isSelf, isAddress;
		public readonly string accessToken;

		public Stack_ParameterVariable(ParameterDefinition definition, string name, bool isSelf, bool isAddress, string accessToken)
		{
			this.definition = definition;
			this.name = name;
			this.isSelf = isSelf;
			this.isAddress = isAddress;
			this.accessToken = accessToken;
		}

		public string GetValueName()
		{
			if (isAddress) return '&' + name;
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

	sealed class Stack_BitwiseOperation : IStack
	{
		public readonly string expression;

		public Stack_BitwiseOperation(string expression)
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
		public readonly MetadataType primitiveType;

		public Stack_PrimitiveOperation(string expression, MetadataType primitiveType)
		{
			this.expression = expression;
			this.primitiveType = primitiveType;
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

	sealed class Stack_Negate : IStack
	{
		public readonly string value;

		public Stack_Negate(string value)
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

	sealed class Stack_Byte : IStack
	{
		public readonly byte value;

		public Stack_Byte(byte value)
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

	sealed class Stack_UInt16 : IStack
	{
		public readonly ushort value;

		public Stack_UInt16(ushort value)
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

	sealed class Stack_UInt32 : IStack
	{
		public readonly uint value;

		public Stack_UInt32(uint value)
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

	sealed class Stack_UInt64 : IStack
	{
		public readonly ulong value;

		public Stack_UInt64(ulong value)
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

	sealed class Stack_Single : IStack
	{
		public readonly float value;

		public Stack_Single(float value)
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
		public readonly MetadataType type;

		public Stack_Cast(string value, MetadataType type)
		{
			this.value = value;
			this.type = type;
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

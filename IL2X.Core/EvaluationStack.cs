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

	abstract class Stack_Typed : IStack
	{
		public readonly string name;
		public readonly TypeReference type;
		public readonly bool isAddress;

		public Stack_Typed(string name, TypeReference type, bool isAddress)
		{
			this.name = name;
			this.type = type;
			this.isAddress = isAddress;
		}

		public virtual string GetValueName()
		{
			if (isAddress) return $"(&{name})";
			return name;
		}

		public virtual string GetAccessToken()
		{
			return (type.IsValueType && !isAddress) ? "." : "->";
		}
	}

	sealed class Stack_LocalVariable : Stack_Typed
	{
		public readonly LocalVariable variable;

		public Stack_LocalVariable(LocalVariable variable, bool isAddress)
		: base(variable.name, variable.definition.VariableType, isAddress)
		{
			this.variable = variable;
		}
	}

	sealed class Stack_ParameterVariable : Stack_Typed
	{
		public readonly ParameterDefinition definition;
		public readonly bool isSelf;
		public readonly string accessToken;

		public Stack_ParameterVariable(ParameterDefinition definition, string name, bool isSelf, bool isAddress, string accessToken)
		: base(name, definition.ParameterType, isAddress)
		{
			this.definition = definition;
			this.isSelf = isSelf;
			this.accessToken = accessToken;
		}

		public override string GetAccessToken()
		{
			if (isSelf) return accessToken;
			else return base.GetAccessToken();
		}
	}

	sealed class Stack_FieldVariable : Stack_Typed
	{
		public readonly FieldDefinition field;

		public Stack_FieldVariable(FieldDefinition field, string name, bool isAddress)
		: base(name, field.FieldType, isAddress)
		{
			this.field = field;
		}
	}

	sealed class Stack_ArrayElement : Stack_Typed
	{
		public readonly ArrayType arrayType;
		public readonly string expression;

		public Stack_ArrayElement(ArrayType arrayType, string expression)
		: base (expression, arrayType.ElementType, false)
		{
			this.arrayType = arrayType;
			this.expression = expression;
		}

		public override string GetValueName()
		{
			return expression;
		}

		public override string GetAccessToken()
		{
			return "->";
		}
	}

	sealed class Stack_ArrayLength : IStack
	{
		public readonly string expression;

		public Stack_ArrayLength(string expression)
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
			string result = value.ToString();//value.ToString("F5");
			if (!result.Contains('.'))
			{
				if (!result.Contains('E')) return result + ".0f";
				else return result + 'f';
			}
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

	sealed class Stack_Call : Stack_Typed
	{
		public readonly MethodReference method;
		public readonly string methodInvoke;

		public Stack_Call(MethodReference method, string methodInvoke)
		: base(methodInvoke, method.ReturnType, false)
		{
			this.method = method;
			this.methodInvoke = methodInvoke;
		}

		public override string GetValueName()
		{
			return methodInvoke;
		}
	}
}

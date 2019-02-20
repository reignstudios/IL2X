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

	class LocalVariable
	{
		public VariableDefinition definition;
		public string name;
	}

	sealed class Stack_LocalVariable : IStack
	{
		public readonly LocalVariable variable;

		public Stack_LocalVariable(LocalVariable variable)
		{
			this.variable = variable;
		}

		public string GetValueName()
		{
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

	sealed class Stack_FieldVariable : IStack
	{
		public readonly string name;

		public Stack_FieldVariable(string name)
		{
			this.name = name;
		}

		public string GetValueName()
		{
			return name;
		}
	}

	sealed class Stack_Null : IStack
	{
		public string GetValueName()
		{
			return null;
		}
	}

	sealed class Stack_Int32 : IStack
	{
		public readonly string value;

		public Stack_Int32(int value)
		{
			this.value = value.ToString();
		}

		public string GetValueName()
		{
			return value;
		}
	}
}

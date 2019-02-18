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

	class LocalVariable
	{
		public VariableDefinition definition;
		public string name;
	}
}

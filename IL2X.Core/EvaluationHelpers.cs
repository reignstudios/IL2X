using Mono.Cecil;
using Mono.Cecil.Cil;

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

	sealed class EvaluationObject
	{
		public readonly TypeReference type;
		public readonly string value;

		public EvaluationObject(TypeReference type, string value)
		{
			this.type = type;
			this.value = value;
		}
	}

	sealed class ExceptionHandlerGroup
	{
		public ExceptionHandlerGroup parent;
		public ExceptionHandler start, end, _finally;
	}
}

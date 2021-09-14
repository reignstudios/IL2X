using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;

namespace IL2X.Core
{
	struct EvaluationStackItem
	{
		public Instruction op;
		public object obj;

		public EvaluationStackItem(Instruction op, object obj)
		{
			this.op = op;
			this.obj = obj;
		}
	}

	class EvaluationStackProcessed
	{
		public List<Instruction> preProcessedEvalStack;
		public int asmIndex;
		public ASMObject asmOperation;
		public Instruction op;

		public EvaluationStackProcessed(Instruction op, Stack<EvaluationStackItem> evalStack, int asmIndex, ASMObject asmOperation)
		{
			this.op = op;
			this.asmIndex = asmIndex;
			this.asmOperation = asmOperation;
			preProcessedEvalStack = new List<Instruction>();
			foreach (var item in evalStack)
			{
				preProcessedEvalStack.Add(item.op);
			}
		}
	}
}

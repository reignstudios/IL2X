using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;

namespace IL2X.Core
{
	public enum ASMCode
	{
		ThisPtr,
		Local,
		EvalStackLocal,
		Parameter,

		PrimitiveLiteral,
		StringLiteral,

		// arithmatic
		Add,
		Sub,
		Mul,
		Div,

		// writes
		WriteLocal,

		// branching
		ReturnVoid,
		ReturnValue,
		BranchMarker,
		Branch,
		BranchIfTrue,
		BranchIfFalse,
		BranchIfEqual,
		BranchIfNotEqual,
		BranchIfGreater,
		BranchIfLess,
		BranchIfGreaterOrEqual,
		BranchIfLessOrEqual,
		CmpEqual_1_0,
		CmpNotEqual_1_0,
		CmpGreater_1_0,
		CmpLess_1_0,

		// invoke
		CallMethod
	}

	public class ASMObject
	{
		public readonly ASMCode code;
		public virtual IASMLocal GetResultLocal() => null;
		public virtual void SetResultLocal(IASMLocal local) => throw new NotSupportedException();

		public ASMObject(ASMCode code)
		{
			this.code = code;
		}
	}

	public abstract class IASMLocal : ASMObject
	{
		public virtual string name { get; set; }
		public virtual TypeReference type { get; set; }

		public IASMLocal(ASMCode code)
		: base(code)
		{}
	}

	public abstract class IASMBranch : ASMObject
	{
		public int asmJumpToIndex { get; set; }
		public ASMObject jumpToOperation { get; set; }

		public IASMBranch(ASMCode code)
		: base(code)
		{}
	}

	public class ASMThisPtr : ASMObject
	{
		public readonly static ASMThisPtr handle = new ASMThisPtr();

		public ASMThisPtr()
		: base(ASMCode.ThisPtr)
		{}
	}

	public class ASMLocal : IASMLocal
	{
		public VariableDefinition variable;
		public override TypeReference type => variable.VariableType;
		public bool canInit;

		public ASMLocal(VariableDefinition variable, string name, bool canInit)
		: base(ASMCode.Local)
		{
			this.variable = variable;
			this.name = name;
			this.canInit = canInit;
		}

		public override string ToString()
		{
			return "ASMLocal: " + name;
		}
	}

	public class ASMEvalStackLocal : IASMLocal
	{
		public int refCount = 1;

		public ASMEvalStackLocal(TypeReference type, string name)
		: base(ASMCode.EvalStackLocal)
		{
			this.type = type;
			this.name = name;
		}

		public override string ToString()
		{
			return "ASMEvalStackLocal: " + name;
		}
	}

	public class ASMParameter : ASMObject
	{
		public ParameterDefinition parameter;
		public string name;

		public ASMParameter(ParameterDefinition parameter, string name)
		: base(ASMCode.Parameter)
		{
			this.parameter = parameter;
			this.name = name;
		}

		public override string ToString()
		{
			return "ASMLocal: " + name;
		}
	}

	public class ASMPrimitiveLiteral : ASMObject
	{
		public object value;

		public ASMPrimitiveLiteral(object value)
		: base(ASMCode.PrimitiveLiteral)
		{
			this.value = value;
		}
	}

	public class ASMStringLiteral : ASMObject
	{
		public string value;

		public ASMStringLiteral(string value)
		: base(ASMCode.StringLiteral)
		{
			this.value = value;
		}
	}

	public class ASMBranchMarker : ASMObject
	{
		public int asmIndex {get; set;}

		public ASMBranchMarker(int asmIndex)
		: base(ASMCode.BranchMarker)
		{
			this.asmIndex = asmIndex;
		}
	}

	public class ASMBranch : IASMBranch
	{
		public ASMBranch(int asmJumpToIndex, ASMObject jumpToOperation)
		: base(ASMCode.Branch)
		{
			this.asmJumpToIndex = asmJumpToIndex;
			this.jumpToOperation = jumpToOperation;
		}
	}

	public class ASMBranchCondition : IASMBranch
	{
		public ASMObject[] values;

		public ASMBranchCondition(ASMCode code, ASMObject[] values, int asmJumpToIndex, ASMObject jumpToOperation)
		: base(code)
		{
			this.values = values;
			this.asmJumpToIndex = asmJumpToIndex;
			this.jumpToOperation = jumpToOperation;
		}
	}

	public class ASMCmp : ASMObject
	{
		public ASMObject value1, value2;
		public IASMLocal resultLocal;

		public ASMCmp(ASMCode code, ASMObject value1, ASMObject value2, IASMLocal resultLocal)
		: base(code)
		{
			this.value1 = value1;
			this.value2 = value2;
			this.resultLocal = resultLocal;
		}

		public override IASMLocal GetResultLocal() => resultLocal;
		public override void SetResultLocal(IASMLocal local) => resultLocal = local;
	}

	public class ASMCallMethod : ASMObject
	{
		public MethodReference method;
		public IASMLocal resultLocal;
		public List<ASMObject> parameters;

		public ASMCallMethod(ASMCode code, MethodReference method, IASMLocal resultLocal, List<ASMObject> parameters)
		: base(code)
		{
			this.method = method;
			this.resultLocal = resultLocal;
			this.parameters = parameters;
		}

		public override IASMLocal GetResultLocal() => resultLocal;
		public override void SetResultLocal(IASMLocal local) => resultLocal = local;
	}

	public class ASMArithmatic : ASMObject
	{
		public ASMObject value1, value2;
		public IASMLocal resultLocal;

		public ASMArithmatic(ASMCode code, ASMObject value1, ASMObject value2, IASMLocal resultLocal)
		: base(code)
		{
			this.value1 = value1;
			this.value2 = value2;
			this.resultLocal = resultLocal;
		}

		public override IASMLocal GetResultLocal() => resultLocal;
		public override void SetResultLocal(IASMLocal local) => resultLocal = local;
	}

	public class ASMWriteLocal : ASMObject
	{
		public ASMLocal resultLocal;
		public ASMObject value;

		public ASMWriteLocal(ASMLocal resultLocal, ASMObject value)
		: base(ASMCode.WriteLocal)
		{
			this.resultLocal = resultLocal;
			this.value = value;
		}

		public override IASMLocal GetResultLocal() => resultLocal;
		public override void SetResultLocal(IASMLocal local) => resultLocal = (ASMLocal)local;
	}

	public class ASMReturnValue : ASMObject
	{
		public ASMObject value;

		public ASMReturnValue(ASMObject value)
		: base(ASMCode.ReturnValue)
		{
			this.value = value;
		}
	}
}

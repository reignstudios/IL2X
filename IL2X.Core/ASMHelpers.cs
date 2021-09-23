using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;

namespace IL2X.Core
{
	public enum ASMCode
	{
		Field,
		ThisPtr,
		Local,
		EvalStackLocal,
		Parameter,

		PrimitiveLiteral,
		StringLiteral,

		SizeOf,

		// arithmatic
		Add,
		Sub,
		Mul,
		Div,

		// writes
		WriteLocal,
		WriteField,

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
		public virtual ASMField GetResultField() => null;
		public virtual void SetResultLocal(IASMLocal local) => throw new NotSupportedException();

		public ASMObject(ASMCode code)
		{
			this.code = code;
		}
	}

	public abstract class IASMLocal : ASMObject
	{
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

		public ASMLocal(VariableDefinition variable, bool canInit)
		: base(ASMCode.Local)
		{
			this.variable = variable;
			this.canInit = canInit;
		}

		public override string ToString()
		{
			return "ASMLocal: " + variable.Index.ToString();
		}
	}

	public class ASMEvalStackLocal : IASMLocal
	{
		public int refCount = 1;
		public int index;

		public ASMEvalStackLocal(TypeReference type, int index)
		: base(ASMCode.EvalStackLocal)
		{
			this.type = type;
			this.index = index;
		}

		public override string ToString()
		{
			return "ASMEvalStackLocal: " + index.ToString();
		}
	}

	public class ASMParameter : ASMObject
	{
		public ParameterReference parameter;

		public ASMParameter(ParameterReference parameter)
		: base(ASMCode.Parameter)
		{
			this.parameter = parameter;
		}

		public override string ToString()
		{
			return "ASMParameter: " + parameter.Name;
		}
	}

	public class ASMField : ASMObject
	{
		public object self;
		public FieldReference field;
		public TypeReference type => field.FieldType;

		public ASMField(object self, FieldReference field)
		: base(ASMCode.Field)
		{
			this.self = self;
			this.field = field;
		}

		public override string ToString()
		{
			return "ASMField: " + field.Name;
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

	public class ASMWriteField : ASMObject
	{
		public ASMField resultField;
		public ASMObject value;

		public ASMWriteField(ASMField resultField, ASMObject value)
		: base(ASMCode.WriteField)
		{
			this.resultField = resultField;
			this.value = value;
		}

		public override ASMField GetResultField() => resultField;
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

	public class ASMSizeOf : ASMObject
	{
		public TypeReference type;

		public ASMSizeOf(TypeReference type)
		: base(ASMCode.SizeOf)
		{
			this.type = type;
		}

		public int GetAgnosticJitValue(int pointerSize)
		{
			if (type is PointerType) return pointerSize;
			if (type.IsPrimitive)
			{
				switch (type.MetadataType)
				{
					case MetadataType.Boolean: return sizeof(bool);
					case MetadataType.SByte: return sizeof(SByte);
					case MetadataType.Byte: return sizeof(Byte);
					case MetadataType.Char: return sizeof(char);
					case MetadataType.Int16: return sizeof(Int16);
					case MetadataType.Int32: return sizeof(Int32);
					case MetadataType.Int64: return sizeof(Int64);
					case MetadataType.Single: return sizeof(Single);
					case MetadataType.Double: return sizeof(Double);
				}
			}
			throw new NotImplementedException("Unknown Jit size for type: " + type.ToString());
		}
	}
}

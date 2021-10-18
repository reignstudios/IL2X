using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IL2X.Core.Jit
{
	public partial class MethodJit
	{
		public readonly MethodDefinition method;
		public readonly TypeJit type;

		public LinkedList<ASMObject> asmOperations;
		public List<ASMParameter> asmParameters;
		public List<ASMLocal> asmLocals;
		public List<ASMEvalStackLocal> asmEvalLocals;
		public HashSet<TypeReference> sizeofTypes;

		private Stack<EvaluationStackItem> evalStack;
		private Dictionary<TypeReference, ASMEvalStackLocal> evalStackVars;
		private Dictionary<Instruction, List<EvaluationStackProcessed>> processedInstructionPaths;
		private int asmJumpIndex;

		public MethodJit(MethodDefinition method, TypeJit type)
		{
			this.method = method;
			this.type = type;
			type.methods.Add(this);
		}

		private TypeReference ResolveGenericParameter(TypeReference type)
		{
			var declaringType = type.DeclaringType.Resolve();
			if (declaringType == null) throw new Exception("Failed to resolve generic parameter types declaration: " + type.FullName);
			int index = -1;
			for (int i = 0; i != declaringType.GenericParameters.Count; ++i)
			{
				if (TypeJit.TypesEqual(type, declaringType.GenericParameters[i]))
				{
					index = i;
					break;
				}
			}
			if (index == -1) throw new Exception("Failed to find generic argument for parameter: " + type.FullName);
			return this.type.genericTypeReference.GenericArguments[index];
		}

		internal void Jit()
		{
			// add parameters
			asmParameters = new List<ASMParameter>();
			foreach (var parameter in method.Parameters)
			{
				var p = parameter;
				if (parameter.ParameterType.IsGenericInstance)
				{
					var declaringModule = type.module.assembly.solution.FindJitModuleRecursive(parameter.ParameterType.Module);
					if (declaringModule == null) throw new Exception("Failed to find declaring module for generic type: " + parameter.ParameterType.FullName);
					var t = new TypeJit(null, parameter.ParameterType, declaringModule);
					t.Jit();
				}
				else if (parameter.ParameterType.IsGenericParameter)
				{
					var result = ResolveGenericParameter(parameter.ParameterType);
					p = new ParameterDefinition(p.Name, p.Attributes, result);
				}
				asmParameters.Add(new ASMParameter(p));
			}

			// skip rest if no instructions
			if (!method.HasBody || method.Body.Instructions.Count == 0) return;

			// add locals
			asmLocals = new List<ASMLocal>();
			foreach (var variable in method.Body.Variables)
			{
				var v = variable;
				if (variable.VariableType.IsGenericParameter)
				{
					var result = ResolveGenericParameter(variable.VariableType);
					v = new VariableDefinition(result);
				}
				asmLocals.Add(new ASMLocal(v, method.Body.InitLocals));
			}

			// interpret instructions
			sizeofTypes = new HashSet<TypeReference>();
			asmOperations = new LinkedList<ASMObject>();
			evalStack = new Stack<EvaluationStackItem>();
			evalStackVars = new Dictionary<TypeReference, ASMEvalStackLocal>();
			processedInstructionPaths = new Dictionary<Instruction, List<EvaluationStackProcessed>>();
			InterpretInstructionFlow(method.Body.Instructions[0]);

			// copy eval-stack locals into single list
			asmEvalLocals = new List<ASMEvalStackLocal>();
			foreach (var variable in evalStackVars)
			{
				if (!IsVoidType(variable.Key)) asmEvalLocals.Add(variable.Value);
			}

			// validate all IL instruction paths completed
			if (evalStack.Count != 0)
			{
				string error = $"ERROR: 'EvalStack still has items':" + Environment.NewLine;
				foreach (var evalOp in evalStack)
				{
					error += "\t" + evalOp.op.ToString() + Environment.NewLine;
				}
				throw new Exception(error);
			}

			// free unused memory
			evalStack = null;
			evalStackVars = null;
			processedInstructionPaths = null;
		}

		private void InterpretInstructionFlow(Instruction op)
		{
			while (op != null)
			{
				// process ops normally
				switch (op.OpCode.Code)
				{
					// ===================================
					// skip
					// ===================================
					case Code.Nop:
					{
						op = op.Next;
						continue;
					}

					// ===================================
					// arithmatic
					// ===================================
					case Code.Add:
					{
						var p2 = StackPop();
						var p1 = StackPop();
						var evalVarType = GetArithmaticResultType(p1.obj, p2.obj);
						var evalVar = GetEvalStackVar(evalVarType);
						AddASMOp(new ASMArithmatic(ASMCode.Add, OperandToASMOperand(p1.obj), OperandToASMOperand(p2.obj), evalVar));
						StackPush(op, evalVar);
						break;
					}

					case Code.Sub:
					{
						var p2 = StackPop();
						var p1 = StackPop();
						var evalVarType = GetArithmaticResultType(p1.obj, p2.obj);
						var evalVar = GetEvalStackVar(evalVarType);
						AddASMOp(new ASMArithmatic(ASMCode.Sub, OperandToASMOperand(p1.obj), OperandToASMOperand(p2.obj), evalVar));
						StackPush(op, evalVar);
						break;
					}

					case Code.Mul:
					{
						var p2 = StackPop();
						var p1 = StackPop();
						var evalVarType = GetArithmaticResultType(p1.obj, p2.obj);
						var evalVar = GetEvalStackVar(evalVarType);
						AddASMOp(new ASMArithmatic(ASMCode.Mul, OperandToASMOperand(p1.obj), OperandToASMOperand(p2.obj), evalVar));
						StackPush(op, evalVar);
						break;
					}

					case Code.Div:
					{
						var p2 = StackPop();
						var p1 = StackPop();
						var evalVarType = GetArithmaticResultType(p1.obj, p2.obj);
						var evalVar = GetEvalStackVar(evalVarType);
						AddASMOp(new ASMArithmatic(ASMCode.Div, OperandToASMOperand(p1.obj), OperandToASMOperand(p2.obj), evalVar));
						StackPush(op, evalVar);
						break;
					}

					// ===================================
					// loads
					// ===================================
					case Code.Ldc_I4_0: Ldc_X(op, 0); break;
					case Code.Ldc_I4_1: Ldc_X(op, 1); break;
					case Code.Ldc_I4_2: Ldc_X(op, 2); break;
					case Code.Ldc_I4_3: Ldc_X(op, 3); break;
					case Code.Ldc_I4_4: Ldc_X(op, 4); break;
					case Code.Ldc_I4_5: Ldc_X(op, 5); break;
					case Code.Ldc_I4_6: Ldc_X(op, 6); break;
					case Code.Ldc_I4_7: Ldc_X(op, 7); break;
					case Code.Ldc_I4_8: Ldc_X(op, 8); break;
					case Code.Ldc_I4_M1: Ldc_X(op, -1); break;
					case Code.Ldc_I4:
					case Code.Ldc_I4_S:
					{
						Ldc_X(op, (ValueType)op.Operand);
						break;
					}

					case Code.Ldloc_0: Ldloc_X(op, 0); break;
					case Code.Ldloc_1: Ldloc_X(op, 1); break;
					case Code.Ldloc_2: Ldloc_X(op, 2); break;
					case Code.Ldloc_3: Ldloc_X(op, 3); break;
					case Code.Ldloc:
					case Code.Ldloc_S:
					{
						var variable = (VariableDefinition)op.Operand;
						Ldloc_X(op, variable.Index);
						break;
					}

					case Code.Ldloca:
					case Code.Ldloca_S:
					{
						var variable = (VariableDefinition)op.Operand;
						if (variable.VariableType.IsGenericParameter)
						{
							variable = asmLocals[variable.Index].variable;
						}
						StackPush(op, variable);
						break;
					}

					case Code.Ldarg_0: Ldarg_X(op, 0, true); break;
					case Code.Ldarg_1: Ldarg_X(op, 1, true); break;
					case Code.Ldarg_2: Ldarg_X(op, 2, true); break;
					case Code.Ldarg_3: Ldarg_X(op, 3, true); break;
					case Code.Ldarg:
					case Code.Ldarg_S:
					{
						if (op.Operand is short) Ldarg_X(op, (short)op.Operand, true);
						else if (op.Operand is int) Ldarg_X(op, (int)op.Operand, true);
						else if (op.Operand is ParameterDefinition)
						{
							var p = (ParameterDefinition)op.Operand;
							Ldarg_X(op, p.Index, true);
						}
						else throw new NotImplementedException("Ldarg_S unsupported operand: " + op.Operand.GetType());
						break;
					}

					case Code.Ldfld:
					{
						var self = StackPop();
						var field = (FieldReference)op.Operand;
						if (field.FieldType.IsGenericParameter)
						{
							var t = ResolveGenericParameter(field.FieldType);
							field = new FieldReference(field.Name, t);
						}
						Ldfld_X(op, self.obj, field);
						break;
					}

					case Code.Ldflda:
					{
						var self = StackPop();
						var field = (FieldReference)op.Operand;
						if (field.FieldType.IsGenericParameter)
						{
							var t = ResolveGenericParameter(field.FieldType);
							field = new FieldReference(field.Name, t);
						}
						Ldfld_X(op, self.obj, field);
						break;
					}

					case Code.Sizeof:
					{
						var type = (TypeReference)op.Operand;
						if (!sizeofTypes.Any(x => TypeJit.TypesEqual(x, type))) sizeofTypes.Add(type);
						StackPush(op, new ASMSizeOf(type));
						break;
					}

					// ===================================
					// stores
					// ===================================
					case Code.Stloc_0: Stloc_X(0); break;
					case Code.Stloc_1: Stloc_X(1); break;
					case Code.Stloc_2: Stloc_X(2); break;
					case Code.Stloc_3: Stloc_X(3); break;
					case Code.Stloc:
					case Code.Stloc_S:
					{
						var variable = (VariableDefinition)op.Operand;
						Stloc_X(variable.Index);
						break;
					}

					case Code.Stfld:
					{
						var itemRight = StackPop();
						var self = StackPop();
						var fieldLeft = (FieldReference)op.Operand;
						if (fieldLeft.FieldType.IsGenericParameter)
						{
							var t = ResolveGenericParameter(fieldLeft.FieldType);
							fieldLeft = new FieldReference(fieldLeft.Name, t);
						}
						var field = new ASMField(self.obj, fieldLeft);
						AddASMOp(new ASMWriteField(field, OperandToASMOperand(itemRight.obj)));
						break;
					}

					case Code.Initobj:
					{
						var t = StackPop();
						ASMObject o;
						if (t.obj is VariableDefinition v)
						{
							int index = asmLocals.FindIndex(x => x.variable == v);
							o = asmLocals[index];
						}
						else
						{
							throw new NotImplementedException("Unknown initobj type: " + t.obj.ToString());
						}
						AddASMOp(new ASMInitObject(o));
						break;
					}

					// ===================================
					// branching
					// ===================================
					case Code.Ret:
					{
						if (IsVoidType(method.ReturnType))
						{
							AddASMOp(new ASMObject(ASMCode.ReturnVoid));
						}
						else
						{
							var p = StackPop();
							AddASMOp(new ASMReturnValue(OperandToASMOperand(p.obj)));
						}
						return;
					}

					case Code.Br:
					case Code.Br_S:
					{
						var jmpToOp = (Instruction)op.Operand;
						if (IsInstructionPathProcessed(op, out var processedInstructionPath))
						{
							AddASMOp(new ASMBranch(processedInstructionPath.asmIndex, processedInstructionPath.asmOperation));
						}
						else
						{
							AddInstructionPathProcessed(op, out _);
							InterpretInstructionFlow(jmpToOp);
						}
						return;
					}

					case Code.Brtrue:
					case Code.Brtrue_S:
					{
						var jmpToOp = (Instruction)op.Operand;
						var p = StackPop();
						BranchOnCondition(op, jmpToOp, ASMCode.BranchIfTrue, new object[1]{p.obj});
						return;
					}

					case Code.Brfalse:
					case Code.Brfalse_S:
					{
						var jmpToOp = (Instruction)op.Operand;
						var p = StackPop();
						BranchOnCondition(op, jmpToOp, ASMCode.BranchIfFalse, new object[1]{p.obj});
						return;
					}

					case Code.Beq:
					case Code.Beq_S:
					{
						var jmpToOp = (Instruction)op.Operand;
						var p2 = StackPop();
						var p1 = StackPop();
						BranchOnCondition(op, jmpToOp, ASMCode.BranchIfEqual, new object[2]{p1.obj, p2.obj});
						return;
					}

					case Code.Bne_Un:
					case Code.Bne_Un_S:
					{
						var jmpToOp = (Instruction)op.Operand;
						var p2 = StackPop();
						var p1 = StackPop();
						BranchOnCondition(op, jmpToOp, ASMCode.BranchIfNotEqual, new object[2]{p1.obj, p2.obj});
						return;
					}

					case Code.Bgt:
					case Code.Bgt_S:
					case Code.Bgt_Un:
					case Code.Bgt_Un_S:
					{
						var jmpToOp = (Instruction)op.Operand;
						var p2 = StackPop();
						var p1 = StackPop();
						BranchOnCondition(op, jmpToOp, ASMCode.BranchIfGreater, new object[2]{p1.obj, p2.obj});
						return;
					}

					case Code.Blt:
					case Code.Blt_S:
					case Code.Blt_Un:
					case Code.Blt_Un_S:
					{
						var jmpToOp = (Instruction)op.Operand;
						var p2 = StackPop();
						var p1 = StackPop();
						BranchOnCondition(op, jmpToOp, ASMCode.BranchIfLess, new object[2]{p1.obj, p2.obj});
						return;
					}

					case Code.Bge:
					case Code.Bge_S:
					case Code.Bge_Un:
					case Code.Bge_Un_S:
					{
						var jmpToOp = (Instruction)op.Operand;
						var p2 = StackPop();
						var p1 = StackPop();
						BranchOnCondition(op, jmpToOp, ASMCode.BranchIfGreaterOrEqual, new object[2]{p1.obj, p2.obj});
						return;
					}

					case Code.Ble:
					case Code.Ble_S:
					case Code.Ble_Un:
					case Code.Ble_Un_S:
					{
						var jmpToOp = (Instruction)op.Operand;
						var p2 = StackPop();
						var p1 = StackPop();
						BranchOnCondition(op, jmpToOp, ASMCode.BranchIfLessOrEqual, new object[2]{p1.obj, p2.obj});
						return;
					}

					case Code.Ceq:
					{
						var p2 = StackPop();
						var p1 = StackPop();
						var evalVar = GetEvalStackVar(GetTypeSystem().Boolean);
						AddASMOp(new ASMCmp(ASMCode.CmpEqual_1_0, OperandToASMOperand(p1.obj), OperandToASMOperand(p2.obj), evalVar));
						StackPush(op, evalVar);
						break;
					}

					case Code.Cgt:
					case Code.Cgt_Un:
					{
						var p2 = StackPop();
						var p1 = StackPop();
						var evalVar = GetEvalStackVar(GetTypeSystem().Boolean);
						AddASMOp(new ASMCmp(ASMCode.CmpGreater_1_0, OperandToASMOperand(p1.obj), OperandToASMOperand(p2.obj), evalVar));
						StackPush(op, evalVar);
						break;
					}

					case Code.Clt:
					case Code.Clt_Un:
					{
						var p2 = StackPop();
						var p1 = StackPop();
						var evalVar = GetEvalStackVar(GetTypeSystem().Boolean);
						AddASMOp(new ASMCmp(ASMCode.CmpLess_1_0, OperandToASMOperand(p1.obj), OperandToASMOperand(p2.obj), evalVar));
						StackPush(op, evalVar);
						break;
					}

					// ===================================
					// invoke
					// ===================================
					case Code.Call:
					{
						var methodInvoke = (MethodReference)op.Operand;
						var returnVar = GetEvalStackVar(methodInvoke.ReturnType);
						var parameters = new List<ASMObject>();
						if (methodInvoke.HasThis)
						{
							var p = StackPop();
							parameters.Add(OperandToASMOperand(p.obj));
						}
						for (int i = 0; i != methodInvoke.Parameters.Count; ++i)
						{
							var p = StackPop();
							parameters.Add(OperandToASMOperand(p.obj));
						}
						AddASMOp(new ASMCallMethod(ASMCode.CallMethod, methodInvoke, returnVar, parameters));
						if (!IsVoidType(methodInvoke.ReturnType)) StackPush(op, returnVar);
						break;
					}

					// ===================================
					// unsupported
					// ===================================
					default:
					{
						throw new NotImplementedException("Unsupported IL instruction: " + op.ToString());
					}
				}

				// move to next op
				op = op.Next;
			}
		}

		private void AddASMOp(ASMObject asmOp)
		{
			asmOperations.AddLast(asmOp);
		}

		private ASMObject OperandToASMOperand(object obj)
		{
			var type = obj.GetType();

			// primitive
			if
			(
				type == typeof(Boolean) ||
				type == typeof(Byte) || type == typeof(SByte) ||
				type == typeof(Int16) || type == typeof(UInt16) ||
				type == typeof(Int16) || type == typeof(UInt16) ||
				type == typeof(Int32) || type == typeof(UInt32) ||
				type == typeof(Int64) || type == typeof(UInt64)
			)
			{
				return new ASMPrimitiveLiteral(obj);
			}

			// sizeof
			if (obj is ASMSizeOf) return (ASMSizeOf)obj;

			// string
			if (type == typeof(string)) return new ASMStringLiteral((string)obj);

			// variables
			if (obj is ASMField) return (ASMField)obj;
			if (obj is ASMThisPtr) return (ASMThisPtr)obj;
			if (obj is ASMEvalStackLocal) return (ASMEvalStackLocal)obj;
			if (obj == method.DeclaringType) return ASMThisPtr.handle;
			if (obj is VariableReference) return asmLocals.First(x => x.variable == obj);
			if (obj is ParameterReference) return asmParameters.First(x => TypesEqual(x.parameter, (ParameterReference)obj));

			throw new NotImplementedException("Operand type not implimented: " + type.ToString());
		}

		public static bool TypesEqual(ParameterReference p1, ParameterReference p2)
		{
			if (p1 == p2) return true;
			if (TypeJit.TypesEqual(p1.ParameterType, p2.ParameterType) && p1.Name == p2.Name) return true;
			return false;
		}

		private void StackPush(Instruction op, object obj)
		{
			evalStack.Push(new EvaluationStackItem(op, obj));
		}

		private EvaluationStackItem StackPop()
		{
			return evalStack.Pop();
		}

		private void Ldarg_X(Instruction op, int index, bool indexCanThisOffset)
		{
			if (index == 0 && method.HasThis)
			{
				StackPush(op, new ASMThisPtr());
			}
			else
			{
				if (indexCanThisOffset && method.HasThis) --index;
				var p = asmParameters[index].parameter;
				StackPush(op, p);
			}
		}

		private void Ldc_X(Instruction op, ValueType value)
		{
			StackPush(op, value);
		}

		private void Stloc_X(int index)
		{
			var p = StackPop();
			AddASMOp(new ASMWriteLocal(asmLocals.First(x => x.variable == method.Body.Variables[index]), OperandToASMOperand(p.obj)));
		}

		private void Ldloc_X(Instruction op, int index)
		{
			var variable = method.Body.Variables[index];
			if (variable.VariableType.IsGenericParameter)
			{
				variable = asmLocals[variable.Index].variable;
			}
			StackPush(op, variable);
		}

		private void Ldfld_X(Instruction op, object self, FieldReference field)
		{
			StackPush(op, new ASMField(self, field));
		}

		private ASMEvalStackLocal GetEvalStackVar(TypeReference type)
		{
			ASMEvalStackLocal result;
			if (evalStackVars.ContainsKey(type))
			{
				result = evalStackVars[type];
				++result.refCount;
				return result;
			}
			result = new ASMEvalStackLocal(type, evalStackVars.Count);
			evalStackVars.Add(type, result);
			return result;
		}

		private bool IsInstructionPathProcessed(Instruction instruction, out EvaluationStackProcessed processedInstructionPath)
		{
			if (!processedInstructionPaths.ContainsKey(instruction))
			{
				processedInstructionPath = null;
				return false;
			}

			var evaluationStackProcessed = processedInstructionPaths[instruction];
			foreach (var processed in evaluationStackProcessed)
			{
				int count = processed.preProcessedEvalStack.Count;
				if (count != evalStack.Count) continue;
				count = 0;
				bool found = true;
				foreach (var item in evalStack)
				{
					if (processed.preProcessedEvalStack[count] != item.op)
					{
						found = false;
						break;
					}
					++count;
				}

				if (found)
				{
					processedInstructionPath = processed;
					return true;
				}
			}

			processedInstructionPath = null;
			return false;
		}

		private EvaluationStackProcessed AddInstructionPathProcessed(Instruction instruction, out ASMBranchMarker branchMarker)
		{
			if (!processedInstructionPaths.ContainsKey(instruction)) processedInstructionPaths.Add(instruction, new List<EvaluationStackProcessed>());
			var processedInstructionStack = new EvaluationStackProcessed(instruction, evalStack, asmJumpIndex++, null);
			processedInstructionPaths[instruction].Add(processedInstructionStack);
			branchMarker = new ASMBranchMarker(processedInstructionStack.asmIndex);
			processedInstructionStack.asmOperation = branchMarker;
			AddASMOp(branchMarker);
			return processedInstructionStack;
		}

		private void BranchOnCondition(Instruction op, Instruction jmpToOp, ASMCode branchConditionCode, object[] values)//, string condition)
		{
			if (IsInstructionPathProcessed(op, out var processedInstructionPath))
			{
				AddASMOp(new ASMBranch(processedInstructionPath.asmIndex, processedInstructionPath.asmOperation));
			}
			else
			{
				AddInstructionPathProcessed(op, out _);

				var opValues = new ASMObject[values.Length];
				for (int i = 0; i != opValues.Length; ++i) opValues[i] = OperandToASMOperand(values[i]);
				var branchCondition = new ASMBranchCondition(branchConditionCode, opValues, -1, null);// asm jump index unknown here
				AddASMOp(branchCondition);
				InterpretInstructionFlow(op.Next);
				branchCondition.asmJumpToIndex = asmJumpIndex++;
				branchCondition.jumpToOperation = new ASMBranchMarker(branchCondition.asmJumpToIndex);
				AddASMOp(branchCondition.jumpToOperation);
				InterpretInstructionFlow(jmpToOp);
			}
		}

		private TypeReference GetArithmaticResultType(object value1, object value2)
		{
			TypeReference GetType(object value)
			{
				if (value is VariableReference value_Var) return value_Var.VariableType;
				if (value is ParameterReference value_Param) return value_Param.ParameterType;
				if (value is Int16) return GetTypeSystem().Int16;
				if (value is Int32) return GetTypeSystem().Int32;
				if (value is Int64) return GetTypeSystem().Int64;
				if (value is UInt16) return GetTypeSystem().UInt16;
				if (value is UInt32) return GetTypeSystem().UInt32;
				if (value is UInt64) return GetTypeSystem().UInt64;
				if (value is Single) return GetTypeSystem().Single;
				if (value is Double) return GetTypeSystem().Double;
				if (value is ASMSizeOf) return GetTypeSystem().Int32;
				throw new NotImplementedException("Unsupported arithmatic object: " + value.GetType().ToString());
			}

			TypeReference LowestType(TypeReference type)
			{
				if
				(
					type.MetadataType == MetadataType.Byte || type.MetadataType == MetadataType.SByte ||
					type.MetadataType == MetadataType.Int16 || type.MetadataType == MetadataType.UInt16
				)
				{
					return GetTypeSystem().Int32;
				}
				return type;
			}

			static int TypeRank(TypeReference type)
			{
				if (type.MetadataType == MetadataType.Int32) return 0;
				if (type.MetadataType == MetadataType.UInt32) return 1;
				if (type.MetadataType == MetadataType.Int64) return 2;
				if (type.MetadataType == MetadataType.UInt64) return 3;
				if (type.MetadataType == MetadataType.Single) return 4;
				if (type.MetadataType == MetadataType.Double) return 5;
				throw new NotImplementedException("Unsupported arithmatic type: " + type.ToString());
			}
			
			// validate type basics
			var type1 = LowestType(GetType(value1));
			var type2 = LowestType(GetType(value2));
			
			// calculate result matrix
			int rank1 = TypeRank(type1);
			int rank2 = TypeRank(type2);
			TypeReference highType, lowType;
			if (rank1 >= rank2)
			{
				highType = type1;
				lowType = type2;
			}
			else
			{
				highType = type2;
				lowType = type1;
			}
			if (highType.MetadataType == MetadataType.UInt32 && lowType.MetadataType == MetadataType.Int32) return GetTypeSystem().Int64;
			return highType;
		}

		private TypeSystem GetTypeSystem()
		{
			return method.Module.TypeSystem;
		}

		private bool IsVoidType(TypeReference type)
		{
			return type == GetTypeSystem().Void;
		}
	}
}

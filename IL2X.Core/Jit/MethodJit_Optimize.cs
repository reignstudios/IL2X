using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IL2X.Core.Jit
{
	public partial class MethodJit
	{
		internal void Optimize()
		{
			if (asmOperations == null || asmOperations.Count == 0) return;

			// optimize out redundent eval-stack copies
			Optimize_RedundentStackCopies();

			// skip redundent branches
			Optimize_SkipRedundentBranches();

			// remove unused branch-markers
			Optimize_RemoveBranchMarkers();

			// remove unused branches
			Optimize_RemoveUnusedBranches();

			// cleanup redundent branch-markers
			Optimize_CleanupRedundentBranchMarkers();

			// remove unused branch-markers (again)
			Optimize_RemoveBranchMarkers();

			// remove eval-locals with zero references
			Optimize_RemoveUnusedEvalLocals();

			// remove eval-locals with that are only set not used
			//Optimize_RemoveUnusedSetOnlyEvalLocals();
		}

		private void Optimize_RedundentStackCopies()
		{
			var opNextNode = asmOperations.Last;
			while (opNextNode != null && opNextNode.Previous != null)
			{
				// get next op
				var opNext = opNextNode.Value;

				// get current op
				var opNode = opNextNode.Previous;
				var op = opNode.Value;
				while (op.code == ASMCode.BranchMarker || op.code == ASMCode.Branch)// if its a branch marker look back further
				{
					opNode = opNode.Previous;
					if (opNode == null) break;
					op = opNode.Value;
				}

				// if next-op is writing a eval-local into a local skip
				if (opNext.code == ASMCode.WriteLocal)
				{
					var opNextWriteLocal = (ASMWriteLocal)opNext;
					if (opNextWriteLocal.value is ASMEvalStackLocal srcEvalLocal)
					{
						var resultEvalLocal = op.GetResultLocal();
						if (srcEvalLocal == resultEvalLocal)
						{
							op.SetResultLocal(opNextWriteLocal.resultLocal);// change write op to write directly to IL defined local
							opNextNode = opNextNode.Previous;
							asmOperations.Remove(opNextNode.Next);// remove redundend eval-local to local copy
							--srcEvalLocal.refCount;
							continue;
						}
					}
				}

				// if next-op is returning a eval-local
				if (opNext.code == ASMCode.ReturnValue)
				{
					var returnOp = (ASMReturnValue)opNext;
					if
					(
						returnOp.value is ASMEvalStackLocal srcEvalLocal_Return &&
						op.GetResultLocal() == srcEvalLocal_Return
					)
					{
						returnOp.value = op;// change return to directly return operation
						asmOperations.Remove(opNode);// remove redundend eval-local to local copy
						--srcEvalLocal_Return.refCount;
						continue;
					}
				}

				opNextNode = opNextNode.Previous;
			}
		}

		private void Optimize_SkipRedundentBranches()
		{
			var opNode = asmOperations.First;
			while (opNode != null)
			{
				// get op
				var op = opNode.Value;
				if (op.code == ASMCode.Branch || op is not IASMBranch)
				{
					opNode = opNode.Next;
					continue;
				}

				var branchOp = (IASMBranch)op;
				var opJumpToNode = asmOperations.Find(branchOp.jumpToOperation);
				var opJumpToNodeNext = opJumpToNode.Next;
				if (opJumpToNodeNext.Value.code == ASMCode.Branch)
				{
					var opJumpToNext = (ASMBranch)opJumpToNodeNext.Value;
					branchOp.asmJumpToIndex = opJumpToNext.asmJumpToIndex;
					branchOp.jumpToOperation = opJumpToNext.jumpToOperation;
				}

				opNode = opNode.Next;
			}
		}

		private void Optimize_RemoveUnusedBranches()
		{
			var opNode  = asmOperations.First;
			while (opNode != null)
			{
				// get op
				var op = opNode.Value;
				if (op.code != ASMCode.Branch)
				{
					opNode = opNode.Next;
					continue;
				}

				var opNodePrev = opNode.Previous;
				if (opNodePrev != null && opNodePrev.Value.code == ASMCode.Branch)
				{
					opNode = opNode.Next;
					asmOperations.Remove(opNode.Previous);
					continue;
				}

				opNode = opNode.Next;
			}
		}

		private void Optimize_CleanupRedundentBranchMarkers()
		{
			var opNode  = asmOperations.First;
			while (opNode != null)
			{
				// get op
				var op = opNode.Value;
				if (op.code != ASMCode.BranchMarker)
				{
					opNode = opNode.Next;
					continue;
				}
				var opBranchMarker = (ASMBranchMarker)op;

				// get next op
				var opNextNode = opNode.Next;
				if (opNextNode == null || opNextNode.Value.code != ASMCode.BranchMarker)
				{
					opNode = opNode.Next;
					continue;
				}
				var opNext = opNextNode.Value;
				var opNextBranchMarker = (ASMBranchMarker)opNext;

				// find all ops that jump to next op & replace them with current marker
				var opScanNode = asmOperations.First;
				while (opScanNode != null)
				{
					if (opScanNode == opNode)
					{
						opScanNode = opScanNode.Next;
						continue;
					}
					if (opScanNode.Value is IASMBranch branch && branch.asmJumpToIndex == opNextBranchMarker.asmIndex)
					{
						branch.asmJumpToIndex = opBranchMarker.asmIndex;
						branch.jumpToOperation = opBranchMarker;
					}
					opScanNode = opScanNode.Next;
				}

				opNode = opNode.Next;
			}
		}

		private void Optimize_RemoveBranchMarkers()
		{
			var opNode = asmOperations.First;
			while (opNode != null)
			{
				var op = opNode.Value;
				if (op.code == ASMCode.BranchMarker)
				{
					var branchMarker = (ASMBranchMarker)op;
					bool found = false;
					var opNode2 = asmOperations.First;
					while (opNode2 != null)
					{
						if (opNode == opNode2)
						{
							opNode2 = opNode2.Next;
							continue;
						}
						var op2 = opNode2.Value;
						if (op2 is IASMBranch branch)
						{
							if (branch.asmJumpToIndex == branchMarker.asmIndex)
							{
								found = true;
								break;
							}
						}

						opNode2 = opNode2.Next;
					}

					if (!found)
					{
						opNode = opNode.Next;
						asmOperations.Remove(opNode.Previous);
						continue;
					}
				}

				opNode = opNode.Next;
			}
		}

		private void Optimize_RemoveUnusedEvalLocals()
		{
			for (int i = asmEvalLocals.Count - 1; i >= 0; --i)
			{
				if (asmEvalLocals[i].refCount < 0) throw new Exception("Eval ref count is less than 0");
				if (asmEvalLocals[i].refCount == 0) asmEvalLocals.RemoveAt(i);
			}
		}

		/*private void Optimize_RemoveUnusedSetOnlyEvalLocals()
        {
			for (int i = asmEvalLocals.Count - 1; i >= 0; --i)
			{
				var local = asmEvalLocals[i];
				if (local.refCount > 0)
                {
					// check if local is used outside of being set
					bool found = false;
					foreach (var op in asmOperations)
                    {
						if (op.code == ASMCode.CallMethod)
                        {
							var asmCall = (ASMCallMethod)op;
							if (!(asmCall.resultLocal is ASMEvalStackLocal))
                            {
								found = true;
								break;
                            }
						}
                    }
					
					// remove local if its only being set
					if (!found)
                    {
						// remove op connections no longer needed
						foreach (var op in asmOperations)
						{
							if (op.code == ASMCode.CallMethod)
							{
								var asmCall = (ASMCallMethod)op;
								if (asmCall.resultLocal == local)
								{
									asmCall.resultLocal = null;
								}
							}
						}

						// remove local
						asmEvalLocals.RemoveAt(i);
					}
				}
			}
		}*/
	}
}

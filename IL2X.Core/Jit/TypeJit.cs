using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IL2X.Core.Jit
{
	public class TypeJit
	{
		public readonly bool isGeneric;
		public readonly TypeDefinition typeDefinition;
		public readonly TypeReference typeReference;
		public readonly IGenericInstance genericTypeReference;
		public readonly ModuleJit module;
		public List<FieldJit> fields;
		public List<MethodJit> methods;

		public TypeJit(TypeDefinition typeDefinition, TypeReference typeReference, ModuleJit module)
		{
			// resolve definition if needed
			if (typeDefinition == null)
			{
				typeDefinition = typeReference.Resolve();
				if (typeDefinition == null) throw new Exception("Type could not be reolved: " + typeReference.FullName);
			}

			isGeneric = typeDefinition.HasGenericParameters;
			this.typeDefinition = typeDefinition;
			this.typeReference = typeReference;
			genericTypeReference = typeReference as IGenericInstance;
			this.module = module;

			// add to module
			module.allTypes.Add(this);
			if (typeDefinition.IsValueType) module.structTypes.Add(this);
			else if (typeDefinition.IsEnum) module.enumTypes.Add(this);
			else module.classTypes.Add(this);
		}

		internal void Jit()
		{
			// jit fields
			fields = new List<FieldJit>();
			foreach (var field in typeDefinition.Fields)
			{
				var fieldJit = new FieldJit(field, this);
				fields.Add(fieldJit);
				fieldJit.Jit();
			}

			// jit methods
			methods = new List<MethodJit>();
			foreach (var method in typeDefinition.Methods)
			{
				if (method.HasGenericParameters) continue;// don't JIT generic definition methods
				var methodJit = new MethodJit(method, module.module, this);
				methods.Add(methodJit);
				methodJit.Jit();
			}
		}

		internal void Optimize()
		{
			foreach (var method in methods)
			{
				method.Optimize();
			}
		}

		public FieldJit FindJitFieldRecursive(FieldDefinition field)
		{
			foreach (var f in fields)
			{
				if (f.field == field) return f;
			}
			return null;
		}
	}
}

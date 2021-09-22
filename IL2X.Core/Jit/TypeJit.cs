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
		public readonly TypeDefinition type;
		public readonly ModuleJit module;
		public List<FieldJit> fields;
		public List<MethodJit> methods;

		public TypeJit(TypeDefinition type, ModuleJit module)
		{
			this.type = type;
			this.module = module;

			// add to module
			module.allTypes.Add(this);
			if (type.IsValueType) module.structTypes.Add(this);
			else if (type.IsEnum) module.enumTypes.Add(this);
			else module.classTypes.Add(this);

			// jit fields
			fields = new List<FieldJit>();
			foreach (var field in type.Fields)
			{
				var fieldJit = new FieldJit(field, type);
				fields.Add(fieldJit);
			}

			// jit methods
			methods = new List<MethodJit>();
			foreach (var method in type.Methods)
			{
				var methodJit = new MethodJit(method, module.module, this);
				methods.Add(methodJit);
			}
		}

		public void Optimize()
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

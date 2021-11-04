using Mono.Cecil;
using System.Linq;

namespace IL2X.Core.Jit
{
	public class FieldJit
	{
		public readonly FieldDefinition field;
		public TypeReference resolvedFieldType;
		public readonly TypeJit type;

		public FieldJit(FieldDefinition field, TypeJit type)
		{
			this.field = field;
			this.type = type;
			type.fields.Add(this);
		}

		internal void Jit()
		{
			resolvedFieldType = type.module.assembly.solution.ResolveType(field.FieldType, type);
		}
	}
}

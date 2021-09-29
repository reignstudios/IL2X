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
		}

		internal void Jit()
		{
			// resolve field type
			if (field.FieldType.IsGenericParameter)
			{
				int index = type.typeDefinition.Fields.IndexOf(field);
				resolvedFieldType = type.genericTypeReference.GenericArguments[index];
			}
			else
			{
				resolvedFieldType = field.FieldType;
			}
		}
	}
}

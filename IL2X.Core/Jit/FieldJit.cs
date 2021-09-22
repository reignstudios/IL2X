using Mono.Cecil;

namespace IL2X.Core.Jit
{
	public class FieldJit
	{
		public readonly FieldDefinition field;
		public readonly TypeReference type;

		public FieldJit(FieldDefinition field, TypeReference type)
		{
			this.field = field;
			this.type = type;
		}
	}
}

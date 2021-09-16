using Mono.Cecil;

namespace IL2X.Core.Jit
{
	public class FieldJit
	{
		public TypeReference type;
		public string name;

		public FieldJit(TypeReference type)
		{
			this.type = type;
			name = "f_" + type.Name;
		}
	}
}

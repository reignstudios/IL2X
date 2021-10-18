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
			if (field.FieldType.IsGenericInstance)
			{
				var genericInstance = (IGenericInstance)field.FieldType;
				foreach (var arg in genericInstance.GenericArguments)
				{
					if (arg.IsGenericParameter)
					{
						var genericParamArg = (GenericParameter)arg;
						int index = type.typeDefinition.GenericParameters.IndexOf(genericParamArg);
						resolvedFieldType = type.genericTypeReference.GenericArguments[index];
					}
				}
			}
			else if (field.FieldType.IsGenericParameter)
			{
				var genericParamArg = (GenericParameter)field.FieldType;
				int index = type.typeDefinition.GenericParameters.IndexOf(genericParamArg);
				resolvedFieldType = type.genericTypeReference.GenericArguments[index];
			}
			else
			{
				resolvedFieldType = field.FieldType;
			}
		}
	}
}

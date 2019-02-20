using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using IL2X.Core.EvaluationStack;

namespace IL2X.Core
{
	public sealed class ILAssemblyTranslator_C : ILAssemblyTranslator
	{
		private StreamWriter writer;
		private ILAssembly activeAssembly;// current assembly being translated
		private ILModule activeModule;// current module being translated
		private MethodDebugInformation activeMethodDebugInfo;

		#region Core dependency resolution
		public ILAssemblyTranslator_C(string binaryPath, bool loadReferences, params string[] searchPaths)
		: base(binaryPath, loadReferences, searchPaths)
		{
			
		}

		public override void Translate(string outputPath)
		{
			TranslateAssembly(assembly, outputPath, new List<ILAssembly>());
		}

		private void TranslateAssembly(ILAssembly assembly, string outputPath, List<ILAssembly> translatedAssemblies)
		{
			activeAssembly = assembly;

			// validate assembly wasn't already translated
			if (translatedAssemblies.Exists(x => x.assemblyDefinition.FullName == assembly.assemblyDefinition.FullName)) return;
			translatedAssemblies.Add(assembly);

			// translate all modules into C assmebly files
			foreach (var module in assembly.modules)
			{
				// translate reference assemblies
				foreach (var reference in module.references) TranslateAssembly(reference, outputPath, translatedAssemblies);

				// create output folder
				if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

				// translate module
				TranslateModule(module, outputPath);
			}
		}

		private void TranslateModule(ILModule module, string outputPath)
		{
			activeModule = module;

			// get module filename
			string modulePath = Path.Combine(outputPath, module.moduleDefinition.Name.Replace('.', '_'));
			if (module.moduleDefinition.IsMain && (module.moduleDefinition.Kind == ModuleKind.Console || module.moduleDefinition.Kind == ModuleKind.Windows)) modulePath += ".c";
			else modulePath += ".h";

			// write module
			using (var stream = new FileStream(modulePath, FileMode.Create, FileAccess.Write, FileShare.Read))
			using (writer = new StreamWriter(stream))
			{
				// write generate info
				writer.WriteLine("// ###############################");
				writer.WriteLine($"// Generated with IL2X v{Utils.GetAssemblyInfoVersion()}");
				writer.WriteLine("// ###############################");
				writer.WriteLine();

				// write forward declare of types
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Type forward declares");
				writer.WriteLine("// ===============================");
				foreach (var type in module.moduleDefinition.Types) WriteTypeDefinition(type, false);
				writer.WriteLine();

				// write type definitions
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Type definitions");
				writer.WriteLine("// ===============================");
				foreach (var type in module.moduleDefinition.Types) WriteTypeDefinition(type, true);

				// write forward declare of type methods
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Method forward declares");
				writer.WriteLine("// ===============================");
				foreach (var type in module.moduleDefinition.Types)
				{
					foreach (var method in type.Methods) WriteMethodDefinition(method, false);
				}
				writer.WriteLine();

				// write method definitions
				writer.WriteLine("// ===============================");
				writer.WriteLine("// Method definitions");
				writer.WriteLine("// ===============================");
				foreach (var type in module.moduleDefinition.Types)
				{
					foreach (var method in type.Methods) WriteMethodDefinition(method, true);
				}
			}

			writer = null;
		}
		#endregion

		#region Type writers
		private void WriteTypeDefinition(TypeDefinition type, bool writeBody)
		{
			if (type.IsEnum) return;// enums are converted to numarics
			if (type.HasGenericParameters) return;// generics are generated per use

			if (!writeBody)
			{
				writer.WriteLine(string.Format("typedef struct {0} {0};", GetTypeDefinitionFullName(type)));
			}
			else
			{
				writer.WriteLine(string.Format("struct {0}", GetTypeDefinitionFullName(type)));
				writer.WriteLine('{');
				StreamWriterEx.AddTab();
				foreach (var field in type.Fields) WriteFieldDefinition(field);
				StreamWriterEx.RemoveTab();
				writer.WriteLine("};");
				writer.WriteLine();
			}
		}

		private void WriteFieldDefinition(FieldDefinition field)
		{
			if (field.FieldType.IsGenericInstance) throw new Exception("TODO: generate generic type");
			writer.WriteLinePrefix($"{GetTypeReferenceFullName(field.FieldType)} {GetFieldDefinitionName(field)};");
		}

		private void WriteMethodDefinition(MethodDefinition method, bool writeBody)
		{
			if (method.HasGenericParameters || method.DeclaringType.HasGenericParameters) return;// generics are generated per use

			writer.Write($"{GetTypeReferenceFullName(method.ReturnType)} {GetMethodDefinitionFullName(method)}(");
			if (!method.IsStatic)
			{
				writer.Write($"{GetTypeReferenceFullName(method.DeclaringType)} self");
				if (method.HasParameters)
				{
					writer.Write(", ");
					WriteParameterDefinitions(method.Parameters);
				}
			}
			else if (method.HasParameters)
			{
				WriteParameterDefinitions(method.Parameters);
			}
			else
			{
				writer.Write("void");
			}
			writer.Write(')');
			
			if (!writeBody)
			{
				writer.WriteLine(';');
			}
			else
			{
				writer.WriteLine();
				writer.WriteLine('{');
				StreamWriterEx.AddTab();
				activeMethodDebugInfo = null;
				if (activeModule.symbolReader != null) activeMethodDebugInfo = activeModule.symbolReader.Read(method);
				WriteMethodBody(method.Body);
				activeMethodDebugInfo = null;
				StreamWriterEx.RemoveTab();
				writer.WriteLine('}');
				writer.WriteLine();
			}
		}

		private void WriteParameterDefinitions(IEnumerable<ParameterDefinition> parameters)
		{
			var last = parameters.LastOrDefault();
			foreach (var parameter in parameters)
			{
				if (parameter.ParameterType.IsGenericInstance) throw new Exception("TODO: generate generic type");
				WriteParameterDefinition(parameter);
				if (parameter != last) writer.Write(", ");
			}
		}

		private void WriteParameterDefinition(ParameterDefinition parameter)
		{
			writer.Write($"{GetTypeReferenceFullName(parameter.ParameterType)} {GetParameterDefinitionName(parameter)}");
		}
		#endregion

		#region Method Body / IL Instructions
		private void WriteMethodBody(MethodBody body)
		{
			if (body.Method.Name == ".ctor") return;// SKIP FOR TESTING

			// write local stack variables
			var variables = new List<LocalVariable>();
			if (body.HasVariables)
			{
				foreach (var variable in body.Variables)
				{
					variables.Add(WriteVariableDefinition(variable));
				}
			}

			// write instructions
			var stack = new Stack<IStack>(body.MaxStackSize);
			foreach (var instruction in body.Instructions)
			{
				// check if this instruction can be jumped to
				if (body.Instructions.Any(x => x.OpCode.Code == Code.Br_S && ((Instruction)x.Operand).Offset == instruction.Offset))
				{
					writer.WriteLinePrefix($"IL_{instruction.Offset.ToString("x4")}:");// write goto jump label short form
				}
				else if (body.Instructions.Any(x => x.OpCode.Code == Code.Br && ((Instruction)x.Operand).Offset == instruction.Offset))
				{
					writer.WriteLinePrefix($"IL_{instruction.Offset.ToString("x8")}:");// write goto jump label long form
				}

				// handle next instruction
				switch (instruction.OpCode.Code)
				{
					case Code.Nop: continue;

					case Code.Ldloca_S:
					{
						var operand = (VariableDefinition)instruction.Operand;
						stack.Push(new Stack_LocalVariable(variables[operand.Index]));
						break;
					}

					case Code.Ldloc_0: stack.Push(new Stack_LocalVariable(variables[0])); break;
					case Code.Ldloc_1: stack.Push(new Stack_LocalVariable(variables[1])); break;
					case Code.Ldloc_2: stack.Push(new Stack_LocalVariable(variables[2])); break;
					case Code.Ldloc_3: stack.Push(new Stack_LocalVariable(variables[3])); break;

					case Code.Stloc_0:
					{
						var item = stack.Pop();
						var variableLeft = variables[0];
						writer.WriteLinePrefix($"{variableLeft.name} = {item.GetValueName()};");
						break;
					}

					case Code.Stloc_1:
					{
						var item = stack.Pop();
						var variableLeft = variables[1];
						writer.WriteLinePrefix($"{variableLeft.name} = {item.GetValueName()};");
						break;
					}

					case Code.Stloc_2:
					{
						var item = stack.Pop();
						var variableLeft = variables[2];
						writer.WriteLinePrefix($"{variableLeft.name} = {item.GetValueName()};");
						break;
					}

					case Code.Stloc_3:
					{
						var item = stack.Pop();
						var variableLeft = variables[3];
						writer.WriteLinePrefix($"{variableLeft.name} = {item.GetValueName()};");
						break;
					}

					case Code.Br:
					{	
						var operand = (Instruction)instruction.Operand;
						writer.WriteLinePrefix($"goto IL_{operand.Offset.ToString("x8")};");
						break;
					}

					case Code.Br_S:
					{	
						var operand = (Instruction)instruction.Operand;
						writer.WriteLinePrefix($"goto IL_{operand.Offset.ToString("x4")};");
						break;
					}

					case Code.Initobj:
					{
						var item = (Stack_LocalVariable)stack.Pop();
						var variable = item.variable;
						var type = variable.definition.VariableType;
						if (type.IsValueType) writer.WriteLinePrefix($"memset(&{variable.name}, 0, sizeof({GetTypeReferenceFullName(type, true, false)}));");
						else writer.WriteLinePrefix($"memset({variable.name}, 0, sizeof(0));");
						break;
					}

					case Code.Ret:
					{
						if (body.Method.ReturnType.MetadataType == MetadataType.Void)
						{
							writer.WriteLinePrefix("return;");
						}
						else
						{
							var item = stack.Pop();
							writer.WriteLinePrefix($"return {item.GetValueName()};");
						}
						break;
					}

					default: throw new Exception("Unsuported opcode type: " + instruction.OpCode.Code);
				}
			}
		}

		private LocalVariable WriteVariableDefinition(VariableDefinition variable)
		{
			var local = new LocalVariable();
			local.definition = variable;
			if (!activeMethodDebugInfo.TryGetName(variable, out local.name))
			{
				local.name = "var";
			}
			
			local.name = $"l_{local.name}_{variable.Index}";
			writer.WriteLinePrefix($"{GetTypeReferenceFullName(variable.VariableType)} {local.name};");
			return local;
		}
		#endregion

		#region Core name resolution
		protected override string GetTypeDefinitionName(TypeDefinition type)
		{
			string result = base.GetTypeDefinitionName(type);
			if (type.HasGenericParameters) result += '_' + GetGenericParameters(type);
			return result;
		}

		protected override string GetTypeReferenceName(TypeReference type)
		{
			string result = base.GetTypeReferenceName(type);
			if (type.IsGenericInstance) result += '_' + GetGenericArguments((IGenericInstance)type);
			return result;
		}
		
		protected override string GetTypeDefinitionFullName(TypeDefinition type, bool allowPrefix = true)
		{
			string result = base.GetTypeDefinitionFullName(type, allowPrefix);
			if (allowPrefix) result = "t_" + result;
			return result;
		}
		
		protected override string GetTypeReferenceFullName(TypeReference type, bool allowPrefix = true, bool allowSymbols = true)
		{
			string result;
			if (type.MetadataType == MetadataType.Void)
			{
				result = "void";
			}
			else
			{
				result = base.GetTypeReferenceFullName(type, allowPrefix, allowSymbols);
				if (allowPrefix) result = "t_" + result;
				if (allowSymbols)
				{
					var def = GetTypeDefinition(type);
					if (def.IsEnum)
					{
						if (!def.HasFields) throw new Exception("Enum has no fields: " + type.Name);
						return GetTypeReferenceName(def.Fields[0].FieldType);
					}
					else
					{
						result = base.GetTypeReferenceName(type);
					}

					if (type.HasGenericParameters) result += '_' + GetGenericParameters(type);
					if (!type.IsValueType || type.IsPointer) result += '*';
					if (type.IsByReference) result += '*';
				}
			}
			
			return result;
		}

		protected override string GetMethodDefinitionName(MethodDefinition method)
		{
			string result = base.GetMethodDefinitionName(method);
			if (method.HasGenericParameters) result += '_' + GetGenericParameters(method);
			return result;
		}

		protected override string GetMethodReferenceName(MethodReference method)
		{
			string result = base.GetMethodReferenceName(method);
			if (method.HasGenericParameters) result += '_' + GetGenericParameters(method);
			return result;
		}

		protected override string GetMethodDefinitionFullName(MethodDefinition method)
		{
			string result = "m_" + base.GetMethodDefinitionFullName(method);
			return result;
		}

		protected override string GetMethodReferenceFullName(MethodReference method)
		{
			string result = "m_" + base.GetMethodReferenceFullName(method);
			return result;
		}

		protected override string GetGenericParameterName(GenericParameter parameter)
		{
			return "gp_" + base.GetGenericParameterName(parameter);
		}

		protected override string GetGenericArgumentTypeName(TypeReference type)
		{
			return GetTypeReferenceFullName(type);
		}

		protected override string GetParameterDefinitionName(ParameterDefinition parameter)
		{
			return "p_" + base.GetParameterDefinitionName(parameter);
		}

		protected override string GetParameterReferenceName(ParameterReference parameter)
		{
			return "p_" + base.GetParameterReferenceName(parameter);
		}

		protected override string GetFieldDefinitionName(FieldDefinition field)
		{
			return "f_" + base.GetFieldDefinitionName(field);
		}

		protected override string GetFieldReferenceName(FieldReference field)
		{
			return "f_" + base.GetFieldReferenceName(field);
		}

		protected override string GetTypeDefinitionDelimiter()
		{
			return "_";
		}

		protected override string GetTypeReferenceDelimiter()
		{
			return "_";
		}

		protected override string GetMethodDefinitionDelimiter()
		{
			return "_";
		}

		protected override string GetMethodReferenceDelimiter()
		{
			return "_";
		}

		protected override string GetMemberDefinitionNamespaceName(in string name)
		{
			return base.GetMemberDefinitionNamespaceName(name).Replace('.', '_');
		}

		protected override string GetMemberReferenceNamespaceName(in string name)
		{
			return base.GetMemberReferenceNamespaceName(name).Replace('.', '_');
		}

		protected override char GetGenericParameterOpenBracket()
		{
			return '_';
		}

		protected override char GetGenericParameterCloseBracket()
		{
			return '_';
		}

		protected override char GetGenericParameterDelimiter()
		{
			return '_';
		}

		protected override char GetGenericArgumentOpenBracket()
		{
			return '_';
		}

		protected override char GetGenericArgumentCloseBracket()
		{
			return '_';
		}

		protected override char GetGenericArgumentDelimiter()
		{
			return '_';
		}
		#endregion
	}
}

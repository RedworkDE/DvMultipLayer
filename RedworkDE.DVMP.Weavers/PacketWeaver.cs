using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace RedworkDE.DVMP.Weavers
{
	public class PacketWeaver : BaseModuleWeaver
	{
		private TypeDefinition _iPacket;
		private MethodDefinition _getMaxSize;
		private MethodDefinition _parseData;
		private MethodDefinition _serializeData;
		private TypeDefinition _autoPacket;
		private MethodDefinition _autoPacketCtor;
		private MethodReference _objectCtor;
		private TypeDefinition _extensions;
		private MethodDefinition _read;
		private MethodDefinition _readArray;
		private MethodDefinition _readStringA;
		private MethodDefinition _readStringW;
		private MethodDefinition _write;
		private MethodDefinition _writeArray;
		private MethodDefinition _writeStringA;
		private MethodDefinition _writeStringW;
		private MethodReference _stringLength;

		public override void Execute()
		{
			WriteInfo(ModuleDefinition.ToString());
			_iPacket = ModuleDefinition.GetType("DVMP.Networking", "IPacket");
			if (_iPacket is null)
			{
				WriteWarning("IPacket not found");
				return;
			}
			_getMaxSize = _iPacket.Methods.SingleOrDefault(m => m.Name == "get_MaxSize");
			_parseData = _iPacket.Methods.SingleOrDefault(m => m.Name == "ParseData");
			_serializeData = _iPacket.Methods.SingleOrDefault(m => m.Name == "SerializeData");
			_getMaxSize = _iPacket.Methods.SingleOrDefault(m => m.Name == "get_MaxSize");
			_autoPacket = ModuleDefinition.GetType("DVMP.Networking", "AutoPacket");
			if (_autoPacket is null)
			{
				WriteWarning("AutoPacket not found");
				return;
			}
			_autoPacketCtor = _autoPacket.GetConstructors()?.SingleOrDefault();
			if (_autoPacketCtor is null)
			{
				WriteWarning("AutoPacket constructor not found");
				return;
			}
			_objectCtor = ModuleDefinition.ImportReference(_autoPacket.BaseType?.Resolve()?.GetConstructors()?.Single());

			_extensions = ModuleDefinition.GetType("DVMP", "Extensions");
			_read = _extensions.Methods.Single(m => m.Name == "Read");
			_readArray = _extensions.Methods.Single(m => m.Name == "ReadArray" && m.Parameters.Count == 2);
			_readStringA = _extensions.Methods.Single(m => m.Name == "ReadA");
			_readStringW = _extensions.Methods.Single(m => m.Name == "ReadW");
			_write = _extensions.Methods.Single(m => m.Name == "Write");
			_writeArray = _extensions.Methods.Single(m => m.Name == "WriteArray" && m.Parameters.Count == 2);
			_writeStringA = _extensions.Methods.Single(m => m.Name == "WriteA");
			_writeStringW = _extensions.Methods.Single(m => m.Name == "WriteW");

			_stringLength = ModuleDefinition.ImportReference(ModuleDefinition.TypeSystem.String.Resolve().Methods.Single(m => m.Name == "get_Length"));

			var types = ModuleDefinition.GetTypes().Where(t => Inherits(t, _autoPacket)).ToList();

			SortByInheritance(types);

			foreach (var type in types)
			{
				var isOverride = types.IndexOf(type.BaseType as TypeDefinition) != -1;

				MakeMethod(type, _getMaxSize, BeforeGetMaxSize, ItemGetMaxSize, After, isOverride);
				MakeMethod(type, _parseData, BeforeParseData, ItemParseData, After, isOverride);
				MakeMethod(type, _serializeData, BeforeSerializeData, ItemSerializeData, After, isOverride);
				MakeFactory(type);

				// reparent type to skip auto packet
				if (type.BaseType == _autoPacket)
				{
					type.BaseType = _autoPacket.BaseType;
					type.Interfaces.Add(new InterfaceImplementation(_iPacket));
				}
				foreach (var ctor in type.GetConstructors())
				foreach (var instr in ctor.Body.Instructions)
					if (instr.OpCode == OpCodes.Call && instr.Operand == _autoPacketCtor)
						instr.Operand = _objectCtor;

			}

			ModuleDefinition.Types.Remove(_autoPacket);
		}

		private void MakeFactory(TypeDefinition type)
		{
			if (type.Methods.Any(m => m.Name == "CreateInstance")) return;
			var method = new MethodDefinition("CreateInstance", MethodAttributes.Public|MethodAttributes.Static|MethodAttributes.HideBySig, type);
			type.Methods.Add(method);

			var il = method.Body.GetILProcessor();

			il.Emit(OpCodes.Newobj, type.GetConstructors().Single(m => m.Parameters.Count == 0));
			il.Emit(OpCodes.Ret);
		}

		private void SortByInheritance(List<TypeDefinition> types)
		{
			bool swapped = true;
			while (swapped)
			{
				swapped = false;
				for (var i = 0; i < types.Count; i++)
				{
					var type = types[i];
					var bi = types.IndexOf(type.BaseType as TypeDefinition);
					if (bi > i)
					{
						types[i] = types[bi];
						types[bi] = type;
						swapped = true;
					}
				}
			}
		}

		private bool IsUnicode(FieldDefinition field)
		{
			return field.CustomAttributes.Any(c => c.AttributeType.Name == nameof(MarshalAsAttribute) && c.ConstructorArguments[0].Value is UnmanagedType um && um == UnmanagedType.BStr);
		}

		private void BeforeGetMaxSize(ILProcessor il)
		{
			if (il.Body.Method.DeclaringType.BaseType != _autoPacket)
			{
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Call, il.Body.Method.DeclaringType.BaseType.Resolve().Methods.Single(m => m.Name == "get_MaxSize"));
			}
			else
			{
				il.Emit(OpCodes.Ldc_I4, 0);
			}
		}

		private void ItemGetMaxSize(ILProcessor il, FieldDefinition field)
		{
			if (field.FieldType.IsValueType)
			{
				il.Emit(OpCodes.Sizeof, field.FieldType);
			}
			else if (field.FieldType.IsArray && field.FieldType.GetElementType().IsValueType)
			{
				il.Emit(OpCodes.Sizeof, field.FieldType.GetElementType());
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldfld, field);
				il.Emit(OpCodes.Ldlen);
				il.Emit(OpCodes.Mul);
				il.Emit(OpCodes.Add);
				il.Emit(OpCodes.Ldc_I4_2);
			}
			else if (field.FieldType == ModuleDefinition.TypeSystem.String)
			{
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldfld, field);
				il.Emit(OpCodes.Call, _stringLength);
				if (IsUnicode(field))
				{
					il.Emit(OpCodes.Ldc_I4_2);
					il.Emit(OpCodes.Mul);
				}
				il.Emit(OpCodes.Add);
				il.Emit(OpCodes.Ldc_I4_2);
			}
			il.Emit(OpCodes.Add);
		}

		private void After(ILProcessor il)
		{
			il.Emit(OpCodes.Ret);
		}

		
		private void BeforeParseData(ILProcessor il)
		{
			if (il.Body.Method.DeclaringType.BaseType != _autoPacket)
			{
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Call, il.Body.Method.DeclaringType.BaseType.Resolve().Methods.Single(m => m.Name == "ParseData"));
			}
			else
			{
			}
		}

		private void ItemParseData(ILProcessor il, FieldDefinition field)
		{
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldflda, field);

			if (field.FieldType.IsValueType)
			{
				il.Emit(OpCodes.Call, new GenericInstanceMethod(_read) {GenericArguments = {field.FieldType}});
			}
			else if (field.FieldType.IsArray)
			{
				il.Emit(OpCodes.Call, new GenericInstanceMethod(_readArray) { GenericArguments = { field.FieldType.GetElementType() } });
			}
			else if (field.FieldType == ModuleDefinition.TypeSystem.String)
			{
				il.Emit(OpCodes.Call, IsUnicode(field) ? _readStringW : _readStringA);
			}
		}


		private void BeforeSerializeData(ILProcessor il)
		{
			if (il.Body.Method.DeclaringType.BaseType != _autoPacket)
			{
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Call, il.Body.Method.DeclaringType.BaseType.Resolve().Methods.Single(m => m.Name == "SerializeData"));
			}
			else
			{
			}
		}

		private void ItemSerializeData(ILProcessor il, FieldDefinition field)
		{
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldflda, field);


			if (field.FieldType.IsValueType)
			{
				il.Emit(OpCodes.Call, new GenericInstanceMethod(_write) { GenericArguments = { field.FieldType } });
			}
			else if (field.FieldType.IsArray)
			{
				il.Emit(OpCodes.Call, new GenericInstanceMethod(_writeArray) { GenericArguments = { field.FieldType.GetElementType() } });
			}
			else if (field.FieldType == ModuleDefinition.TypeSystem.String)
			{
				il.Emit(OpCodes.Call, IsUnicode(field) ? _writeStringW : _writeStringA);
			}
		}

		private bool Inherits(TypeDefinition type, TypeDefinition baseType)
		{
			if (type?.BaseType == null) return false;
			if (type.BaseType == baseType) return true;
			var res = type.BaseType as TypeDefinition;
			return Inherits(res, baseType);
		}

		private void MakeMethod(TypeDefinition type, MethodDefinition interfaceMethod, Action<ILProcessor> before, Action<ILProcessor, FieldDefinition> item, Action<ILProcessor> after, bool isOverride)
		{
			var method = type.Methods.SingleOrDefault(m => m.Name == interfaceMethod.Name);
			if (method is null)
			{

				method = new MethodDefinition(interfaceMethod.Name, MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | (isOverride ? MethodAttributes.CompilerControlled : MethodAttributes.NewSlot),
					interfaceMethod.ReturnType);

				foreach (var parameter in interfaceMethod.Parameters) method.Parameters.Add(parameter);

				type.Methods.Add(method);
			}
			else
			{
				if (method.Body.Instructions.Count > 2) return; // this method has a proper body ignore
				if (method.Body.Instructions.Count == 2 && method.Body.Instructions[0].OpCode != OpCodes.Ldc_I4_M1) return; // returns something other that placeholder for get_MaxSize
				// last instruction is always ret
				method.Body.Instructions.Clear();
			}

			var il = method.Body.GetILProcessor();

			before?.Invoke(il);
			foreach (var field in type.Fields)
			{
				item?.Invoke(il, field);
			}
			after?.Invoke(il);

		}

		public override IEnumerable<string> GetAssembliesForScanning()
		{
			yield break;
		}
	}
}

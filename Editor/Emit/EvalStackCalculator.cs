﻿using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using Obfuz.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Assertions;

namespace Obfuz.Emit
{
    enum EvalDataType
    {
        None,
        Int32,
        Int64,
        Float,
        Double,
        I,
        Ref,
        Unknown,
    }

    class InstructionParameterInfo
    {
        public readonly EvalDataType op1;
        public readonly EvalDataType op2;
        public readonly EvalDataType retType;
        public InstructionParameterInfo(EvalDataType op1, EvalDataType op2, EvalDataType retType)
        {
            this.op1 = op1;
            this.op2 = op2;
            this.retType = retType;
        }
    }

    class EvalStackCalculator
    {
        private readonly MethodDef _method;
        private readonly BasicBlockCollection _basicBlocks;
        private readonly Dictionary<Instruction, InstructionParameterInfo> _instructionParameterInfos = new Dictionary<Instruction, InstructionParameterInfo>();
        private readonly Dictionary<Instruction, EvalDataType> _evalStackTopDataTypeAfterInstructions = new Dictionary<Instruction, EvalDataType>();

        public EvalStackCalculator(MethodDef method)
        {
            _method = method;
            _basicBlocks = new BasicBlockCollection(method, false);

            SimulateRunAllBlocks();
        }

        public bool TryGetParameterInfo(Instruction inst, out InstructionParameterInfo info)
        {
            return _instructionParameterInfos.TryGetValue(inst, out info);
        }

        public bool TryGetPushResult(Instruction inst, out EvalDataType result)
        {
            return _evalStackTopDataTypeAfterInstructions.TryGetValue(inst, out result);
        }

        class EvalStackState
        {
            public bool visited;

            public readonly List<EvalDataType> inputStackDatas = new List<EvalDataType>();
            public readonly List<EvalDataType> runStackDatas = new List<EvalDataType>();
        }

        private void PushStack(List<EvalDataType> datas, TypeSig type)
        {
            type = type.RemovePinnedAndModifiers();
            switch (type.ElementType)
            {
                case ElementType.Void: break;
                case ElementType.Boolean:
                case ElementType.Char:
                case ElementType.I1:
                case ElementType.U1:
                case ElementType.I2:
                case ElementType.U2:
                case ElementType.I4:
                case ElementType.U4:
                datas.Add(EvalDataType.Int32);
                break;
                case ElementType.I8:
                case ElementType.U8:
                datas.Add(EvalDataType.Int64);
                break;
                case ElementType.R4:
                datas.Add(EvalDataType.Float);
                break;
                case ElementType.R8:
                datas.Add(EvalDataType.Double);
                break;
                case ElementType.I:
                case ElementType.U:
                case ElementType.Ptr:
                case ElementType.FnPtr:
                case ElementType.ByRef:
                datas.Add(EvalDataType.I);
                break;
                case ElementType.String:
                case ElementType.Class:
                case ElementType.Array:
                case ElementType.SZArray:
                case ElementType.Object:
                datas.Add(EvalDataType.Ref);
                break;
                case ElementType.ValueType:
                {
                    TypeDef typeDef = type.ToTypeDefOrRef().ResolveTypeDefThrow();
                    if (typeDef.IsEnum)
                    {
                        PushStack(datas, typeDef.GetEnumUnderlyingType());
                    }
                    else
                    {
                        PushStack(datas, EvalDataType.Unknown);
                    }
                    break;
                }
                case ElementType.GenericInst:
                {
                    GenericInstSig genericInstSig = (GenericInstSig)type;
                    PushStack(datas, genericInstSig.GenericType);
                    break;
                }
                case ElementType.Var:
                case ElementType.MVar:
                case ElementType.ValueArray:
                case ElementType.TypedByRef:
                case ElementType.R:
                case ElementType.CModOpt:
                case ElementType.CModReqd:
                case ElementType.Internal:
                case ElementType.Module:
                case ElementType.Sentinel:
                PushStack(datas, EvalDataType.Unknown);
                break;

                default: throw new Exception($"Unsupported type: {type} in method: {_method.FullName}.");
            }
        }

        private void PushStack(List<EvalDataType> datas, ITypeDefOrRef type)
        {
            PushStack(datas, type.ToTypeSig());
        }

        private void PushStack(List<EvalDataType> datas, EvalDataType type)
        {
            datas.Add(type);
        }

        private  EvalDataType CalcBasicBinOpRetType(List<EvalDataType> datas, EvalDataType op1, EvalDataType op2)
        {
            switch (op1)
            {
                case EvalDataType.Int32:
                {
                    switch (op2)
                    {
                        case EvalDataType.Int32: return EvalDataType.Int32;
                        case EvalDataType.Int64: return EvalDataType.Int64;
                        case EvalDataType.I: return EvalDataType.I;
                        default: throw new Exception($"Unsupported operand type: {op2} for {op1} in binary operation.");
                    }
                }
                case EvalDataType.Int64:
                {
                    switch (op2)
                    {
                        case EvalDataType.Int32: return EvalDataType.Int64;
                        case EvalDataType.Int64:
                        case EvalDataType.I:
                            return EvalDataType.Int64;
                        default: throw new Exception($"Unsupported operand type: {op2} for {op1} in binary operation.");
                    }
                }
                case EvalDataType.I:
                {
                    switch (op2)
                    {
                        case EvalDataType.Int32: return EvalDataType.I;
                        case EvalDataType.Int64: return EvalDataType.Int64;
                        case EvalDataType.I: return EvalDataType.I;
                        default: throw new Exception($"Unsupported operand type: {op2} for {op1} in binary operation.");
                    }
                }
                case EvalDataType.Float:
                {
                    switch (op2)
                    {
                        case EvalDataType.Float: return EvalDataType.Float;
                        default: throw new Exception($"Unsupported operand type: {op2} for {op1} in binary operation.");
                    }
                }
                case EvalDataType.Double:
                {
                    switch (op2)
                    {
                        case EvalDataType.Double: return EvalDataType.Double;
                        default: throw new Exception($"Unsupported operand type: {op2} for {op1} in binary operation.");
                    }
                }
                default: throw new Exception($"Unsupported operand type: {op1} in binary operation.");
            }
        }

        private void SimulateRunAllBlocks()
        {
            Dictionary<BasicBlock, EvalStackState> blockEvalStackStates = _basicBlocks.Blocks.ToDictionary(b => b, b => new EvalStackState());
            bool methodHasReturnValue = _method.ReturnType.ElementType != ElementType.Void;

            CilBody body = _method.Body;
            if (body.HasExceptionHandlers)
            {
                foreach (ExceptionHandler handler in body.ExceptionHandlers)
                {
                    if (handler.IsCatch)
                    {
                        BasicBlock bb = _basicBlocks.GetBasicBlockByInstruction(handler.HandlerStart);
                        blockEvalStackStates[bb].runStackDatas.Add(EvalDataType.Ref); // Exception object is pushed onto the stack.
                    }
                    else if (handler.IsFilter)
                    {
                        BasicBlock bb = _basicBlocks.GetBasicBlockByInstruction(handler.FilterStart);
                        blockEvalStackStates[bb].runStackDatas.Add(EvalDataType.Ref); // Exception object is pushed onto the stack.
                    }
                }
            }

            var newPushedDatas = new List<EvalDataType>();
            IList<TypeSig> methodTypeGenericArgument = _method.DeclaringType.GenericParameters.Count > 0
                ? (IList<TypeSig>)_method.DeclaringType.GenericParameters.Select(p => (TypeSig)new GenericVar(p.Number)).ToList()
                : null;
            IList<TypeSig> methodMethodGenericArgument = _method.GenericParameters.Count > 0
                ? (IList<TypeSig>)_method.GenericParameters.Select(p => (TypeSig)new GenericMVar(p.Number)).ToList()
                : null;
            var gac = new GenericArgumentContext(methodTypeGenericArgument, methodMethodGenericArgument);

            var blockWalkStack = new Stack<BasicBlock>(_basicBlocks.Blocks.Reverse());
            while (blockWalkStack.Count > 0)
            {
                BasicBlock block = blockWalkStack.Pop();
                EvalStackState state = blockEvalStackStates[block];
                if (state.visited)
                    continue;
                state.visited = true;
                state.runStackDatas.AddRange(state.inputStackDatas);
                List<EvalDataType> stackDatas = state.runStackDatas;
                foreach (var inst in block.instructions)
                {
                    int stackSize = stackDatas.Count;
                    newPushedDatas.Clear();
                    switch (inst.OpCode.Code)
                    {
                        case Code.Nop: break;
                        case Code.Break: break;
                        case Code.Ldarg_0:
                        case Code.Ldarg_1:
                        case Code.Ldarg_2:
                        case Code.Ldarg_3:
                        case Code.Ldarg:
                        case Code.Ldarg_S:
                        {
                            PushStack(newPushedDatas, inst.GetParameter(_method.Parameters).Type);
                            break;
                        }
                        case Code.Ldarga:
                        case Code.Ldarga_S:
                        {
                            PushStack(newPushedDatas, EvalDataType.I);
                            break;
                        }
                        case Code.Ldloc_0:
                        case Code.Ldloc_1:
                        case Code.Ldloc_2:
                        case Code.Ldloc_3:
                        case Code.Ldloc:
                        case Code.Ldloc_S:
                        {
                            PushStack(newPushedDatas, inst.GetLocal(body.Variables).Type);
                            break;
                        }
                        case Code.Ldloca:
                        case Code.Ldloca_S:
                        {
                            PushStack(newPushedDatas, EvalDataType.I);
                            break;
                        }
                        case Code.Stloc_0:
                        case Code.Stloc_1:
                        case Code.Stloc_2:
                        case Code.Stloc_3:
                        case Code.Stloc:
                        case Code.Stloc_S:
                        {
                            Assert.IsTrue(stackSize > 0);
                            break;
                        }
                        case Code.Starg:
                        case Code.Starg_S:
                        {
                            Assert.IsTrue(stackSize > 0);
                            break;
                        }
                        case Code.Ldnull:
                        {
                            PushStack(newPushedDatas, EvalDataType.I);
                            break;
                        }
                        case Code.Ldc_I4_M1:
                        case Code.Ldc_I4_0:
                        case Code.Ldc_I4_1:
                        case Code.Ldc_I4_2:
                        case Code.Ldc_I4_3:
                        case Code.Ldc_I4_4:
                        case Code.Ldc_I4_5:
                        case Code.Ldc_I4_6:
                        case Code.Ldc_I4_7:
                        case Code.Ldc_I4_8:
                        case Code.Ldc_I4:
                        case Code.Ldc_I4_S:
                        {
                            PushStack(newPushedDatas, EvalDataType.Int32);
                            break;
                        }
                        case Code.Ldc_I8:
                        {
                            PushStack(newPushedDatas, EvalDataType.Int64);
                            break;
                        }
                        case Code.Ldc_R4:
                        {
                            PushStack(newPushedDatas, EvalDataType.Float);
                            break;
                        }
                        case Code.Ldc_R8:
                        {
                            PushStack(newPushedDatas, EvalDataType.Double);
                            break;
                        }
                        case Code.Dup:
                        {
                            Assert.IsTrue(stackSize > 0);
                            EvalDataType type = stackDatas[stackSize - 1];
                            PushStack(newPushedDatas, type);
                            PushStack(newPushedDatas, type);
                            break;
                        }
                        case Code.Pop:
                        {
                            break;
                        }
                        case Code.Jmp:
                        {
                            break;
                        }
                        case Code.Call:
                        case Code.Callvirt:
                        {
                            IMethod calledMethod = (IMethod)inst.Operand;
                            MethodSig methodSig = MetaUtil.GetInflatedMethodSig(calledMethod, gac);
                            PushStack(newPushedDatas, methodSig.RetType);
                            break;
                        }
                        case Code.Calli:
                        {
                            MethodSig methodSig = (MethodSig)inst.Operand;
                            PushStack(newPushedDatas, methodSig.RetType);
                            break;
                        }
                        case Code.Ret:
                        {
                            break;
                        }
                        case Code.Br:
                        case Code.Br_S:
                        case Code.Brfalse:
                        case Code.Brfalse_S:
                        case Code.Brtrue:
                        case Code.Brtrue_S:
                        case Code.Beq:
                        case Code.Beq_S:
                        case Code.Bge:
                        case Code.Bge_S:
                        case Code.Bge_Un:
                        case Code.Bge_Un_S:
                        case Code.Bgt:
                        case Code.Bgt_S:
                        case Code.Bgt_Un:
                        case Code.Bgt_Un_S:
                        case Code.Ble:
                        case Code.Ble_S:
                        case Code.Ble_Un:
                        case Code.Ble_Un_S:
                        case Code.Blt:
                        case Code.Blt_S:
                        case Code.Blt_Un:
                        case Code.Blt_Un_S:
                        case Code.Bne_Un:
                        case Code.Bne_Un_S:
                        {
                            // Branch instructions do not change the stack.
                            break;
                        }
                        case Code.Ceq:
                        case Code.Cgt:
                        case Code.Cgt_Un:
                        case Code.Clt:
                        case Code.Clt_Un:
                        {
                            Assert.IsTrue(stackSize >= 2);
                            EvalDataType op2 = stackDatas[stackSize - 1];
                            EvalDataType op1 = stackDatas[stackSize - 2];
                            EvalDataType ret = EvalDataType.Int32;
                            _instructionParameterInfos.Add(inst, new InstructionParameterInfo(op1, op2, ret));
                            PushStack(newPushedDatas, ret);
                            break;
                        }
                        case Code.Switch:
                        {
                            // Switch instruction does not change the stack.
                            break;
                        }
                        case Code.Ldind_I1:
                        case Code.Ldind_U1:
                        case Code.Ldind_I2:
                        case Code.Ldind_U2:
                        case Code.Ldind_I4:
                        case Code.Ldind_U4:
                        {
                            Assert.IsTrue(stackSize > 0);
                            PushStack(newPushedDatas, EvalDataType.Int32);
                            break;
                        }
                        case Code.Ldind_I8:
                        {
                            Assert.IsTrue(stackSize > 0);
                            PushStack(newPushedDatas, EvalDataType.Int64);
                            break;
                        }
                        case Code.Ldind_I:
                        {
                            Assert.IsTrue(stackSize > 0);
                            PushStack(newPushedDatas, EvalDataType.I);
                            break;
                        }
                        case Code.Ldind_Ref:
                        {
                            Assert.IsTrue(stackSize > 0);
                            PushStack(newPushedDatas, EvalDataType.Ref);
                            break;
                        }
                        case Code.Ldind_R4:
                        {
                            Assert.IsTrue(stackSize > 0);
                            PushStack(newPushedDatas, EvalDataType.Float);
                            break;
                        }
                        case Code.Ldind_R8:
                        {
                            Assert.IsTrue(stackSize > 0);
                            PushStack(newPushedDatas, EvalDataType.Double);
                            break;
                        }
                        case Code.Stind_I1:
                        case Code.Stind_I2:
                        case Code.Stind_I4:
                        case Code.Stind_I8:
                        case Code.Stind_I:
                        case Code.Stind_R4:
                        case Code.Stind_R8:
                        case Code.Stind_Ref:
                        {
                            Assert.IsTrue(stackSize >= 2);
                            break;
                        }

                        case Code.Add:
                        case Code.Add_Ovf:
                        case Code.Add_Ovf_Un:
                        case Code.Sub:
                        case Code.Sub_Ovf:
                        case Code.Sub_Ovf_Un:
                        case Code.Mul:
                        case Code.Mul_Ovf:
                        case Code.Mul_Ovf_Un:
                        case Code.Div:
                        case Code.Div_Un:
                        case Code.Rem:
                        case Code.Rem_Un:

                        case Code.And:
                        case Code.Or:
                        case Code.Xor:
                        {
                            Assert.IsTrue(stackSize >= 2);
                            EvalDataType op2 = stackDatas[stackSize - 1];
                            EvalDataType op1 = stackDatas[stackSize - 2];
                            EvalDataType ret = CalcBasicBinOpRetType(stackDatas, op1, op2);
                            _instructionParameterInfos.Add(inst, new InstructionParameterInfo(op1, op2, ret));
                            PushStack(newPushedDatas, ret);
                            break;
                        }
                        case Code.Shl:
                        case Code.Shr:
                        case Code.Shr_Un:
                        {
                            Assert.IsTrue(stackSize >= 2);
                            EvalDataType op2 = stackDatas[stackSize - 1];
                            EvalDataType op1 = stackDatas[stackSize - 2];
                            if (op1 != EvalDataType.Int32 && op1 != EvalDataType.Int64 && op1 != EvalDataType.I)
                                throw new Exception($"Unsupported operand type: {op1} in shift operation.");
                            if (op2 != EvalDataType.Int32 && op2 != EvalDataType.Int64)
                                throw new Exception($"Unsupported operand type: {op2} for {op1} in shift operation.");
                            EvalDataType ret = op1;
                            _instructionParameterInfos.Add(inst, new InstructionParameterInfo(op1, op2, ret));
                            PushStack(newPushedDatas, ret);
                            break;
                        }
                        case Code.Neg:
                        {
                            Assert.IsTrue(stackSize > 0);
                            EvalDataType op = stackDatas[stackSize - 1];
                            EvalDataType ret = op;
                            switch (op)
                            {
                                case EvalDataType.Int32:
                                case EvalDataType.Int64:
                                case EvalDataType.I:
                                case EvalDataType.Float:
                                case EvalDataType.Double:
                                break;
                                default:
                                throw new Exception($"Unsupported operand type: {op} in unary operation.");
                            }
                            _instructionParameterInfos.Add(inst, new InstructionParameterInfo(op, EvalDataType.None, ret));
                            PushStack(newPushedDatas, ret);
                            break;
                        }
                        case Code.Not:
                        {
                            Assert.IsTrue(stackSize > 0);
                            EvalDataType op = stackDatas[stackSize - 1];
                            EvalDataType ret = op;
                            if (op != EvalDataType.Int32 && op != EvalDataType.Int64 && op != EvalDataType.I)
                                throw new Exception($"Unsupported operand type: {op} in unary operation.");
                            _instructionParameterInfos.Add(inst, new InstructionParameterInfo(op, EvalDataType.None, ret));
                            PushStack(newPushedDatas, ret);
                            break;
                        }
                        case Code.Conv_I1:
                        case Code.Conv_U1:
                        case Code.Conv_I2:
                        case Code.Conv_U2:
                        case Code.Conv_I4:
                        case Code.Conv_U4:
                        {
                            PushStack(newPushedDatas, EvalDataType.Int32);
                            break;
                        }
                        case Code.Conv_I8:
                        case Code.Conv_U8:
                        {
                            PushStack(newPushedDatas, EvalDataType.Int64);
                            break;
                        }
                        case Code.Conv_I:
                        case Code.Conv_U:
                        {
                            PushStack(newPushedDatas, EvalDataType.I);
                            break;
                        }
                        case Code.Conv_R4:
                        {
                            PushStack(newPushedDatas, EvalDataType.Float);
                            break;
                        }
                        case Code.Conv_R8:
                        {
                            PushStack(newPushedDatas, EvalDataType.Double);
                            break;
                        }
                        case Code.Conv_Ovf_I1:
                        case Code.Conv_Ovf_I1_Un:
                        case Code.Conv_Ovf_U1:
                        case Code.Conv_Ovf_U1_Un:
                        case Code.Conv_Ovf_I2:
                        case Code.Conv_Ovf_I2_Un:
                        case Code.Conv_Ovf_U2:
                        case Code.Conv_Ovf_U2_Un:
                        case Code.Conv_Ovf_I4:
                        case Code.Conv_Ovf_I4_Un:
                        case Code.Conv_Ovf_U4:
                        case Code.Conv_Ovf_U4_Un:
                        {
                            PushStack(newPushedDatas, EvalDataType.Int32);
                            break;
                        }
                        case Code.Conv_Ovf_I8:
                        case Code.Conv_Ovf_I8_Un:
                        case Code.Conv_Ovf_U8:
                        case Code.Conv_Ovf_U8_Un:
                        {
                            PushStack(newPushedDatas, EvalDataType.Int64);
                            break;
                        }
                        case Code.Conv_Ovf_I:
                        case Code.Conv_Ovf_I_Un:
                        case Code.Conv_Ovf_U:
                        case Code.Conv_Ovf_U_Un:
                        {
                            PushStack(newPushedDatas, EvalDataType.I);
                            break;
                        }
                        case Code.Conv_R_Un:
                        {
                            //PushStack(newDataTack, EvalDataType.Double);
                            //break;
                            throw new Exception($"unsupported opcode:{inst}");
                        }
                        case Code.Cpobj:
                        case Code.Initobj:
                        case Code.Stobj:
                        {
                            break;
                        }
                        case Code.Ldobj:
                        {
                            PushStack(newPushedDatas, (ITypeDefOrRef)inst.Operand);
                            break;
                        }
                        case Code.Ldstr:
                        {
                            PushStack(newPushedDatas, EvalDataType.Ref);
                            break;
                        }
                        case Code.Newobj:
                        {
                            IMethod ctor = (IMethod)inst.Operand;
                            PushStack(newPushedDatas, ctor.DeclaringType);
                            break;
                        }
                        case Code.Castclass:
                        {
                            PushStack(newPushedDatas, (ITypeDefOrRef)inst.Operand);
                            break;
                        }
                        case Code.Isinst:
                        {
                            PushStack(newPushedDatas, EvalDataType.Ref);
                            break;
                        }
                        case Code.Unbox:
                        {
                            Assert.IsTrue(stackSize > 0);
                            PushStack(newPushedDatas, EvalDataType.I);
                            break;
                        }
                        case Code.Unbox_Any:
                        {
                            Assert.IsTrue(stackSize > 0);
                            PushStack(newPushedDatas, (ITypeDefOrRef)inst.Operand);
                            break;
                        }
                        case Code.Box:
                        {
                            Assert.IsTrue(stackSize > 0);
                            PushStack(newPushedDatas, EvalDataType.Ref);
                            break;
                        }
                        case Code.Throw:
                        {
                            // Throw instruction does not change the stack.
                            break;
                        }
                        case Code.Rethrow:
                        {
                            // Rethrow instruction does not change the stack.
                            break;
                        }
                        case Code.Ldfld:
                        case Code.Ldsfld:
                        {
                            IField field = (IField)inst.Operand;
                            TypeSig fieldType = MetaUtil.InflateFieldSig(field, gac);
                            PushStack(newPushedDatas, field.FieldSig.GetFieldType());
                            break;
                        }
                        case Code.Ldflda:
                        case Code.Ldsflda:
                        {
                            PushStack(newPushedDatas, EvalDataType.I);
                            break;
                        }
                        case Code.Stfld:
                        case Code.Stsfld:
                        {
                            break;
                        }
                        case Code.Newarr:
                        {
                            Assert.IsTrue(stackSize > 0);
                            PushStack(newPushedDatas, EvalDataType.Ref);
                            break;
                        }
                        case Code.Ldlen:
                        {
                            Assert.IsTrue(stackSize > 0);
                            PushStack(newPushedDatas, EvalDataType.I);
                            break;
                        }
                        case Code.Ldelema:
                        {
                            Assert.IsTrue(stackSize >= 2);
                            PushStack(newPushedDatas, EvalDataType.I);
                            break;
                        }
                        case Code.Ldelem_I1:
                        case Code.Ldelem_U1:
                        case Code.Ldelem_I2:
                        case Code.Ldelem_U2:
                        case Code.Ldelem_I4:
                        case Code.Ldelem_U4:
                        {
                            Assert.IsTrue(stackSize >= 2);
                            PushStack(newPushedDatas, EvalDataType.Int32);
                            break;
                        }
                        case Code.Ldelem_I8:
                        {
                            Assert.IsTrue(stackSize >= 2);
                            PushStack(newPushedDatas, EvalDataType.Int64);
                            break;
                        }
                        case Code.Ldelem_I:
                        {
                            Assert.IsTrue(stackSize >= 2);
                            PushStack(newPushedDatas, EvalDataType.I);
                            break;
                        }
                        case Code.Ldelem_R4:
                        {
                            Assert.IsTrue(stackSize >= 2);
                            PushStack(newPushedDatas, EvalDataType.Float);
                            break;
                        }
                        case Code.Ldelem_R8:
                        {
                            Assert.IsTrue(stackSize >= 2);
                            PushStack(newPushedDatas, EvalDataType.Double);
                            break;
                        }
                        case Code.Ldelem_Ref:
                        {
                            Assert.IsTrue(stackSize >= 2);
                            PushStack(newPushedDatas, EvalDataType.Ref);
                            break;
                        }
                        case Code.Ldelem:
                        {
                            Assert.IsTrue(stackSize >= 2);
                            PushStack(newPushedDatas, (ITypeDefOrRef)inst.Operand);
                            break;
                        }
                        case Code.Stelem_I1:
                        case Code.Stelem_I2:
                        case Code.Stelem_I4:
                        case Code.Stelem_I8:
                        case Code.Stelem_I:
                        case Code.Stelem_R4:
                        case Code.Stelem_R8:
                        case Code.Stelem_Ref:
                        case Code.Stelem:
                        {
                            Assert.IsTrue(stackSize >= 3);
                            break;
                        }
                        case Code.Mkrefany:
                        {
                            PushStack(newPushedDatas, EvalDataType.Unknown);
                            break;
                        }
                        case Code.Refanytype:
                        {
                            PushStack(newPushedDatas, EvalDataType.Unknown);
                            break;
                        }
                        case Code.Refanyval:
                        {
                            Assert.IsTrue(stackSize > 0);
                            PushStack(newPushedDatas, (ITypeDefOrRef)inst.Operand);
                            break;
                        }
                        case Code.Ldtoken:
                        {
                            PushStack(newPushedDatas, EvalDataType.Unknown);
                            break;
                        }
                        case Code.Endfinally:
                        case Code.Leave:
                        case Code.Leave_S:
                        {
                            break;
                        }
                        case Code.Endfilter:
                        {
                            break;
                        }
                        case Code.Arglist:
                        {
                            break;
                        }
                        case Code.Ldftn:
                        case Code.Ldvirtftn:
                        {
                            PushStack(newPushedDatas, EvalDataType.Unknown);
                            break;
                        }
                        case Code.Localloc:
                        {
                            PushStack(newPushedDatas, EvalDataType.I);
                            break;
                        }
                        case Code.Unaligned:
                        case Code.Volatile:
                        case Code.Tailcall:
                        case Code.No:
                        case Code.Readonly:
                        case Code.Constrained:
                        {
                            break;
                        }
                        case Code.Cpblk:
                        case Code.Initblk:
                        {
                            break;
                        }
                        case Code.Sizeof:
                        {
                            PushStack(newPushedDatas, EvalDataType.Int32);
                            break;
                        }
                        default: throw new Exception($"not supported opcode: {inst} in method: {_method.FullName}.");
                    }

                    inst.CalculateStackUsage(methodHasReturnValue, out var pushed, out var pops);
                    if (pushed != newPushedDatas.Count)
                    {
                        throw new Exception($"Instruction {inst} in method {_method.FullName} pushed {newPushedDatas.Count} items, but expected {pushed} items.");
                    }
                    if (pops == -1)
                    {
                        stackDatas.Clear();
                    }
                    else
                    {
                        if (stackSize < pops)
                        {
                            throw new Exception($"Instruction {inst} in method {_method.FullName} pops {pops} items, but only {stackSize} items are available on the stack.");
                        }
                        stackDatas.RemoveRange(stackDatas.Count - pops, pops);
                        stackDatas.AddRange(newPushedDatas);
                        Assert.AreEqual(stackSize + pushed - pops, stackDatas.Count);
                    }
                    if (pushed > 0 && stackDatas.Count > 0)
                    {
                        _evalStackTopDataTypeAfterInstructions[inst] = stackDatas.Last();
                    }
                }
                foreach (BasicBlock outBb in block.outBlocks)
                {
                    EvalStackState outState = blockEvalStackStates[outBb];
                    if (outState.visited)
                    {
                        if (stackDatas.Count != outState.inputStackDatas.Count)
                        {
                            throw new Exception($"Block {block} in method {_method.FullName} has inconsistent stack data. Expected {outState.inputStackDatas.Count}, but got {stackDatas.Count}.");
                        }
                    }
                    else if (outState.inputStackDatas.Count != stackDatas.Count)
                    {
                        if (outState.inputStackDatas.Count > 0)
                        {
                            throw new Exception($"Block {outBb} in method {_method.FullName} has inconsistent stack data. Expected {outState.inputStackDatas.Count}, but got {stackDatas.Count}.");
                        }
                        outState.inputStackDatas.AddRange(stackDatas);
                        blockWalkStack.Push(outBb);
                    }
                }
            }
        }
    }
}

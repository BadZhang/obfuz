﻿using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System.Collections.Generic;

namespace Obfuz.ObfusPasses.ExprObfus
{
    public class ExprObfusPass : InstructionObfuscationPassBase
    {
        public override ObfuscationPassType Type => ObfuscationPassType.ExprObfus;

        public override void Start()
        {

        }

        public override void Stop()
        {

        }

        protected override bool NeedObfuscateMethod(MethodDef method)
        {
            return false;
        }

        protected override bool TryObfuscateInstruction(MethodDef callingMethod, Instruction inst, IList<Instruction> instructions, int instructionIndex, List<Instruction> outputInstructions, List<Instruction> totalFinalInstructions)
        {
            return false;
        }
    }
}

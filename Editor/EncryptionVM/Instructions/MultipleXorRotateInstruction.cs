﻿using Obfuz.Utils;
using System.Collections.Generic;

namespace Obfuz.EncryptionVM.Instructions
{
    public class MultipleXorRotateInstruction : EncryptionInstructionBase
    {
        // x = x * p1 + secretKey[index1];
        // x = x ^ p3 ^ salt;
        // x = Rotate(x, p2)

        private readonly int _multipleValue;
        private readonly int _revertMultipleValue;
        private readonly int _index1;
        private readonly int _rotateBitNum;
        private readonly int _xorValue;

        public MultipleXorRotateInstruction(int multipleValue, int index1, int xorValue, int rotateBitNum)
        {
            _multipleValue = multipleValue;
            _revertMultipleValue = MathUtil.ModInverse32(multipleValue);
            _index1 = index1;
            _rotateBitNum = rotateBitNum;
            _xorValue = xorValue;
        }

        public override int Encrypt(int value, int[] secretKey, int salt)
        {
            value = value * _multipleValue + secretKey[_index1];
            value ^= _xorValue ^ salt;
            uint part1 = (uint)value << _rotateBitNum;
            uint part2 = (uint)value >> (32 - _rotateBitNum);
            value = (int)(part1 | part2);
            return value;
        }

        public override int Decrypt(int value, int[] secretKey, int salt)
        {
            uint value2 = (uint)value >> _rotateBitNum;
            uint part1 = (uint)value << (32 - _rotateBitNum);
            value = (int)(value2 | part1);
            value ^= _xorValue ^ salt;
            value = (value - secretKey[_index1]) * _revertMultipleValue;
            return value;
        }

        public override void GenerateEncryptCode(List<string> lines, string indent)
        {
            lines.Add(indent + $"value = value * {_multipleValue} + _secretKey[{_index1}];");
            lines.Add(indent + $"value ^= {_xorValue} ^ salt;");
            lines.Add(indent + $"uint part1 = (uint)value << {_rotateBitNum};");
            lines.Add(indent + $"uint part2 = (uint)value >> (32 - {_rotateBitNum});");
            lines.Add(indent + $"value = (int)(part1 | part2);");
        }

        public override void GenerateDecryptCode(List<string> lines, string indent)
        {
            lines.Add(indent + $"uint value2 = (uint)value >> {_rotateBitNum};");
            lines.Add(indent + $"uint part1 = (uint)value << (32 - {_rotateBitNum});");
            lines.Add(indent + $"value = (int)(value2 | part1);");
            lines.Add(indent + $"value ^= {_xorValue} ^ salt;");
            lines.Add(indent + $"value = (value - _secretKey[{_index1}]) * {_revertMultipleValue};");
        }
    }
}

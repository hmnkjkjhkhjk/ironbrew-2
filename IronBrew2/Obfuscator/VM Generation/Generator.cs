using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using IronBrew2.Bytecode_Library.Bytecode;
using IronBrew2.Bytecode_Library.IR;
using IronBrew2.Extensions;
using IronBrew2.Obfuscator.Opcodes;

namespace IronBrew2.Obfuscator.VM_Generation
{
    public class Generator
    {
        private readonly ObfuscationContext _context;
        private readonly Random _rng;

        public Generator(ObfuscationContext context)
        {
            _context = context;
            _rng = new Random(Environment.TickCount);
        }

        public bool IsUsed(Chunk chunk, VOpcode virt)
        {
            bool used = false;
            foreach (Instruction ins in chunk.Instructions)
            {
                if (virt.IsInstruction(ins))
                {
                    if (!_context.InstructionMapping.ContainsKey(ins.OpCode))
                        _context.InstructionMapping.Add(ins.OpCode, virt);
                    ins.CustomData = new CustomInstructionData { Opcode = virt };
                    used = true;
                }
            }
            foreach (Chunk s in chunk.Functions)
                used |= IsUsed(s, virt);
            return used;
        }

        public static List<int> Compress(byte[] uncompressed)
        {
            var dict = new Dictionary<string, int>();
            for (int i = 0; i < 256; i++) dict.Add(((char)i).ToString(), i);
            string w = string.Empty;
            var outLzw = new List<int>();
            foreach (byte b in uncompressed)
            {
                string wc = w + (char)b;
                if (dict.ContainsKey(wc)) w = wc;
                else { outLzw.Add(dict[w]); dict.Add(wc, dict.Count); w = ((char)b).ToString(); }
            }
            if (!string.IsNullOrEmpty(w)) outLzw.Add(dict[w]);
            return outLzw;
        }

        public static string ToBase36(ulong value)
        {
            const string base36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var sb = new StringBuilder(13);
            do { sb.Insert(0, base36[(byte)(value % 36)]); value /= 36; } while (value != 0);
            return sb.ToString();
        }

        public static string CompressedToString(List<int> compressed)
        {
            var sb = new StringBuilder();
            foreach (int i in compressed)
            {
                string n = ToBase36((ulong)i);
                sb.Append(ToBase36((ulong)n.Length));
                sb.Append(n);
            }
            return sb.ToString();
        }

        public List<OpMutated> GenerateMutations(List<VOpcode> opcodes)
        {
            var r = _rng;
            var mutated = new List<OpMutated>();
            foreach (var opc in opcodes)
            {
                if (opc is OpSuperOperator) continue;
                for (int i = 0; i < r.Next(35, 50); i++)
                {
                    int[] rand = { 0, 1, 2 };
                    rand.Shuffle();
                    mutated.Add(new OpMutated { Registers = rand, Mutated = opc });
                }
            }
            mutated.Shuffle();
            return mutated;
        }

        public void FoldMutations(List<OpMutated> mutations, HashSet<OpMutated> chosen, Chunk chunk)
        {
            bool[] skip = new bool[chunk.Instructions.Count + 1];

            for (int i = 0; i < chunk.Instructions.Count; i++)
            {
                Instruction ins = chunk.Instructions[i];
                if (ins.OpCode == Opcode.Closure)
                    for (int j = 1; j <= ((Chunk)ins.RefOperands[0]).UpvalueCount; j++)
                        skip[i + j] = true;
            }

            for (int i = 0; i < chunk.Instructions.Count; i++)
            {
                if (skip[i]) continue;
                var data = chunk.Instructions[i].CustomData;
                foreach (var mut in mutations)
                    if (data.Opcode == mut.Mutated && data.WrittenOpcode == null)
                    {
                        if (!chosen.Contains(mut)) chosen.Add(mut);
                        data.Opcode = mut;
                        break;
                    }
            }

            foreach (Chunk s in chunk.Functions)
                FoldMutations(mutations, chosen, s);
        }

        public List<OpSuperOperator> GenerateSuperOperators(Chunk chunk, int maxSize, int minSize = 5)
        {
            var results = new List<OpSuperOperator>();
            bool[] skip = new bool[chunk.Instructions.Count + 1];

            for (int i = 0; i < chunk.Instructions.Count - 1; i++)
            {
                switch (chunk.Instructions[i].OpCode)
                {
                    case Opcode.Closure:
                        skip[i] = true;
                        for (int j = 0; j < ((Chunk)chunk.Instructions[i].RefOperands[0]).UpvalueCount; j++)
                            skip[i + j + 1] = true;
                        break;
                    case Opcode.Eq:
                    case Opcode.Lt:
                    case Opcode.Le:
                    case Opcode.Test:
                    case Opcode.TestSet:
                    case Opcode.TForLoop:
                    case Opcode.SetList:
                    case Opcode.LoadBool when chunk.Instructions[i].C != 0:
                        skip[i + 1] = true;
                        break;
                    case Opcode.ForLoop:
                    case Opcode.ForPrep:
                    case Opcode.Jmp:
                        chunk.Instructions[i].UpdateRegisters();
                        skip[i + 1] = true;
                        skip[i + chunk.Instructions[i].B + 1] = true;
                        break;
                }
                if (chunk.Instructions[i].CustomData.WrittenOpcode is OpSuperOperator su && su.SubOpcodes != null)
                    for (int j = 0; j < su.SubOpcodes.Length; j++)
                        skip[i + j] = true;
            }

            int c = 0;
            while (c < chunk.Instructions.Count)
            {
                int target = maxSize;
                var so = new OpSuperOperator { SubOpcodes = new VOpcode[target] };
                bool direct = true;
                int cut = target;

                for (int j = 0; j < target; j++)
                    if (c + j > chunk.Instructions.Count - 1 || skip[c + j]) { cut = j; direct = false; break; }

                if (!direct)
                {
                    if (cut < minSize) { c += cut + 1; continue; }
                    target = cut;
                    so = new OpSuperOperator { SubOpcodes = new VOpcode[target] };
                }

                for (int j = 0; j < target; j++)
                    so.SubOpcodes[j] = chunk.Instructions[c + j].CustomData.Opcode;

                results.Add(so);
                c += target + 1;
            }

            foreach (var f in chunk.Functions)
                results.AddRange(GenerateSuperOperators(f, maxSize, minSize));

            return results;
        }

        public void FoldAdditionalSuperOperators(Chunk chunk, List<OpSuperOperator> ops, ref int folded)
        {
            bool[] skip = new bool[chunk.Instructions.Count + 1];

            for (int i = 0; i < chunk.Instructions.Count - 1; i++)
            {
                switch (chunk.Instructions[i].OpCode)
                {
                    case Opcode.Closure:
                        skip[i] = true;
                        for (int j = 0; j < ((Chunk)chunk.Instructions[i].RefOperands[0]).UpvalueCount; j++)
                            skip[i + j + 1] = true;
                        break;
                    case Opcode.Eq:
                    case Opcode.Lt:
                    case Opcode.Le:
                    case Opcode.Test:
                    case Opcode.TestSet:
                    case Opcode.TForLoop:
                    case Opcode.SetList:
                    case Opcode.LoadBool when chunk.Instructions[i].C != 0:
                        skip[i + 1] = true;
                        break;
                    case Opcode.ForLoop:
                    case Opcode.ForPrep:
                    case Opcode.Jmp:
                        chunk.Instructions[i].UpdateRegisters();
                        skip[i + 1] = true;
                        skip[i + chunk.Instructions[i].B + 1] = true;
                        break;
                }
                if (chunk.Instructions[i].CustomData.WrittenOpcode is OpSuperOperator su && su.SubOpcodes != null)
                    for (int j = 0; j < su.SubOpcodes.Length; j++)
                        skip[i + j] = true;
            }

            int c = 0;
            while (c < chunk.Instructions.Count)
            {
                if (skip[c]) { c++; continue; }
                bool used = false;

                foreach (var op in ops)
                {
                    int target = op.SubOpcodes.Length;
                    bool contig = true;
                    for (int j = 0; j < target; j++)
                    {
                        if (c + j > chunk.Instructions.Count - 1 || skip[c + j]) { contig = false; break; }
                    }
                    if (!contig) continue;

                    var taken = chunk.Instructions.Skip(c).Take(target).ToList();
                    if (op.IsInstruction(taken))
                    {
                        for (int j = 0; j < target; j++)
                        {
                            skip[c + j] = true;
                            chunk.Instructions[c + j].CustomData.WrittenOpcode = new OpSuperOperator { VIndex = 0 };
                        }
                        chunk.Instructions[c].CustomData.WrittenOpcode = op;
                        used = true;
                        break;
                    }
                }

                if (!used) c++; else folded++;
            }

            foreach (var f in chunk.Functions)
                FoldAdditionalSuperOperators(f, ops, ref folded);
        }

        private string WrapHandlerPolymorphic(string body, int variant, int a, int b)
        {
            if (variant == 0)
                return $@"do local t=(Enum%{a})+1 if ((t%2)==((t*{b})%2)) then (function(){body} end)() else (function(){body} end)() end end";
            else if (variant == 1)
                return $@"do local g=0 local function s1() g=(bx(Enum,_mk)%{a}) end local function s2() if (((g+1)%2)==1) then {body} else {body} end end s1() s2() end";
            else
                return $@"do local v={{}} local f1=function(){body} end local f2=function(){body} end v[1]=f1 v[2]=f2 local i=((bx(Enum,_mk)+{a})%2)+1 v[i]() end";
        }

        public string GenerateVM(ObfuscationSettings settings)
        {
            int seed0 = Environment.TickCount;
            int key = _context.PrimaryXorKey & 0xFF;
            const uint GOLD = 0x9E3779B9u;
            uint mix = ((uint)key) * GOLD;
            int seed = unchecked(seed0 ^ (int)mix);

            var r = _rng;

            var virtuals = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => t.IsSubclassOf(typeof(VOpcode)))
                .Select(Activator.CreateInstance)
                .Cast<VOpcode>()
                .Where(t => IsUsed(_context.HeadChunk, t))
                .ToList();

            if (settings.Mutate)
            {
                var muts = GenerateMutations(virtuals).Take(settings.MaxMutations).ToList();
                var chosen = new HashSet<OpMutated>();
                FoldMutations(muts, chosen, _context.HeadChunk);
                virtuals.AddRange(chosen);
            }

            if (settings.SuperOperators)
            {
                int folded = 0;

                var mega = GenerateSuperOperators(_context.HeadChunk, 80, 60).OrderBy(_ => r.Next())
                    .Take(settings.MaxMegaSuperOperators).ToList();
                virtuals.AddRange(mega);
                FoldAdditionalSuperOperators(_context.HeadChunk, mega, ref folded);

                var mini = GenerateSuperOperators(_context.HeadChunk, 12, 7).OrderBy(_ => r.Next())
                    .Take(settings.MaxMiniSuperOperators).ToList();
                virtuals.AddRange(mini);
                FoldAdditionalSuperOperators(_context.HeadChunk, mini, ref folded);
            }

            virtuals.Shuffle();
            int n = virtuals.Count;
            int[] realToFake = Enumerable.Range(0, n).OrderBy(_ => r.Next()).ToArray();
            int[] fakeToReal = new int[n];
            for (int i = 0; i < n; i++) fakeToReal[realToFake[i]] = i;
            for (int i = 0; i < n; i++) virtuals[i].VIndex = realToFake[i];
            string mapLua = "{" + string.Join(",", fakeToReal.Select(v => v.ToString())) + "}";

            byte[] bs = new Serializer(_context, settings).SerializeLChunk(_context.HeadChunk);

            ulong checksum = 0UL;
            for (int i = 0; i < bs.Length; i++)
                checksum = (checksum + (ulong)bs[i]) & 0xFFFFFFFFUL;

            var vm = new StringBuilder();
            vm.Append(@"
local b=string.byte
local c=string.char
local s=string.sub
local tc=table.concat
local ld=math.ldexp
local gf=getfenv or function() return _ENV end
local sf=select
local up=unpack or table.unpack
");

            if (settings.BytecodeCompress)
            {
                vm.Append(@"local function d0(bx)local c1,d1,e1='', '', {} local f1=256 local g1={} for h1=0,f1-1 do g1[h1]=c(h1) end local i1=1 local function b36(len) local n=0 for j1=0,len-1 do local ch=b(bx,i1+j1) local v if ch>=48 and ch<=57 then v=ch-48 elseif ch>=65 and ch<=90 then v=ch-55 elseif ch>=97 and ch<=122 then v=ch-87 else v=0 end n=n*36+v end i1=i1+len return n end local function k() local l=b36(1) local m=b36(l) return m end c1=c(k()) e1[1]=c1 while i1<#bx do local n2=k() if g1[n2] then d1=g1[n2] else d1=c1..s(c1,1,1) end g1[f1]=c1..s(d1,1,1) e1[#e1+1],c1,f1=d1,d1,f1+1 end return table.concat(e1) end;");
                vm.Append("local p0={};");
                int parts = Math.Max(2, Math.Min(6, bs.Length / 1024));
                int chunk = (int)Math.Ceiling(bs.Length / (double)parts);

                for (int pi = 0; pi < parts; pi++)
                {
                    int start = pi * chunk;
                    int len = Math.Min(chunk, bs.Length - start);
                    if (len <= 0) break;

                    byte[] seg = new byte[len];
                    Buffer.BlockCopy(bs, start, seg, 0, len);

                    string enc = CompressedToString(Compress(seg));
                    vm.Append($"p0[#p0+1]=d0('{enc}');");
                }

                vm.Append("__bs=table.concat(p0);");
            }
            else
            {
                vm.Append("local p0={};");
                int parts = Math.Max(2, Math.Min(6, bs.Length / 1024));
                int chunk = (int)Math.Ceiling(bs.Length / (double)parts);

                for (int pi = 0; pi < parts; pi++)
                {
                    int start = pi * chunk;
                    int len = Math.Min(chunk, bs.Length - start);
                    if (len <= 0) break;

                    var sbSeg = new StringBuilder();
                    for (int k = 0; k < len; k++)
                    {
                        sbSeg.Append('\\');
                        sbSeg.Append(((int)bs[start + k]).ToString());
                    }
                    vm.Append($"p0[#p0+1]='{sbSeg}';");
                }
                vm.Append("__bs=table.concat(p0);");
            }

            string vmp1 = VMStrings.VMP1
                .Replace("CONST_BOOL", _context.ConstantMapping[1].ToString())
                .Replace("CONST_FLOAT", _context.ConstantMapping[2].ToString())
                .Replace("CONST_STRING", _context.ConstantMapping[3].ToString());
            vm.Append(vmp1);

            for (int i = 0; i < (int)ChunkStep.StepCount; i++)
            {
                switch (_context.ChunkSteps[i])
                {
                    case ChunkStep.ParameterCount:
                        vm.Append("Chunk[3]=n8();");
                        break;

                    case ChunkStep.Instructions:
                        vm.Append("for Idx=1,n32() do local D=n8() if (gb(D,1,1)==0) then local T=gb(D,2,3) local M=gb(D,4,6) local Inst={ n16(), n16(), nil, nil } if (T==0) then Inst[OP_B]=n16() Inst[OP_C]=n16() elseif(T==1) then Inst[OP_B]=n32() elseif(T==2) then Inst[OP_B]=n32()-(2^16) elseif(T==3) then Inst[OP_B]=n32()-(2^16) Inst[OP_C]=n16() end if (gb(M,1,1)==1) then Inst[OP_A]=Consts[Inst[OP_A]] end if (gb(M,2,2)==1) then Inst[OP_B]=Consts[Inst[OP_B]] end if (gb(M,3,3)==1) then Inst[OP_C]=Consts[Inst[OP_C]] end Instrs[Idx]=Inst end end;");
                        break;

                    case ChunkStep.Functions:
                        vm.Append("for Idx=1,n32() do Functions[Idx-1]=Deserialize() end;");
                        break;

                    case ChunkStep.LineInfo:
                        if (settings.PreserveLineInfo)
                            vm.Append("for Idx=1,n32() do Lines[Idx]=n32() end;");
                        break;
                }
            }

            vm.Append("return Chunk;end;");
            vm.Append(settings.PreserveLineInfo ? VMStrings.VMP2_LI : VMStrings.VMP2);

            int MK = _rng.Next(1, 65521);
            int ADD = _rng.Next(0, 65521);
            int MUL = _rng.Next(1, 32768) * 2 + 1;
            const int MOD = 65521;

            vm.Append($@"
if not __t then
  _mk,_mu,_ad,_md={MK},{MUL},{ADD},{MOD}
  __idx=function(e) return ((bx(e,_mk))*_mu+_ad)%_md end
  __t={{}}");

            for (int real = 0; real < n; real++)
            {
                string body = virtuals[real].GetObfuscated(_context);
                int a = _rng.Next(5, 17);
                int b2 = _rng.Next(3, 11);
                int variant = _rng.Next(0, 3);
                string wrapped = WrapHandlerPolymorphic(body, variant, a, b2);
                vm.Append($"__t[__idx({real})]=function(){wrapped} end;");
            }

            int dummies = Math.Min(12, Math.Max(3, n / 4));
            var usedDummyKeys = new HashSet<int>();
            for (int i = 0; i < dummies; i++)
            {
                int fakeKey = _rng.Next(0, 65521);
                if (usedDummyKeys.Contains(fakeKey)) { i--; continue; }
                usedDummyKeys.Add(fakeKey);
                vm.Append($"__t[{fakeKey}]=function() local z=0 z=z end;");
            }
            vm.Append("end;");

            vm.Append("local __h=__t and __idx and __t[__idx(Enum)] if type(__h)=='function' then __h() else error('bad') end;");
            vm.Append(settings.PreserveLineInfo ? VMStrings.VMP3_LI : VMStrings.VMP3);

            string outLua = vm.ToString()
                .Replace("OP_ENUM", "1")
                .Replace("OP_A", "2")
                .Replace("OP_B", "3")
                .Replace("OP_C", "4")
                .Replace("__KS_ENABLE__", "0")
                .Replace("__KS_SEED__", seed.ToString())
                .Replace("__TAMPER_ON__", "1")
                .Replace("__CHECKSUM__", ((int)checksum).ToString())
                .Replace("__MAP__", mapLua)
                .Replace("__XK__", (_context.PrimaryXorKey & 0xFF).ToString());

            return outLua;
        }
    }
}

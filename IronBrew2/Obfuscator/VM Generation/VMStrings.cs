namespace IronBrew2.Obfuscator.VM_Generation
{
    public static class VMStrings
    {
        public static string VMP1 = @"
local ke=__KS_ENABLE__
local ks=__KS_SEED__
local tp=__TAMPER_ON__
local cs=__CHECKSUM__
local mp=__MAP__
local xk=__XK__
local b=string.byte
local c=string.char
local s=string.sub
local tc=table.concat
local ld=math.ldexp
local gf=getfenv or function() return _ENV end
local sf=select
local up=unpack or table.unpack
local bx=bit and bit.bxor or function(a,d)a=a or 0 d=d or 0 local p,q=1,0 while a>0 and d>0 do local ra,rb=a%2,d%2 if ra~=rb then q=q+p end a,d,p=(a-ra)/2,(d-rb)/2,p*2 end if a<d then a=d end while a>0 do local ra=a%2 if ra>0 then q=q+p end a,p=(a-ra)/2,p*2 end return q end
local BitXOR=bx
local __t,__idx,_mk,_mu,_ad,_md
local Byte=b
local Char=c
local Sub=s
local Concat=tc
local LDExp=ld
local GetFEnv=gf
local Select=sf
local Unpack=up
local Insert=table.insert
local Setmetatable=setmetatable
local function h32(z)local q=0 for i=1,#z do q=(q+b(z,i))%4294967296 end return q end
if tp==1 then if h32(__bs)~=cs then while true do end end end
local a8=69
local c8=57
local s8=((ks%256)+256)%256
local function km(i0) return ((a8*((i0+s8)%256))+c8)%256 end
local p=1
local function rem() return #__bs-p+1 end
local md=-1
local function tr(v,of)
  if md==0 then return v
  elseif md==1 then return bx(v,xk)
  elseif md==2 then return bx(v,km(of%256))
  elseif md==3 then return bx(bx(v,xk),km(of%256))
  else return bx(bx(v,km(of%256)),xk) end
end
local function pick()
  local function rd(mode)
    local w=b(__bs,1) or 0
    local x=b(__bs,2) or 0
    local y=b(__bs,3) or 0
    local z=b(__bs,4) or 0
    local function t(v,i)
      if mode==0 then return v
      elseif mode==1 then return bx(v,xk)
      elseif mode==2 then return bx(v,km((i-1)%256))
      elseif mode==3 then return bx(bx(v,xk),km((i-1)%256))
      else return bx(bx(v,km((i-1)%256)),xk) end
    end
    local W=t(w,1) local X=t(x,2) local Y=t(y,3) local Z=t(z,4)
    return Z*16777216+Y*65536+X*256+W
  end
  local cand
  if ke==0 then cand={0,1,3,4} else cand={2,3,4,1} end
  for i=1,#cand do
    local m=cand[i]
    local cc=rd(m)
    if cc>=0 and cc<=200000 then md=m return end
  end
  md=(ke==1) and 2 or 1
end
pick()
local function n8()
  local w=b(__bs,p) or 0
  local r=tr(w,(p-1))
  p=p+1
  return r%256
end
local function n16()
  local w=b(__bs,p) or 0
  local x=b(__bs,p+1) or 0
  local W=tr(w,(p-1))%256
  local X=tr(x,(p))%256
  p=p+2
  return X*256+W
end
local function n32()
  local w=b(__bs,p) or 0
  local x=b(__bs,p+1) or 0
  local y=b(__bs,p+2) or 0
  local z=b(__bs,p+3) or 0
  local W=tr(w,(p-1))%256
  local X=tr(x,(p))%256
  local Y=tr(y,(p+1))%256
  local Z=tr(z,(p+2))%256
  p=p+4
  return Z*16777216+Y*65536+X*256+W
end
local function gb(ti,st,en) if en then local r=(ti/2^(st-1))%2^((en-1)-(st-1)+1) return r-r%1 else local pp=2^(st-1) return (ti%(pp+pp)>=pp) and 1 or 0 end end
local function nf()
  local l=n32() local r=n32() local nn=1
  local m=(gb(r,1,20)*(2^32))+l
  local e=gb(r,21,31)
  local sg=((-1)^gb(r,32))
  if e==0 then if m==0 then return sg*0 else e=1 nn=0 end
  elseif e==2047 then return (m==0) and (sg*(1/0)) or (sg*(0/0)) end
  return ld(sg,e-1023)*(nn+(m/(2^52)))
end
local sz=n32
local function ns(len)
  if not len then len=sz() if len==0 then return '' end end
  if len<0 or len>rem() or len>16777216 then error('bad') end
  local t={} local j=1
  for i=0,len-1 do
    local rb=b(__bs,p+i) or 0
    t[j]=c(tr(rb,(p+i-1))%256) j=j+1
  end
  p=p+len
  return tc(t)
end
local function rr(...) return {...}, sf('#', ...) end
local function Deserialize()
  local Instrs={} local Functions={} local Lines={}
  local Chunk={Instrs,Functions,nil,Lines}
  local ConstCount=sz()
  local Consts={}
  for Idx=1,ConstCount do
    local Type=n8() local Cons
    if (Type==CONST_BOOL) then Cons=(n8()~=0)
    elseif (Type==CONST_FLOAT) then Cons=nf()
    elseif (Type==CONST_STRING) then Cons=ns() end
    Consts[Idx]=Cons
  end
";

        public static string VMP2 = @"
local function Wrap(Chunk,Upvalues,Env)
  local Instr=Chunk[1] local Proto=Chunk[2] local Params=Chunk[3]
  return function(...)
    local Instr=Instr local Proto=Proto local Params=Params
    local _R=rr local InstrPoint=1 local Top=-1
    local Vararg={} local Args={...} local PCount=sf('#', ...)-1
    local Lupvals={} local Stk={}
    for Idx=0,PCount do if (Idx>=Params) then Vararg[Idx-Params]=Args[Idx+1] else Stk[Idx]=Args[Idx+1] end end
    local Varargsz=PCount-Params+1
    local Inst local Enum
    while true do
      Inst=Instr[InstrPoint] Enum=Inst[OP_ENUM] if mp then Enum=mp[Enum+1] or Enum end
";

        public static string VMP3 = @"
      InstrPoint=InstrPoint+1
    end
  end
end
return Wrap(Deserialize(),{},gf())()
";

        public static string VMP2_LI = @"
local pc=pcall
local function Wrap(Chunk,Upvalues,Env)
  local Instr=Chunk[1] local Proto=Chunk[2] local Params=Chunk[3]
  return function(...)
    local InstrPoint=1 local Top=-1
    local Args={...} local PCount=sf('#', ...)-1
    local function Loop()
      local Instr=Instr local Proto=Proto local Params=Params
      local _R=rr local Vararg={} local Lupvals={} local Stk={}
      for Idx=0,PCount do if (Idx>=Params) then Vararg[Idx-Params]=Args[Idx+1] else Stk[Idx]=Args[Idx+1] end end
      local Varargsz=PCount-Params+1
      local Inst local Enum
      while true do
        Inst=Instr[InstrPoint] Enum=Inst[OP_ENUM] if mp then Enum=mp[Enum+1] or Enum end
";

        public static string VMP3_LI = @"
        InstrPoint=InstrPoint+1
      end
    end
    local A,B=rr(pc(Loop))
    if not A[1] then
      local line=(Chunk[4] and Chunk[4][InstrPoint]) or '?'
      error('x:'..tostring(A[2])..'@'..tostring(line))
    else
      return up(A,2,B)
    end
  end
end
return Wrap(Deserialize(),{},gf())()
";
    }
}

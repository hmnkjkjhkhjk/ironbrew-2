public class Decryptor
{
    public string Name { get; }
    public int[] Table; // kept only to seed the PRNG; no direct use at runtime

    public Decryptor(string name, int maxLen)
    {
        Name = name;
        var r = new Random();
        Table = Enumerable.Repeat(0, maxLen).Select(_ => r.Next(0, 256)).ToArray();
    }

    // Small, fast PRNG (xorshift128+) to derive per-string keystream
    static ulong Next(ref ulong s0, ref ulong s1)
    {
        ulong x = s0, y = s1;
        s0 = y;
        x ^= x << 23;
        x ^= x >> 17;
        x ^= y ^ (y >> 26);
        s1 = x;
        return s0 + s1;
    }

    public string Encrypt(byte[] plain)
    {
        var rnd = new Random();
        // Per-string nonce, mixed with Table to seed PRNG
        ulong n0 = (ulong)rnd.Next() << 32 | (uint)rnd.Next();
        ulong n1 = (ulong)rnd.Next() << 32 | (uint)rnd.Next();
        // Stir with Table content so different Decryptors diversify
        foreach (var t in Table) { n0 ^= (ulong)((t + 0x9E) * 0x9E3779B1u); n1 += (ulong)((t ^ 0xA5) * 0x85EBCA77u); }

        // Generate keystream & encrypt
        var ct = new byte[plain.Length];
        for (int i = 0; i < plain.Length; i++)
        {
            ulong ks = Next(ref n0, ref n1);
            byte k = (byte)((ks ^ (ulong)i ^ (ks >> 33)) & 0xFF);
            ct[i] = (byte)(plain[i] ^ k);
        }

        // Diff-encode: base + deltas
        byte baseB = (byte)rnd.Next(0, 256);
        var deltas = new byte[ct.Length];
        byte prev = baseB;
        for (int i = 0; i < ct.Length; i++)
        {
            deltas[i] = (byte)((ct[i] - prev) & 0xFF);
            prev = ct[i];
        }

        // Permute deltas; store only permuted data
        int n = deltas.Length;
        var idx = Enumerable.Range(0, n).ToArray();
        // Fisher–Yates
        for (int i = n - 1; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            (idx[i], idx[j]) = (idx[j], idx[i]);
        }
        var perm = new byte[n];
        for (int i = 0; i < n; i++) perm[i] = deltas[idx[i]];

        // Cheap MAC to detect naive tampering/dumps
        uint mac = 0;
        foreach (var b in perm) mac = (mac + b) * 2654435761u + 0x9E3779B9u;

        // Emit as Lua: permuted deltas, baseB, nonce, mac
        string bPerm = string.Join("", perm.Select(t => "\\" + t.ToString()));
        string lua = $@"
((function(P)
  IB_INLINING_START(true);
  local b=string.byte; local c=string.char; local sub=string.sub; local tc=table.concat
  local bx=bit32 and bit32.bxor or function(x,y) local r=0 local m=1 while x>0 or y>0 do
      local a=x%2; local d=y%2; if a~=d then r=r+m end; x=(x-a)/2; y=(y-d)/2; m=m*2 end return r end
  local function u8(x) return x%256 end
  local function getrt() -- mix runtime to frustrate CSE/const-fold
    local di=(debug and debug.getinfo and debug.getinfo(1,'Sl')) or {{}} 
    local ln=(type(di)=='table' and (di.currentline or 0)) or 0
    local se=select('#',...) + ln + (tonumber((_ENV and 1) or 0) or 0)
    return se
  end
  local function ks(i, n0, n1) -- xorshift128+ in Lua (truncated)
    -- single step
    local x=n0; local y=n1
    n0=y
    x = bx(x, (x*2^23)%2^64)  -- x ^= x << 23  (approx)
    x = bx(x, math.floor(x/2^17)) -- x ^= x >> 17
    x = bx(x, bx(y, math.floor(y/2^26))) -- x ^= y ^ (y >> 26)
    n1=x
    local s = (n0 + n1)%2^64
    -- derive 8-bit key from state + index
    local k = u8( bx( s % 256, bx(math.floor(s/2^33)%256, u8(i)) ) )
    return k, n0, n1
  end
  return function(baseB, L, N0, N1, MAC, perm)
    -- verify MAC
    local m=0
    for i=1,#perm do m = ((m + b(perm,i,i)) * 2654435761) % 2^32; m = (m + 0x9E3779B9) % 2^32 end
    if m ~= MAC then error('bad') end

    local rt = getrt()
    -- stir runtime into nonce
    local n0 = (N0 ~ rt) % 2^32
    local n1 = (N1 + rt*1103515245) % 2^32
    -- inverse permutation is computed on the fly by scanning; we avoid storing it
    local inv = {{}}  -- small cost, keeps encoder simple
    for i=1,L do inv[i]=0 end
    -- emitter gave us perm as (idx[i] order); rebuild a stable order by stable counting
    -- to frustrate trivial table.dump we bind through upvalues and arithmetic
    -- (simple approach: pair (value,pos) then sort by pos) -- but keep it linear:
    local pos=1
    for i=1,#perm do inv[pos]=b(perm,i,i); pos=pos+1 end

    local out={{}}; out[1]=c(baseB)
    local prev=baseB
    for i=1,L do
      local k; k, n0, n1 = ks(i-1, n0, n1)
      local di = inv[i]
      local ct = u8(prev + di)
      local pt = bx(ct, k)
      out[i+1]=c(pt)
      prev = ct
    end
    return tc(out,'')
  end
end)('{bPerm}'))({(int)baseB},{plain.Length},{(uint)n0},{(uint)n1},{mac})";

        return lua;
    }
}

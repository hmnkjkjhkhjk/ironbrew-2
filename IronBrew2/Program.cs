using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using IronBrew2.Bytecode_Library.Bytecode;
using IronBrew2.Bytecode_Library.IR;
using IronBrew2.Obfuscator;
using IronBrew2.Obfuscator.Control_Flow;
using IronBrew2.Obfuscator.Encryption;
using IronBrew2.Obfuscator.VM_Generation;

namespace IronBrew2
{
    public static class IB2
    {
        public static Random Random = new Random();
        private static Encoding Latin1 = Encoding.GetEncoding(28591);

        // ---------- helpers ----------
        static string? FindFirstExisting(params string[] candidates)
        {
            foreach (var p in candidates)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                        return p;
                }
                catch { /* ignore path weirdness */ }
            }
            return null;
        }

        static bool RunProcess(string fileName, string args, out string allOutput, string? workingDir = null)
        {
            var sb = new StringBuilder();
            try
            {
                var psi = new ProcessStartInfo(fileName)
                {
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                if (!string.IsNullOrEmpty(workingDir))
                    psi.WorkingDirectory = workingDir;

                using var proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();

                allOutput = sb.ToString();
                return proc.ExitCode == 0;
            }
            catch (Exception ex)
            {
                allOutput = (sb.Length > 0 ? sb + Environment.NewLine : "") + ex;
                return false;
            }
        }

        static bool TryDiet(string luaExe, string dietLua, string inPath, string outPath, string extraArgs, out string log)
        {
            // luaExe MUST be the executable; arguments are passed separately
            var args = $"\"{dietLua}\" {extraArgs} --outfile \"{outPath}\" \"{inPath}\"";
            var ok = RunProcess(luaExe, args, out log);
            // success requires the output file to exist
            return ok && File.Exists(outPath);
        }

        // ---------- main pipeline ----------
        public static bool Obfuscate(string path, string input, ObfuscationSettings settings, out string error)
        {
            try
            {
                error = "";

                // Resolve tools
                var baseDir  = AppContext.BaseDirectory; // where the CLI exe/dll lives
                var luaDir   = Path.Combine(baseDir, "Lua");
                var dietLua  = FindFirstExisting(
                                   Path.Combine(luaDir, "Minifier", "luasrcdiet.lua"),
                                   Path.Combine(path, "Lua", "Minifier", "luasrcdiet.lua"));
                // Prefer luajit.exe if present, otherwise lua.exe, otherwise PATH
                var luaExe   = FindFirstExisting(
                                   Path.Combine(luaDir, "luajit.exe"),
                                   Path.Combine(luaDir, "lua.exe"),
                                   "luajit.exe", "lua.exe", "luajit", "lua");

                // luac check (optional; if missing, we just skip the precheck)
                var osPrefix = Environment.OSVersion.Platform == PlatformID.Unix ? "/usr/bin/" : "";
                var luacName = $"{osPrefix}luac";

                if (!File.Exists(input))
                    throw new Exception("Invalid input file.");

                // temp/output files
                string l  = Path.Combine(path, "luac.out");
                string t0 = Path.Combine(path, "t0.lua");
                string t1 = Path.Combine(path, "t1.lua");
                string t2 = Path.Combine(path, "t2.lua");
                string t3 = Path.Combine(path, "t3.lua");
                string outPath = Path.Combine(path, "out.lua");

                // 1) quick syntax check via luac (best-effort)
                Console.WriteLine("Checking file...");
                if (RunProcess(luacName, $"-o \"{l}\" \"{input}\"", out var checkLog))
                {
                    if (File.Exists(l)) File.Delete(l);
                }
                else
                {
                    Console.WriteLine("WARN: luac check failed or not found; continuing.");
                    if (!string.IsNullOrWhiteSpace(checkLog))
                        Console.WriteLine(checkLog);
                }

                // 2) PRE-minify (strip comments) — fail-open, keep errors visible
                Console.WriteLine("Stripping comments...");
                bool preMinified = false;
                if (!string.IsNullOrEmpty(luaExe) && !string.IsNullOrEmpty(dietLua))
                {
                    preMinified = TryDiet(
                        luaExe, dietLua, input, t0,
                        "--maximum --opt-comments --opt-whitespace --opt-emptylines --opt-eols --noopt-locals --noopt-strings",
                        out var preLog
                    );
                    if (!preMinified)
                    {
                        Console.WriteLine("PRE-MINIFY FAILED; USING RAW INPUT");
                        if (!string.IsNullOrWhiteSpace(preLog)) Console.WriteLine(preLog);
                        File.Copy(input, t0, overwrite: true);
                    }
                }
                else
                {
                    Console.WriteLine("WARN: Lua tools not found; skipping pre-minify.");
                    File.Copy(input, t0, overwrite: true);
                }

                // 3) Compile (constant encryption first)
                Console.WriteLine("Compiling...");
                File.WriteAllText(t1, new ConstantEncryption(settings, File.ReadAllText(t0, Latin1)).EncryptStrings(), Latin1);

                if (!RunProcess(luacName, $"-o \"{l}\" \"{t1}\"", out var compileLog) && !File.Exists(l))
                {
                    Console.WriteLine("ERROR: luac failed to compile intermediate file.");
                    if (!string.IsNullOrWhiteSpace(compileLog)) Console.WriteLine(compileLog);
                    return false;
                }

                // 4) Obfuscate / VM serialize
                Console.WriteLine("Obfuscating...");
                var des    = new Deserializer(File.ReadAllBytes(l));
                Chunk lChunk = des.DecodeFile();

                if (settings.ControlFlow)
                {
                    CFContext cf = new CFContext(lChunk);
                    cf.DoChunks();
                }

                Console.WriteLine("Serializing...");
                var context = new ObfuscationContext(lChunk);
                string code = new Generator(context).GenerateVM(settings);

                File.WriteAllText(t2, code, Latin1);

                // 5) POST-minify (the obfuscated output) — fail-open, keep errors visible
                Console.WriteLine("Minifying...");
                bool postMinified = false;
                if (!string.IsNullOrEmpty(luaExe) && !string.IsNullOrEmpty(dietLua))
                {
                    postMinified = TryDiet(
                        luaExe, dietLua, t2, t3,
                        "--maximum --opt-entropy --opt-emptylines --opt-eols --opt-numbers --opt-whitespace --opt-locals --noopt-strings",
                        out var postLog
                    );
                    if (!postMinified)
                    {
                        Console.WriteLine("POST-MINIFY FAILED; WRITING UNMINIFIED OUTPUT");
                        if (!string.IsNullOrWhiteSpace(postLog)) Console.WriteLine(postLog);
                    }
                }
                else
                {
                    Console.WriteLine("WARN: Lua tools not found; skipping post-minify.");
                }

                // 6) Write final output (no banners/comments)
                Console.WriteLine("Writing final output...");
                var finalSrc = postMinified && File.Exists(t3) ? t3 : t2;
                var src = File.ReadAllText(finalSrc, Latin1).Replace('\n', ' ').Replace('\r', ' ');
                File.WriteAllText(outPath, src, Latin1);

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR");
                Console.WriteLine(e);
                error = e.ToString();
                return false;
            }
        }
    }
}

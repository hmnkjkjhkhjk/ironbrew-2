using System;
using System.IO;
using IronBrew2.Obfuscator;

namespace IronBrew2
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("===============================================");
            Console.WriteLine("     Excel Obfuscator CLI (based on IB2)");
            Console.WriteLine("===============================================");
            Console.ResetColor();

            if (args.Length < 1)
            {
                Console.WriteLine("Usage: ExcelObf <input.lua> [outputDir]");
                return;
            }

            string inputFile = args[0];
            string outputDir = args.Length > 1
                ? args[1]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");

            Directory.CreateDirectory(outputDir);

            var settings = new ObfuscationSettings
            {
                ControlFlow = true,
                EncryptStrings = true,
                VirtualizeAll = true
                // adjust other flags as needed
            };

            Console.WriteLine($"[+] Input : {inputFile}");
            Console.WriteLine($"[+] Output: {outputDir}");
            Console.WriteLine("[*] Starting obfuscation...");

            if (!IB2.Obfuscate(outputDir, inputFile, settings, out string error))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[!] Obfuscation failed.");
                Console.WriteLine(error);
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[+] Obfuscation completed successfully.");
                Console.WriteLine($"[+] Output file: {Path.Combine(outputDir, "out.lua")}");
                Console.ResetColor();
            }
        }
    }
}

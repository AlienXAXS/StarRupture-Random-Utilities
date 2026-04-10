using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace uasset_Json_Translation_Merger
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // ── 1. Load en.json ───────────────────────────────────────────────
            string exeDir = AppContext.BaseDirectory;
            string enJsonPath = Path.Combine(exeDir, "translation", "en.json");

            if (!File.Exists(enJsonPath))
            {
                Console.Error.WriteLine($"[ERROR] Could not find translation file at: {enJsonPath}");
                Environment.Exit(1);
            }

            Console.WriteLine($"Loading translation file: {enJsonPath}");

            string enJsonText = File.ReadAllText(enJsonPath);
            var translationData = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(enJsonText);

            if (translationData == null)
            {
                Console.Error.WriteLine("[ERROR] Failed to parse en.json.");
                Environment.Exit(1);
            }

            // ── 2. Build flat lookup (namespace key → value) ──────────────────
            // Keys can exist in multiple namespaces; last-write-wins, with a warning.
            var flatLookup = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (namespaceName, entries) in translationData)
            {
                if (entries == null) continue;
                foreach (var (key, value) in entries)
                {
                    if (flatLookup.ContainsKey(key))
                        Console.WriteLine($"[WARN] Duplicate key \"{key}\" in namespace \"{namespaceName}\" — overwriting previous value.");
                    flatLookup[key] = value;
                }
            }

            Console.WriteLine($"Loaded {flatLookup.Count} translation entries across {translationData.Count} namespaces.");

            // ── 3. Validate FModel export path argument ───────────────────────
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Usage: uasset-json-merger <path-to-fmodel-export-folder>");
                Environment.Exit(1);
            }

            string exportPath = args[0];
            if (!Directory.Exists(exportPath))
            {
                Console.Error.WriteLine($"[ERROR] Export folder not found: {exportPath}");
                Environment.Exit(1);
            }

            // ── 4. Scan for .json files and extract Name ──────────────────────
            var results = new Dictionary<string, string>(StringComparer.Ordinal);
            int found = 0;
            int missed = 0;
            int skipped = 0;

            var jsonFiles = Directory.EnumerateFiles(exportPath, "*.json", SearchOption.AllDirectories);

            foreach (string filePath in jsonFiles)
            {
                string? originalName = null;

                try
                {
                    string fileText = File.ReadAllText(filePath);
                    var jArray = JArray.Parse(fileText);

                    if (jArray.Count == 0) { skipped++; continue; }

                    var firstObj = jArray[0] as JObject;
                    if (firstObj == null) { skipped++; continue; }

                    originalName = firstObj["Name"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(originalName)) { skipped++; continue; }
                }
                catch
                {
                    skipped++;
                    continue;
                }

                // ── 5. Transform key ──────────────────────────────────────────
                string transformed = StripPrefix(originalName!);
                transformed += "_name";

                // ── 6. Lookup ─────────────────────────────────────────────────
                if (flatLookup.TryGetValue(transformed, out string? translation))
                {
                    if (!results.ContainsKey(originalName!))
                        results[originalName!] = translation;
                    found++;
                }
                else
                {
                    Console.WriteLine($"[MISS] {originalName} (looked up: {transformed})");
                    missed++;
                }
            }

            // ── 7. Write names.json ───────────────────────────────────────────
            string outputPath = Path.Combine(Directory.GetCurrentDirectory(), "names.json");
            string outputJson = JsonConvert.SerializeObject(results, Formatting.Indented);
            File.WriteAllText(outputPath, outputJson);

            Console.WriteLine();
            Console.WriteLine($"Done! {found} found, {missed} missed, {skipped} skipped (no Name / parse error).");
            Console.WriteLine($"Output written to: {outputPath}");
        }

        /// <summary>
        /// Strips a leading "CR_" (case-insensitive) or "Cr" prefix from an asset name.
        /// Examples:
        ///   "CR_RailT2"    → "RailT2"
        ///   "cr_RailT2"    → "RailT2"
        ///   "CrSomething"  → "Something"
        ///   "I_QuartzOre"  → "I_QuartzOre"  (no prefix)
        /// </summary>
        private static string StripPrefix(string name)
        {
            // CR_ prefix (case-insensitive)
            if (name.Length > 3 && name[0..3].Equals("CR_", StringComparison.OrdinalIgnoreCase))
                return name[3..];

            // Cr prefix (case-sensitive to avoid false positives like "Create...")
            if (name.Length > 2 && name.StartsWith("Cr", StringComparison.Ordinal))
                return name[2..];

            return name;
        }
    }
}

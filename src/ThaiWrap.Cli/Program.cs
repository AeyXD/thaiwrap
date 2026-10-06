using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ThaiWrap;
using ThaiWrap.Formats;

namespace ThaiWrap.Cli
{
    public static class Program
    {
        private sealed class Config
        {
            public string? InputPath;
            public string? OutputPath;
            public string WordList = "data/words_th.txt";
            public bool WordsExplicit;
            public string Mode = "zwsp"; // zwsp | sep
            public string Format = "auto";
            public string? Columns;
            public string? JsonKeys;
            public string BreakChar = "zwsp";
            public bool DryRun;
            public bool Recursive;
            public bool InPlace;
            public int MaxDiff = 40;
            public bool Stats;
            public ThaiWrapOptions Options = new ThaiWrapOptions();
        }

        private sealed class Totals
        {
            public int Files, ChangedFiles, ChangedLines;
            public long ZwspInserted;
        }

        public static int Main(string[] args)
        {
            var cfg = new Config();
            try
            {
                if (!ParseArgs(args, cfg)) return 0; // --help แสดง usage แล้วจบปกติ
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(Usage);
                return 2;
            }

            cfg.WordList = ResolveWordList(cfg);
            if (!File.Exists(cfg.WordList))
            {
                Console.Error.WriteLine("ไม่พบ word list: " + cfg.WordList + " (ใช้ --words <path> หรือวาง data/words_th.txt ข้าง executable)");
                return 2;
            }
            var segmenter = ThaiSegmenter.FromFile(cfg.WordList, cfg.Options);
            Func<string, string> transform = cfg.Mode == "sep"
                ? (Func<string, string>)(s => segmenter.SegmentToString(s))
                : s => segmenter.InsertZwsp(s);

            if (cfg.InputPath == null)
            {
                // stdin → stdout (โหมด text เท่านั้น)
                string input = Console.In.ReadToEnd();
                Console.Out.Write(ProcessContent(input, "text", cfg, transform));
                return 0;
            }

            if (Directory.Exists(cfg.InputPath)) return RunFolder(cfg, transform, segmenter);
            if (File.Exists(cfg.InputPath)) return RunFile(cfg, cfg.InputPath, cfg.OutputPath, transform, segmenter, new Totals());

            Console.Error.WriteLine("ไม่พบ input: " + cfg.InputPath);
            return 2;
        }

        // ---------- argument parsing ----------

        /// <summary>คืน false เมื่อผู้ใช้ขอ --help (แสดง usage แล้วให้จบ exit 0)</summary>
        private static bool ParseArgs(string[] args, Config cfg)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--out":
                    case "-o": cfg.OutputPath = Val(args, ref i); break;
                    case "--words":
                    case "-w": cfg.WordList = Val(args, ref i); cfg.WordsExplicit = true; break;
                    case "--mode":
                    case "-m": cfg.Mode = Val(args, ref i); break;
                    case "--format":
                    case "-f": cfg.Format = Val(args, ref i); break;
                    case "--columns": cfg.Columns = Val(args, ref i); break;
                    case "--json-keys": cfg.JsonKeys = Val(args, ref i); break;
                    case "--break-char":
                        cfg.BreakChar = Val(args, ref i);
                        if (cfg.BreakChar != "zwsp" && cfg.BreakChar != "hairspace")
                            throw new ArgumentException("--break-char ใช้ได้: zwsp | hairspace");
                        cfg.Options.BreakChar = cfg.BreakChar == "hairspace" ? '\u200A' : '\u200B';
                        break;
                    case "--dry-run": cfg.DryRun = true; break;
                    case "--recursive":
                    case "-r": cfg.Recursive = true; break;
                    case "--inplace": cfg.InPlace = true; break;
                    case "--max-diff": cfg.MaxDiff = int.Parse(Val(args, ref i)); break;
                    case "--maxword": cfg.Options.MaxWordLength = int.Parse(Val(args, ref i)); break;
                    case "--unknown-threshold": cfg.Options.UnknownBreakThreshold = int.Parse(Val(args, ref i)); break;
                    case "--stats": cfg.Stats = true; break;
                    case "--help":
                    case "-h":
                        Console.Out.WriteLine(Usage);
                        return false;
                    default:
                        if (a.StartsWith("-")) throw new ArgumentException("ตัวเลือกไม่รู้จัก: " + a);
                        if (cfg.InputPath != null) throw new ArgumentException("ระบุ input ได้ที่เดียว");
                        cfg.InputPath = a;
                        break;
                }
            }
            ValidateFormat(cfg.Format);
            return true;
        }

        private static string Val(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("ขาดค่าหลัง " + args[i]);
            return args[++i];
        }

        /// <summary>
        /// หา word list: ถ้าผู้ใช้ระบุ -w ใช้ตามนั้น · default จะลอง working directory
        /// ก่อน แล้วตามตำแหน่งของ executable (data/words_th.txt และ words_th.txt)
        /// เพื่อให้ zip ที่แจก "แตกแล้วรันได้เลย" ทำงานแม้เรียกจากโฟลเดอร์อื่น
        /// </summary>
        private static string ResolveWordList(Config cfg)
        {
            if (cfg.WordsExplicit) return cfg.WordList;
            if (File.Exists(cfg.WordList)) return cfg.WordList; // working directory

            string exeDir = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(exeDir, "data", "words_th.txt"),
                Path.Combine(exeDir, "words_th.txt"),
            };
            foreach (string c in candidates)
                if (File.Exists(c)) return c;
            return cfg.WordList; // คืน default เพื่อให้ error message ชี้ตำแหน่งเดิม
        }

        private static void ValidateFormat(string f)
        {
            string[] valid = { "auto", "text", "csv", "tsv", "json", "po", "keyvalue" };
            if (!valid.Contains(f)) throw new ArgumentException("format ไม่รู้จัก: " + f + " (ใช้ได้: " + string.Join("|", valid) + ")");
        }

        // ---------- single file / folder ----------

        private static int RunFolder(Config cfg, Func<string, string> transform, ThaiSegmenter segmenter)
        {
            if (cfg.InPlace && cfg.DryRun == false && cfg.OutputPath != null)
                Console.Error.WriteLine("หมายเหตุ: โหมด folder ไม่ใช้ -o — ใช้ --inplace หรือค่า default (.zwsp.<ext>)");

            var totals = new Totals();
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".csv", ".tsv", ".json", ".po", ".pot", ".txt" };
            var files = Directory.EnumerateFiles(cfg.InputPath!, "*",
                    cfg.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(p => exts.Contains(Path.GetExtension(p)))
                .Where(p => !p.Contains(".zwsp."))
                .Where(p => !p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            foreach (var file in files)
            {
                RunFile(cfg, file, outputPath: null, transform, segmenter, totals);
            }

            Console.Error.WriteLine("---");
            Console.Error.WriteLine($"ไฟล์ทั้งหมด {totals.Files} · เปลี่ยน {totals.ChangedFiles} · บรรทัดที่เปลี่ยน {totals.ChangedLines} · จุดตัดที่แทรก {totals.ZwspInserted}");
            return 0;
        }

        private static int RunFile(Config cfg, string path, string? outputPath, Func<string, string> transform, ThaiSegmenter segmenter, Totals totals)
        {
            totals.Files++;
            string? format = ResolveFormat(cfg, path);
            if (format == null)
            {
                Console.Error.WriteLine("ข้าม (นามสกุลไม่รู้จัก): " + path);
                return 0;
            }

            (string input, Encoding enc) = ReadTextWithBom(path);
            string output = ProcessContent(input, format, cfg, transform);

            bool changed = output != input;
            char brk = cfg.Options.BreakChar;
            long inserted = CountBreak(output, brk) - CountBreak(input, brk);
            if (changed) { totals.ChangedFiles++; totals.ChangedLines += CountChangedLines(input, output); }
            totals.ZwspInserted += Math.Max(0, inserted);

            if (cfg.DryRun)
            {
                if (changed) PrintDiff(path, input, output, cfg.MaxDiff, cfg.Options.BreakChar);
                else Console.Error.WriteLine("ไม่เปลี่ยนแปลง: " + path);
                return 0;
            }

            if (outputPath != null)
            {
                WriteTextWithBom(outputPath, output, enc);
            }
            else if (cfg.InputPath != null && Directory.Exists(cfg.InputPath))
            {
                // โหมด folder — ไฟล์ที่ไม่เปลี่ยนไม่เขียนอะไรเลย (กัน .bak ต้นฉบับถูกแทนที่เมื่อรันซ้ำ)
                if (!changed)
                {
                    Console.Error.WriteLine("ไม่เปลี่ยนแปลง ข้าม: " + path);
                }
                else if (cfg.InPlace)
                {
                    string bak = path + ".bak";
                    if (!File.Exists(bak)) File.Copy(path, bak); // เก็บ .bak รอบแรก (ต้นฉบับแท้) ไว้ตลอด
                    WriteTextWithBom(path, output, enc);
                    Console.Error.WriteLine("เขียนทับ" + (File.Exists(bak) ? "" : " (+.bak ใหม่)") + ": " + path + $"  [{BreakName(brk)} +{inserted}]");
                }
                else
                {
                    string dst = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".zwsp" + Path.GetExtension(path));
                    WriteTextWithBom(dst, output, enc);
                    Console.Error.WriteLine("เขียนใหม่: " + dst + $"  [{BreakName(brk)} +{inserted}]");
                }
            }
            else
            {
                Console.Out.Write(output);
            }

            if (cfg.Stats)
                Console.Error.WriteLine($"[{path}] format={format} changed={changed} {BreakName(brk)}+{inserted} dict={segmenter.WordCount}");
            return 0;
        }

        // ---------- format dispatch ----------

        private static string? ResolveFormat(Config cfg, string path)
        {
            if (cfg.Format != "auto") return cfg.Format;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".csv" => "csv",
                ".tsv" => "tsv",
                ".json" => "json",
                ".po" or ".pot" => "po",
                ".txt" => "text",
                _ => null
            };
        }

        private static string ProcessContent(string input, string format, Config cfg, Func<string, string> transform)
        {
            switch (format)
            {
                case "csv":
                {
                    var opts = BuildCsvOptions(cfg, DetectCsvDelimiter(input));
                    return CsvProcessor.Process(input, opts, transform);
                }
                case "tsv":
                {
                    var opts = BuildCsvOptions(cfg, '\t');
                    return CsvProcessor.Process(input, opts, transform);
                }
                case "json":
                    return JsonStringProcessor.Process(input, transform, BuildJsonKeyFilter(cfg));
                case "po":
                    return PoProcessor.Process(input, transform);
                case "keyvalue":
                    return KeyValueProcessor.Process(input, transform);
                default:
                {
                    var sb = new StringBuilder(input.Length);
                    foreach (var line in TextLines.SplitKeepTerminators(input))
                    {
                        string content = TextLines.StripTerminator(line, out string term);
                        sb.Append(transform(content)).Append(term);
                    }
                    return sb.ToString();
                }
            }
        }

        /// <summary>--json-keys "desc,tooltip" → แปลงเฉพาะ value ใต้ key เหล่านี้ (null = ทุก value)</summary>
        private static Func<string, bool>? BuildJsonKeyFilter(Config cfg)
        {
            if (cfg.JsonKeys == null) return null;
            var set = new HashSet<string>(cfg.JsonKeys.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.Ordinal);
            if (set.Count == 0) return null;
            return k => set.Contains(k);
        }

        private static CsvOptions BuildCsvOptions(Config cfg, char delimiter)
        {
            var opts = new CsvOptions { Delimiter = delimiter };
            if (cfg.Columns == null) return opts;
            var indices = new List<int>();
            var names = new List<string>();
            foreach (var part in cfg.Columns.Split(','))
            {
                string name = part.Trim();
                if (name.Length == 0) continue;
                if (name.All(char.IsDigit)) indices.Add(int.Parse(name));
                else names.Add(name);
            }
            if (indices.Count > 0) opts.ColumnIndices = indices;
            if (names.Count > 0) opts.ColumnNames = names;
            return opts;
        }

        private static char DetectCsvDelimiter(string input)
        {
            // นับ , ; tab ในบรรทัดแรกที่ไม่ว่าง (คร่าวๆ นอกเครื่องหมายคำพูด) — เลือกตัวที่เจอมากสุด
            string firstLine = input.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
            int commas = 0, semis = 0, tabs = 0; bool q = false;
            foreach (char c in firstLine)
            {
                if (c == '"') q = !q;
                else if (!q) { if (c == ',') commas++; else if (c == ';') semis++; else if (c == '\t') tabs++; }
            }
            if (semis > commas && semis > tabs) return ';';
            if (tabs > commas && tabs > semis) return '\t';
            return ',';
        }

        // ---------- encoding / io ----------

        private static (string text, Encoding enc) ReadTextWithBom(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true));
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode);
            return (Encoding.UTF8.GetString(bytes), new UTF8Encoding(false));
        }

        private static void WriteTextWithBom(string path, string text, Encoding enc)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, text, enc);
        }

        // ---------- diff ----------

        private static string BreakName(char brk) => brk == '\u200A' ? "hairspace" : "ZWSP";

        private static long CountBreak(string s, char brk)
        {
            long c = 0;
            foreach (char ch in s) if (ch == brk) c++;
            return c;
        }

        private static int CountChangedLines(string before, string after)
        {
            var a = before.Split('\n');
            var b = after.Split('\n');
            int n = Math.Min(a.Length, b.Length), count = Math.Abs(a.Length - b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) count++;
            return count;
        }

        private static void PrintDiff(string label, string before, string after, int maxDiff, char brk)
        {
            var a = before.Split('\n');
            var b = after.Split('\n');
            bool approximate = a.Length != b.Length;
            Console.Error.WriteLine($"--- {label} ({CountChangedLines(before, after)} บรรทัดเปลี่ยน{(approximate ? " ~" : "")})");
            int shown = 0;
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n && shown < maxDiff; i++)
            {
                string old = i < a.Length ? a[i].TrimEnd() : "<ไม่มี>";
                string now = i < b.Length ? b[i].TrimEnd() : "<ไม่มี>";
                if (old == now) continue;
                Console.Error.WriteLine($"  {i + 1,4} - {Show(old, brk)}");
                Console.Error.WriteLine($"        + {Show(now, brk)}");
                shown++;
            }
            if (shown >= maxDiff) Console.Error.WriteLine($"  … (ตัดเหลือ — ใช้ --max-diff เพื่อดูเพิ่ม)");
        }

        private static string Show(string s, char brk) => s.Replace(brk.ToString(), "·");

        private const string Usage =
            "thaiwrap — แทรก ZWSP ที่รอยต่อคำไทย เพื่อให้เกมตัดบรรทัดถูกจุด\n" +
            "usage: thaiwrap [options] [infile | folder]\n" +
            "  ไม่ระบุ infile = อ่าน stdin เขียน stdout (โหมด text)\n" +
            "options:\n" +
            "  -o, --out <file>      เขียนผลลัพธ์ลงไฟล์ (default: stdout)\n" +
            "  -w, --words <file>    word list (default: data/words_th.txt)\n" +
            "  -m, --mode <mode>     zwsp (default) | sep — sep แสดงคำคั่นด้วย |\n" +
            "  -f, --format <fmt>    auto | text | csv | tsv | json | po | keyvalue (default auto ตามนามสกุล)\n" +
            "  --columns <list>      csv/tsv: \"1,3\" หรือ \"ชื่อคอลัมน์\" — default ทุกคอลัมน์ (ใช้ชื่อ = ข้ามแถว header)\n" +
            "  --json-keys <list>    json: แปลงเฉพาะ value ใต้ key เหล่านี้ เช่น \"desc,tooltip\" — default ทุก string value\n" +
            "  --break-char <c>      zwsp (default) | hairspace — เกม Scaleform/Flash (เช่น Witcher 3) ต้องใช้ hairspace\n" +
            "  --dry-run             แสดง diff (ZWSP แสดงเป็น ·) โดยไม่เขียนไฟล์\n" +
            "  -r, --recursive       โหมด folder: เดินทั้งต้นไม้\n" +
            "  --inplace             โหมด folder: เขียนทับไฟล์เดิม + สำรอง .bak (default: สร้าง <ชื่อ>.zwsp.<ext>)\n" +
            "  --max-diff <n>        จำนวนบรรทัด diff สูงสุดที่แสดง (default 40)\n" +
            "  --maxword <n>         ความยาวคำสูงสุดจากพจนานุกรม (default 0 = ไม่จำกัด)\n" +
            "  --unknown-threshold <n> จำนวน cluster ก่อนยอมให้ตัดคำนอกพจนานุกรม (default 8)\n" +
            "  --stats               สถิติเพิ่มเติมทาง stderr";
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ThaiWrap;
using ThaiWrap.Formats;

namespace ThaiWrap.Cli
{
    /// <summary>
    /// --report: รายงาน QA รวมผลตัวตรวจทุกตัว (#9) — แตรปครบไหม · placeholder · วางผิดแถว · รูปพิมพ์ผิด ·
    /// ตัวตรวจคำผิดตามบริบท · คำนอกพจนานุกรม · ไม่เขียนไฟล์แปลเลย (เหมือน --check) เขียนเฉพาะไฟล์รายงาน
    /// </summary>
    public static partial class Program
    {
        private static readonly string[] ReportCheckOrder =
            { "placeholders", "unwrapped", "mispaste", "typo", "ambiguous", "ral-swap", "wrong-word" };

        private static string? ReportKind(string path)
        {
            if (path == "-") return "json";
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".json" ? "json" : ext == ".html" || ext == ".htm" ? "html" : null;
        }

        /// <summary>ตรวจค่าก่อนโหลด dict — คืนข้อความ error (exit 2) หรือ null</summary>
        private static string? ValidateReport(Config cfg)
        {
            string r = cfg.Report!;
            if (cfg.InputPath == null) return "--report ใช้กับไฟล์/โฟลเดอร์ (ไม่ใช่ stdin)";
            if (cfg.OutputPath != null || cfg.InPlace)
                return "--report ไม่เขียนไฟล์แปล — ใช้ร่วม -o/--inplace ไม่ได้ (จะแปลงไฟล์ให้รันอีกครั้งโดยไม่ใส่ --report)";
            if (ReportKind(r) == null) return "--report ต้องลงท้าย .json หรือ .html (หรือ - = JSON ออก stdout) ได้รับ: " + r;
            if (!File.Exists(cfg.InputPath) && !Directory.Exists(cfg.InputPath)) return "ไม่พบ input: " + cfg.InputPath;
            return r == "-" ? null : ValidateReportTarget(cfg, r);
        }

        /// <summary>
        /// #17: ปลายทางรายงานต้องไม่ใช่ไฟล์แปล — เทียบหลังตาม symlink จนสุด (alias ถึงต้นฉบับก็ห้าม) กับทุกไฟล์ที่จะตรวจ
        /// และถ้ามีไฟล์อยู่แล้วต้องเป็นรายงานของ thaiwrap เท่านั้นจึงเขียนทับได้ (เดิมตัดปลายทางออกจากรายการตรวจแล้วเขียนทับเงียบ ๆ)
        /// </summary>
        private static string? ValidateReportTarget(Config cfg, string r)
        {
            if (Directory.Exists(r)) return "--report ชี้ไปที่โฟลเดอร์: " + r + " (ระบุชื่อไฟล์ .json/.html)";
            var link = new FileInfo(r);
            // File.Exists ของ symlink ที่ปลายทางหายคืน true บน macOS (ดูที่ตัวลิงก์) — ต้องดูปลายทางจริง
            if (link.LinkTarget != null && !(link.ResolveLinkTarget(true) is FileInfo real && real.Exists))
                return "--report เป็น symlink ที่ปลายทางไม่มีอยู่: " + r + " → " + link.LinkTarget;
            string target = RealPath(r);
            var inputs = Directory.Exists(cfg.InputPath) ? ListFolder(cfg) : new List<string> { cfg.InputPath! };
            foreach (var f in inputs)
                if (string.Equals(RealPath(f), target, StringComparison.OrdinalIgnoreCase))
                    return "--report ชนไฟล์แปลที่กำลังตรวจ (จะเขียนทับต้นฉบับ): " + r +
                        (SamePathText(f, r) ? "" : " = " + f) + " — ใช้ชื่ออื่นหรือเขียนรายงานนอกโฟลเดอร์ที่ตรวจ";
            if (File.Exists(r) && !IsQaReportOutput(r))
                return "--report: มีไฟล์ " + r + " อยู่แล้วและไม่ใช่รายงานของ thaiwrap — ไม่เขียนทับ (ลบเองหรือใช้ชื่ออื่น)";
            return null;
        }

        private static bool SamePathText(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>path จริงหลังตาม symlink จนสุด (ไฟล์ที่ยังไม่มี = full path เดิม)</summary>
        private static string RealPath(string p)
        {
            var fi = new FileInfo(Path.GetFullPath(p));
            string full = fi.LinkTarget != null ? fi.ResolveLinkTarget(true)?.FullName ?? fi.FullName : fi.FullName;
            // โฟลเดอร์แม่ที่เป็น symlink (เช่น /tmp → /private/tmp บน macOS) — resolve ทีละชั้นจากบนลงล่าง
            string? dir = Path.GetDirectoryName(full);
            if (dir == null) return full;
            return Path.Combine(RealDir(dir), Path.GetFileName(full));
        }

        private static string RealDir(string dir)
        {
            string? parent = Path.GetDirectoryName(dir);
            string resolved = parent == null ? dir : Path.Combine(RealDir(parent), Path.GetFileName(dir));
            var di = new DirectoryInfo(resolved);
            if (di.Exists && di.LinkTarget != null) return di.ResolveLinkTarget(true)?.FullName ?? resolved;
            return resolved;
        }

        /// <summary>ไฟล์ที่ --report เคยเขียน (JSON ขึ้นต้น JsonHead · HTML มี generator meta) — เขียนทับได้</summary>
        private static bool IsQaReportOutput(string path)
        {
            var head = new byte[600];
            int n;
            using (var fs = File.OpenRead(path)) n = fs.Read(head, 0, head.Length);
            string s = Encoding.UTF8.GetString(head, 0, n).TrimStart('\uFEFF').Replace("\r\n", "\n"); // CRLF จาก git autocrlf/editor ก็ยังเป็นรายงาน
            return s.StartsWith(QaReport.JsonHead, StringComparison.Ordinal) || s.Contains(QaReport.HtmlGenerator);
        }

        private static string ToolVersion()
        {
            var v = typeof(Program).Assembly.GetName().Version;
            return v == null ? "?" : v.Major + "." + v.Minor + "." + v.Build;
        }

        private static int RunReport(Config cfg, Func<string, string> transform, ThaiSegmenter segmenter,
            string? extraPath, string? noBreakPath, string? allowPath)
        {
            var report = new QaReport { Version = ToolVersion(), Input = cfg.InputPath! };
            void Set(string k, string? v) { if (v != null) report.Settings.Add(new KeyValuePair<string, string>(k, v)); }
            Set("words", cfg.WordList + " (" + segmenter.WordCount + " คำ)");
            Set("words_extra", extraPath);
            Set("nobreak", noBreakPath);
            Set("check_allow", allowPath);
            Set("break-char", cfg.BreakChar);
            if (cfg.Format != "auto") Set("format", cfg.Format);
            Set("columns", cfg.Columns);
            Set("source-column", cfg.SourceColumn);
            Set("json-keys", cfg.JsonKeys);
            Set("json-path", cfg.JsonPath);
            if (cfg.Normalize) Set("normalize", "on");
            if (cfg.Check) Set("check", "on — ไฟล์ที่ยังไม่แตรปนับเป็น error");

            // ปลายทางที่ชนไฟล์แปลถูกปฏิเสธแล้วใน ValidateReport (#17) · รายงาน JSON เก่าในโฟลเดอร์ ListFolder ข้ามให้เอง
            List<string> files = Directory.Exists(cfg.InputPath) ? ListFolder(cfg) : new List<string> { cfg.InputPath! };

            var unknown = new Dictionary<string, int>();
            var checks = new List<CheckEntry>();
            char brk = cfg.Options.BreakChar;
            foreach (var file in files)
            {
                string? format = ResolveFormat(cfg, file);
                var info = new QaFileInfo { Path = file, Format = format };
                report.Files.Add(info);
                if (format == null) { info.Note = "ข้าม (นามสกุลไม่รู้จัก)"; continue; }

                (string input, _) = ReadTextWithBom(file);
                string output;
                try { output = ProcessContent(input, format, cfg, transform); }
                catch (ArgumentException ex) { throw new ArgumentException(file + ": " + ex.Message); }

                info.Wrapped = output == input;
                info.BreaksToInsert = Math.Max(0, CountBreak(output, brk) - CountBreak(input, brk));
                if (!info.Wrapped.Value)
                    report.Findings.Add(new QaFinding
                    {
                        Check = "unwrapped", Severity = cfg.Check ? QaSeverity.Error : QaSeverity.Info, File = file,
                        Message = "การแปลงจะแก้ " + CountChangedLines(input, output) + " บรรทัด · แทรก " + BreakName(brk) + " +" + info.BreaksToInsert,
                    });

                var issues = ThaiNormalizer.Count(input);
                info.Typos = issues.Total;
                info.Ambiguous = issues.WarningsTotal;
                if (issues.Total > 0)
                {
                    var parts = new List<string>();
                    void P(string label, int n) { if (n > 0) parts.Add(label + " " + n); }
                    P("เเ", issues.DoubleE); P("มาร์คซ้อน", issues.DoubledMark); P("ลำดับมาร์ค", issues.MarkOrder);
                    P("ํ+า→ำ", issues.SaraAm); P("วรรณยุกต์หลังสระหน้า", issues.StrayTone);
                    report.Findings.Add(new QaFinding
                    {
                        Check = "typo", Severity = QaSeverity.Warning, File = file,
                        Message = issues.Total + " จุด (" + string.Join(" · ", parts) + ")" + (cfg.Normalize ? " — --normalize แก้ให้ตอนแปลง" : " — แก้ด้วย --normalize"),
                    });
                }
                if (issues.WarningsTotal > 0)
                    report.Findings.Add(new QaFinding
                    {
                        Check = "ambiguous", Severity = QaSeverity.Warning, File = file,
                        Message = "สระ/วรรณยุกต์ต่างตัวซ้อน " + (issues.MixedVowels + issues.MixedTones) + " จุด · สระหน้า+มาร์ค " + issues.LeadingVowelMark + " จุด — แก้อัตโนมัติไม่ได้",
                    });

                CollectUnknowns(segmenter, input, unknown);
                checks.AddRange(BuildCheckEntries(cfg, file, input, format));

                if (format == "csv" || format == "tsv" || format == "po" || format == "keyvalue")
                {
                    int noSource = 0;
                    List<SourcePair> pairs;
                    try { pairs = BuildSourcePairs(cfg, input, format, ref noSource); }
                    catch (ArgumentException ex) { throw new ArgumentException(file + ": " + ex.Message); }
                    report.PlaceholderNoSource += noSource;
                    foreach (var p in pairs)
                    {
                        report.PlaceholderPairs++;
                        var m = PlaceholderChecker.Compare(p.Source, p.Translation);
                        if (m == null) continue;
                        report.Findings.Add(new QaFinding
                        {
                            Check = "placeholders", Severity = QaSeverity.Error, File = file, Line = p.Line, IsRow = p.IsRow,
                            Message = DescribeMismatch(m), Source = p.Source, Translation = p.Translation,
                        });
                    }
                }
                else info.Note = "ไม่ได้ตรวจ placeholder (ไม่มีต้นฉบับคู่ในไฟล์ " + format + ")";

                if (format == "csv" || format == "tsv")
                {
                    var table = CsvProcessor.ParseTable(input, format == "tsv" ? '\t' : DetectCsvDelimiter(input));
                    foreach (var h in DetectMispaste(cfg, table))
                        report.Findings.Add(new QaFinding
                        {
                            Check = "mispaste", Severity = QaSeverity.Warning, File = file, Line = h.RowNo, IsRow = true,
                            Message = h.Reason, Source = h.English, Translation = h.Thai,
                        });
                }
            }

            var groups = BuildSuggestionGroups(cfg, checks, out int withEnglish);
            report.CheckTexts = checks.Count;
            report.CheckTextsWithEnglish = withEnglish;
            foreach (var g in groups.OrderBy(g => g.First.Kind).ThenByDescending(g => g.Count).ThenBy(g => g.First.Wrong, StringComparer.Ordinal))
            {
                SuggestionLocation(g, out int line, out string ctx);
                report.Findings.Add(new QaFinding
                {
                    Check = g.First.Kind == SuggestionKind.RalSwap ? "ral-swap" : "wrong-word", Severity = QaSeverity.Warning,
                    File = g.Entry.Path, Line = line, IsRow = g.Entry.IsRow,
                    Message = g.First.Wrong + " → " + g.First.Suggested + " ×" + g.Count,
                    Wrong = g.First.Wrong, Suggested = g.First.Suggested, Count = g.Count, Context = ctx,
                });
            }
            // ลำดับคงที่ตามความรุนแรงของตัวตรวจ (OrderBy เสถียร — ในตัวตรวจเดียวกันคงลำดับไฟล์/แถว)
            report.Findings = report.Findings.OrderBy(f => Array.IndexOf(ReportCheckOrder, f.Check)).ToList();
            report.UnknownWords = unknown.Where(kv => kv.Value >= 2)
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();

            string kind = ReportKind(cfg.Report!)!;
            string content = kind == "html" ? report.ToHtml() : report.ToJson();
            if (cfg.Report == "-") Console.Out.Write(content);
            else WriteTextWithBom(cfg.Report!, content, new UTF8Encoding(false));

            int err = report.Count(QaSeverity.Error), warn = report.Count(QaSeverity.Warning), inf = report.Count(QaSeverity.Info);
            Console.Error.WriteLine("--- รายงาน QA: " + report.Files.Count + " ไฟล์ · ต้องแก้ " + err + " · ควรตรวจ " + warn + " · แจ้ง " + inf +
                (cfg.Report == "-" ? " (JSON ออก stdout)" : " → " + cfg.Report) + " · ไม่ได้เขียนไฟล์แปล");
            foreach (var check in ReportCheckOrder)
            {
                int n = report.Findings.Count(f => f.Check == check);
                if (n > 0) Console.Error.WriteLine("  " + QaReport.CheckTitle(check) + ": " + n);
            }
            return err > 0 ? 1 : 0;
        }
    }
}

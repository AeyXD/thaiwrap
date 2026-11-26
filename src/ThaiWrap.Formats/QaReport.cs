using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ThaiWrap.Formats
{
    /// <summary>ระดับของข้อตรวจ — error = ต้องแก้ (exit 1) · warning = ควรตรวจด้วยตา · info = แจ้งให้ทราบ</summary>
    public enum QaSeverity { Error, Warning, Info }

    /// <summary>ข้อตรวจหนึ่งจุดในรายงาน QA (--report)</summary>
    public sealed class QaFinding
    {
        /// <summary>รหัสคงที่ของตัวตรวจ: placeholders · mispaste · unwrapped · typo · ambiguous · ral-swap · wrong-word</summary>
        public string Check = "";
        public QaSeverity Severity;
        public string File = "";
        /// <summary>บรรทัด (หรือเลขแถวเมื่อ <see cref="IsRow"/>) — 0 = ทั้งไฟล์</summary>
        public int Line;
        public bool IsRow;
        public string Message = "";
        public string? Source;
        public string? Translation;
        /// <summary>ตัวตรวจคำผิด: รูปที่พบ → รูปที่เสนอ · จำนวนครั้งทั้งคลัง (Line/Context = ตำแหน่งแรกที่พบ)</summary>
        public string? Wrong;
        public string? Suggested;
        public int Count = 1;
        public string? Context;
    }

    /// <summary>สรุปต่อไฟล์</summary>
    public sealed class QaFileInfo
    {
        public string Path = "";
        public string? Format;
        /// <summary>true = ข้อความไทยถูกแตรปครบแล้ว (รันแปลงซ้ำไม่เปลี่ยน) · null = ไม่ได้ตรวจ (ข้ามไฟล์)</summary>
        public bool? Wrapped;
        /// <summary>จำนวนจุดตัดที่การแปลงจะแทรกเพิ่ม</summary>
        public long BreaksToInsert;
        public int Typos;
        public int Ambiguous;
        public string? Note;
    }

    /// <summary>
    /// รายงาน QA รวมผลตัวตรวจทุกตัว (#9) — ส่งต่อทีม/เครื่องมืออื่นได้ แทนข้อความ stderr ·
    /// JSON = สคีมาคงที่ (schema 1) สำหรับเครื่องมือ · HTML = ไฟล์เดียวเปิดในเบราว์เซอร์ได้เลย ไม่โหลดอะไรจากเน็ต
    /// </summary>
    public sealed class QaReport
    {
        public const int Schema = 1;

        /// <summary>หัวไฟล์ JSON ที่ <see cref="ToJson"/> เขียนเสมอ — CLI ใช้แยกรายงานเก่าออกจากไฟล์แปลในโหมด folder</summary>
        public const string JsonHead = "{\n  \"tool\": \"thaiwrap\",";

        public string Version = "";
        public DateTime GeneratedUtc = DateTime.UtcNow;
        public string Input = "";
        /// <summary>ค่าตั้งที่ใช้ตรวจ (ชื่อ → ค่า) ตามลำดับที่ใส่</summary>
        public List<KeyValuePair<string, string>> Settings = new List<KeyValuePair<string, string>>();
        public List<QaFileInfo> Files = new List<QaFileInfo>();
        public List<QaFinding> Findings = new List<QaFinding>();
        /// <summary>คำนอกพจนานุกรมที่พบ ≥2 ครั้ง เรียงตามความถี่ — ตัวเลือกเพิ่มใน words_extra.txt</summary>
        public List<KeyValuePair<string, int>> UnknownWords = new List<KeyValuePair<string, int>>();
        public int PlaceholderPairs;
        public int PlaceholderNoSource;
        public int CheckTexts;
        public int CheckTextsWithEnglish;

        public int Count(QaSeverity s)
        {
            int n = 0;
            foreach (var f in Findings) if (f.Severity == s) n++;
            return n;
        }

        public static string SeverityName(QaSeverity s) =>
            s == QaSeverity.Error ? "error" : s == QaSeverity.Warning ? "warning" : "info";

        /// <summary>ชื่อไทยของตัวตรวจ — ใช้ใน HTML และข้อความสรุป</summary>
        public static string CheckTitle(string check)
        {
            switch (check)
            {
                case "placeholders": return "placeholder/tag ไม่ตรงต้นฉบับ";
                case "unwrapped": return "ข้อความไทยยังไม่ถูกแตรป";
                case "mispaste": return "คำแปลน่าจะวางผิดแถว";
                case "typo": return "รูปพิมพ์ผิดที่กระทบการตัดคำ";
                case "ambiguous": return "สระ/วรรณยุกต์กำกวม (ตรวจด้วยตา)";
                case "ral-swap": return "พิมพ์สลับ ร↔ล / สะกดผิด";
                case "wrong-word": return "สะกดถูกแต่น่าจะผิดบริบท";
                default: return check;
            }
        }

        private static readonly string[] CheckOrder =
            { "placeholders", "unwrapped", "mispaste", "typo", "ambiguous", "ral-swap", "wrong-word" };

        // ---------- JSON ----------

        public string ToJson()
        {
            var w = new JsonOut();
            w.Open('{');
            w.Prop("tool").Str("thaiwrap");
            w.Prop("version").Str(Version);
            w.Prop("schema").Num(Schema);
            w.Prop("generated").Str(GeneratedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            w.Prop("input").Str(Input);
            w.Prop("settings").Open('{');
            foreach (var kv in Settings) w.Prop(kv.Key).Str(kv.Value);
            w.Close('}');
            w.Prop("summary").Open('{');
            w.Prop("files").Num(Files.Count);
            w.Prop("errors").Num(Count(QaSeverity.Error));
            w.Prop("warnings").Num(Count(QaSeverity.Warning));
            w.Prop("infos").Num(Count(QaSeverity.Info));
            w.Prop("byCheck").Open('{');
            foreach (var c in ChecksPresent()) w.Prop(c).Num(CountCheck(c));
            w.Close('}');
            w.Prop("placeholderPairs").Num(PlaceholderPairs);
            w.Prop("placeholderNoSource").Num(PlaceholderNoSource);
            w.Prop("checkTexts").Num(CheckTexts);
            w.Prop("checkTextsWithEnglish").Num(CheckTextsWithEnglish);
            w.Close('}');
            w.Prop("files").Open('[');
            foreach (var f in Files)
            {
                w.Item().Open('{');
                w.Prop("path").Str(f.Path);
                w.Prop("format").StrOrNull(f.Format);
                w.Prop("wrapped").Raw(f.Wrapped == null ? "null" : f.Wrapped.Value ? "true" : "false");
                w.Prop("breaksToInsert").Num(f.BreaksToInsert);
                w.Prop("typos").Num(f.Typos);
                w.Prop("ambiguous").Num(f.Ambiguous);
                if (f.Note != null) w.Prop("note").Str(f.Note);
                w.Close('}');
            }
            w.Close(']');
            w.Prop("findings").Open('[');
            foreach (var f in Findings)
            {
                w.Item().Open('{');
                w.Prop("check").Str(f.Check);
                w.Prop("severity").Str(SeverityName(f.Severity));
                w.Prop("file").Str(f.File);
                w.Prop(f.IsRow ? "row" : "line").Num(f.Line);
                w.Prop("message").Str(f.Message);
                if (f.Source != null) w.Prop("source").Str(f.Source);
                if (f.Translation != null) w.Prop("translation").Str(f.Translation);
                if (f.Wrong != null) w.Prop("wrong").Str(f.Wrong);
                if (f.Suggested != null) w.Prop("suggested").Str(f.Suggested);
                if (f.Wrong != null) w.Prop("count").Num(f.Count);
                if (f.Context != null) w.Prop("context").Str(f.Context);
                w.Close('}');
            }
            w.Close(']');
            w.Prop("unknownWords").Open('[');
            foreach (var kv in UnknownWords)
            {
                w.Item().Open('{');
                w.Prop("word").Str(kv.Key);
                w.Prop("count").Num(kv.Value);
                w.Close('}');
            }
            w.Close(']');
            w.Close('}');
            return w.ToString() + "\n";
        }

        private List<string> ChecksPresent()
        {
            var list = new List<string>();
            foreach (var c in CheckOrder) if (CountCheck(c) > 0) list.Add(c);
            foreach (var f in Findings) if (list.IndexOf(f.Check) < 0) list.Add(f.Check);
            return list;
        }

        private int CountCheck(string check)
        {
            int n = 0;
            foreach (var f in Findings) if (f.Check == check) n++;
            return n;
        }

        /// <summary>เขียน JSON แบบย่อหน้า 2 ช่อง (ไม่พึ่ง System.Text.Json — Formats เป็น netstandard2.0 ไม่มี dependency)</summary>
        private sealed class JsonOut
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private readonly List<bool> _first = new List<bool>(); // ต่อชั้น: ยังไม่มีสมาชิก
            private bool _afterProp;

            private void NewMember()
            {
                int d = _first.Count - 1;
                if (!_first[d]) _sb.Append(',');
                _first[d] = false;
                _sb.Append('\n').Append(' ', 2 * _first.Count);
            }

            public JsonOut Prop(string name)
            {
                NewMember();
                _sb.Append(JsonStringProcessor.Encode(name)).Append(": ");
                _afterProp = true;
                return this;
            }

            public JsonOut Item()
            {
                NewMember();
                _afterProp = true;
                return this;
            }

            public JsonOut Open(char c)
            {
                if (!_afterProp && _first.Count > 0) NewMember();
                _afterProp = false;
                _sb.Append(c);
                _first.Add(true);
                return this;
            }

            public JsonOut Close(char c)
            {
                bool empty = _first[_first.Count - 1];
                _first.RemoveAt(_first.Count - 1);
                if (!empty) _sb.Append('\n').Append(' ', 2 * _first.Count);
                _sb.Append(c);
                return this;
            }

            public JsonOut Raw(string s) { _sb.Append(s); _afterProp = false; return this; }
            public JsonOut Str(string s) => Raw(JsonStringProcessor.Encode(s));
            public JsonOut StrOrNull(string? s) => Raw(s == null ? "null" : JsonStringProcessor.Encode(s));
            public JsonOut Num(long n) => Raw(n.ToString(CultureInfo.InvariantCulture));

            public override string ToString() => _sb.ToString();
        }

        // ---------- HTML ----------

        /// <summary>หน้าเดียวจบในตัว (CSS ฝังในไฟล์ · ไม่มีสคริปต์ · โหมดมืดตามระบบ) — ส่งให้ทีมแปลเปิดดูได้เลย</summary>
        public string ToHtml()
        {
            var sb = new StringBuilder();
            int err = Count(QaSeverity.Error), warn = Count(QaSeverity.Warning), info = Count(QaSeverity.Info);
            sb.Append("<!doctype html>\n<html lang=\"th\">\n<head>\n<meta charset=\"utf-8\">\n")
              .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
              .Append("<title>รายงาน QA thaiwrap</title>\n<style>\n").Append(Css).Append("</style>\n</head>\n<body>\n<main>\n");
            sb.Append("<h1>รายงาน QA</h1>\n<p class=\"meta\">").Append(H(Input)).Append(" · thaiwrap ").Append(H(Version))
              .Append(" · สร้างเมื่อ ").Append(GeneratedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(" UTC</p>\n");

            sb.Append("<div class=\"cards\">\n");
            Card(sb, "ไฟล์", Files.Count, "");
            Card(sb, "ต้องแก้", err, err > 0 ? "error" : "ok");
            Card(sb, "ควรตรวจ", warn, warn > 0 ? "warning" : "ok");
            Card(sb, "แจ้งให้ทราบ", info, "info");
            sb.Append("</div>\n");
            sb.Append("<p class=\"note\">รายงานนี้ไม่แก้ไฟล์แปล · ข้อ \"ควรตรวจ\" เป็นข้อเสนอ ตรวจบริบทก่อนแก้ที่ไฟล์ต้นทางทุกข้อ</p>\n");

            if (Findings.Count == 0) sb.Append("<p class=\"clean\">ไม่พบปัญหา</p>\n");
            foreach (var check in ChecksPresent())
            {
                var items = Findings.FindAll(f => f.Check == check);
                var sev = items[0].Severity;
                sb.Append("<section>\n<h2><span class=\"badge ").Append(SeverityName(sev)).Append("\">").Append(SeverityLabel(sev))
                  .Append("</span> ").Append(H(CheckTitle(check))).Append(" <span class=\"count\">").Append(items.Count).Append("</span></h2>\n");
                sb.Append("<div class=\"scroll\"><table>\n<thead><tr><th>ตำแหน่ง</th><th>รายละเอียด</th>");
                bool pair = items.Exists(f => f.Source != null || f.Translation != null);
                bool ctx = items.Exists(f => f.Context != null);
                if (pair) sb.Append("<th>ต้นฉบับ</th><th>คำแปล</th>");
                if (ctx) sb.Append("<th>บริบท (ตำแหน่งแรก)</th>");
                sb.Append("</tr></thead>\n<tbody>\n");
                foreach (var f in items)
                {
                    sb.Append("<tr><td class=\"loc\">").Append(H(Location(f))).Append("</td><td>");
                    if (f.Wrong != null)
                        sb.Append("<b>").Append(H(f.Wrong)).Append("</b> → <b>").Append(H(f.Suggested ?? "")).Append("</b> ×").Append(f.Count);
                    else sb.Append(H(f.Message));
                    sb.Append("</td>");
                    if (pair) sb.Append("<td class=\"text\">").Append(H(f.Source ?? "")).Append("</td><td class=\"text\">").Append(H(f.Translation ?? "")).Append("</td>");
                    if (ctx) sb.Append("<td class=\"text\">").Append(f.Context == null ? "" : "…" + H(f.Context) + "…").Append("</td>");
                    sb.Append("</tr>\n");
                }
                sb.Append("</tbody>\n</table></div>\n</section>\n");
            }

            if (UnknownWords.Count > 0)
            {
                const int max = 300;
                sb.Append("<section>\n<h2>คำนอกพจนานุกรมที่พบบ่อย <span class=\"count\">").Append(UnknownWords.Count).Append("</span></h2>\n")
                  .Append("<p class=\"note\">คำเฉพาะของเกมที่ควรเพิ่มใน words_extra.txt ให้ตัดคำถูก</p>\n<p class=\"words\">");
                for (int i = 0; i < UnknownWords.Count && i < max; i++)
                    sb.Append("<span>").Append(H(UnknownWords[i].Key)).Append(" <small>×").Append(UnknownWords[i].Value).Append("</small></span> ");
                if (UnknownWords.Count > max) sb.Append("<span class=\"more\">อีก ").Append(UnknownWords.Count - max).Append(" คำในรายงาน JSON</span>");
                sb.Append("</p>\n</section>\n");
            }

            sb.Append("<section>\n<h2>ไฟล์ที่ตรวจ <span class=\"count\">").Append(Files.Count).Append("</span></h2>\n")
              .Append("<div class=\"scroll\"><table>\n<thead><tr><th>ไฟล์</th><th>ฟอร์แมต</th><th>แตรป</th><th>รูปพิมพ์ผิด</th><th>หมายเหตุ</th></tr></thead>\n<tbody>\n");
            foreach (var f in Files)
            {
                string wrapped = f.Wrapped == null ? "–" : f.Wrapped.Value ? "ครบ" : "ยังไม่ครบ (+" + f.BreaksToInsert + ")";
                sb.Append("<tr><td class=\"loc\">").Append(H(f.Path)).Append("</td><td>").Append(H(f.Format ?? "–"))
                  .Append("</td><td>").Append(H(wrapped)).Append("</td><td>").Append(f.Typos + f.Ambiguous)
                  .Append("</td><td>").Append(H(f.Note ?? "")).Append("</td></tr>\n");
            }
            sb.Append("</tbody>\n</table></div>\n</section>\n");

            if (Settings.Count > 0)
            {
                sb.Append("<section>\n<h2>ค่าตั้งที่ใช้</h2>\n<dl class=\"settings\">\n");
                foreach (var kv in Settings)
                    sb.Append("<dt>").Append(H(kv.Key)).Append("</dt><dd>").Append(H(kv.Value)).Append("</dd>\n");
                sb.Append("</dl>\n</section>\n");
            }
            sb.Append("</main>\n</body>\n</html>\n");
            return sb.ToString();
        }

        private static string SeverityLabel(QaSeverity s) =>
            s == QaSeverity.Error ? "ต้องแก้" : s == QaSeverity.Warning ? "ควรตรวจ" : "แจ้ง";

        private static string Location(QaFinding f) =>
            f.Line <= 0 ? f.File : f.File + (f.IsRow ? " แถว " : ":") + f.Line;

        private static void Card(StringBuilder sb, string label, int n, string cls) =>
            sb.Append("<div class=\"card ").Append(cls).Append("\"><div class=\"n\">").Append(n).Append("</div><div>").Append(label).Append("</div></div>\n");

        /// <summary>escape HTML (ข้อความแปลมี &lt;tag&gt; ของเกมเยอะ — ต้องแสดงเป็นตัวอักษร ไม่ใช่ markup)</summary>
        public static string H(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private const string Css =
            ":root{--bg:#fbfbfa;--fg:#1f2328;--muted:#656d76;--line:#d8dee4;--card:#fff;--err:#c62828;--warn:#9a6700;--info:#0969da;--ok:#1a7f37}\n" +
            "@media (prefers-color-scheme:dark){:root{--bg:#0d1117;--fg:#e6edf3;--muted:#8d96a0;--line:#30363d;--card:#161b22;--err:#ff7b72;--warn:#d29922;--info:#58a6ff;--ok:#3fb950}}\n" +
            "body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.6 system-ui,-apple-system,\"Segoe UI\",\"Noto Sans Thai\",\"Leelawadee UI\",sans-serif}\n" +
            "main{max-width:1200px;margin:0 auto;padding:24px 16px 48px}\n" +
            "h1{margin:0 0 4px;font-size:26px}h2{font-size:18px;margin:32px 0 8px}\n" +
            ".meta,.note{color:var(--muted);margin:4px 0}.clean{color:var(--ok);font-weight:600}\n" +
            ".cards{display:flex;flex-wrap:wrap;gap:12px;margin:16px 0}\n" +
            ".card{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:10px 16px;min-width:110px}\n" +
            ".card .n{font-size:26px;font-weight:700}.card.error .n{color:var(--err)}.card.warning .n{color:var(--warn)}.card.info .n{color:var(--info)}.card.ok .n{color:var(--ok)}\n" +
            ".badge{font-size:12px;border-radius:999px;padding:2px 8px;color:#fff;vertical-align:middle}\n" +
            ".badge.error{background:var(--err)}.badge.warning{background:var(--warn)}.badge.info{background:var(--info)}\n" +
            ".count{color:var(--muted);font-weight:400}\n" +
            ".scroll{overflow-x:auto}table{border-collapse:collapse;width:100%;background:var(--card)}\n" +
            "th,td{border:1px solid var(--line);padding:6px 8px;text-align:left;vertical-align:top}th{font-weight:600;white-space:nowrap}\n" +
            "td.loc{font-family:ui-monospace,Menlo,Consolas,monospace;font-size:13px;word-break:break-all;min-width:140px}\n" +
            "td.text{white-space:pre-wrap;min-width:180px}\n" +
            ".words span{display:inline-block;margin:2px 6px 2px 0;padding:1px 8px;border:1px solid var(--line);border-radius:6px;background:var(--card)}\n" +
            ".words small,.more{color:var(--muted)}\n" +
            ".settings{display:grid;grid-template-columns:max-content 1fr;gap:4px 16px}.settings dt{color:var(--muted)}.settings dd{margin:0;word-break:break-all}\n";
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ThaiWrap;
using ThaiWrap.Formats;

namespace ThaiWrap.Tests
{
    public static class Program
    {
        private static int _failures;
        private static int _checks;

        private static void Check(string name, bool condition, string detail = "")
        {
            _checks++;
            if (condition)
            {
                Console.WriteLine("  ok  " + name);
            }
            else
            {
                _failures++;
                Console.WriteLine("FAIL  " + name + (detail.Length > 0 ? "  [" + detail + "]" : ""));
            }
        }

        public static int Main(string[] args)
        {
            string wordList = args.Length > 0 ? args[0] : "data/words_th.txt";
            if (!File.Exists(wordList)) { Console.Error.WriteLine("word list not found: " + wordList); return 2; }

            var seg = ThaiSegmenter.FromFile(wordList);
            var zw = '\u200B';

            Console.WriteLine("== dictionary ==");
            Check("word count > 50000", seg.WordCount > 50000, seg.WordCount.ToString());

            Console.WriteLine("== invariants on all sample sentences ==");
            string[] lines = File.ReadAllLines("samples/game_sentences.txt", Encoding.UTF8)
                .Where(l => l.Trim().Length > 0).ToArray();
            Check("sample set loaded (>= 35 sentences)", lines.Length >= 35, lines.Length.ToString());

            bool roundTrip = true, noMarkBefore = true, noLeadingVowelAfter = true,
                 noWsAdjacent = true, noEdgeZwsp = true, someInserted = false;
            long insertedTotal = 0;
            var edgeOffenders = new List<string>();
            foreach (var line in lines)
            {
                string outText = seg.InsertZwsp(line);

                if (outText.Replace(zw.ToString(), "") != line) roundTrip = false;
                for (int i = 1; i < outText.Length; i++)
                {
                    char prev = outText[i - 1];
                    char cur = outText[i];
                    if (cur == zw)
                    {
                        insertedTotal++;
                        char before = prev;
                        char after = i + 1 < outText.Length ? outText[i + 1] : '\0';
                        if (after != '\0' && (ThaiChars.IsCombiningMark(after) || ThaiChars.IsThaiPunctuation(after)
                            || ThaiChars.IsWhitespace(after) || ThaiChars.NoBreakBefore(after)))
                            noMarkBefore = false;
                        if (ThaiChars.IsLeadingVowel(before)) noLeadingVowelAfter = false;
                        if (ThaiChars.IsWhitespace(before)) noWsAdjacent = false;
                    }
                }
                if (outText.StartsWith(zw.ToString(), StringComparison.Ordinal) ||
                    outText.EndsWith(zw.ToString(), StringComparison.Ordinal))
                {
                    noEdgeZwsp = false;
                    edgeOffenders.Add(Show(outText));
                }
                if (outText.Contains(zw)) someInserted = true;
            }
            Check("round-trip: stripping ZWSP restores original", roundTrip);
            Check("no ZWSP before combining marks / ๆ ฯ / whitespace / closing punct", noMarkBefore);
            Check("no ZWSP after leading vowels เ แ โ ใ ไ", noLeadingVowelAfter);
            Check("no ZWSP adjacent to existing whitespace", noWsAdjacent);
            Check("no ZWSP at string start/end", noEdgeZwsp, string.Join(" // ", edgeOffenders));
            Check("ZWSP actually inserted somewhere", someInserted, "inserted=" + insertedTotal);

            Console.WriteLine("== specific cases ==");
            string t1 = seg.InsertZwsp("นักเดินทางจากแดนไกล เจ้ามาถึงหมู่บ้านแห่งนี้เพื่อเหตุใด");
            Check("Thai words get break points between them", t1.Count(ch => ch == zw) >= 8, Show(t1));
            Check("no break inside '25' or before '%'", !t1.Contains("25" + zw) && !t1.Contains(zw + "%"), Show(t1));

            string t2 = seg.InsertZwsp("กดปุ่ม A เพื่อโจมตี");
            Check("latin token 'A' intact", t2.Contains("A") && !t2.Contains(zw + "A") && !t2.Contains("A" + zw), Show(t2));

            string t3 = seg.InsertZwsp("Hello World");
            Check("pure latin unchanged", t3 == "Hello World", Show(t3));

            string t4 = seg.InsertZwsp("");
            Check("empty string ok", t4 == "");

            // Long unknown run (gibberish name) must get cluster break points; short one must not.
            string longName = "กวุณโณตลฑฆฑพฌชฬฐซึืูเฌฎโณฒฒฒ"; // no dictionary words here
            string t5 = seg.InsertZwsp(longName);
            Check("long unknown run is breakable", t5.Contains(zw), Show(t5));
            string shortName = "ฬฌฅญฐฎ";
            string t6 = seg.InsertZwsp(shortName);
            Check("short unknown run stays atomic", !t6.Contains(zw), Show(t6));

            string t7 = seg.InsertZwsp("เขาเดินเข้ามาอย่างเงียบๆ ไม่พูดอะไรสักคำ");
            Check("ไม่ตัดก่อน ๆ (mai yamok)", !t7.Contains(zw + "ๆ"), Show(t7));

            string t8 = seg.InsertZwsp("บอส: มังกรเพลิงคราม (ระดับ S)");
            Check("no break after '(' or before ')'", !t8.Contains("(" + zw) && !t8.Contains(zw + ")"), Show(t8));

            string t9 = seg.InsertZwsp("โหมดยาก: ศัตรูแข็งแกร่งขึ้น 50% และไอเทมฟื้นฟูหายากขึ้น");
            Check("dialogue with colon/percent passes invariants",
                t9.Replace(zw.ToString(), "") == "โหมดยาก: ศัตรูแข็งแกร่งขึ้น 50% และไอเทมฟื้นฟูหายากขึ้น" && !t9.Contains(zw + ":"),
                Show(t9));

            // newline preservation
            string t10 = seg.InsertZwsp("บรรทัดที่หนึ่ง\nบรรทัดที่สอง");
            Check("newlines preserved", t10.Contains('\n') && !t10.Contains(zw + "\n") && !t10.Contains("\n" + zw), Show(t10));

            Console.WriteLine("== placeholders / rich-text / ฯ ==");
            string g1 = seg.InsertZwsp("โปรดพบ {ชื่อผู้เล่น} ที่หอคอยกลางเมืองเมื่อพระอาทิตย์ตกดิน");
            Check("mask: {…} ครบและไม่มี ZWSP ข้างใน", g1.Contains("{ชื่อผู้เล่น}"), Show(g1));
            Check("mask: ข้อความรอบ mask ยังถูกตัดคำ", g1.Contains(zw), Show(g1));
            Check("mask: ไม่มี ZWSP ติดขอบ mask",
                !g1.Contains("}" + zw) && !g1.Contains(zw + "{") && !g1.Contains("{" + zw) && !g1.Contains(zw + "}"), Show(g1));
            string g2 = seg.InsertZwsp("<color=red>คำเตือน</color> มีกับดักอยู่ข้างหน้า");
            Check("mask: แท็ก rich-text ครบทั้งเปิด-ปิด", g2.Contains("<color=red>") && g2.Contains("</color>"), Show(g2));
            Check("mask: ข้อความในแท็กยังถูกตัดคำ",
                g2.Replace("<color=red>", "").Replace("</color>", "").Contains(zw), Show(g2));
            string g3 = seg.InsertZwsp("เดินทางจากกรุงเทพฯ ไปยังอยุธยาด้วยเรือโดยสาร");
            Check("ฯ (U+0E2F): ไม่แทรก ZWSP ก่อน/หลัง ฯ", !g3.Contains(zw + "ฯ") && !g3.Contains("ฯ" + zw), Show(g3));
            string g4in = "ข้อความ" + zw + "{ชื่อผู้เล่น}ทดสอบระบบ";
            string g4 = seg.InsertZwsp(g4in);
            Check("mask: ZWSP เดิมก่อน mask ถูกรักษาไว้", g4.Contains("ข้อความ" + zw + "{ชื่อผู้เล่น}"), Show(g4));
            Check("mask: ข้อความหลัง mask ยังถูกตัดคำ", g4.Contains("}" + "ทดสอบ" + zw), Show(g4));
            Check("mask: round-trip ถูกแม้มี ZWSP เดิม", g4.Replace(zw.ToString(), "") == g4in.Replace(zw.ToString(), ""));

            Console.WriteLine("== DP fallback & hairspace ==");
            string u1 = seg.InsertZwsp("ฌาฌาฌาฌาฌาฌาฌาฌาฌา");
            Check("unknown ยาว: ไม่แยก า ออกจากพยัญชนะ (ฌา เป็นหน่วยเดียว)",
                !u1.Contains("ฌ" + zw) && u1.Contains(zw), Show(u1));
            string u2 = seg.InsertZwsp("เฌนเฌนเฌนเฌนเฌนเฌนเฌนเฌนเฌน");
            Check("unknown ยาว: สระหน้าไม่ค้างท้ายชิ้น (fallback ใช้กฎขอบเขต DP)",
                !u2.Contains("เ" + zw) && u2.Contains(zw), Show(u2));
            var segHair = ThaiSegmenter.FromFile(wordList, new ThaiWrapOptions { BreakChar = '\u200A' });
            string h1 = segHair.InsertZwsp("นักเดินทางจากแดนไกล");
            Check("hairspace: แทรก U+200A โดยไม่ปน ZWSP",
                h1.Contains('\u200A') && !h1.Contains(zw));
            Check("hairspace: idempotent", segHair.InsertZwsp(h1) == h1);
            string h2 = segHair.InsertZwsp("ฌาฌาฌาฌาฌาฌาฌาฌาฌา");
            Check("hairspace: กฎ fallback ใช้ได้กับ break char อื่น",
                !h2.Contains("ฌ" + '\u200A') && h2.Contains('\u200A'));

            Console.WriteLine("== formats: csv ==");
            string csv = File.ReadAllText("tests/samples/items.csv", Encoding.UTF8);
            var table0 = CsvProcessor.ParseTable(csv, ',');
            string csvOut = CsvProcessor.Process(csv,
                new CsvOptions { ColumnNames = new List<string> { "thai" } },
                s => seg.InsertZwsp(s));
            var table1 = CsvProcessor.ParseTable(csvOut, ',');
            Check("csv: จำนวนแถวคงเดิม", table1.Count == table0.Count, table1.Count.ToString());
            Check("csv: คอลัมน์ key ไม่ถูกแตะ", Enumerable.SequenceEqual(
                table0.Select(r => r[0]), table1.Select(r => r[0])));
            Check("csv: คอลัมน์ en ไม่ถูกแตะ", Enumerable.SequenceEqual(
                table0.Select(r => r[2]), table1.Select(r => r[2])));
            Check("csv: คอลัมน์ thai ถูกแปลง", table1.Skip(1).All(r => r[1].Contains(zw)));
            Check("csv: แถว header ไม่ถูกแตะ (โหมดเลือกตามชื่อ)", table1[0][1] == "thai");
            Check("csv: CRLF ถูกรักษาไว้", csvOut.Contains("\r\n"));
            Check("csv: ฟิลด์มี comma ยัง parse ถูก (คงเป็นฟิลด์เดียว)",
                table1[3][1].Replace(zw.ToString(), "") == "เกราะหนังมังกร, ค่าป้องกันสูง", table1[3][1]);

            Console.WriteLine("== formats: json ==");
            string json = File.ReadAllText("tests/samples/dialogs.json", Encoding.UTF8);
            string jsonOut = JsonStringProcessor.Process(json, s => seg.InsertZwsp(s));
            Check("json: keys byte-identical", ExtractJsonKeys(json) == ExtractJsonKeys(jsonOut));
            Check("json: ค่า latin ไม่ถูกแตะ", jsonOut.Contains("\"Sword of Dawn\"") == json.Contains("\"Sword of Dawn\""));
            Check("json: ค่าไทยถูกแปลง", jsonOut.Contains(zw));
            Check("json: ค่าที่ escape \\\\uXXXX ถูก decode+แปลงถูก", jsonOut.Contains("นักเดินทาง" + zw));
            Check("json: โครงสร้างยัง valid (สแกนซ้ำไม่พัง)",
                JsonStringProcessor.Process(jsonOut, s => s) == jsonOut);
            string jsonKeys = JsonStringProcessor.Process(json, s => seg.InsertZwsp(s), k => k == "desc");
            Check("json --json-keys: แปลงเฉพาะ key ที่เลือก",
                jsonKeys.Contains(zw) && jsonKeys.Contains("\"name\": \"ดาบแห่งรุ่งอรุณ\""));

            Console.WriteLine("== formats: json (nested --json-keys) ==");
            const string nestedJson = "{\"items\":[{\"desc\":\"ข้อความไทย\"},\"ข้อความไทย\"]}";
            string nestedOut = JsonStringProcessor.Process(nestedJson, s => seg.InsertZwsp(s), k => k == "desc");
            int bareCount = CountOccurrences(nestedOut, "\"ข้อความไทย\"");
            Check("json ซ้อน: value ท้าย array ไม่ถูกแปลง (key ไม่รั่วข้ามระดับ)",
                bareCount == 1 && nestedOut.Contains(zw), nestedOut.Replace(zw.ToString(), "·"));
            const string nestedJson2 = "{\"lines\":[\"ข้อความไทย\",\"อีกข้อความ\"]}";
            string nestedOut2 = JsonStringProcessor.Process(nestedJson2, s => seg.InsertZwsp(s), k => k == "lines");
            Check("json ซ้อน: value ใน array ใช้ key ของ array ที่ครอบ (แปลงทั้งสอง element)",
                nestedOut2.Split(zw).Length - 1 >= 2, nestedOut2.Replace(zw.ToString(), "·"));

            Console.WriteLine("== formats: po ==");
            string po = File.ReadAllText("tests/samples/messages.po", Encoding.UTF8);
            string poOut = PoProcessor.Process(po, s => seg.InsertZwsp(s));
            Check("po: บรรทัด msgid ไม่ถูกแตะ", LinesWith(poOut, "msgid ") == LinesWith(po, "msgid "));
            Check("po: header entry ไม่ถูกแตะ", poOut.Contains("Content-Type"));
            Check("po: msgstr ถูกแปลง", poOut.Contains(zw));
            Check("po: obsolete (#~) ไม่ถูกแตะ", poOut.Contains("#~ msgstr"));

            Console.WriteLine("== formats: keyvalue ==");
            string kv = File.ReadAllText("tests/samples/autotr.txt", Encoding.UTF8);
            string kvOut = KeyValueProcessor.Process(kv, s => seg.InsertZwsp(s));
            Check("kv: key ไม่ถูกแตะ", kvOut.Split('\n').Any(l => l.TrimEnd().StartsWith("You found a sword.=")));
            Check("kv: คำแปลถูกแปลง", kvOut.Contains(zw));
            Check("kv: คอมเมนต์ไม่ถูกแตะ", kvOut.Contains("# comment line"));

            Console.WriteLine("== runtime filter (BepInEx hook logic) ==");
            var filter = new ThaiWrap.BepInEx.ThaiTextFilter(seg) { Enabled = true };
            string f1 = ProcessNonNull(filter, "Hello World");
            Check("filter: ข้อความไม่มีไทยผ่านเป๊ะ (fast reject)", f1 == "Hello World" && filter.SkippedCount == 1);
            string f2 = ProcessNonNull(filter, "นักเดินทางจากแดนไกล");
            Check("filter: ข้อความไทยได้ ZWSP", f2.Contains(zw));
            string f3 = ProcessNonNull(filter, "นักเดินทางจากแดนไกล");
            Check("filter: cache hit เมื่อ set ซ้ำ", f3 == f2 && filter.CacheHitCount == 1);
            string f4 = ProcessNonNull(filter, f2);
            Check("filter: idempotent (ป้อนข้อความที่มี ZWSP แล้ว)", f4 == f2);
            filter.Enabled = false;
            string f5 = ProcessNonNull(filter, "ข้อความทดสอบใหม่");
            Check("filter: Enabled=false = ผ่านตรง", f5 == "ข้อความทดสอบใหม่");
            filter.Enabled = true;
            // typewriter: เกมผลักข้อความทีละตัวทุกเฟรม — ทุก intermediate ต้อง valid และไม่พังเมื่อถึงเต็ม
            string full = "เขาเดินเข้ามาในห้องโถงอย่างเงียบๆ";
            string built = "";
            bool typewriterOk = true;
            for (int i = 1; i <= full.Length; i++)
            {
                built = filter.Process(full.Substring(0, i)) ?? "";
                if (built.Replace(zw.ToString(), "") != full.Substring(0, i)) { typewriterOk = false; break; }
            }
            Check("filter: typewriter ทุกขั้น round-trip ถูก", typewriterOk, Show(built));
            Check("filter: typewriter จบแล้วได้ ZWSP ครบ", built.Contains(zw));

            Console.WriteLine("== segmentation coverage ==");
            var allTokens = new List<Token>();
            foreach (var line in lines) allTokens.AddRange(seg.Tokenize(line));
            int thaiChars = allTokens.Sum(t => t.Text.Count(ThaiChars.IsThaiRange));
            int dictChars = allTokens.Where(t => t.Kind == TokenKind.DictionaryWord)
                .Sum(t => t.Text.Count(ThaiChars.IsThaiRange));
            int unknownRuns = allTokens.Count(t => t.Kind == TokenKind.UnknownRun || t.Kind == TokenKind.Cluster);
            Console.WriteLine("  Thai chars: " + thaiChars + ", covered by dictionary words: " +
                dictChars + " (" + (100.0 * dictChars / thaiChars).ToString("F1") + "%), unknown runs: " + unknownRuns);

            Console.WriteLine();
            Console.WriteLine("== segmentation preview (first 12 sentences) ==");
            foreach (var line in lines.Take(12))
                Console.WriteLine("  " + seg.SegmentToString(line));

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "ALL " + _checks + " CHECKS PASSED"
                : _failures + " / " + _checks + " CHECKS FAILED");
            return _failures == 0 ? 0 : 1;
        }

        private static string Show(string s) => s.Replace('\u200B', '·');

        private static string ProcessNonNull(ThaiWrap.BepInEx.ThaiTextFilter f, string s)
        {
            var r = f.Process(s);
            if (r == null) throw new InvalidOperationException("Process returned null for non-null input");
            return r;
        }

        /// <summary>ดึง string literal ทุกตัวที่เป็น key (ตามด้วย ':') แบบ raw — ใช้เทียบว่า keys ไม่ถูกแตะ</summary>
        private static string ExtractJsonKeys(string json)
        {
            var sb = new StringBuilder();
            int i = 0, n = json.Length;
            while (i < n)
            {
                if (json[i] != '"') { i++; continue; }
                int start = i;
                i++;
                while (i < n)
                {
                    if (json[i] == '\\') { i += 2; continue; }
                    if (json[i] == '"') { i++; break; }
                    i++;
                }
                int j = i;
                while (j < n && (json[j] == ' ' || json[j] == '\t' || json[j] == '\r' || json[j] == '\n')) j++;
                if (j < n && json[j] == ':') sb.Append(json, start, i - start).Append(';');
            }
            return sb.ToString();
        }

        private static int LinesWith(string s, string prefix) =>
            s.Split('\n').Count(l => l.TrimStart().StartsWith(prefix));

        private static int CountOccurrences(string s, string needle)
        {
            int count = 0, idx = 0;
            while ((idx = s.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) { count++; idx += needle.Length; }
            return count;
        }
    }
}

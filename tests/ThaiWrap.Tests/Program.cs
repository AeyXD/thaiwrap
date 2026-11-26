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

            Console.WriteLine("== normalize (เเ→แ + มาร์คซ้อน) ==");
            Check("unit: เเ → แ ทุกคู่", ThaiNormalizer.Normalize("ของเเท้ราคาเเพง") == "ของแท้ราคาแพง");
            Check("unit: เเ ท้ายบรรทัดต้องถูกแก้แม้ เ แรกอยู่ในคำอื่น (บั๊ก pre-check เคยพลาดจริงในคลัง)",
                ThaiNormalizer.Normalize("ข้าเริ่มโกรธเเล้วนะ") == "ข้าเริ่มโกรธแล้วนะ");
            Check("unit: เเเ → แเ (แทนทีละคู่จากซ้าย ไม่ซ้อนทับ)", ThaiNormalizer.Normalize("เเเ") == "แเ");
            Check("unit: มาร์คซ้อนยุบเหลือตัวเดียว", ThaiNormalizer.Normalize("สวััสดีเป็็็นไปได้") == "สวัสดีเป็นไปได้");
            Check("unit: า และพยัญชนะซ้อนไม่ถูกแตะ (ลากเสียง/อุทาน)",
                ThaiNormalizer.Normalize("โอ๊ยยยย อาาา ฮั่กกกก") == "โอ๊ยยยย อาาา ฮั่กกกก");
            Check("unit: มาร์คคนละชนิดติดกันไม่ถูกแตะ (ยังไม่มีกฎแก้ที่ปลอดภัย)",
                ThaiNormalizer.Normalize("ทัิ") == "ทัิ");
            string twice = ThaiNormalizer.Normalize(ThaiNormalizer.Normalize("ของเเท้สวััสดี"));
            Check("unit: idempotent", twice == ThaiNormalizer.Normalize("ของเเท้สวััสดี"));
            var cnt = ThaiNormalizer.Count("ของเเท้ราคาเเพงครับัั");
            Check("unit: Count แยกเเ กับมาร์คซ้อน", cnt.DoubleE == 2 && cnt.DoubledMark == 1, cnt.Total.ToString());
            Check("unit: Count นับ run ยาวเป็นจุดเดียว", ThaiNormalizer.Count("เร็็็็ด").DoubledMark == 1);

            Console.WriteLine("== normalize รอบ 2: ลำดับมาร์ค · ํ+า→ำ · วรรณยุกต์หลังสระหน้า ==");
            // ทุกประโยคด้านล่างเป็นของจริงจากคลังแปล W3 (assets/translations.json.gz) ยกเว้นที่ระบุ
            Check("ลำดับมาร์ค: วรรณยุกต์ก่อนสระบน (ท่ี)", ThaiNormalizer.Normalize("มาท่ีนี่") == "มาที่นี่");
            Check("ลำดับมาร์ค: สอดแทรก (ที่ี่ — อยู่ทีี่ไหน)", ThaiNormalizer.Normalize("อยู่ทีี่ไหน") == "อยู่ที่ไหน");
            Check("ลำดับมาร์ค: ั กับ วรรณยุกต์ สลับ (ทั่ั่ง — นกระทั่ั่ง)", ThaiNormalizer.Normalize("นกระทั่ั่ง") == "นกระทั่ง");
            Check("ลำดับมาร์ค: สระ+วรรณยุกต์ซ้อนสองรอบ (ดิ้ิ้น — มันดิ้ิ้นอีก)", ThaiNormalizer.Normalize("มันดิ้ิ้นอีก") == "มันดิ้นอีก");
            Check("ลำดับมาร์ค: ข้อความถูกอยู่แล้วไม่เปลี่ยน", ThaiNormalizer.Normalize("ที่นี่ทั่งไหนก็เป็นไปได้") == "ที่นี่ทั่งไหนก็เป็นไปได้");
            Check("ํ+า→ำ: ไม่มีวรรณยุกต์ (คํานึง)", ThaiNormalizer.Normalize("ไม่คํานึงถึง") == "ไม่คำนึงถึง");
            Check("ํ+า→ำ: วรรณยุกต์คั่น (นํ้า — ระบายนํ้า)", ThaiNormalizer.Normalize("ระบายนํ้า") == "ระบายน้ำ");
            Check("ํ+า→ำ: วรรณยุกต์คั่นอีกรูป (ดํ่า — ดื่มดํ่า)", ThaiNormalizer.Normalize("ดื่มดํ่า") == "ดื่มด่ำ");
            Check("ํ+า→ำ: วรรณยุกต์มาก่อน ํ (น้ํา)", ThaiNormalizer.Normalize("น้ําใส") == "น้ำใส");
            Check("ํ ลอยไม่นำหน้า า ไม่ถูกแตะ (บาลี/สันสกฤต)", ThaiNormalizer.Normalize("สํ") == "สํ");
            Check("วรรณยุกต์หลงหลังสระหน้า: มีตัวถูกอยู่แล้ว → ลบ (ไ่ม่ — ข้าไ่ม่มีวัน)",
                ThaiNormalizer.Normalize("ข้าไ่ม่มีวันเสีย") == "ข้าไม่มีวันเสีย");
            Check("วรรณยุกต์หลังสระหน้า: ไ่ด้ (นไปไมไ่ด้)", ThaiNormalizer.Normalize("ไ่ด้") == "ได้");
            Check("สระหน้า+วรรณยุกต์ที่ต้องย้าย: ไม่แตะ (เ้วย — อีกเ้วย Geral…)",
                ThaiNormalizer.Normalize("อีกเ้วย") == "อีกเ้วย");
            string allDirty = "ข้าไ่ม่มีวันเเท้ มาท่ีนี่ ระบายนํ้า สวััสดีเป็็็น";
            Check("รวมทุกกฎ + idempotent", ThaiNormalizer.Normalize(ThaiNormalizer.Normalize(allDirty)) == ThaiNormalizer.Normalize(allDirty));
            var cnt2 = ThaiNormalizer.Count("มาท่ีนี่ นํ้า ไ่ม่ เหมืิอน คุู่ เสื้่อ อีกเ้วย");
            Check("Count รอบ 2: แยกตระกูลที่แก้ได้",
                cnt2.MarkOrder == 1 && cnt2.SaraAm == 1 && cnt2.StrayTone == 1, $"{cnt2.Total}");
            Check("Count รอบ 2: คำเตือนเคสกำกวม",
                cnt2.MixedVowels == 2 && cnt2.MixedTones == 1 && cnt2.LeadingVowelMark == 1, $"{cnt2.WarningsTotal}");
            Check("Count: ข้อความสะอาด = ศูนย์ทุกช่อง",
                new[] { "ที่นี่ทั่งไหน ไก่นี้ดี กระทั่ง ดิ้น ไม่มีวัน", "Hello World" }
                    .All(s => { var c = ThaiNormalizer.Count(s); return c.Total == 0 && c.WarningsTotal == 0; }));
            bool cleanCorpusStable = true;
            foreach (var line in lines)
                if (ThaiNormalizer.Normalize(line) != line) { cleanCorpusStable = false; break; }
            Check("normalize: ประโยคตัวอย่างทั้งชุดผ่านกฎใหม่ไม่เปลี่ยนเลย", cleanCorpusStable);

            var segNorm = ThaiSegmenter.FromFile(wordList, new ThaiWrapOptions { Normalize = true });
            string dirty = "อาวุธเเข็งแกร่งของเเผ่นดิน";
            Check("default: ปิด normalize อนุรักษ์ข้อความเดิม (ไม่แก้ตัวอักษร)",
                seg.InsertZwsp(dirty).Replace(zw.ToString(), "") == dirty);
            string dirtyNorm = segNorm.InsertZwsp(dirty);
            Check("normalize: strip ZWSP ได้ข้อความที่ยุบแล้ว", dirtyNorm.Replace(zw.ToString(), "") == ThaiNormalizer.Normalize(dirty), Show(dirtyNorm));
            Check("normalize: ได้จุดตัดคืนมา", dirtyNorm.Contains(zw) && dirtyNorm.Contains("อาวุธ" + zw) && dirtyNorm.Contains("แข็งแกร่ง" + zw + "ของ"), Show(dirtyNorm));
            Check("normalize: idempotent (รันซ้ำบนข้อความที่แก้แล้ว)", segNorm.InsertZwsp(dirtyNorm) == dirtyNorm);
            string maskDirty = segNorm.InsertZwsp("ทดสอบ{เเทส}เเล็กน้อย");
            Check("normalize: ไม่แตะข้างใน placeholder", maskDirty.Contains("{เเทส}"), Show(maskDirty));
            Check("normalize: นอก placeholder ยังถูกแก้", maskDirty.Replace(zw.ToString(), "").Contains("ทดสอบ") && maskDirty.Replace(zw.ToString(), "").EndsWith("แล็กน้อย"), Show(maskDirty));
            bool normRoundTrip = true;
            foreach (var line in lines)
                if (segNorm.InsertZwsp(line).Replace(zw.ToString(), "") != ThaiNormalizer.Normalize(line)) { normRoundTrip = false; break; }
            Check("normalize: round-trip ทุกประโยคตัวอย่าง = ข้อความที่ยุบแล้ว", normRoundTrip);
            Check("normalize: ประโยคสะอาดไม่เปลี่ยน (normalize = no-op)", segNorm.InsertZwsp(lines[0]) == seg.InsertZwsp(lines[0]));
            Check("รอบ 2 integration: ลำดับมาร์ค — คำกลับเข้าพจนานุกรม",
                segNorm.SegmentToString("มาท่ีนี่") == "มา|ที่นี่", segNorm.SegmentToString("มาท่ีนี่"));
            Check("รอบ 2 integration: ํ+า→ำ — คำกลับเข้าพจนานุกรม (น้ำทิ้ง เป็นคำประสมใน dict)",
                segNorm.SegmentToString("ระบายนํ้าทิ้ง") == "ระบาย|น้ำทิ้ง", segNorm.SegmentToString("ระบายนํ้าทิ้ง"));
            Check("รอบ 2 integration: วรรณยุกต์หลังสระหน้า — คำกลับเข้าพจนานุกรม",
                segNorm.SegmentToString("ข้าไ่ม่มีวันเสีย") == "ข้า|ไม่มี|วัน|เสีย", segNorm.SegmentToString("ข้าไ่ม่มีวันเสีย"));

            Console.WriteLine("== phrase-chunk + space mode (เอนจินที่ตัดเฉพาะ ASCII space — พอร์ตจาก ThaiW1Setup) ==");
            var segSpace = ThaiSegmenter.FromFile(wordList, new ThaiWrapOptions
            {
                BreakChar = ' ', MaxChunk = 12, MinThaiLength = 40
            });
            Check("space: ข้อความสั้นไม่ถูกแตะ (min-len กัน 'เกม ใหม่' กลางป้าย)",
                segSpace.InsertZwsp("เกมใหม่") == "เกมใหม่" && segSpace.InsertZwsp("ค่าป้องกันสูง") == "ค่าป้องกันสูง");
            string longThai = "นักผจญเหตุการณ์วิเศษพาเพื่อนๆเดินทางข้ามป่าลึกเพื่อตามหาสมบัติโบราณที่สาบสูญมานานหลายร้อยปี";
            string sp = segSpace.InsertZwsp(longThai);
            Check("space: ข้อความยาวได้ช่องว่าง และตัดออกแล้วได้ข้อความเดิม", sp.Contains(" ") && sp.Replace(" ", "") == longThai, sp);
            Check("space: ทุกกลุ่มวลี ≤ 12+8 ตัวอักษร (เพดานแข็งของ DP)",
                sp.Split(' ').All(seg => seg.Count(ThaiChars.IsThaiRange) <= 20), sp);
            Check("space: ไม่มีช่องว่างซ้อน/ติดช่องว่างเดิม", !sp.Contains("  ") && !longThai.Contains(" "));
            Check("space: เว้นแบบกลุ่มวลี ไม่ใช่ทุกคำ (จุดเว้นน้อยกว่าจำนวนคำ)",
                sp.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < seg.Tokenize(longThai).Count(t => t.Kind != TokenKind.Whitespace), sp);
            string clitic = "เขาวิ่งไปอย่างรวดเร็วแล้วกลับมาหาผู้อื่นเพื่อขอความช่วยเหลืออีกครั้งในที่สุด";
            string spClitic = segSpace.InsertZwsp(clitic);
            Check("space: ไม่เว้นก่อนคำเกาะท้าย (แล้ว ได้ ไป มา)", !spClitic.Contains(" แล้ว") && !spClitic.Contains(" ได้") && !spClitic.Contains(" ไป") && !spClitic.Contains(" มา"), spClitic);
            Check("space: ช่องว่างเดิมของทีมแปลคงเดิม (ไม่ซ้อน ไม่หาย)",
                segSpace.InsertZwsp("หนึ่งในสามของป่าลึกทางทิศเหนือ มีสัตว์ประหลาดดุร้ายอาศัยอยู่จำนวนมาก")
                    .Split(' ').Any(sg => sg == "ทิศเหนือ" || sg == "เหนือ"), segSpace.InsertZwsp("หนึ่งในสามของป่าลึกทางทิศเหนือ มีสัตว์ประหลาดดุร้ายอาศัยอยู่จำนวนมาก"));
            Check("space: ไม่แทรกรอบตัวอักษรละติน (กดปุ่มAเพื่อ คงเดิมรอบ A)",
                segSpace.InsertZwsp("กดปุ่มAเพื่อยืนยันการทำรายการซื้อขายสินค้าทั้งหมดในร้านค้าแห่งนี้")
                    .Replace(" ", "").Contains("ปุ่มAเพื่อ"), segSpace.InsertZwsp("กดปุ่มAเพื่อยืนยันการทำรายการซื้อขายสินค้าทั้งหมดในร้านค้าแห่งนี้"));
            string spMask = segSpace.InsertZwsp("ระบบแจ้งเตือน{ชื่อผู้เล่น}เมื่อพลังชีวิตต่ำกว่ากึ่งหนึ่งของค่าสูงสุดลงเรื่อยๆ");
            Check("space: ไม่แตะข้างใน placeholder", spMask.Contains("{ชื่อผู้เล่น}"), spMask);
            var segChunkZwsp = ThaiSegmenter.FromFile(wordList, new ThaiWrapOptions { MaxChunk = 12 });
            Check("chunk ใช้กับ ZWSP ได้: จุดตัดน้อยลงกว่าโหมดทุกรอยคำ",
                segChunkZwsp.InsertZwsp(longThai).Count(ch => ch == zw) < seg.InsertZwsp(longThai).Count(ch => ch == zw),
                Show(segChunkZwsp.InsertZwsp(longThai)));
            Check("default: ไม่มี --chunk พฤติกรรมเดิมทุกรอยคำ (สูงสุด)",
                seg.InsertZwsp(longThai).Count(ch => ch == zw) == seg.Tokenize(longThai).Count(t => t.Kind != TokenKind.Whitespace) - 1);

            Console.WriteLine("== words_extra.txt (คำเฉพาะโปรเจกต์: +เพิ่ม −ลบ #คอมเมนต์) ==");
            string extraTmp = Path.GetTempFileName();
            try
            {
                File.WriteAllLines(extraTmp,
                    new[] { "# คอมเมนต์และบรรทัดว่างต้องถูกข้าม", "วิทเชอร์", "-ใช้การ", "", "เอลฟ์" }, Encoding.UTF8);
                var merge = ThaiSegmenter.MergeWordFiles(wordList, extraTmp);
                Check("overlay: นับ +เพิ่ม −ลบ ถูก (ข้าม # และบรรทัดว่าง)",
                    merge.Added == 2 && merge.Removed == 1, $"+{merge.Added} −{merge.Removed}");
                var segExtra = new ThaiSegmenter(merge.Words);
                Check("overlay: คำที่เพิ่มเข้าเป็นคำในพจนานุกรม (วิทเชอร์ เอลฟ์)",
                    segExtra.Tokenize("วิทเชอร์ต่อสู้กับเอลฟ์ในป่า").Any(t => (t.Text == "วิทเชอร์" || t.Text == "เอลฟ์") && t.Kind == TokenKind.DictionaryWord));
                Check("overlay: ตัวเทียบ — dict หลักยังไม่มี วิทเชอร์/เอลฟ์ (เทสจับผลจริง)",
                    seg.Tokenize("วิทเชอร์ต่อสู้กับเอลฟ์ในป่า").All(t => t.Text != "วิทเชอร์"));
                string w1Case = segExtra.SegmentToString("ใช้การเล่นเป็นประจำ");
                Check("overlay: -ใช้การ แก้คำแย่งพยางค์ (เคสจริงจาก W1: ใช้การเล่น → ใช้|การ|เล่น)",
                    !w1Case.Contains("ใช้การ") && w1Case.StartsWith("ใช้|", StringComparison.Ordinal), w1Case);
                string baseCase = seg.SegmentToString("ใช้การเล่นเป็นประจำ");
                Check("overlay: ตัวเทียบ — ไม่มี extra ยังติดกันเป็น ใช้การ", baseCase.Contains("ใช้การ"), baseCase);
                Check("overlay: FromFiles ให้ผลเทียบเท่าการ merge เอง",
                    ThaiSegmenter.FromFiles(wordList, extraTmp).WordCount == segExtra.WordCount);
                Check("overlay: extra ไม่มีไฟล์ = ใช้ dict หลักเท่าเดิม",
                    ThaiSegmenter.MergeWordFiles(wordList, null).Words.Count == File.ReadAllLines(wordList).Length);
            }
            finally { File.Delete(extraTmp); }

            Console.WriteLine("== mispaste (วางคำแปลผิดแถว — เกณฑ์จาก production ของ W3) ==");
            {
                string sharedThai = "พายุโหระเหร่านำร่องพัดผ่านหมู่บ้านชายฝั่งอย่างรุนแรงจนบ้านเรือนถล่มราบเป็นหน้ากลอง"; // 69 ตัวอักษร ≥ 40
                string enOwner = "A fierce storm swept through the coastal village, flattening all the houses."; // ยาวราวกับไทย → เจ้าของ
                string enOther = "Deliver this sword to the captain."; // สั้นกว่าไทยมาก → ฝั่งวางผิดชัดเจน
                var rows = new List<List<string>>
                {
                    new List<string> { "id", "en", "thai" },
                    new List<string> { "1", "Good morning, traveler.", "สวัสดีผู้เดินทาง" },
                    new List<string> { "2", enOwner, sharedThai },   // เจ้าของ (ความยาวเข้ากัน) = แถวไฟล์ 3
                    new List<string> { "3", enOther, sharedThai },   // วางผิด (อังกฤษคนละเรื่อง) = แถวไฟล์ 4
                    new List<string> { "4", "A huge blazing fireball!", "บอลไฟอันไพศาลและยิ่งใหญ่บันดาลใจให้กองทัพทหารทั้งหลายเกิดความหวังที่จะได้รับชัยชนะกลับคืนมาในที่สุด" },
                    new List<string> { "5", "OK", "ตกลง" },
                };
                var hits = MispasteDetector.Detect(rows);
                Check("mispaste: จับได้ 2 จุด (ไทยซ้ำ + ratio ผิดปกติ)", hits.Count == 2, string.Join(" / ", hits.ConvertAll(h => h.RowNo + ":" + h.Reason)));
                var shared = hits.FirstOrDefault(h => h.OwnerRows.Count > 0);
                Check("mispaste: ระบุแถวเจ้าของถูกต้อง (แถวไฟล์ 4 วางผิด เจ้าของคือแถวไฟล์ 3)",
                    shared != null && shared.RowNo == 4 && shared.OwnerRows.Count == 1 && shared.OwnerRows[0] == 3,
                    shared == null ? "-" : $"แถว {shared.RowNo} เจ้าของ {string.Join(",", shared.OwnerRows)}");
                Check("mispaste: ratio ผิดปกติจับแถวที่ไทยยาวเกินอังกฤษมาก (แถวไฟล์ 5)",
                    hits.Any(h => h.RowNo == 5 && h.OwnerRows.Count == 0));
                Check("mispaste: แถวปกติ/สั้นไม่ถูกจับ (แถวไฟล์ 2 และ 6)",
                    hits.All(h => h.RowNo != 2 && h.RowNo != 6));
                var clean = MispasteDetector.Detect(new List<List<string>>
                {
                    new List<string> { "id", "en", "thai" },
                    new List<string> { "1", "Hello there, my friend, how are you today?", "สวัสดีเพื่อนยายของฉันเต็มแก่วันนี้เป็นอย่างไรบ้าง" },
                    new List<string> { "2", "Good bye, and good luck out there.", "ลาก่อนแล้วขอให้โชคดีนะที่นั่น" },
                });
                Check("mispaste: ตารางสะอาดไม่มีรายงาน", clean.Count == 0);
            }

            Console.WriteLine("== nobreak.txt วลีห้ามตัดกลางวลี (NBSP — วิธีของ ThaiW2Setup) ==");
            string nbTmp = Path.GetTempFileName();
            try
            {
                File.WriteAllLines(nbTmp, new[] { "# คอมเมนต์", "เดอะ วิทเชอร์", "", "ลา วาเล็ตต์", "อาร์ยัน ลา วาเล็ตต์" }, Encoding.UTF8);
                var phrases = ThaiSegmenter.LoadNoBreakPhrases(nbTmp);
                Check("nobreak: โหลดข้าม # /บรรทัดว่าง + เรียงยาวก่อน (วลีซ้อนจับยาวก่อน)",
                    phrases.Count == 3 && phrases[0] == "อาร์ยัน ลา วาเล็ตต์", string.Join("|", phrases));
                var nb = '\u00A0';
                var segNb = ThaiSegmenter.FromFile(wordList, new ThaiWrapOptions { NoBreakPhrases = phrases });
                string t = segNb.InsertZwsp("ตัวละครในเดอะ วิทเชอร์ต้องเดินทางไกลข้ามแผ่นดินที่สงบสุขเสมอ");
                Check("nobreak: ช่องว่างกลางวลีกลายเป็น NBSP และไม่มีจุดตัดคั่นกลางวลี",
                    t.Contains("เดอะ" + nb + "วิทเชอร์") && !t.Contains(zw.ToString() + nb) && !t.Contains(nb.ToString() + zw), Show(t));
                Check("nobreak: รอบนอกวลียังแทรกจุดตัดปกติ และช่องว่างอื่นคือเดิม (ยังเป็น space)",
                    t.Contains(zw) && !t.Contains(' '), Show(t));
                Check("nobreak: วลีซ้อน — จับวลียาวก่อน (อาร์ยัน ลา วาเล็ตต์ = NBSP สองตัว)",
                    segNb.InsertZwsp("ขุนนางอาร์ยัน ลา วาเล็ตต์ปกครองเมืองเล็กๆ อย่างสงบเรียบร้อยมานานหลายปี")
                        .Contains("อาร์ยัน" + nb + "ลา" + nb + "วาเล็ตต์"));
                string twice2 = segNb.InsertZwsp(segNb.InsertZwsp("เดินทางไปพบเดอะ วิทเชอร์ที่หมู่บ้านชายขอบป่าลึกในค่ำคืนนั้น"));
                Check("nobreak: idempotent (รันซ้ำผลเดิม)", twice2 == segNb.InsertZwsp("เดินทางไปพบเดอะ วิทเชอร์ที่หมู่บ้านชายขอบป่าลึกในค่ำคืนนั้น"));
                string maskNb = segNb.InsertZwsp("รายงาน{เดอะ วิทเชอร์}ถึงผู้การเมืองทันทีเมื่อพบเห็นสัตว์ประหลาดบินได้");
                Check("nobreak: ไม่แตะข้างใน mask", maskNb.Contains("{เดอะ วิทเชอร์}"), Show(maskNb));
                var segNbNorm = ThaiSegmenter.FromFile(wordList,
                    new ThaiWrapOptions { NoBreakPhrases = phrases, Normalize = true });
                string dirtyNb = segNbNorm.InsertZwsp("เขาอ่านเเรื่องเดอะ วิทเชอร์จนเกือบเที่ยงคืนแล้วยังไม่หลับสักทีเลย");
                Check("nobreak: ทำงานหลัง normalize (เเ→แ แล้วจับวลีที่สะกดถูกได้)",
                    dirtyNb.Contains("อ่าน" + zw) && dirtyNb.Contains("เดอะ" + nb + "วิทเชอร์")
                    && !dirtyNb.Contains("เชอ" + zw), Show(dirtyNb));
                var segNbSpace = ThaiSegmenter.FromFile(wordList,
                    new ThaiWrapOptions { NoBreakPhrases = phrases, BreakChar = ' ', MaxChunk = 12, MinThaiLength = 40 });
                string sp2 = segNbSpace.InsertZwsp("ชาวบ้านเล่าว่าเดอะ วิทเชอร์เคยผ่านหมู่บ้านนี้มาก่อนเมื่อหลายสิบปีที่ผ่านมาแล้ว");
                Check("nobreak: โหมด space — ไม่แทรกช่องว่างจุดตัดกลางวลี (มีแต่ NBSP)",
                    sp2.Contains("เดอะ" + nb + "วิทเชอร์") && sp2.Contains(" "), sp2);
                Check("nobreak: ไม่มีไฟล์วลี = พฤติกรรมเดิมทุกอย่าง (ไม่มี NBSP โผล่)",
                    !seg.InsertZwsp("ตัวละครในเดอะ วิทเชอร์ต้องเดินทางไกลข้ามแผ่นดินที่สงบสุขเสมอ").Contains('\u00A0'));
            }
            finally { File.Delete(nbTmp); }

            Console.WriteLine("== ไม้ยมกแบบเว้นวรรค → NBSP (พอร์ต YAMOK_SPACE จาก ThaiW3Setup 5535611) ==");
            {
                var nb = ' ';
                var segYm = ThaiSegmenter.FromFile(wordList, new ThaiWrapOptions { YamokNbsp = true });
                // เคสชุดเดียวกับ devtools/test_thai_wrap.py ของ W3
                string y1 = segYm.InsertZwsp("ต่าง ๆ นานา");
                Check("yamok: ช่องว่างหน้า ๆ → NBSP · ช่องว่างหลัง ๆ ยังเป็น space",
                    y1.Replace(zw.ToString(), "") == "ต่าง" + nb + "ๆ นานา", Show(y1));
                Check("yamok: ๆ ซ้อน + เครื่องหมายท้าย", segYm.InsertZwsp("เร็ว ๆๆ!") == "เร็ว" + nb + "ๆๆ!");
                Check("yamok: ช่องว่างสองตัว → NBSP สองตัว (ต่างจาก W3 ที่ยุบเหลือตัวเดียว — อนุรักษ์ข้อมูลเดิม)",
                    segYm.InsertZwsp("จริง  ๆ") == "จริง" + nb + nb + "ๆ");
                Check("yamok: ๆ ต้นข้อความ / ไม่มี ๆ = ไม่มี NBSP",
                    !segYm.InsertZwsp("ๆ ต้นข้อความ").Contains(nb) && !segYm.InsertZwsp("ไม่มี ไม้ยมก").Contains(nb));
                // ประโยคเกมจริงยาวพอให้มีจุดตัดรอบ ๆ — ข้อความทดสอบในกล่อง Glossary ของ W3 ใช้รูปแบบนี้
                string y2 = segYm.InsertZwsp("ชาวบ้านต่าง ๆ พากันหนีออกจากหมู่บ้านเมื่อเห็นสัตว์ประหลาดเดินเข้ามาใกล้ ๆ กำแพงเมือง");
                Check("yamok: ประโยคยาว — ไม่มีจุดตัดใดติดหน้า ๆ และ ๆ ทุกตัวติด NBSP",
                    !y2.Contains(" ๆ") && !y2.Contains(zw + "ๆ") && CountOccurrences(y2, nb + "ๆ") == 2 && y2.Contains(zw), Show(y2));
                Check("yamok: idempotent (รันซ้ำผลเดิม)", segYm.InsertZwsp(y2) == y2);
                var segYmHair = ThaiSegmenter.FromFile(wordList, new ThaiWrapOptions { YamokNbsp = true, BreakChar = ' ' });
                string y3 = segYmHair.InsertZwsp("ต่าง  ๆ นานา​ๆ");
                Check("yamok: ZWSP/hairspace ที่ติดหน้า ๆ ถูกตัดทิ้ง (เป็นจุดตัดหน้า ๆ)",
                    !y3.Contains(" ๆ") && !y3.Contains(zw + "ๆ") && y3.Contains("ต่าง" + nb + "ๆ"), Show(y3));
                var segYmNb = ThaiSegmenter.FromFile(wordList,
                    new ThaiWrapOptions { YamokNbsp = true, NoBreakPhrases = new List<string> { "เดอะ วิทเชอร์" } });
                string y4 = segYmNb.InsertZwsp("แฟนเดอะ วิทเชอร์ต่าง ๆ รอคอยภาคใหม่มานานหลายปีแล้ว");
                Check("yamok: ทำงานร่วมกับ nobreak", y4.Contains("เดอะ" + nb + "วิทเชอร์") && y4.Contains("ต่าง" + nb + "ๆ"), Show(y4));
                Check("yamok: ปิดอยู่ (default) = พฤติกรรมเดิม ช่องว่างหน้า ๆ คงเป็น space",
                    seg.InsertZwsp("ต่าง ๆ นานา").Replace(zw.ToString(), "") == "ต่าง ๆ นานา");
            }

            Console.WriteLine("== ร↔ล suggester (ข้อเสนอการสะกด — พอร์ตจาก find_ral_swaps ของ W3) ==");
            Check("ral: IsKnownWord ถูก (มี/ไม่มีใน dict)", seg.IsKnownWord("กระบี่") && !seg.IsKnownWord("บุกลุก"));
            Check("ral: บุกลุก → บุกรุก (เคสจริงจาก W3 — ทุกพยางค์เป็นคำจริงจึงรอดตัวตรวจคำสะกด)",
                seg.SuggestRalSwap("บุกลุก") == "บุกรุก", seg.SuggestRalSwap("บุกลุก") ?? "-");
            Check("ral: คำที่ถูกอยู่แล้วไม่เสนอ (null)", seg.SuggestRalSwap("บุกรุก") == null && seg.SuggestRalSwap("กระบี่") == null);
            Check("ral: คำสั้น/ไม่มี ร ล ไม่เสนอ", seg.SuggestRalSwap("กก") == null && seg.SuggestRalSwap("เดินไป") == null);

            Console.WriteLine("== ตัวตรวจคำผิดตามบริบท (พอร์ต find_ral_swaps / find_wrong_word ของ W3) ==");
            {
                var words = File.ReadAllLines(wordList);
                Check("ctx: EnumerateWords ครบเท่า WordCount", seg.EnumerateWords().Count() == seg.WordCount);
                var ck = new ThaiContextChecker(words);
                string Ral(string t) => string.Join(" ", ck.CheckRal(t).Select(s => s.Wrong + ">" + s.Suggested));
                Check("ctx ral: คำประสมที่ทุกพยางค์เป็นคำจริง (บุกลุก → บุกรุก)", Ral("ศัตรูบุกลุกเข้ามาในหมู่บ้าน") == "บุกลุก>บุกรุก", Ral("ศัตรูบุกลุกเข้ามาในหมู่บ้าน"));
                Check("ctx ral: การ นำหน้าคำถัดไปไม่ใช่ กาล (ตลอดการเดินทาง)", Ral("ตลอดการเดินทางข้าไม่เคยหลับ") == "");
                Check("ctx ral: การ + คำลงท้าย = กาล (ตลอดการเลย)", Ral("ข้าจะจำเจ้าไว้ตลอดการเลย") == "ตลอดการ>ตลอดกาล", Ral("ข้าจะจำเจ้าไว้ตลอดการเลย"));
                Check("ctx ral: ...ละ ที่เป็นต้นคำถัดไป (มาละลาย) ไม่รายงาน", Ral("เอามาละลายน้ำก่อนดื่ม") == "", Ral("เอามาละลายน้ำก่อนดื่ม"));
                Check("ctx ral: ไม่...หรอก = คำลงท้ายปฏิเสธ ไม่ใช่ หลอก", Ral("ข้าไม่ได้เผาหรอกนะ") == "", Ral("ข้าไม่ได้เผาหรอกนะ"));
                Check("ctx ral: คู่คำที่ต้องดูบริบท (สมุนไหร → สมุนไพร · เท่าไหร → เท่าไหร่)",
                    Ral("สมุนไหรชนิดนี้ราคาเท่าไหร") == "สมุนไหร>สมุนไพร เท่าไหร>เท่าไหร่", Ral("สมุนไหรชนิดนี้ราคาเท่าไหร"));
                Check("ctx ral: ไหร ไม่มีบริบทกำกับ = ไม่เสนอ", Ral("ไหรกันนะ") == "", Ral("ไหรกันนะ"));
                Check("ctx ral: คำผิดที่รู้คำแก้ (รุงแรง → รุนแรง ผ่าน HINTS)", Ral("ความรุงแรงในเมืองนี้") == "รุง>รุน", Ral("ความรุงแรงในเมืองนี้"));
                Check("ctx ral: เครื่องหมาย/แท็กติดคำยังจับได้ (ตัดคำทีละช่วงอักษรไทย)", Ral("<b>บุกลุก!</b>") == "บุกลุก>บุกรุก", Ral("<b>บุกลุก!</b>"));
                Check("ctx ral: ตำแหน่งอ้างอิงข้อความเดิม", ck.CheckRal("ศัตรูบุกลุก")[0].Start == 5);

                var allow = CheckAllow.Parse(new[] { "# ชื่อเฉพาะ", "บุกลุก", "ข้า[ไม่]ได้", "ตลอดการ>ตลอดกาล" });
                var cka = new ThaiContextChecker(words, allow);
                Check("ctx allow: รูปเดี่ยว / คู่ / นับรายการ (ข้าม #)",
                    cka.CheckRal("ศัตรูบุกลุกเข้ามา").Count == 0 && cka.CheckRal("จำไว้ตลอดการเลย").Count == 0 && allow.Count == 3);
                Check("ctx allow: วลี — ถูกเฉพาะในวลีที่ระบุ", allow.AllowsAt("ไม่", "ข้าไม่ได้ทำ", 3) && !allow.AllowsAt("ไม่", "เขาไม่ได้ทำ", 3));

                // สะกดถูกแต่ผิดบริบท: คลังมี พรุ่งนี้|ข้า|จะ 5 ครั้ง (แน่นอน) แต่ พรุ่งนี้|ขา|จะ ครั้งเดียว (typo)
                // (เลี่ยง เข้าไป — เป็นคำเดียวในพจนานุกรม จึงไม่เกิดคู่ เดิน|เข้า ให้นับ)
                var ckw = new ThaiContextChecker(words);
                for (int k = 0; k < 5; k++) ckw.AddCorpus("พรุ่งนี้ข้าจะกลับมา");
                ckw.AddCorpus("พรุ่งนี้ขาจะกลับมา");
                var ww = ckw.CheckWrongWord("พรุ่งนี้ขาจะกลับมา");
                Check("ctx word: ขา → ข้า เมื่อคลังยืนยันบริบท (≥5) และคำเดิมแทบไม่เคยอยู่ (≤1)",
                    ww.Count == 1 && ww[0].Wrong == "ขา" && ww[0].Suggested == "ข้า" && ww[0].Kind == SuggestionKind.WrongWord,
                    string.Join(" ", ww.Select(s => s.Wrong + ">" + s.Suggested)));
                Check("ctx word: ประโยคปกติในคลังไม่ถูกรายงาน", ckw.CheckWrongWord("พรุ่งนี้ข้าจะกลับมา").Count == 0);
                var ckFew = new ThaiContextChecker(words);
                for (int k = 0; k < 4; k++) ckFew.AddCorpus("พรุ่งนี้ข้าจะกลับมา");
                Check("ctx word: หลักฐานไม่พอ (4 < 5) = ไม่เสนอ", ckFew.CheckWrongWord("พรุ่งนี้ขาจะกลับมา").Count == 0);
                var ckPair = new ThaiContextChecker(words, CheckAllow.Parse(new[] { "ขา>ข้า" }));
                for (int k = 0; k < 5; k++) ckPair.AddCorpus("พรุ่งนี้ข้าจะกลับมา");
                Check("ctx word: คู่ใน check_allow ไม่ถูกเสนอ", ckPair.CheckWrongWord("พรุ่งนี้ขาจะกลับมา").Count == 0);
                var ckNa = new ThaiContextChecker(words);
                for (int k = 0; k < 5; k++) ckNa.AddCorpus("ไปกันเถอะนะเพื่อน");
                Check("ctx word: คำลงท้ายที่ใช้ได้ทั้งสองรูป (น่ะ/นะ) ไม่ถูกเสนอ", ckNa.CheckWrongWord("ไปกันเถอะน่ะเพื่อน").Count == 0);

                // กรองด้วยต้นฉบับอังกฤษ (พอร์ต en_sense.py ของ W3)
                Check("en: ความหมายของคำเดิมอยู่ในอังกฤษ → คำเดิมถูก (ขา + legs)",
                    ThaiEnglishSense.RuledOut("ขา", "ข้า", "wherever our legs carry us"));
                Check("en: ความหมายของคำที่เสนอไม่มีในอังกฤษ → ไม่มีหลักฐานให้เปลี่ยน (รุก→ลุกขึ้น ไม่มี stand/rise)",
                    ThaiEnglishSense.RuledOut("รุกขึ้น", "ลุกขึ้น", "push north"));
                Check("en: อังกฤษยืนยันคำที่เสนอ = ยังเสนอ", !ThaiEnglishSense.RuledOut("รุกขึ้น", "ลุกขึ้น", "He rose and left"));
                Check("en: ไม่มีอังกฤษ / คำนอกรายการ = ไม่ตัดสิน",
                    !ThaiEnglishSense.RuledOut("ขา", "ข้า", null) && !ThaiEnglishSense.RuledOut("กข", "คง", "anything"));
                Check("en: คำสั้น ≤3 ต้องจบคำ (pa ไม่ใช่ past)", !ThaiEnglishSense.RuledOut("พ่อ", "พอ", "in the past it was enough") &&
                    ThaiEnglishSense.RuledOut("พ่อ", "พอ", "ask your pa"));
                Check("en: คำใน Senses อยู่กลางช่วงคำได้ (แรก ใน แต่แรก) แต่ไม่ใช่ถ้ามีสระ/วรรณยุกต์ต่อท้าย (หมู ใน หมู่)",
                    ThaiEnglishSense.RuledOut("แต่แลก", "แต่แรก", "for a price") && !ThaiEnglishSense.RuledOut("หมู่", "หมู", "pigs"));
                var ckEn = new ThaiContextChecker(words);
                for (int k = 0; k < 5; k++) ckEn.AddCorpus("พรุ่งนี้ข้าจะกลับมา");
                Check("en: CheckWrongWord ตัดข้อเสนอเมื่ออังกฤษยืนยันคำเดิม",
                    ckEn.CheckWrongWord("พรุ่งนี้ขาจะกลับมา", "my legs will carry me back").Count == 0 &&
                    ckEn.CheckWrongWord("พรุ่งนี้ขาจะกลับมา", "I will be back").Count == 1);
                // เคสจริงของ W3: "รุกขึ้นเหนือ" = push north — กลไกสลับเสนอ ลุกขึ้น แต่ต้นฉบับไม่มี stand/rise
                Check("en: CheckRal ตัดข้อเสนอเมื่ออังกฤษไม่มีความหมายของคำที่เสนอ (รุกขึ้นเหนือ = push north)",
                    Ral("กองทัพรุกขึ้นเหนือ").Contains(">ลุกขึ้น") && ck.CheckRal("กองทัพรุกขึ้นเหนือ", "the army pushed north").Count == 0 &&
                    ck.CheckRal("กองทัพรุกขึ้นเหนือ", "the soldiers rose up").Count == 1, Ral("กองทัพรุกขึ้นเหนือ"));
            }

            Console.WriteLine("== คู่ไทย-อังกฤษสำหรับตัวตรวจคำผิด (csv แถว · po msgid) ==");
            {
                MispasteDetector.PickThaiEnglish(new[] { "item_01", "A sharp sword", "ดาบคม" }, out var th1, out var en1);
                Check("pair: csv ไทย = เซลล์ไทยมากสุด · อังกฤษ = เซลล์ละตินมากสุด", th1 == "ดาบคม" && en1 == "A sharp sword", en1 ?? "-");
                MispasteDetector.PickThaiEnglish(new[] { "1", "2" }, out var th2, out var en2);
                Check("pair: แถวไม่มีไทย = null", th2 == null && en2 == null);
                var poEntries = PoProcessor.ParseEntries("msgid \"\"\nmsgstr \"Content-Type: text/plain\"\n\nmsgid \"Hello\"\nmsgstr \"สวัสดี\"\n\n#~ msgid \"Old\"\n#~ msgstr \"เก่า\"\n");
                Check("pair: po คู่ msgid–msgstr ข้าม header และ #~ · บรรทัดของ msgstr",
                    poEntries.Count == 1 && poEntries[0].MsgId == "Hello" && poEntries[0].MsgStr == "สวัสดี" && poEntries[0].Line == 5,
                    poEntries.Count.ToString());
            }

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

            Console.WriteLine("== formats: json (--json-path) ==");
            {
                const string pj = "{\"menu.start\":\"เริ่มเกมใหม่\",\"items\":[{\"name\":\"ดาบเหล็ก\",\"desc\":\"ดาบคมกริบ\"},{\"name\":\"โล่ไม้\",\"desc\":\"โล่ธรรมดา\"}],\"deep\":{\"x\":{\"desc\":\"ลึกมากเลย\"}}}";
                Func<string, string> byPath = spec =>
                {
                    var pats = JsonPathPattern.ParseList(spec);
                    return JsonStringProcessor.Process(pj, s => "<" + s + ">", null, path => pats.Any(x => x.Matches(path)));
                };
                string a1 = byPath("/items/*/desc");
                Check("json-path: /items/*/desc แปลง desc ใน array ทุกตัว ไม่แตะ name/deep",
                    a1.Contains("\"<ดาบคมกริบ>\"") && a1.Contains("\"<โล่ธรรมดา>\"") && a1.Contains("\"ดาบเหล็ก\"") && a1.Contains("\"ลึกมากเลย\""), a1);
                string a2 = byPath("/items/1/name");
                Check("json-path: index ของ array นับถูก (element ที่ 2 เท่านั้น)",
                    a2.Contains("\"<โล่ไม้>\"") && a2.Contains("\"ดาบเหล็ก\"") && !a2.Contains("<ดาบคมกริบ>"), a2);
                string a3 = byPath("/menu.start");
                Check("json-path: key ที่มีจุดเขียนตรงได้ (ไม่ใช่ตัวคั่น)", a3.Contains("\"<เริ่มเกมใหม่>\"") && CountOccurrences(a3, "<") == 1, a3);
                string a4 = byPath("/**/desc");
                Check("json-path: ** = กี่ชั้นก็ได้", CountOccurrences(a4, "<") == 3 && a4.Contains("<ลึกมากเลย>"), a4);
                Check("json-path: หลาย pattern คั่นด้วย ,", CountOccurrences(byPath("/menu.start, /deep/x/desc"), "<") == 2);
                Check("json-path: escape ~1 = / ตาม RFC 6901",
                    JsonPathPattern.Parse("/a~1b").Matches(new[] { "a/b" }) && !JsonPathPattern.Parse("/a~1b").Matches(new[] { "a", "b" }));
                bool threw = false;
                try { JsonPathPattern.Parse("items/desc"); } catch (ArgumentException) { threw = true; }
                Check("json-path: ไม่ขึ้นต้นด้วย / = error", threw);
                string a5 = JsonStringProcessor.Process(pj, s => "<" + s + ">", k => k == "name", path => path.Count == 1 && path[0] == "menu.start");
                Check("json-path + json-keys: value ที่ตรงตัวใดตัวหนึ่ง", CountOccurrences(a5, "<") == 3, a5);
                const string arr = "[[\"ก\",\"ข\"],{\"k\":[1,{\"t\":\"ค\"}]}]";
                string a6 = JsonStringProcessor.Process(arr, s => "<" + s + ">", null, path => string.Join("/", path) == "1/k/1/t" || string.Join("/", path) == "0/1");
                Check("json-path: array ซ้อน/ตัวเลขใน array นับ index ถูก", a6 == "[[\"ก\",\"<ข>\"],{\"k\":[1,{\"t\":\"<ค>\"}]}]", a6);
            }

            Console.WriteLine("== คอลัมน์ต้นฉบับ/คำแปลที่ระบุเอง (TableColumns) ==");
            {
                var t = new List<List<string>>
                {
                    new List<string> { "id", "en", "note", "th", "th_short" },
                    new List<string> { "q1", "Get {count} gold", "A very long translator note about the gold reward", "ได้ทอง {count}", "" },
                };
                var auto = TableColumns.Resolve(null, null, t[0]);
                MispasteDetector.PickThaiEnglish(t[1], out var aTh, out var aEn);
                Check("columns: ไม่ระบุ = เดาเอง (เดิม) — คอลัมน์ note ยาวกว่าถูกเลือกเป็นอังกฤษ",
                    auto.IsAuto && aEn == t[1][2] && auto.TranslationCells(t[1], 1).Single() == "ได้ทอง {count}");
                var named = TableColumns.Resolve("th,th_short", "en", t[0]);
                Check("columns: ชื่อ → index + ข้ามแถว header + เซลล์ว่างไม่นับ",
                    named.Source == 1 && named.FirstDataRow == 1 && named.TranslationCells(t[0], 0).Count == 0 &&
                    named.TranslationCells(t[1], 1).Single() == "ได้ทอง {count}" && named.SourceCell(t[1], out bool ex) == "Get {count} gold" && ex);
                var numeric = TableColumns.Resolve(null, "2", t[0]);
                Check("columns: เลข 1-based ไม่ข้าม header · คำแปลยังเดาเอง", numeric.Source == 1 && numeric.FirstDataRow == 0 && numeric.Translation == null);
                Func<Action, bool> throws = act => { try { act(); return false; } catch (ArgumentException) { return true; } };
                Check("columns: error — ชื่อไม่พบ · เลข 0 · ต้นฉบับซ้ำคำแปล · หลายคอลัมน์ต้นฉบับ",
                    throws(() => TableColumns.Resolve(null, "bogus", t[0])) && throws(() => TableColumns.Resolve("0", null, t[0])) &&
                    throws(() => TableColumns.Resolve("th", "th", t[0])) && throws(() => TableColumns.Resolve(null, "en,note", t[0])));
                // #16 (REVIEW-fa8176c): เลขเกินตารางเคยถูกข้ามทุกแถว → เทียบ 0 คู่ exit 0 เหมือนผ่าน
                Check("columns #16: เลขคอลัมน์เกินแถวที่กว้างที่สุด = error ทั้งต้นฉบับและคำแปล · เท่าความกว้างพอดีได้",
                    throws(() => TableColumns.Resolve(null, "999", t[0], 5)) && throws(() => TableColumns.Resolve("6", null, t[0], 5)) &&
                    TableColumns.Resolve("5", "1", t[0], 5).Source == 0 && TableColumns.Resolve(null, "999", t[0], 0).Source == 998);
                Check("csv #16: --columns เลขเกินตารางในโหมดแปลง = error (เดิมไม่แปลงอะไรแล้วจบปกติ)",
                    throws(() => CsvProcessor.Process("a,b\n1,2\n", new CsvOptions { ColumnIndices = new List<int> { 3 } }, x => x)) &&
                    CsvProcessor.Process("a,b\n1,2,3\n", new CsvOptions { ColumnIndices = new List<int> { 3 } }, x => x + "!") == "a,b\n1,2,3!\n");
                var row2 = new List<string> { "q2", "", "x", "ทดสอบ" };
                Check("columns: ต้นฉบับที่ระบุว่าง/เกินแถว = null แต่ explicit (ไม่เดาแทน)",
                    named.SourceCell(row2, out bool ex2) == null && ex2 && TableColumns.Resolve(null, "9", null).SourceCell(row2, out _) == null);

                // --mispaste: picker เลือกคอลัมน์เอง — ตารางที่คอลัมน์หมายเหตุอังกฤษยาวทำให้กฎ ratio ฟ้องผิด
                string longTh = "ข้อความไทยที่ยาวพอสมควรสำหรับทดสอบอัตราส่วนความยาว";
                var mrows = new List<List<string>>
                {
                    new List<string> { "en", "note", "th" },
                    new List<string> { "Short text here", string.Concat(Enumerable.Repeat("a long translator note about context ", 12)), longTh },
                };
                int autoHits = MispasteDetector.Detect(mrows).Count;
                var msel = TableColumns.Resolve("th", "en", mrows[0]);
                int explicitHits = MispasteDetector.Detect(mrows, (IReadOnlyList<string> row, int ri, out string? th, out string? en) =>
                {
                    var cells = msel.TranslationCells(row, ri);
                    th = cells.Count > 0 ? cells[0] : null;
                    en = th == null ? null : msel.SourceCell(row, out _);
                }).Count;
                Check("mispaste: picker ใช้คอลัมน์ที่ระบุ (note ยาวไม่ทำให้ฟ้องผิด)", autoHits == 1 && explicitHits == 0, autoHits + "/" + explicitHits);
            }

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

            Console.WriteLine("== file safety ==");
            // CSV: record ว่างท้ายไฟล์ที่เปิด-ปิด quote แล้ว ไม่มี newline ปิด ต้องไม่หาย
            var tCsv = CsvProcessor.ParseTable("thai\r\n\"\"", ',');
            Check("csv: record ว่างท้ายไฟล์ (\"\") ไม่หาย", tCsv.Count == 2 && tCsv[1].Count == 1 && tCsv[1][0] == "", tCsv.Count.ToString());
            Check("csv: ผลลัพธ์คงจำนวน record",
                CsvProcessor.Process("thai\r\n\"\"", new CsvOptions { ColumnIndices = new List<int> { 1 } }, s => s).Replace("\r\n", "\n") == "thai\n\"\"");
            var tCsv2 = CsvProcessor.ParseTable("a,b\n", ',');
            Check("csv: newline ท้ายไฟล์ยังไม่สร้าง record ว่างเกิน (regression)", tCsv2.Count == 1);

            // Encoding: ไม่ใช่ UTF-8 และไม่มี BOM → ปฏิเสธ ไม่ใช่ยอมแทนอักขระเงียบๆ
            try
            {
                TextFileEncoding.Decode(new byte[] { 0xBE, 0xD4, 0xB4, 0xD2, 0xCB }); // CP874 "ทดสอบ" แบบไม่มี BOM
                Check("encoding: CP874 ไม่มี BOM ต้องโยน IOException", false);
            }
            catch (IOException) { Check("encoding: CP874 ไม่มี BOM ต้องโยน IOException", true); }
            catch (Exception ex) { Check("encoding: CP874 ไม่มี BOM ต้องโยน IOException", false, ex.GetType().Name); }
            var utf8 = TextFileEncoding.Decode(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("ทดสอบ")).ToArray());
            Check("encoding: UTF-8 BOM อ่านได้ คง BOM", utf8.Text == "ทดสอบ" && utf8.Enc.GetPreamble().Length == 3);
            var plain = TextFileEncoding.Decode(Encoding.UTF8.GetBytes("ทดสอบ abc"));
            Check("encoding: UTF-8 ไม่มี BOM อ่านได้ปกติ", plain.Text == "ทดสอบ abc" && plain.Enc.GetPreamble().Length == 0);
            var utf16 = TextFileEncoding.Decode(new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("ทดสอบ")).ToArray());
            Check("encoding: UTF-16 LE BOM อ่านได้", utf16.Text == "ทดสอบ");
            // #5 (ตรวจ 2026-11-26): มี BOM แต่ข้อมูลเสีย เดิมถูกแทน U+FFFD เงียบๆ แล้ว exit 0 → ต้องปฏิเสธ
            foreach (var bad in new (string Name, byte[] Bytes)[]
            {
                ("UTF-8 BOM + byte FF", new byte[] { 0xEF, 0xBB, 0xBF, 0x61, 0x62, 0x63, 0xFF }),
                ("UTF-16 LE BOM + byte ค้างท้าย", new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x01 }),
                ("UTF-16 BE BOM + byte ค้างท้าย", new byte[] { 0xFE, 0xFF, 0x00, 0x41, 0x01 }),
                ("UTF-16 LE BOM + surrogate เดี่ยว", new byte[] { 0xFF, 0xFE, 0x00, 0xD8, 0x41, 0x00 }),
                ("UTF-8 BOM + ไทยตัดครึ่ง", new byte[] { 0xEF, 0xBB, 0xBF, 0xE0, 0xB8 }),
            })
            {
                string name = "encoding: " + bad.Name + " ต้องโยน IOException";
                try { TextFileEncoding.Decode(bad.Bytes); Check(name, false); }
                catch (IOException) { Check(name, true); }
                catch (Exception ex) { Check(name, false, ex.GetType().Name); }
            }
            var utf16be = TextFileEncoding.Decode(new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes("ทดสอบ")).ToArray());
            Check("encoding: UTF-16 BE BOM อ่านได้", utf16be.Text == "ทดสอบ");
            Check("encoding: ไฟล์มีแค่ BOM อ่านได้เป็นว่าง",
                TextFileEncoding.Decode(new byte[] { 0xEF, 0xBB, 0xBF }).Text == "" &&
                TextFileEncoding.Decode(new byte[] { 0xFF, 0xFE }).Text == "");

            // #6 (ตรวจ 2026-11-26): field ที่ไม่แตะต้องเป็นไบต์เดิม — เดิม "001" ถูกถอด quote และ --check ฟ้องผิด
            var thaiCol = new CsvOptions { ColumnNames = new List<string> { "thai" } };
            string quoted = "id,thai\n\"001\",\"Hello\"\n";
            Check("csv: ไม่มีอะไรเปลี่ยน = ไบต์เดิมทั้งไฟล์ (quote คงอยู่)",
                CsvProcessor.Process(quoted, thaiCol, s => seg.InsertZwsp(s)) == quoted,
                CsvProcessor.Process(quoted, thaiCol, s => seg.InsertZwsp(s)));
            string mixed = "\"id\",thai,note\r\n\"001\",\"สวัสดีครับท่าน\",\"a\"\"b\"\r\n002,ไปไหนมา,  x \r\n";
            string mixedOut = CsvProcessor.Process(mixed, thaiCol, s => seg.InsertZwsp(s));
            Check("csv: field ที่ไม่แตะคงข้อความดิบ (quote/escape/ช่องว่าง)",
                mixedOut.StartsWith("\"id\",thai,note\r\n\"001\",") && mixedOut.Contains(",\"a\"\"b\"\r\n002,")
                && mixedOut.EndsWith(",  x \r\n"), mixedOut);
            Check("csv: field ที่เปลี่ยนคงสไตล์ quote เดิม",
                mixedOut.Contains(",\"สวัสดี" ) && mixedOut.Contains(",ไป") && !mixedOut.Contains(",\"ไป"), mixedOut);
            Check("csv: ลบ ZWSP แล้วได้ต้นฉบับทุกไบต์", mixedOut.Replace(zw.ToString(), "") == mixed);
            string multi = "thai,en\n\"บรรทัดแรก\nบรรทัดสอง\",\"x,y\"\n\"เปิดค้าง";
            Check("csv: identity transform = ไบต์เดิม (หลายบรรทัด/quote ค้างท้ายไฟล์)",
                CsvProcessor.Process(multi, new CsvOptions { ColumnIndices = new List<int> { 1, 2 } }, s => s) == multi);

            Console.WriteLine("== placeholders ==");
            {
                Func<string, string, string> cmp = (en, th) =>
                {
                    var m = PlaceholderChecker.Compare(en, th);
                    return m == null ? "ok" : "-" + string.Join("|", m.Missing) + " +" + string.Join("|", m.Extra);
                };
                Check("ph: ตรงกัน (สลับตำแหน่งได้)", cmp("Hello %1$s and %2$s", "สวัสดี %2$s และ %1$s") == "ok");
                Check("ph: {0} หาย", cmp("You found {0} gold", "คุณพบทอง") == "-{0} +");
                Check("ph: แปลชื่อ placeholder เป็นไทย = {name} หาย", cmp("Hi {name}", "สวัสดี {ชื่อ}") == "-{name} +");
                Check("ph: สะกดชื่อ placeholder ผิด = หาย+เกิน", cmp("Hi {name}", "สวัสดี {nmae}") == "-{name} +{nmae}");
                Check("ph: tag ปิดผิด", cmp("<b>Warning</b>", "<b>คำเตือน<b>") == "-</b> +<b>");
                Check("ph: tag สะกดผิด", cmp("<color=red>Fire</color>", "<colour=red>ไฟ</color>") == "-<color=red> +<colour=red>");
                Check("ph: นับจำนวนซ้ำ", cmp("HP: %d/%d", "พลังชีวิต: %d") == "-%d +");
                Check("ph: %% และ 100% sure ไม่ใช่ placeholder", cmp("100% sure, 50%% off", "มั่นใจ 100% ลด 50%%") == "ok");
                Check("ph: printf width/precision/length", PlaceholderChecker.Describe(PlaceholderChecker.Extract("%5.2f %-3d %ld %llu %.*s %@")) == "%5.2f, %-3d, %ld, %llu, %.*s, %@");
                Check("ph: ZWSP ที่แทรกแล้วไม่มีผล", cmp("Deals {0} damage to <i>{1}</i>",
                    seg.InsertZwsp("สร้างความเสียหาย {0} แก่ <i>{1}</i> ทันที")) == "ok");
                Check("ph: [วงเล็บเหลี่ยม] ไม่นับ", cmp("[Press E] to open", "[กด E] เพื่อเปิด") == "ok");
                // กฎจากคลังชุมชน W3 แบบดิบ 94k คู่ (2026-11-26): 2,527 จุด → 312 จุดที่เกือบทั้งหมดเป็นแท็กพังรูปจริงในข้อมูลดิบ
                Check("ph: <br> เพิ่ม/ลดเพื่อจัดบรรทัดไม่นับ", cmp("Line one<br>line two", "บรรทัดหนึ่ง<BR>บรรทัด<br/>สอง") == "ok");
                Check("ph: <br > / < br> รูปเพี้ยนยังนับ", cmp("A<br>B", "ก<br >ข< br>ค") == "- +<br >|< br>");
                Check("ph: แท็กแทรกช่องว่าง (เกมไม่รู้จัก) จับได้", cmp("Press <<GUI_Select> to buy", "กด << GUI_Select > เพื่อซื้อ") == "-<<GUI_Select> +<< GUI_Select >");
                Check("ph: แท็ก <br ไม่ปิดกินข้อความ จับได้", cmp("Item", "<brไอเทมของ<br>").StartsWith("- +<brไอเทม"));
                Check("ph: {ถ้อยคำ} ไม่ใช่ placeholder (W3 Elder Speech/ท่าทาง)",
                    cmp("{shakes his head} No.", "{ส่ายหัว} ไม่") == "ok" && cmp("{Hrrrr..} Fine.", "{เฮ้อออ} ก็ได้") == "ok" &&
                    cmp("{*Whistles*}", "{*ผิวปาก*}") == "ok");
                Check("ph: {placeholder} แบบต่าง ๆ ยังนับ", PlaceholderChecker.Describe(PlaceholderChecker.Extract(
                    "{0} {name} {0:N2} {player.name} {color=#f00} {/color} {{ }")) == "{0}, {name}, {0:N2}, {player.name}, {color=#f00}, {/color}");
                Check("ph: ตัวปิดต้องอยู่บรรทัดเดียวกัน", PlaceholderChecker.Extract("a {b\nc} d").Count == 0);
                Check("ph: ต้นฉบับต้องดูเป็นถ้อยคำ", PlaceholderChecker.LooksLikeSource("Deals {0} damage") &&
                    PlaceholderChecker.LooksLikeSource("{0}") && !PlaceholderChecker.LooksLikeSource("item_sword_01") &&
                    !PlaceholderChecker.LooksLikeSource("ดาบ {0}") && !PlaceholderChecker.LooksLikeSource("Start"));
                // รีวิว v0.12.0 (#14–#15): ของครบแต่โครงสร้างผิด
                Func<string, string, string> prob = (en, th) =>
                {
                    var m = PlaceholderChecker.Compare(en, th);
                    return m == null ? "ok" : string.Join(" | ", m.Problems);
                };
                Check("ph#14: printf ไม่ระบุตำแหน่งสลับชนิด = ผิด", prob("Name %s count %d", "จำนวน %d ชื่อ %s").StartsWith("printf ไม่ระบุตำแหน่ง"),
                    prob("Name %s count %d", "จำนวน %d ชื่อ %s"));
                Check("ph#14: printf ระบุตำแหน่งสลับได้", prob("Name %1$s count %2$d", "จำนวน %2$d ชื่อ %1$s") == "ok");
                Check("ph#14: {n} สลับได้", prob("{0} hit {1}", "{1} ถูก {0} ตี") == "ok");
                Check("ph#14: printf เรียงเดิมผ่าน", prob("%s has %d gold", "%s มีทอง %d") == "ok");
                Check("ph#14: ปนแบบระบุตำแหน่งกับ {n} ไม่กระทบ", prob("%s: {0} and %d", "{0} กับ %d ของ %s") .StartsWith("printf"));
                Check("ph#15: แท็กปิดข้ามชั้น = ผิด", prob("<b><i>Hello</i></b>", "<b><i>สวัสดี</b></i>") == "แท็กปิดข้ามชั้น: </b> ปิดก่อน </i>",
                    prob("<b><i>Hello</i></b>", "<b><i>สวัสดี</b></i>"));
                Check("ph#15: ย้ายคู่แท็กทั้งคู่ได้", prob("<b>A</b> and <i>B</i>", "<i>ข</i> กับ <b>ก</b>") == "ok");
                Check("ph#15: แท็กชั้นในไม่ปิด (sprite/color) ไม่ฟ้อง",
                    prob("<b>x<sprite=1>y</b>", "<b>ก<sprite=1>ข</b>") == "ok" && prob("<b><color=red>x</b>", "<b><color=red>ก</b>") == "ok");
                Check("ph#15: แท็กปุ่ม W3 <<GUI> ไม่ฟ้อง", prob("<i>Press <<GUI_Select></i>", "<i>กด <<GUI_Select></i>") == "ok");
                Check("ph#15: ต้นฉบับพังแบบเดียวกัน ไม่ใช่ความผิดคำแปล", prob("<b><i>A</b></i>", "<b><i>ก</b></i>") == "ok");
                Check("ph#15: จับคู่ชื่อแท็กไม่สนตัวพิมพ์", prob("<B><i>A</i></b>", "<B><i>ก</i></b>") == "ok" &&
                    prob("<B><i>A</i></b>", "<B><i>ก</b></i>") == "แท็กปิดข้ามชั้น: </b> ปิดก่อน </i>");
                Check("ph: Describe รวมตัวซ้ำ", PlaceholderChecker.Describe(new[] { "{0}", "<b>", "{0}" }) == "{0} ×2, <b>");
                // ตัวตัดคำใช้นิยามเดียวกัน — ไม่แทรกในส่วนควบคุมที่ตัวตรวจนับ
                string wrapped = seg.InsertZwsp("ยินดีต้อนรับ{player_name}สู่<color=red>ดินแดนมังกร</color>");
                Check("ph: segmenter ไม่แทรกใน {…}/<…> ที่ตัวตรวจนับ",
                    PlaceholderChecker.Extract(wrapped).SequenceEqual(new[] { "{player_name}", "<color=red>", "</color>" }), wrapped);
            }

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

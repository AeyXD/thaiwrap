using System;
using System.Text;

namespace ThaiWrap
{
    /// <summary>ผลสแกนหารูปพิมพ์ผิดในอินพุต (นับเป็น run — ััั รวมเป็นจุดเดียว)</summary>
    public sealed class ThaiTextIssues
    {
        // ---- รูปที่ Normalize แก้ได้ (เรียกใช้ด้วย --normalize) ----

        /// <summary>จำนวน run ของ เ ติดกันตั้งแต่สองตัว (เเ ซึ่งตั้งใจพิมพ์ แ)</summary>
        public int DoubleE;

        /// <summary>จำนวน run ของมาร์ค (สระจ่อย/วรรณยุกต์/ไม้ทัด) ที่ซ้ำตัวเอง</summary>
        public int DoubledMark;

        /// <summary>ลำดับมาร์คผิด: วรรณยุกต์มาก่อนสระบน รวมแบบสอดแทรก (ท่ี ที่ี่ ทั่ั่ง ดิ้ิ้น)</summary>
        public int MarkOrder;

        /// <summary>ํ (+วรรณยุกต์คั่น) + า แทน ำ เช่น นํ้า คํานึง ดํ่า</summary>
        public int SaraAm;

        /// <summary>วรรณยุกต์หลงหลังสระหน้า ทู่กับที่พยัญชนะมีวรรณยุกต์ถูกอยู่แล้ว (ไ่ม่→ไม่ ไ่ด้→ได้)</summary>
        public int StrayTone;

        public int Total
        {
            get { return DoubleE + DoubledMark + MarkOrder + SaraAm + StrayTone; }
        }

        // ---- รูปที่กำกวม แก้อัตโนมัติไม่ได้ ต้องตรวจด้วยตาที่ต้นทาง ----

        /// <summary>สระบนหรือสระล่างต่างตัวติดกัน (ิี ืิ ุู) — คนละเสียง ไม่รู้ว่าจะเก็บตัวไหน</summary>
        public int MixedVowels;

        /// <summary>วรรณยุกต์สองตัวต่างตัวติดกัน (่้ เสื้่อ) — ทิ้งตัวไหนได้คำคนละความหมาย</summary>
        public int MixedTones;

        /// <summary>สระหน้า+มาร์คเลยโดยไม่มีพยัญชนะคั่น ที่ย้าย/ลบไม่ได้ (เ้วย ไ่ก) — ตำแหน่งวรรณยุกต์ที่ถูกต้องต้องดูโครงสร้างพยางค์</summary>
        public int LeadingVowelMark;

        public int WarningsTotal
        {
            get { return MixedVowels + MixedTones + LeadingVowelMark; }
        }
    }

    /// <summary>
    /// ยุบรูปพิมพ์ผิดที่**เป็นไปไม่ได้ทางภาษาไทย** (ตรวจทานแล้วว่าปลอดภัยกับข้อความทุกชิ้น)
    /// คำผิดเหล่านี้ทำให้คำกลายเป็น unknown แล้ว merge pass เกาะกินคำถูกข้างเคียง
    /// จนจุดตัดหลุดทั้งย่อหน้า ไม่ใช่แค่คำนั้นคำเดียว · editor/Excel render glyph ซ้อนให้
    /// "ดูถูกต้อง" จึงต้องตรวจที่ระดับ codepoint เท่านั้น
    ///
    /// กฎทั้งหมดมาจากสิ่งที่เจอจริงใน production ของ ThaiW3Setup (แพตช์ซับไทย Witcher 3
    /// คลังแปล 96,882–176,230 id) — สถิติในวงเล็บคือจำนวนจุดที่พบในคลังจริง:
    ///
    /// 1. เเ → แ (1,945 จุด) — คำไทยไม่ลงท้ายด้วยสระเอ สระหน้าต้องมีพยัญชนะตามเสมอ
    ///    จึงแทนคู่นั้นได้เลย ทีละคู่จากซ้ายไม่ให้ซ้อนทับ (mirror ของ str.replace)
    ///    [find_typos.py: fix_double_e]
    /// 2. มาร์คซ้ำตัวเอง ยุบเหลือตัวเดียว (415 จุด: สวััสดี เป็็็น กระดููก)
    ///    [find_typos.py: MARK_RUN]
    /// 3. ลำดับมาร์คผิด (108 จุด: มาท่ีนี่ ที่ี่ ทั่ั่ง ดิ้ิ้น) — ไทยที่ถูกต้องเรียงสระบนก่อน
    ///    วรรณยุกต์เสมอ และหนึ่ง cluster มีสระบน/วรรณยุกต์ได้ slot ละตัว จึงจัด run ของ
    ///    มาร์คใหม่ (stable partition) แล้วยุบตัวซ้ำ แล้วเหลือรูปเดียวที่เป็นไปได้เสมอ
    /// 4. ํ (+วรรณยุกต์คั่น) + า → (วรรณยุกต์) + ำ (156 จุด: นํ้า คํานึง ดํ่า) — Unicode เอง
    ///    ก็ถือว่าเป็น canonical decomposition ของ U+0E33 (NFC ก็ compose ให้)
    ///    ฟอนต์ render ให้ดูเหมือน ำ ปกติจึงรอดสายตาคนมาทั้งหมด
    /// 5. วรรณยุกต์หลงหลังสระหน้า โดยพยัญชนะถัดไปมีวรรณยุกต์ถูกอยู่แล้ว → ลบตัวหลงทิ้ง
    ///    (ไ่ม่→ไม่ ไ่ด้→ได้) — สระหน้าไม่มีทางรับวรรณยุกต์ และตัวถูกมีอยู่แล้วจึงไม่กำกวม
    ///
    /// เจตนาไม่แตะ (ตามที่ตรวจทานกับทีมแปล W3): า ลากเสียง (อาาา) · พยัญชนะซ้ำ (ฮั่กกกก) ·
    /// ฺ ํ ที่ไม่นำหน้า า (ยังไม่มีข้อสรุป — ํ ใช้จริงในบาลี/สันสกฤต) · สระบน/ล่างต่างตัวซ้อน
    /// (ิี ุู — คนละคำ) · วรรณยุกต์สองตัวต่างตัว (เสื้่อ — ทิ้งตัวไหนได้คำคนละความหมาย) ·
    /// สระหน้า+วรรณยุกต์ที่ต้องย้าย (ไ่ก→ไก่, เ้วย→เว้ย — ตำแหน่งปลายทางขึ้นกับกลุ่มพยัญชนะ
    /// ไ่หล = ไหล่ ไม่ใช่ ไห่ล) — สามกลุ่มท้ายนี้ Count นับเป็นคำเตือนให้ตรวจด้วยตา ·
    /// คำสะกดเฉพาะคำ (ปฎิ→ปฏิ ทึ่→ที่) และการสลับ ร↔ล (บุกลุก) เป็นข้อมูล/บริบทของแต่ละ
    /// โปรเจกต์ ไม่อยู่ในเครื่องมือกลาง (ดู find_typos.py WORD_FIXES และ find_ral_swaps.py
    /// ของ ThaiW3Setup ซึ่งทำเป็น "ข้อเสนอ" ให้ทีมแปลยืนยันเท่านั้น)
    /// </summary>
    public static class ThaiNormalizer
    {
        // ั ิ ี ึ ื ุ ู ็ ่ ้ ๊ ๋ ์ — มาร์คที่ซ้ำตัวเองไม่ได้ในภาษาไทย (ชุด DOUBLE_MARKS
        // ของ find_typos.py: ไม่รวม า เพราะลากเสียงถูกต้อง และไม่รวม ฺ ํ ๎)
        private const string FixableMarks =
            "\u0E31\u0E34\u0E35\u0E36\u0E37\u0E38\u0E39\u0E47\u0E48\u0E49\u0E4A\u0E4B\u0E4C";

        private const char VowelE = '\u0E40';   // เ
        private const char VowelAe = '\u0E41';  // แ
        private const char Nikhahit = '\u0E4D'; // ํ
        private const char SaraAa = '\u0E32';   // า
        private const char SaraAm = '\u0E33';   // ำ

        private static bool IsVowelAbove(char c) =>
            c == '\u0E31' || (c >= '\u0E34' && c <= '\u0E37'); // ั ิ ี ึ ื

        private static bool IsVowelBelow(char c) => c == '\u0E38' || c == '\u0E39'; // ุ ู

        private static bool IsTone(char c) => c >= '\u0E48' && c <= '\u0E4B'; // ่ ้ ๊ ๋

        private static bool IsFixableMark(char c) => FixableMarks.IndexOf(c) >= 0;

        // ตัวอักษรที่เป็นฐานของ cluster ได้ (ก-ฬ — ไม่รวมสระหน้า/ะ/ำ/ฯ/ๆ/ๅ และมาร์คทั้งหมด)
        private static bool IsBaseConsonant(char c) => c >= '\u0E01' && c <= '\u0E2A';

        /// <summary>
        /// ยุบรูปพิมพ์ผิดตามกฎข้างต้น — คืน instance เดิมเมื่อไม่มีอะไรแก้ · ทำงานกับ string
        /// ใดๆ ไม่จำกัดภาษาไทย: อักขระอื่นผ่านตรงๆ และไม่แตะ ZWSP ที่มีอยู่
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string t = FixDoubleE(text);
            t = ComposeSaraAm(t);
            t = CanonicalizeMarkRuns(t);
            t = RemoveStrayToneAfterLeadingVowel(t);
            return t;
        }

        /// <summary>นับรูปพิมพ์ผิดในอินพุต (สแกนตรงๆ ไม่แก้ข้อความ) — ใช้ทำคำเตือน/--stats</summary>
        public static ThaiTextIssues Count(string text)
        {
            var issues = new ThaiTextIssues();
            if (string.IsNullOrEmpty(text)) return issues;
            int i = 1;
            int n = text.Length;
            while (i < n)
            {
                char cur = text[i];
                char prev = text[i - 1];

                // เเ / มาร์คซ้ำ — นับเป็น run ละจุด
                if (prev == cur && (cur == VowelE || IsFixableMark(cur)))
                {
                    if (cur == VowelE) issues.DoubleE++;
                    else issues.DoubledMark++;
                    while (i + 1 < n && text[i + 1] == cur) i++;
                    i++;
                    continue;
                }

                // ลำดับมาร์คผิด: วรรณยุกต์มาก่อนสระบน (ทั้งแบบสลับและสอดแทรก)
                if (IsTone(prev) && IsVowelAbove(cur))
                {
                    issues.MarkOrder++;
                    i++;
                    continue;
                }

                // ํ (+วรรณยุกต์) + า = ำ แยกสะกด
                if (prev == Nikhahit && (cur == SaraAa || IsTone(cur)))
                {
                    // ยืนยันว่าจบด้วย า (ถ้า ํ+วรรณยุกต์แล้วไม่ใช่ า คือรูปอื่น ไม่นับ)
                    if (cur == SaraAa || (i + 1 < n && text[i + 1] == SaraAa)) issues.SaraAm++;
                    i++;
                    continue;
                }

                // สระหน้า + มาร์ค
                if (IsLeadingVowel(prev) && (IsFixableMark(cur) || cur == Nikhahit))
                {
                    if (IsTone(cur) && StrayToneFixable(text, i)) issues.StrayTone++;
                    else issues.LeadingVowelMark++;
                    i++;
                    continue;
                }

                // เคสกำกวม: สระบน/ล่างต่างตัวติดกัน และวรรณยุกต์สองตัวต่างตัว
                if (IsVowelAbove(cur) && IsVowelAbove(prev) && cur != prev) issues.MixedVowels++;
                else if (IsVowelBelow(cur) && IsVowelBelow(prev) && cur != prev) issues.MixedVowels++;
                else if (IsTone(cur) && IsTone(prev) && cur != prev) issues.MixedTones++;

                i++;
            }
            return issues;
        }

        private static bool IsLeadingVowel(char c) => c >= '\u0E40' && c <= '\u0E44'; // เ แ โ ใ ไ

        /// <summary>ไ่ม่ แบบที่ลบได้: [สระหน้า][วรรณยุกต์หลง] แล้วพยัญชนะ (+สระบน) มีวรรณยุกต์ถูกอยู่</summary>
        private static bool StrayToneFixable(string text, int tonePos)
        {
            int k = tonePos + 1;
            if (k >= text.Length || !IsBaseConsonant(text[k])) return false;
            k++;
            if (k < text.Length && IsVowelAbove(text[k])) k++; // ไ่กี้ → ไกี้
            return k < text.Length && IsTone(text[k]);
        }

        // ---- pass 1: เเ → แ (ทีละคู่จากซ้าย ไม่ให้ซ้อนทับ) ----

        private static string FixDoubleE(string text)
        {
            // pre-check ต้องสแกนหา "คู่" — ห้ามดูแค่ เ ตัวแรกของข้อความ
            // (ข้าเริ่มโกรธเเล้วนะ: เ แรกอยู่ใน เริ่ม แต่ เเ อยู่ท้ายบรรทัด)
            bool found = false;
            for (int k = 0; k + 1 < text.Length && !found; k++)
                if (text[k] == VowelE && text[k + 1] == VowelE) found = true;
            if (!found) return text;
            var sb = new StringBuilder(text.Length);
            int i = 0, n = text.Length;
            while (i < n)
            {
                if (text[i] == VowelE && i + 1 < n && text[i + 1] == VowelE)
                {
                    sb.Append(VowelAe);
                    i += 2;
                }
                else
                {
                    sb.Append(text[i]);
                    i++;
                }
            }
            return sb.ToString();
        }

        // ---- pass 2: ํ (+วรรณยุกต์คั่น) + า → (วรรณยุกต์) + ำ ----

        private static string ComposeSaraAm(string text)
        {
            int at = text.IndexOf(Nikhahit);
            if (at < 0) return text;
            var sb = new StringBuilder(text.Length);
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (c == Nikhahit)
                {
                    // ํ ตามด้วย า หรือ วรรณยุกต์+า → ำ (วรรณยุกต์ย้ายไปหน้า ำ ตามลำดับที่ถูก)
                    if (i + 1 < n && text[i + 1] == SaraAa)
                    {
                        sb.Append(SaraAm);
                        i += 2;
                        continue;
                    }
                    if (i + 2 < n && IsTone(text[i + 1]) && text[i + 2] == SaraAa)
                    {
                        sb.Append(text[i + 1]).Append(SaraAm);
                        i += 3;
                        continue;
                    }
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        // ---- pass 3: จัด run ของมาร์ค — สระบนก่อนวรรณยุกต์ + ยุบตัวซ้ำ ----

        private static string CanonicalizeMarkRuns(string text)
        {
            // ผลลัพธ์เปลี่ยนจริงเฉพาะเมื่อมีคู่มาร์คติดกันที่ซ้ำกันเอง หรือวรรณยุกต์นำหน้าสระบน —
            // ข้อความไทยปกติเกือบทุกชิ้นมีมาร์ค จึงต้องมี pre-check นี้กัน allocate โดยไม่จำเป็น
            bool needed = false;
            for (int i = 1; i < text.Length && !needed; i++)
            {
                char a = text[i - 1], b = text[i];
                if (IsFixableMark(a) && IsFixableMark(b) && (a == b || (IsTone(a) && IsVowelAbove(b)))) needed = true;
            }
            if (!needed) return text;

            var sb = new StringBuilder(text.Length);
            char[] run = new char[16];
            int i2 = 0, n = text.Length;
            while (i2 < n)
            {
                if (!IsFixableMark(text[i2])) { sb.Append(text[i2]); i2++; continue; }

                int len = 0;
                while (i2 + len < n && IsFixableMark(text[i2 + len])) len++;
                if (len > run.Length) run = new char[len]; // กัน run ยาวผิดปกติ (อุทาน/กลับมาทาง)
                for (int k = 0; k < len; k++) run[k] = text[i2 + k];
                AppendCanonicalRun(sb, run, len);
                i2 += len;
            }
            return sb.ToString();
        }

        /// <summary>
        /// จัด run ของมาร์คความยาว len: ถ้าทั้ง run เป็นสระบน+วรรณยุกต์ ให้แยกกลุ่ม stable
        /// (สระบนทั้งหมดก่อน ตามลำดับเดิม แล้ววรรณยุกต์ทั้งหมดตามลำดับเดิม) แล้วยุบตัวที่ซ้ำ
        /// ติดกันเหลือตัวเดียว — รูปที่ถูกต้องมาแต่ต้นผ่านไปเหมือนเดิม (สระบนอยู่หน้าวรรณยุกต์อยู่แล้ว)
        /// </summary>
        private static void AppendCanonicalRun(StringBuilder sb, char[] run, int len)
        {
            if (len == 1) { sb.Append(run[0]); return; }

            bool allCanon = true;
            for (int i = 0; i < len; i++)
                if (!(IsVowelAbove(run[i]) || IsTone(run[i]))) { allCanon = false; break; }

            char last = '\0';
            if (allCanon)
            {
                for (int i = 0; i < len; i++) // สระบนก่อน (stable)
                    if (IsVowelAbove(run[i]) && run[i] != last) { sb.Append(run[i]); last = run[i]; }
                last = '\0';
                for (int i = 0; i < len; i++) // วรรณยุกต์ทีหลัง (stable)
                    if (IsTone(run[i]) && run[i] != last) { sb.Append(run[i]); last = run[i]; }
            }
            else
            {
                for (int i = 0; i < len; i++) // มี ุ ู ็ ์ ปน — ยุบซ้ำเฉยๆ ไม่สลับที่
                    if (run[i] != last) { sb.Append(run[i]); last = run[i]; }
            }
        }

        // ---- pass 4: ลบวรรณยุกต์หลงหลังสระหน้า (เมื่อมีตัวถูกอยู่แล้ว) ----

        private static string RemoveStrayToneAfterLeadingVowel(string text)
        {
            int n = text.Length;
            bool found = false;
            for (int i = 0; i + 1 < n && !found; i++)
                if (IsLeadingVowel(text[i]) && IsTone(text[i + 1])) found = true;
            if (!found) return text;

            var sb = new StringBuilder(text.Length);
            int i2 = 0;
            while (i2 < n)
            {
                char c = text[i2];
                if (IsLeadingVowel(c) && i2 + 1 < n && IsTone(text[i2 + 1]) && StrayToneFixable(text, i2 + 1))
                {
                    sb.Append(c); // สระหน้า
                    i2 += 2;      // ข้ามวรรณยุกต์หลง
                    continue;
                }
                sb.Append(c);
                i2++;
            }
            return sb.ToString();
        }
    }
}

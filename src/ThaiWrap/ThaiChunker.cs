using System.Collections.Generic;

namespace ThaiWrap
{
    /// <summary>
    /// รวมคำติดกันเป็น "กลุ่มวลี" ก่อนแทรกตัวคั่น — สำหรับโหมด break char ที่**มองเห็นได้**
    /// (ช่องว่างจริงสำหรับเอนจินที่ตัดบรรทัดเฉพาะ ASCII space เช่น Aurora ของ Witcher 1):
    /// ถ้าเว้นช่องว่างทุกรอยคำ ข้อความจะอ่านเหมือน "เว้น วรรค ทุก คำ" — จึงเลือกจุดเว้นด้วย
    /// DP ชั้นที่สองบนลำดับคำ ให้ได้กลุ่มยาวไม่เกิน MaxChunk ตัวอักษร (default 12)
    /// และตกที่จุดที่ภาษาไทยเว้นวรรคกันจริง
    ///
    /// ต้นทุน (พอร์ตจาก ThaiW1Setup scripts/thai_wrap.py — พิสูจน์ในเกมจริง 27 ต.ค. 66, MIT):
    /// - เว้นหลังคำที่ต้องมีคำตาม (ผู้ การ ความ พวก ใน ไม่ จะ ...) และก่อนคำเกาะท้าย
    ///   (ได้ แล้ว ไป มา อยู่ ...) แพงมาก — จุดเว้นแบบนั้นผิดหลักภาษา
    /// - เว้นก่อนคำเชื่อม/บุพบท (และ แต่ ที่ ใน ของ ...) ถูกลง — จุดเว้นธรรมชาติ
    /// - กลุ่มยาวเกิน MaxChunk คิดค่าเพิ่มต่อตัวอักษร เกิน MaxChunk+8 ห้าม (ยกเว้นคำเดี่ยว)
    ///
    /// ความต่างจากต้นฉบับโดยตั้งใจ: W1 กลุ่มคำก่อน unknown ยาวถูกแยกเป็น cluster —
    /// ที่นี่ chunk ทำหลัง AddUnknown ดังนั้นก้อน unknown ยาวได้จุดตัดระดับ cluster เหมือน
    /// โหมด ZWSP (กันล้นกรอบข้อความ) และ base ตายตัวที่ 1.0 (โหมดทดลอง base&lt;0 ของ W1
    /// ไม่ได้เป็นค่าปริยายที่ใช้จริง)
    /// </summary>
    internal static class ThaiChunker
    {
        private const int ChunkHard = 8;          // กลุ่มหลายคำยาวได้ไม่เกิน maxChunk + 8 ตัวอักษร
        private const float BaseCost = 1.0f;
        private const float BindPenalty = 4.0f;
        private const float GoodBeforeBonus = 0.7f;
        private const float OverflowUnitCost = 0.4f;

        /// <summary>คำที่ต้องมีคำตาม — ห้ามเว้นวรรคหลังคำเหล่านี้</summary>
        private static readonly HashSet<string> BindNext = new HashSet<string>
        {
            "ผู้", "การ", "ความ", "นัก", "พวก", "ชาว", "หมู่", "เหล่า", "ช่าง", "ใน", "กับ", "แก่", "แห่ง",
            "จาก", "ของ", "ที่", "ซึ่ง", "อัน", "และ", "แต่", "หรือ", "เพื่อ", "ให้", "โดย", "ถึง", "ว่า",
            "เมื่อ", "ถ้า", "หาก", "แม้", "เพราะ", "จึง", "จน", "ไม่", "จะ", "ก็", "ต้อง", "ยัง", "เคย",
            "กำลัง", "คง", "อาจ", "ควร", "เป็น", "คือ", "ไร้", "ทุก", "แต่ละ", "บาง", "หลาย", "สู่", "ต่อ",
            "ด้วยการ", "ทั้ง", "ตาม",
        };

        /// <summary>คำเกาะท้าย — ห้ามเว้นวรรคก่อนคำเหล่านี้</summary>
        private static readonly HashSet<string> BindPrev = new HashSet<string>
        {
            "ได้", "แล้ว", "ไป", "มา", "อยู่", "ด้วย", "เลย", "กัน", "ขึ้น", "ลง", "เอง", "นะ", "ครับ",
            "ค่ะ", "คะ", "จ้ะ", "สิ", "ล่ะ", "หรอก", "เถอะ", "นั้น", "นี้", "โน้น", "มาก", "ไหม", "เท่านั้น",
            "ที่สุด", "ไว้", "เสีย",
        };

        /// <summary>คำเชื่อม/บุพบท — จุดเว้นก่อนคำเหล่านี้เป็นธรรมชาติ</summary>
        private static readonly HashSet<string> GoodBefore = new HashSet<string>
        {
            "และ", "แต่", "หรือ", "ที่", "ซึ่ง", "ใน", "กับ", "ของ", "เพื่อ", "จาก", "ว่า", "เมื่อ", "ถ้า",
            "หาก", "แม้", "เพราะ", "จึง", "จน", "โดย", "ให้", "แห่ง", "สำหรับ",
        };

        /// <summary>
        /// รวม tokens ช่วง [start, end) ที่เป็นคำไทยของ run เดียวกันเป็นกลุ่มวลี in-place —
        /// token ที่ได้เป็นก้อนข้อความที่จุดตัดตกเฉพาะรอยต่อระหว่างกลุ่ม
        /// </summary>
        public static void ChunkInPlace(List<Token> tokens, int start, int end, int maxChunk)
        {
            int n = end - start;
            if (n < 2) return;

            var parts = new string[n];
            var lengths = new int[n + 1];
            for (int i = 0; i < n; i++)
            {
                parts[i] = tokens[start + i].Text;
                lengths[i + 1] = lengths[i] + parts[i].Length;
            }

            var best = new float[n + 1];
            var prev = new int[n + 1];
            best[0] = 0;
            for (int j = 1; j <= n; j++) best[j] = float.PositiveInfinity;

            for (int j = 1; j <= n; j++)
            {
                for (int i = j - 1; i >= 0; i--)
                {
                    int count = j - i;
                    int len = lengths[j] - lengths[i];
                    if (count > 1 && len > maxChunk + ChunkHard) break; // ยิ่งรวมยิ่งยาว — ตัดทางเลือกที่เหลือ
                    float span = len <= maxChunk ? 0 : (len - maxChunk) * OverflowUnitCost;
                    float cost = best[i] + span + (i > 0 ? BreakCost(parts, i) : 0);
                    if (cost < best[j])
                    {
                        best[j] = cost;
                        prev[j] = i;
                    }
                }
            }

            var merged = new List<Token>(n);
            int k = n;
            while (k > 0)
            {
                int i = prev[k];
                bool allWords = true;
                for (int t = i; t < k; t++)
                    if (tokens[start + t].Kind != TokenKind.DictionaryWord) { allWords = false; break; }
                var sb = new System.Text.StringBuilder(lengths[k] - lengths[i]);
                for (int t = i; t < k; t++) sb.Append(parts[t]);
                merged.Add(new Token(sb.ToString(),
                    allWords ? TokenKind.DictionaryWord : TokenKind.UnknownRun));
                k = i;
            }
            merged.Reverse();
            tokens.RemoveRange(start, n);
            tokens.InsertRange(start, merged);
        }

        /// <summary>ต้นทุนการเว้นวรรคก่อน parts[k] (จุดที่ k=0 ไม่มีการเว้น)</summary>
        private static float BreakCost(string[] parts, int k)
        {
            float c = BaseCost;
            if (BindNext.Contains(parts[k - 1])) c += BindPenalty;
            if (BindPrev.Contains(parts[k])) c += BindPenalty;
            if (GoodBefore.Contains(parts[k])) c -= GoodBeforeBonus;
            return c;
        }
    }
}

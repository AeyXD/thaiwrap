using System;
using System.Text;

namespace ThaiWrap
{
    /// <summary>จำนวนรูปพิมพ์ผิดที่พบในอินพุต (นับเป็น run — ััั รวมเป็นจุดเดียว)</summary>
    public sealed class ThaiTextIssues
    {
        /// <summary>จำนวน run ของ เ ติดกันตั้งแต่สองตัว (เเ ซึ่งตั้งใจพิมพ์ แ)</summary>
        public int DoubleE;

        /// <summary>จำนวน run ของมาร์ค (สระจ่อย/วรรณยุกต์/ไม้ทัด) ที่ซ้ำตัวเอง</summary>
        public int DoubledMark;

        public int Total
        {
            get { return DoubleE + DoubledMark; }
        }
    }

    /// <summary>
    /// ยุบรูปพิมพ์ผิดสองตระกูลที่พบบ่อยสุดในข้อความไทยจากทีมแปลจริง (สถิติจากคลัง
    /// 96,882 id: เเ 769 จุด, มาร์คซ้อน 190 จุด) — คำผิดสองรูปนี้ทำให้คำกลายเป็น unknown
    /// แล้ว merge pass เกาะกินคำถูกข้างเคียง จนจุดตัดหลุดทั้งย่อหน้า ไม่ใช่แค่คำนั้นคำเดียว
    ///
    /// กฎมาจาก production ของ ThaiW3Setup (devtools/find_typos.py, branch
    /// devtools-checkers: fix_double_e + MARK_RUN) ซึ่งตรวจทานกับทีมแปลแล้วว่าปลอดภัย
    /// กับข้อความไทยทุกชิ้น:
    /// - เเ → แ : คำไทยไม่ลงท้ายด้วยสระเอ สระหน้าต้องมีพยัญชนะตามเสมอ จึงแทนคู่นั้นได้เลย
    ///   (แทนทีละคู่จากซ้ายไม่ให้ซ้อนทับ — mirror ของ str.replace ใน Python)
    /// - มาร์คที่ซ้ำตัวเอง (ัั ็็ ี่ี ฯลฯ) → ยุบเหลือตัวเดียว
    ///
    /// เจตนาไม่แตะ (ตามที่ตรวจทานกับทีมแปล): า (ลากเสียง อาาา ถูกต้องในบทพูด) ·
    /// พยัญชนะซ้ำ (กกก ฮั่กกก) · ฺ ํ ๎ (ยังไม่มีข้อสรุป) · มาร์คต่างชนิดที่ติดกัน (ัิ —
    /// อาจเป็นลำดับมาร์คผิด แต่ยังไม่มีกฎแก้ที่ปลอดภัยพอ) · คำสะกดเฉพาะคำ
    /// (ปฎิ→ปฏิ, ทึ่→ที่ ฯลฯ) เป็นข้อมูลของแต่ละโปรเจกต์ ไม่อยู่ในเครื่องมือกลาง
    /// </summary>
    public static class ThaiNormalizer
    {
        // ั ิ ี ึ ื ุ ู ็ ่ ้ ๊ ๋ ์ — มาร์คที่ซ้ำตัวเองไม่ได้ในภาษาไทย
        // (ชุดเดียวกับ DOUBLE_MARKS ใน find_typos.py ของ ThaiW3Setup)
        private const string DoubledMarks =
            "\u0E31\u0E34\u0E35\u0E36\u0E37\u0E38\u0E39\u0E47\u0E48\u0E49\u0E4A\u0E4B\u0E4C";

        private const char VowelE = '\u0E40'; // เ
        private const char VowelAe = '\u0E41'; // แ

        /// <summary>
        /// ยุบรูปพิมพ์ผิดตามกฎข้างต้น — ส่งกลับ string เดิม (instance เดิม) เมื่อไม่มีอะไรแก้
        /// ทำงานกับ string ใดๆ ไม่จำกัดภาษาไทย: อักขระอื่นผ่านตรงๆ และไม่แตะ ZWSP ที่มีอยู่
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text) || !ContainsFixable(text)) return text;

            var sb = new StringBuilder(text.Length);
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (c == VowelE && i + 1 < n && text[i + 1] == VowelE)
                {
                    sb.Append(VowelAe);
                    i += 2;
                    continue;
                }
                sb.Append(c);
                if (IsDoubledMark(c))
                {
                    i++;
                    while (i < n && text[i] == c) i++;
                }
                else
                {
                    i++;
                }
            }
            return sb.ToString();
        }

        /// <summary>นับรูปพิมพ์ผิดที่ Normalize แก้ได้ — ใช้ทำคำเตือน/--stats โดยไม่แก้ข้อความ</summary>
        public static ThaiTextIssues Count(string text)
        {
            var issues = new ThaiTextIssues();
            if (string.IsNullOrEmpty(text)) return issues;
            for (int i = 1; i < text.Length; i++)
            {
                char cur = text[i];
                if (text[i - 1] != cur) continue;
                if (cur == VowelE) issues.DoubleE++;
                else if (IsDoubledMark(cur)) issues.DoubledMark++;
                else continue;
                while (i + 1 < text.Length && text[i + 1] == cur) i++; // นับทั้ง run เป็นจุดเดียว
            }
            return issues;
        }

        private static bool IsDoubledMark(char c) => DoubledMarks.IndexOf(c) >= 0;

        private static bool ContainsFixable(string text)
        {
            for (int i = 1; i < text.Length; i++)
            {
                char cur = text[i];
                if (text[i - 1] == cur && (cur == VowelE || IsDoubledMark(cur))) return true;
            }
            return false;
        }
    }
}

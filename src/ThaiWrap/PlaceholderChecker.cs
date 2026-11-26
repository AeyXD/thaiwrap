using System;
using System.Collections.Generic;
using System.Text;

namespace ThaiWrap
{
    /// <summary>ผลเทียบส่วนควบคุมของคำแปลกับต้นฉบับ — Missing = มีในต้นฉบับแต่หายจากคำแปล · Extra = เกินมาในคำแปล</summary>
    public sealed class PlaceholderMismatch
    {
        public List<string> Missing = new List<string>();
        public List<string> Extra = new List<string>();
    }

    /// <summary>
    /// ตรวจส่วนควบคุมของเกมในคำแปลเทียบต้นฉบับ: {placeholder} · &lt;rich-text tag&gt; · printf (%s %d %1$s %.2f)
    /// — ของที่หาย/เกิน/สะกดผิด ({ชื่อ} แทน {name}, &lt;colour&gt; แทน &lt;color&gt;) ทำให้เกมแทนค่าไม่ได้ แสดงผลพัง
    /// หรือ crash ได้ ร้ายแรงกว่าคำผิด · เทียบแบบ multiset ไม่สนลำดับ เพราะไวยากรณ์ไทยสลับตำแหน่งได้ตามปกติ
    /// · {…} และ &lt;…&gt; หาขอบเขตด้วยกฎเดียวกับ <see cref="ThaiWrapOptions.ProtectPlaceholders"/> (ตัวปิดต้องอยู่บรรทัดเดียวกัน)
    ///   แล้วกรองที่ไม่ใช่ placeholder จริงออก (ดู Counts) · ตรวจกับคลังแปล W3 94k คู่ก่อนกำหนดกฎ
    /// · ไม่นับ [วงเล็บเหลี่ยม] เพราะข้อความเกมทั่วไปใช้เป็นถ้อยคำที่แปลได้ (เช่น [กด E])
    /// </summary>
    public static class PlaceholderChecker
    {
        /// <summary>
        /// ตำแหน่งตัวปิดของ {…} หรือ &lt;…&gt; ที่เริ่มที่ <paramref name="i"/> (ในบรรทัดเดียวกัน) — -1 = ไม่ใช่ส่วนควบคุม
        /// · ใช้ร่วมกับตัวตัดคำ (ThaiSegmenter.SplitControlParts) ให้นิยามตรงกันที่เดียว
        /// </summary>
        public static int FindControlEnd(string text, int i)
        {
            char c = text[i];
            if (c != '{' && c != '<') return -1;
            char close = c == '{' ? '}' : '>';
            int j = i + 1;
            while (j < text.Length && text[j] != close && text[j] != '\n' && text[j] != '\r') j++;
            return j < text.Length && text[j] == close ? j : -1;
        }

        /// <summary>ส่วนควบคุมทั้งหมดในข้อความตามลำดับที่พบ (ซ้ำได้)</summary>
        public static List<string> Extract(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            int n = text.Length;
            for (int i = 0; i < n; i++)
            {
                int end = FindControlEnd(text, i);
                if (end >= 0)
                {
                    string token = text.Substring(i, end - i + 1);
                    if (Counts(token)) list.Add(token);
                    i = end;
                    continue;
                }
                if (text[i] == '%')
                {
                    if (i + 1 < n && text[i + 1] == '%') { i++; continue; } // %% = เครื่องหมาย % จริง
                    int len = MatchPrintf(text, i);
                    if (len > 0)
                    {
                        list.Add(text.Substring(i, len));
                        i += len - 1;
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// ส่วนควบคุมนี้ต้องเหมือนต้นฉบับหรือไม่ (ตัวตัดคำยังปกป้องทุก {…}/&lt;…&gt; เหมือนเดิม — นี่คือเกณฑ์ของตัวตรวจเท่านั้น):
        /// · {…} นับเฉพาะหน้าตาแบบ placeholder — {0} {name} {0:N2} {player.name} {color=#f00} {/b} · {…} ที่มีช่องว่าง/ไทย/
        ///   เครื่องหมายประโยค เป็นถ้อยคำที่แปลได้ (W3 ใช้ครอบภาษา Elder Speech/ท่าทาง เช่น {shakes his head} — ฟ้องผิด
        ///   ~200 จุดบนคลังจริงก่อนตั้งกฎนี้)
        /// · &lt;br&gt; &lt;br/&gt; &lt;br /&gt; (ตัวพิมพ์ใดก็ได้) ไม่นับ — ทีมแปลเพิ่ม/ลดบรรทัดตามความยาวภาษาไทยเป็นเรื่องปกติ (~1,900 จุดบนคลัง W3
        ///   ล้วนตั้งใจ) · รูปเพี้ยนอย่าง &lt;br &gt; หรือ &lt; br&gt; ยังนับ เพราะเกมไม่รู้จักแล้วแสดงเป็นตัวอักษร
        /// </summary>
        private static bool Counts(string token)
        {
            if (token[0] == '<')
            {
                string t = token.ToLowerInvariant();
                return t != "<br>" && t != "<br/>" && t != "<br />";
            }
            if (token.Length == 2) return false; // {} ว่าง
            for (int k = 1; k < token.Length - 1; k++)
            {
                char c = token[k];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                          c == '_' || c == '.' || c == ':' || c == '=' || c == '#' || c == '+' || c == '-' ||
                          c == '/' || c == ',' || c == '$' || c == '@' || c == '[' || c == ']';
                if (!ok) return false;
            }
            return !token.Contains("..") && token[token.Length - 2] != '.'; // {Hrrrr..} {Feadhainn.} = ถ้อยคำ
        }

        /// <summary>
        /// เทียบคำแปลกับต้นฉบับ — null = ตรงกัน (หรือไม่มีส่วนควบคุมทั้งคู่) · ตัวคั่นที่แทรก
        /// (ZWSP/hairspace) ไม่มีผล เพราะไม่ถูกแทรกในส่วนควบคุม
        /// </summary>
        public static PlaceholderMismatch? Compare(string source, string translation)
        {
            var src = Extract(source);
            var dst = Extract(translation);
            if (src.Count == 0 && dst.Count == 0) return null;

            var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var t in src) remaining[t] = remaining.TryGetValue(t, out int k) ? k + 1 : 1;
            var result = new PlaceholderMismatch();
            foreach (var t in dst)
            {
                if (remaining.TryGetValue(t, out int k) && k > 0) remaining[t] = k - 1;
                else result.Extra.Add(t);
            }
            foreach (var t in src) // ตามลำดับต้นฉบับ — รายงานอ่านง่าย
            {
                if (remaining.TryGetValue(t, out int k) && k > 0)
                {
                    result.Missing.Add(t);
                    remaining[t] = k - 1;
                }
            }
            return result.Missing.Count == 0 && result.Extra.Count == 0 ? null : result;
        }

        /// <summary>
        /// ข้อความนี้ใช้เป็นต้นฉบับได้หรือไม่ — ไม่มีอักษรไทย และดูเป็นถ้อยคำ (ละติน + ช่องว่าง) หรือมีส่วนควบคุม
        /// ("{0}" ล้วนก็นับ) · กันคอลัมน์ ID/key อย่าง item_sword_01 ที่ไม่มี placeholder ถูกจับเป็นต้นฉบับ แล้วฟ้องว่า
        /// คำแปล "เกิน" ทั้งที่ถูก
        /// </summary>
        public static bool LooksLikeSource(string? text)
        {
            if (text == null || text.Trim().Length == 0) return false; // net35 ไม่มี IsNullOrWhiteSpace
            bool latin = false, space = false;
            foreach (char c in text)
            {
                if (c >= '\u0E00' && c <= '\u0E7F') return false;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin = true;
                else if (c == ' ') space = true;
            }
            return (latin && space) || Extract(text).Count > 0;
        }

        /// <summary>
        /// printf ที่ขึ้นต้นที่ i: %[n$][flags][width][.precision][length]conv — conv เฉพาะ s d i f u x X @
        /// (ตัดตัวที่ชนคำอังกฤษบ่อยอย่าง %o %e %c ออก) · ไม่รับ flag ช่องว่าง ("100% sure" ไม่ใช่ % s)
        /// </summary>
        private static int MatchPrintf(string s, int i)
        {
            int n = s.Length, j = i + 1;
            int digits = j;
            while (j < n && s[j] >= '0' && s[j] <= '9') j++;
            if (j > digits && j < n && s[j] == '$') j++;
            else j = digits;
            while (j < n && (s[j] == '-' || s[j] == '+' || s[j] == '0' || s[j] == '#')) j++;
            if (j < n && s[j] == '*') j++;
            else while (j < n && s[j] >= '0' && s[j] <= '9') j++;
            if (j < n && s[j] == '.')
            {
                int p = j + 1;
                if (p < n && s[p] == '*') p++;
                else while (p < n && s[p] >= '0' && s[p] <= '9') p++;
                if (p == j + 1) return 0;
                j = p;
            }
            if (j + 1 < n && s[j] == 'l' && s[j + 1] == 'l') j += 2;
            else if (j < n && (s[j] == 'l' || s[j] == 'h' || s[j] == 'z')) j++;
            if (j >= n) return 0;
            switch (s[j])
            {
                case 's': case 'd': case 'i': case 'f': case 'u': case 'x': case 'X': case '@':
                    break;
                default:
                    return 0;
            }
            return j + 1 - i;
        }

        /// <summary>แสดงรายการส่วนควบคุมแบบอ่านง่าย: {0} ×2, &lt;b&gt;</summary>
        public static string Describe(IEnumerable<string> tokens)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var t in tokens)
            {
                if (!counts.ContainsKey(t)) { counts[t] = 0; order.Add(t); }
                counts[t]++;
            }
            var sb = new StringBuilder();
            foreach (var t in order)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(t);
                if (counts[t] > 1) sb.Append(" ×").Append(counts[t]);
            }
            return sb.ToString();
        }
    }
}

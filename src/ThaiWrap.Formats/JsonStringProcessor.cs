using System;
using System.Text;

namespace ThaiWrap.Formats
{
    /// <summary>
    /// ประมวลผลไฟล์ JSON แบบ "อนุรักษ์เดิม" — สแกนทีละตัวอักษรแล้วแก้เฉพาะเนื้อหา string literal
    /// ที่อยู่ตำแหน่ง "ค่า" (ไม่ใช่ key) และมีการเปลี่ยนแปลงจริงเท่านั้น
    /// ลำดับ key, whitespace, ตัวเลข, รูปแบบ escape เดิมของ string ที่ไม่แตะ ถูกคงไว้ byte-to-byte
    /// </summary>
    public static class JsonStringProcessor
    {
        public static string Process(string input, Func<string, string> transform)
        {
            var sb = new StringBuilder(input.Length);
            int i = 0, n = input.Length;

            while (i < n)
            {
                char c = input[i];
                if (c != '"') { sb.Append(c); i++; continue; }

                // อ่าน string literal ทั้งอัน (รวม ")
                int start = i;
                i++;
                while (i < n)
                {
                    if (input[i] == '\\') { i += 2; continue; }
                    if (input[i] == '"') { i++; break; }
                    i++;
                }
                string literal = input.Substring(start, i - start);

                // key หรือ value? — ดูตัวถัดไปที่ไม่ใช่ whitespace ว่าเป็น ':' หรือไม่
                int j = i;
                while (j < n && (input[j] == ' ' || input[j] == '\t' || input[j] == '\r' || input[j] == '\n')) j++;
                bool isKey = j < n && input[j] == ':';

                if (isKey) { sb.Append(literal); continue; }

                string decoded = Decode(literal);
                string transformed = transform(decoded);
                if (transformed == decoded) { sb.Append(literal); continue; }
                sb.Append(Encode(transformed));
            }
            return sb.ToString();
        }

        /// <summary>ถอดรหัส string literal (รวม ") เป็นค่าจริง</summary>
        internal static string Decode(string literal)
        {
            var sb = new StringBuilder(literal.Length);
            int i = 1; // ข้าม " เปิด
            int n = literal.Length - 1; // ก่อน " ปิด
            while (i < n)
            {
                char c = literal[i];
                if (c != '\\') { sb.Append(c); i++; continue; }
                i++;
                if (i >= n) break;
                switch (literal[i])
                {
                    case '"': sb.Append('"'); i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    case '/': sb.Append('/'); i++; break;
                    case 'b': sb.Append('\b'); i++; break;
                    case 'f': sb.Append('\f'); i++; break;
                    case 'n': sb.Append('\n'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case 'u':
                        if (i + 4 < n + 1 && TryHex(literal, i + 1, 4, out int cp))
                        {
                            sb.Append((char)cp);
                            i += 5;
                        }
                        else { sb.Append('\\'); i++; }
                        break;
                    default: sb.Append(literal[i]); i++; break;
                }
            }
            return sb.ToString();
        }

        /// <summary>เข้ารหัสค่าเป็น string literal JSON</summary>
        internal static string Encode(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static bool TryHex(string s, int start, int count, out int value)
        {
            value = 0;
            if (start + count > s.Length) return false;
            for (int k = 0; k < count; k++)
            {
                int d = s[start + k] switch
                {
                    char c when c >= '0' && c <= '9' => c - '0',
                    char c when c >= 'a' && c <= 'f' => c - 'a' + 10,
                    char c when c >= 'A' && c <= 'F' => c - 'A' + 10,
                    _ => -1
                };
                if (d < 0) return false;
                value = value * 16 + d;
            }
            return true;
        }
    }
}

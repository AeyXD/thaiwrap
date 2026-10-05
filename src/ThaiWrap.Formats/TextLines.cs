using System.Collections.Generic;
using System.Text;

namespace ThaiWrap.Formats
{
    /// <summary>ตัวช่วยรักษาบรรทัด: แยกบรรทัดพร้อมตัวจบบรรทัดเดิม (\n, \r\n, \r) เพื่อเขียนกลับแบบเหมือนเดิม</summary>
    public static class TextLines
    {
        /// <summary>แยกข้อความเป็นบรรทัดที่ยังติดตัวจบบรรทัดอยู่ — concat กลับได้ได้ข้อความเดิมเป๊ะ</summary>
        public static List<string> SplitKeepTerminators(string text)
        {
            var lines = new List<string>();
            var sb = new StringBuilder();
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                sb.Append(c);
                if (c == '\n')
                {
                    lines.Add(sb.ToString());
                    sb.Clear();
                }
                else if (c == '\r')
                {
                    if (i + 1 < n && text[i + 1] == '\n') sb.Append('\n');
                    lines.Add(sb.ToString());
                    sb.Clear();
                    i += (i + 1 < n && text[i + 1] == '\n') ? 2 : 1;
                    continue;
                }
                i++;
            }
            if (sb.Length > 0) lines.Add(sb.ToString());
            return lines;
        }

        /// <summary>ปอกตัวจบบรรทัดออก</summary>
        public static string StripTerminator(string line, out string terminator)
        {
            if (line.EndsWith("\r\n")) { terminator = "\r\n"; return line.Substring(0, line.Length - 2); }
            if (line.EndsWith("\n")) { terminator = "\n"; return line.Substring(0, line.Length - 1); }
            if (line.EndsWith("\r")) { terminator = "\r"; return line.Substring(0, line.Length - 1); }
            terminator = "";
            return line;
        }
    }
}

using System;
using System.Text;

namespace ThaiWrap.Formats
{
    /// <summary>
    /// ประมวลผลไฟล์ key=value (สไตล์ XUnity.AutoTranslator: บทต้นฉบับ=คำแปล)
    /// transform เฉพาะฝั่งขวาของ '=' ตัวแรก — key และบรรทัดคอมเมนต์ (#) ไม่ถูกแตะ
    /// </summary>
    public static class KeyValueProcessor
    {
        public static string Process(string input, Func<string, string> transform)
        {
            var sb = new StringBuilder(input.Length);
            foreach (var line in TextLines.SplitKeepTerminators(input))
            {
                string content = TextLines.StripTerminator(line, out string term);
                int eq = content.IndexOf('=');

                bool processable = eq > 0 && !content.TrimStart().StartsWith("#", StringComparison.Ordinal);
                if (!processable) { sb.Append(line); continue; }

                string key = content.Substring(0, eq);
                string value = content.Substring(eq + 1);
                string transformed = transform(value);
                if (transformed == value) { sb.Append(line); continue; }

                sb.Append(key).Append('=').Append(transformed).Append(term);
            }
            return sb.ToString();
        }
    }
}

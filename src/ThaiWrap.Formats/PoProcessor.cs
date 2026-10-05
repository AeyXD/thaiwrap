using System;
using System.Collections.Generic;
using System.Text;

namespace ThaiWrap.Formats
{
    /// <summary>
    /// ประมวลผลไฟล์ gettext (.po) — transform เฉพาะเนื้อหา msgstr / msgstr[n]
    /// msgid, คอมเมนต์ (#, #., #~ ฯลฯ) และ entry ที่ไม่เปลี่ยน ถูกคงไว้ทั้งบรรทัดเดิม
    /// entry ที่ถูกแก้จะถูกยุบ msgstr เป็นบรรทัดเดียว (ยังเป็น PO ที่ถูกต้อง)
    /// </summary>
    public static class PoProcessor
    {
        public static string Process(string input, Func<string, string> transform)
        {
            var lines = TextLines.SplitKeepTerminators(input);
            var output = new List<string>();
            int i = 0;

            while (i < lines.Count)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("msgstr", StringComparison.Ordinal))
                {
                    int start = i;
                    var block = new List<string> { lines[i] };
                    i++;
                    while (i < lines.Count && lines[i].TrimStart().StartsWith("\"", StringComparison.Ordinal))
                    {
                        block.Add(lines[i]);
                        i++;
                    }

                    string header = FirstToken(trimmed);            // msgstr หรือ msgstr[0] ...
                    string terminator = TerminatorOf(lines[start]);
                    string content = ConcatDecoded(block);

                    // msgstr ของ header entry (msgid "") เป็น metadata ภาษาละติน — transform จะ no-op เอง
                    string transformed = transform(content);
                    if (transformed != content && content.Length > 0)
                    {
                        output.Add(header + " " + JsonStringProcessor.Encode(transformed) + terminator);
                    }
                    else
                    {
                        output.AddRange(block);
                    }
                    continue;
                }
                output.Add(lines[i]);
                i++;
            }
            return string.Concat(output.ToArray());
        }

        private static string FirstToken(string line)
        {
            int quote = line.IndexOf('"');
            return quote < 0 ? line.TrimEnd() : line.Substring(0, quote).TrimEnd();
        }

        private static string TerminatorOf(string line)
        {
            _ = TextLines.StripTerminator(line, out string term);
            return term;
        }

        /// <summary>ต่อ string หลายบรรทัดของบล็อก msgstr เป็นค่าเดียว (decode ตาม escape ของ PO/JSON)</summary>
        private static string ConcatDecoded(List<string> block)
        {
            var sb = new StringBuilder();
            foreach (var line in block)
            {
                string content = line.Trim();
                int open = content.IndexOf('"');
                int close = content.LastIndexOf('"');
                if (open < 0 || close <= open) continue;
                string literal = content.Substring(open, close - open + 1);
                sb.Append(JsonStringProcessor.Decode(literal));
            }
            return sb.ToString();
        }
    }
}

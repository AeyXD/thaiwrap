using System;
using System.Collections.Generic;
using System.Text;

namespace ThaiWrap.Formats
{
    public sealed class CsvOptions
    {
        /// <summary>ตัวคั่นฟิลด์ (default ',')</summary>
        public char Delimiter = ',';

        /// <summary>คอลัมน์เป้าหมายแบบเลขลำดับ 1-based — null = ทุกคอลัมน์</summary>
        public IReadOnlyList<int>? ColumnIndices;

        /// <summary>คอลัมน์เป้าหมายแบบชื่อ (ใช้แถวแรกเป็น header) — null = ไม่ใช้</summary>
        public IReadOnlyList<string>? ColumnNames;
    }

    /// <summary>
    /// ประมวลผลไฟล์ CSV แบบ RFC4180 — transform เฉพาะคอลัมน์ที่ระบุ
    /// รักษาตัวจบบรรทัดของแต่ละแถวไว้เดิม (\n หรือ \r\n) และ field ที่ค่าไม่เปลี่ยนเขียนกลับเป็นข้อความดิบเดิม
    /// ทุกไบต์ (quote เดิม เช่น "001" ไม่ถูกถอด) — escape ใหม่เฉพาะ field ที่ transform เปลี่ยนค่า
    /// </summary>
    public static class CsvProcessor
    {
        public static string Process(string input, CsvOptions options, Func<string, string> transform)
        {
            var records = Parse(input, options.Delimiter);
            var targets = ResolveTargetColumns(records, options);
            foreach (var rec in records) rec.Original = new List<string>(rec.Fields);

            for (int r = options.ColumnNames != null ? 1 : 0; r < records.Count; r++) // มี header-based เลือกคอลัมน์ → ข้ามแถว header
            {
                var fields = records[r].Fields;
                foreach (int col in targets)
                {
                    if (col - 1 < fields.Count)
                        fields[col - 1] = transform(fields[col - 1]);
                }
            }
            return Serialize(records, options.Delimiter);
        }

        /// <summary>แยก records แล้วคืนเป็นตาราง string — ใช้ตรวจสอบผลลัพธ์ย้อนหลังได้</summary>
        public static List<List<string>> ParseTable(string input, char delimiter)
        {
            var records = Parse(input, delimiter);
            var table = new List<List<string>>();
            foreach (var r in records) table.Add(r.Fields);
            return table;
        }

        private static List<int> ResolveTargetColumns(List<Record> records, CsvOptions options)
        {
            if (options.ColumnIndices != null) return new List<int>(options.ColumnIndices);
            if (options.ColumnNames != null && records.Count > 0)
            {
                var result = new List<int>();
                var header = records[0].Fields;
                foreach (var name in options.ColumnNames)
                {
                    int idx = header.FindIndex(h => h.Trim() == name);
                    if (idx < 0) throw new ArgumentException("ไม่พบคอลัมน์ชื่อ '" + name + "' ใน header (พบ: " + string.Join(", ", header) + ")");
                    result.Add(idx + 1);
                }
                return result;
            }
            var all = new List<int>();
            int width = 0;
            foreach (var r in records) width = Math.Max(width, r.Fields.Count);
            for (int c = 1; c <= width; c++) all.Add(c);
            return all;
        }

        private sealed class Record
        {
            public List<string> Fields = new List<string>();
            public List<string> Raw = new List<string>(); // ข้อความดิบของแต่ละ field ตามไฟล์ (คู่กับ Fields)
            public List<string>? Original;                // ค่าก่อน transform — สร้างตอน Process
            public string Terminator = "";
            public bool Started; // เคยเห็น field นี้เริ่มขึ้นจริง (แม้จะว่างเปล่า เช่น "" ท้ายไฟล์)
        }

        private static List<Record> Parse(string input, char delim)
        {
            var records = new List<Record>();
            var rec = new Record();
            var field = new StringBuilder();
            bool inQuotes = false;
            int i = 0, n = input.Length, start = 0;

            while (i < n)
            {
                char c = input[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < n && input[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                        inQuotes = false; i++; continue;
                    }
                    field.Append(c); i++; continue;
                }
                if (c == '"') { inQuotes = true; rec.Started = true; i++; continue; }
                if (c == delim)
                {
                    rec.Fields.Add(field.ToString()); field.Clear(); rec.Started = true;
                    rec.Raw.Add(input.Substring(start, i - start)); i++; start = i; continue;
                }
                if (c == '\r' || c == '\n')
                {
                    rec.Fields.Add(field.ToString()); field.Clear();
                    rec.Raw.Add(input.Substring(start, i - start));
                    string term = c == '\r' && i + 1 < n && input[i + 1] == '\n' ? "\r\n" : (c == '\r' ? "\r" : "\n");
                    rec.Terminator = term;
                    records.Add(rec); rec = new Record();
                    i += term.Length; start = i; continue;
                }
                field.Append(c); rec.Started = true; i++;
            }
            // เก็บ record สุดท้ายเมื่อมันเริ่มขึ้นจริง (มีอักขระหรือ quote เปิด) แม้ไม่มี newline ปิด
            // — record ว่างหลัง newline ท้ายไฟล์ยังถูกทิ้งตามเดิม
            if (rec.Started || rec.Fields.Count > 0)
            {
                rec.Fields.Add(field.ToString());
                rec.Raw.Add(input.Substring(start, n - start));
                records.Add(rec);
            }
            return records;
        }

        private static string Serialize(List<Record> records, char delim)
        {
            var sb = new StringBuilder(records.Count * 32);
            for (int rIdx = 0; rIdx < records.Count; rIdx++)
            {
                var r = records[rIdx];
                bool lastRecordNoNewline = rIdx == records.Count - 1 && r.Terminator.Length == 0;
                for (int f = 0; f < r.Fields.Count; f++)
                {
                    if (f > 0) sb.Append(delim);
                    // field ว่างเดี่ยวใน record สุดท้ายที่ไม่มี newline ปิด ต้องเขียนเป็น ""
                    // ไม่งั้น output เป็นสตริงว่าง = record หายเวลา parse กลับ (กำกวมกับ "ไม่มี record")
                    if (r.Original != null && f < r.Raw.Count && r.Fields[f] == r.Original[f])
                    {
                        sb.Append(r.Raw[f]); // ค่าไม่เปลี่ยน → ข้อความดิบเดิม (อนุรักษ์ quote/รูปเดิม)
                        continue;
                    }
                    bool ambiguousEmpty = lastRecordNoNewline && f == r.Fields.Count - 1 &&
                                          r.Fields[f].Length == 0 && r.Fields.Count == 1;
                    // field เดิมครอบ quote → ค่าใหม่ครอบ quote ด้วย (คงสไตล์เดิมของไฟล์)
                    bool wasQuoted = f < r.Raw.Count && r.Raw[f].Length > 0 && r.Raw[f][0] == '"';
                    sb.Append(ambiguousEmpty || (wasQuoted && r.Fields[f].Length == 0) ? "\"\""
                        : wasQuoted ? Quote(r.Fields[f]) : EscapeField(r.Fields[f], delim));
                }
                sb.Append(r.Terminator);
            }
            return sb.ToString();
        }

        private static string EscapeField(string s, char delim)
        {
            if (s.IndexOf('"') >= 0 || s.IndexOf(delim) >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
                return Quote(s);
            return s;
        }

        private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}

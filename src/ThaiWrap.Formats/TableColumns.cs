using System;
using System.Collections.Generic;

namespace ThaiWrap.Formats
{
    /// <summary>
    /// คอลัมน์คำแปล/ต้นฉบับที่ผู้ใช้ระบุเองสำหรับโหมดตรวจ (--placeholders · --stats · --mispaste) ของ csv/tsv
    /// — แทนการเดาจากเนื้อหา (เซลล์ไทยมากสุด/ละตินมากสุด) ที่พลาดได้เมื่อตารางมีหลายคอลัมน์ไทย
    /// หรือคอลัมน์ ID/หมายเหตุภาษาอังกฤษยาวกว่าต้นฉบับ · ฝั่งที่ไม่ระบุยังเดาเหมือนเดิม
    /// · สเปกใช้รูปเดียวกับ --columns: เลข 1-based หรือชื่อตาม header — มีชื่อฝั่งใดฝั่งหนึ่ง = ข้ามแถว header
    /// </summary>
    public sealed class TableColumns
    {
        /// <summary>คอลัมน์คำแปล (0-based) — null = เดาเอง (เซลล์ไทยมากสุด)</summary>
        public IReadOnlyList<int>? Translation { get; private set; }

        /// <summary>คอลัมน์ต้นฉบับ (0-based) — -1 = เดาเอง</summary>
        public int Source { get; private set; } = -1;

        /// <summary>แถวแรกที่เป็นข้อมูล (0-based) — 1 เมื่อเลือกคอลัมน์ด้วยชื่อ (แถว 0 = header)</summary>
        public int FirstDataRow { get; private set; }

        public bool IsAuto => Translation == null && Source < 0;

        /// <param name="translationSpec">ค่า --columns เช่น "3" "thai" "th,th_short" — null = เดาเอง</param>
        /// <param name="sourceSpec">ค่า --source-column (คอลัมน์เดียว) — null = เดาเอง</param>
        /// <param name="header">แถวแรกของตาราง (ใช้เมื่อสเปกเป็นชื่อ)</param>
        /// <exception cref="ArgumentException">เลขคอลัมน์ผิด/ไม่พบชื่อ/ต้นฉบับซ้ำกับคอลัมน์คำแปล</exception>
        public static TableColumns Resolve(string? translationSpec, string? sourceSpec, IReadOnlyList<string>? header)
        {
            var sel = new TableColumns();
            bool byName = false;
            if (translationSpec != null)
            {
                var list = new List<int>();
                foreach (var part in translationSpec.Split(','))
                {
                    string name = part.Trim();
                    if (name.Length == 0) continue;
                    list.Add(ResolveOne(name, header, "--columns", ref byName));
                }
                if (list.Count > 0) sel.Translation = list;
            }
            if (sourceSpec != null)
            {
                string name = sourceSpec.Trim();
                if (name.Length == 0 || name.IndexOf(',') >= 0)
                    throw new ArgumentException("--source-column ระบุได้คอลัมน์เดียว (เลข 1-based หรือชื่อ header) ได้รับ: " + sourceSpec);
                sel.Source = ResolveOne(name, header, "--source-column", ref byName);
                if (sel.Translation != null && IndexOf(sel.Translation, sel.Source) >= 0)
                    throw new ArgumentException("--source-column ซ้ำกับคอลัมน์คำแปลใน --columns: " + sourceSpec);
            }
            sel.FirstDataRow = byName ? 1 : 0;
            return sel;
        }

        private static int IndexOf(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return i;
            return -1;
        }

        private static int ResolveOne(string name, IReadOnlyList<string>? header, string opt, ref bool byName)
        {
            bool digits = true;
            foreach (char c in name) if (c < '0' || c > '9') { digits = false; break; }
            if (digits)
            {
                if (!int.TryParse(name, out int col) || col < 1)
                    throw new ArgumentException(opt + " หมายเลขคอลัมน์เริ่มที่ 1 ได้รับ: " + name);
                return col - 1;
            }
            byName = true;
            if (header != null)
                for (int i = 0; i < header.Count; i++)
                    if (header[i] != null && header[i].Trim() == name) return i;
            throw new ArgumentException(opt + ": ไม่พบคอลัมน์ชื่อ '" + name + "' ใน header (พบ: " +
                (header == null ? "-" : string.Join(", ", header)) + ")");
        }

        /// <summary>
        /// คำแปลของแถว: ระบุคอลัมน์ = ทุกเซลล์ในคอลัมน์ที่เลือกที่ไม่ว่าง (ไม่บังคับต้องมีอักษรไทย —
        /// "{0}" ล้วนก็ตรวจได้) · เดาเอง = เซลล์ไทยมากสุดเซลล์เดียว · แถว header (เมื่อเลือกด้วยชื่อ) = ว่าง
        /// </summary>
        public List<string> TranslationCells(IReadOnlyList<string> row, int rowIndex)
        {
            var list = new List<string>();
            if (rowIndex < FirstDataRow) return list;
            if (Translation == null)
            {
                MispasteDetector.PickThaiEnglish(row, out string? thai, out _);
                if (thai != null) list.Add(thai);
                return list;
            }
            foreach (int c in Translation)
                if (c < row.Count && row[c] != null && row[c].Trim().Length > 0) list.Add(row[c]);
            return list;
        }

        /// <summary>เซลล์ต้นฉบับที่ระบุ — null ถ้าไม่ได้ระบุ (ให้ผู้เรียกเดาเอง) หรือเซลล์ว่าง/ไม่มีในแถวนั้น</summary>
        public string? SourceCell(IReadOnlyList<string> row, out bool explicitColumn)
        {
            explicitColumn = Source >= 0;
            if (Source < 0 || Source >= row.Count) return null;
            string cell = row[Source];
            return cell == null || cell.Trim().Length == 0 ? null : cell;
        }
    }
}

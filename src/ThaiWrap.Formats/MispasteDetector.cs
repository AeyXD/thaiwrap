using System;
using System.Collections.Generic;
using System.Text;

namespace ThaiWrap.Formats
{
    public sealed class MispasteHit
    {
        public int RowNo;
        public string English = "";
        public string Thai = "";
        /// <summary>แถวเจ้าของข้อความจริง (กฎไทยซ้ำ — ว่างสำหรับกฎ ratio)</summary>
        public List<int> OwnerRows = new List<int>();
        /// <summary>เหตุผลโดยย่อเป็นภาษาไทย พร้อมตัวเลขให้ตรวจต่อ</summary>
        public string Reason = "";
    }

    /// <summary>
    /// ตรวจ "วางคำแปลผิดแถว" — ทีมแปล copy ข้อความของแถวอื่นมาวาง ทำให้คำแปลไม่ตรงกับต้นฉบับ
    /// และ ZWSP ที่แทรกจะผิดทั้งแถว**โดยเงียบ** · ทุกผลลัพธ์เป็นข้อเสนอ ต้องตรวจด้วยตาก่อนแก้ที่ต้นทาง
    ///
    /// เกณฑ์พิสูจน์แล้วบนคลังแปลจริงของ ThaiW3Setup (mispaste_report.py — จับ 31/31 ที่ทีมยืนยันว่าผิดจริง):
    /// 1. ข้อความไทย (ตัดแท็ก/ยุบช่องว่างแล้ว) ยาว ≥ 40 ตัวอักษร ซ้ำกันหลายแถว แต่ภาษาอังกฤษ
    ///    ต่างกันชัด (ความคล้าย &lt; 0.6 แบบกลุ่ม) → กลุ่มที่ความยาวอังกฤษเข้ากับไทยที่สุดคือเจ้าของ
    ///    แถวในกลุ่มอื่น = วางผิด
    /// 2. ความยาวไทย/อังกฤษ (normalize แล้วทั้งคู่ ต้องยาว ≥ 15) ออกนอกช่วง 0.2–3.5 → น่าสงสัย
    ///    (ไทยมักยาว 0.8–1.2 เท่าของอังกฤษ)
    ///
    /// สิ่งที่ตั้งใจไม่ทำตามต้นฉบับ: known-owner/allowlist เป็นข้อมูลเฉพาะของทีม W3 — ใครเจอบรรทัด
    /// ที่ตรวจแล้วถูกต้อง (แปลขยายความ/อังกฤษคล้ายกัน) ให้อ่านรายงานข้ามไป
    /// </summary>
    public static class MispasteDetector
    {
        private const int MinSharedThai = 40;
        private const int MinLen = 15;
        private const double EnSimilarCluster = 0.6;
        private const double RatioLow = 0.2;
        private const double RatioHigh = 3.5;

        /// <summary>
        /// ตรวจตาราง (csv/tsv) — เลือกเซลล์เอง: เซลล์ที่มีตัวอักษรไทยมากสุด = คำแปล ·
        /// ที่เหลือเซลล์ที่มีตัวละตินมากสุด = ต้นฉบับ · คืนรายการน่าสงสัยเรียงตามแถว (นับรวมแถวหัวตาราง)
        /// </summary>
        public static List<MispasteHit> Detect(IEnumerable<IReadOnlyList<string>> rows)
        {
            var pairs = new List<RowPair>(); // rowNo 1-based
            int r = 0;
            foreach (var row in rows)
            {
                r++;
                string? thai = null, en = null;
                int thaiCount = 0, enCount = 0;
                foreach (var cell in row)
                {
                    if (cell == null) continue;
                    int th = CountThai(cell);
                    if (th > thaiCount) { thaiCount = th; thai = cell; }
                }
                if (thai == null) continue;
                foreach (var cell in row)
                {
                    if (cell == null || ReferenceEquals(cell, thai)) continue;
                    int lat = CountLatin(cell);
                    if (lat > enCount) { enCount = lat; en = cell; }
                }
                if (en == null) continue;
                pairs.Add(new RowPair { RowNo = r, En = Norm(en), Thai = Norm(thai) });
            }

            var flagged = new HashSet<int>(); // RowNo ที่ถูกจับแล้ว (กฎ 1) — กันรายงานซ้ำในกฎ 2
            var hits = new List<MispasteHit>();

            // กฎ 1: ไทยยาวซ้ำกัน แต่อังกฤษแตกเป็นคนละเรื่อง
            var groups = new Dictionary<string, List<RowPair>>();
            foreach (var p in pairs)
            {
                if (p.Thai.Length < MinSharedThai) continue;
                if (!groups.TryGetValue(p.Thai, out var list)) groups[p.Thai] = list = new List<RowPair>();
                list.Add(p);
            }
            foreach (var group in groups.Values)
            {
                if (group.Count < 2) continue;
                var clusters = new List<List<RowPair>>();
                foreach (var p in group)
                {
                    string e = p.En.ToLowerInvariant();
                    bool joined = false;
                    foreach (var c in clusters)
                    {
                        if (Similarity(c[0].En.ToLowerInvariant(), e) > EnSimilarCluster) { c.Add(p); joined = true; break; }
                    }
                    if (!joined) clusters.Add(new List<RowPair> { p });
                }
                if (clusters.Count < 2) continue; // อังกฤษคล้ายกันทั้งกลุ่ม = ต้นฉบับเดียวกัน ไม่ใช่วางผิด

                List<RowPair> owner = clusters[0];
                double bestBias = double.MaxValue;
                foreach (var c in clusters)
                {
                    double bias = Math.Abs((double)group[0].Thai.Length / Math.Max(1, c[0].En.Length) - 1.0);
                    if (bias < bestBias) { bestBias = bias; owner = c; }
                }
                foreach (var c in clusters)
                {
                    if (ReferenceEquals(c, owner)) continue;
                    foreach (var p in c)
                    {
                        var owners = new List<int>();
                        foreach (var o in owner) owners.Add(o.RowNo);
                        hits.Add(new MispasteHit
                        {
                            RowNo = p.RowNo,
                            English = p.En,
                            Thai = p.Thai,
                            OwnerRows = owners,
                            Reason = "ไทยเป็นข้อความเดียวกับแถว " + string.Join(", ", owners) +
                                     " แต่อังกฤษต่างกันชัด — น่าจะวางคำแปลผิดแถว",
                        });
                        flagged.Add(p.RowNo);
                    }
                }
            }

            // กฎ 2: ความยาวไทย/อังกฤษผิดปกติ
            foreach (var p in pairs)
            {
                if (flagged.Contains(p.RowNo)) continue;
                if (p.En.Length < MinLen || p.Thai.Length < MinLen || p.Thai.Length == 0) continue;
                double ratio = (double)p.Thai.Length / p.En.Length;
                if (ratio < RatioLow || ratio > RatioHigh)
                    hits.Add(new MispasteHit
                    {
                        RowNo = p.RowNo,
                        English = p.En,
                        Thai = p.Thai,
                        Reason = "ความยาวไทย/อังกฤษ = " + ratio.ToString("F1") + " (ปกติ 0.2–3.5) — น่าสงสัยว่าไม่ใช่คำแปลของกัน",
                    });
            }

            hits.Sort((a, b) => a.RowNo.CompareTo(b.RowNo));
            return hits;
        }

        private sealed class RowPair
        {
            public int RowNo;
            public string En = "";
            public string Thai = "";
        }

        /// <summary>ตัดแท็ก &lt;…&gt; และยุบช่องว่างเหลือที่เดียว — ให้ข้อความที่ต่างกันแค่ format ถือว่าเหมือนกัน</summary>
        private static string Norm(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool inTag = false, lastSpace = true; // lastSpace=true ตัดช่องว่างนำหน้าด้วย
            foreach (char c in s)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (inTag) continue;
                if (char.IsWhiteSpace(c))
                {
                    if (!lastSpace) { sb.Append(' '); lastSpace = true; }
                    continue;
                }
                sb.Append(c);
                lastSpace = false;
            }
            return sb.ToString().TrimEnd();
        }

        private static int CountThai(string s)
        {
            int n = 0;
            foreach (char c in s)
                if (c >= '\u0E01' && c <= '\u0E5B') n++;
            return n;
        }

        private static int CountLatin(string s)
        {
            int n = 0;
            foreach (char c in s)
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) n++;
            return n;
        }

        /// <summary>ความคล้ายแบบ 2×LCS/(ผลรวมความยาว) — ใช้แทน difflib.SequenceMatcher ของต้นฉบับ</summary>
        private static double Similarity(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            int m = a.Length, n = b.Length;
            var prev = new int[n + 1];
            var cur = new int[n + 1];
            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                    cur[j] = a[i - 1] == b[j - 1] ? prev[j - 1] + 1 : Math.Max(prev[j], cur[j - 1]);
                var t = prev; prev = cur; cur = t;
            }
            return 2.0 * prev[n] / (m + n);
        }
    }
}

using System;
using System.Collections.Generic;

namespace ThaiWrap.Formats
{
    /// <summary>
    /// รูปแบบ path ของ --json-path — เขียนแบบ JSON Pointer (RFC 6901) เติม wildcard:
    /// <c>/items/*/desc</c> · segment คั่นด้วย <c>/</c> · <c>*</c> = key หรือ index ใดก็ได้หนึ่งชั้น ·
    /// <c>**</c> = กี่ชั้นก็ได้ (รวมศูนย์ชั้น) · escape ตาม RFC: <c>~1</c> = "/" · <c>~0</c> = "~"
    /// (key ที่มีจุดอย่าง "menu.start" เขียนตรง ๆ ได้ — ไม่ใช้จุดเป็นตัวคั่นเพราะ JSON แบบ flat ใช้จุดใน key บ่อย)
    /// </summary>
    public sealed class JsonPathPattern
    {
        private readonly string[] _segments;

        private JsonPathPattern(string[] segments) { _segments = segments; }

        /// <summary>แยกรายการคั่นด้วย , เป็นหลาย pattern</summary>
        /// <exception cref="ArgumentException">pattern ไม่ขึ้นต้นด้วย / หรือ escape ~ ผิด</exception>
        public static List<JsonPathPattern> ParseList(string spec)
        {
            var list = new List<JsonPathPattern>();
            foreach (var part in spec.Split(','))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                list.Add(Parse(p));
            }
            if (list.Count == 0) throw new ArgumentException("--json-path ว่าง — ตัวอย่าง: \"/items/*/desc\"");
            return list;
        }

        public static JsonPathPattern Parse(string pattern)
        {
            if (!pattern.StartsWith("/", StringComparison.Ordinal))
                throw new ArgumentException("--json-path ต้องขึ้นต้นด้วย / แบบ JSON Pointer เช่น \"/items/*/desc\" ได้รับ: " + pattern);
            var raw = pattern.Substring(1).Split('/');
            var segs = new string[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                string r = raw[i];
                for (int k = 0; k < r.Length; k++)
                    if (r[k] == '~' && (k + 1 >= r.Length || (r[k + 1] != '0' && r[k + 1] != '1')))
                        throw new ArgumentException("--json-path: ~ ต้องตามด้วย 0 (= ~) หรือ 1 (= /) ได้รับ: " + pattern);
                segs[i] = r.Replace("~1", "/").Replace("~0", "~");
            }
            return new JsonPathPattern(segs);
        }

        public bool Matches(IReadOnlyList<string> path) => Match(0, path, 0);

        private bool Match(int si, IReadOnlyList<string> path, int pi)
        {
            if (si == _segments.Length) return pi == path.Count;
            string seg = _segments[si];
            if (seg == "**")
            {
                for (int k = pi; k <= path.Count; k++)
                    if (Match(si + 1, path, k)) return true;
                return false;
            }
            if (pi >= path.Count) return false;
            if (seg != "*" && seg != path[pi]) return false;
            return Match(si + 1, path, pi + 1);
        }
    }
}

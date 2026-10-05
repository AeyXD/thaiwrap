using System.Collections.Generic;

namespace ThaiWrap.BepInEx
{
    /// <summary>
    /// ตัวกรองข้อความสำหรับ runtime hook — ประมวลผลเฉพาะ string ที่มีตัวอักษรไทย
    /// พร้อม cache กันการตัดคำซ้ำเมื่อเกม set ข้อความเดิมซ้ำๆ ทุกเฟรม
    /// (typewriter effect ก็อยู่ได้: ทุก intermediate ถูก segment ใหม่อย่างถูกต้อง
    /// เพราะ InsertZwsp เป็น idempotent)
    /// คลาสนี้ไม่พึ่ง BepInEx/Unity — ใช้ unit test ได้ตรงๆ
    /// </summary>
    public sealed class ThaiTextFilter
    {
        private readonly ThaiSegmenter _segmenter;
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>();
        private readonly int _cacheLimit;

        public bool Enabled = true;
        public long ProcessedCount;
        public long CacheHitCount;
        public long SkippedCount;

        public ThaiTextFilter(ThaiSegmenter segmenter, int cacheLimit = 4096)
        {
            _segmenter = segmenter;
            _cacheLimit = cacheLimit < 16 ? 16 : cacheLimit;
        }

        public string? Process(string? value)
        {
            if (!Enabled || value == null) return value;
            if (!ContainsThai(value)) { SkippedCount++; return value; }

            string cached;
            if (_cache.TryGetValue(value, out cached)) { CacheHitCount++; return cached; }

            string result = _segmenter.InsertZwsp(value);
            ProcessedCount++;
            if (_cache.Count >= _cacheLimit) _cache.Clear(); // นโยบายง่าย: เต็มก็ล้าง (typewriter churn ไม่ทำให้หน่วยความจำโต)
            _cache[value] = result;
            return result;
        }

        public int CacheCount { get { return _cache.Count; } }

        /// <summary>fast reject: ไม่มีตัวอักษรไทยเลย = ไม่ต้องทำงาน</summary>
        internal static bool ContainsThai(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '\u0E01' && c <= '\u0E5B') return true;
            }
            return false;
        }
    }
}

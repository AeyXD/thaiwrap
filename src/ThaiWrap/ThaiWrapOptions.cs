namespace ThaiWrap
{
    public sealed class ThaiWrapOptions
    {
        /// <summary>
        /// ความยาวคำสูงสุดจากพจนานุกรมที่ยอมรับ (ตัวรายการที่ยาวกว่าจะถูกข้ามตอนโหลด)
        /// 0 = ไม่จำกัด (แนะนำ — อัลกอริทึม DP เลือกจุดตัดแบบปลอดภัยอยู่แล้ว)
        /// </summary>
        public int MaxWordLength { get; set; } = 0;

        /// <summary>
        /// An unknown Thai run (proper name, typo, new word) stays unbreakable while it is
        /// shorter than this many clusters. Longer runs get cluster-level break points —
        /// cutting an unknown name is worse than cutting a known word, but overflowing the
        /// text box is worse still.
        /// </summary>
        public int UnknownBreakThreshold { get; set; } = 8;

        /// <summary>
        /// ปกป้องส่วนควบคุมของเกมก่อนตัดคำ: {placeholder}, %s และแท็ก rich-text อย่าง
        /// &lt;color=red&gt;…&lt;/color&gt; หรือ &lt;link="…"&gt; จะไม่ถูกแทรก ZWSP เด็ดขาด
        /// เพราะการแทนค่า/อ้างอิงจะเสีย แม้ข้างในจะมีตัวอักษรไทย
        /// </summary>
        public bool ProtectPlaceholders { get; set; } = true;

        /// <summary>
        /// ตัวอักษรที่แทรกที่รอยต่อคำ — default U+200B (ZWSP) ใช้ได้กับ TextMeshPro/Ren'Py/
        /// เอนจินที่ตัดที่ space ทั่วไป · สำหรับ Scaleform/Flash (เช่น Witcher 3) ต้องใช้
        /// U+200A hair space เพราะไม่นับ ZWSP เป็นจุดตัด และต้องเติม glyph ว่างให้ฟอนต์ด้วย
        /// </summary>
        public char BreakChar { get; set; } = '\u200B';

        /// <summary>
        /// ยุบรูปพิมพ์ผิดก่อนตัดคำ (เเ→แ, มาร์คซ้อนยุบเหลือตัวเดียว) — default false
        /// เพราะแก้ตัวอักษรของผู้ใช้จริง ต่างจากการแทรก ZWSP ที่ลบออกแล้วได้ของเดิม:
        /// คำผิดจะกลายเป็น unknown จน merge pass เกาะกินคำข้างเคียง จุดตัดจะถูกต้อง
        /// เฉพาะเมื่อแก้คำก่อน — กฎทั้งหมดอยู่ที่ <see cref="ThaiNormalizer"/> (CLI: --normalize)
        /// ชิ้นที่เป็น placeholder/แท็ก ({…} &lt;…&gt;) ไม่ถูก normalize ตามกฎปกป้องส่วนควบคุม
        /// </summary>
        public bool Normalize { get; set; }
    }
}

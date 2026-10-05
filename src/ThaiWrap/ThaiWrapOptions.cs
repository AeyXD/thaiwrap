namespace ThaiWrap
{
    public sealed class ThaiWrapOptions
    {
        /// <summary>
        /// Longest dictionary match attempted, in characters. Dictionary entries longer
        /// than this are matched as smaller sub-words instead, so that break opportunities
        /// stay dense enough for narrow game text boxes (a 20-character "word" cannot fit
        /// a 300px dialog box).
        /// </summary>
        public int MaxWordLength { get; set; } = 12;

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
    }
}

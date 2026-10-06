namespace ThaiWrap
{
    public enum TokenKind
    {
        /// <summary>Thai word matched from the dictionary.</summary>
        DictionaryWord,

        /// <summary>Unmatched Thai text (name, typo, neologism) kept as one unbreakable unit.</summary>
        UnknownRun,

        /// <summary>Cluster from a long unknown run; long runs get cluster-level break
        /// points so they can never overflow a narrow game text box.</summary>
        Cluster,

        /// <summary>Latin/digit run.</summary>
        NonThaiText,

        /// <summary>Non-Thai punctuation; attaches to the neighbouring token.</summary>
        Punctuation,

        /// <summary>Whitespace, including newlines — already a natural break point.</summary>
        Whitespace
    }

    public sealed class Token
    {
        public Token(string text, TokenKind kind)
        {
            Text = text;
            Kind = kind;
        }

        public string Text { get; }
        public TokenKind Kind { get; }

        /// <summary>ตัดขอบหลัง token นี้ได้ (ก้อน unknown ตัดรอบขอบได้แต่ไม่ตัดข้างใน — ตามแนวทาง W3)</summary>
        public bool IsBreakableAfter =>
            Kind == TokenKind.DictionaryWord || Kind == TokenKind.Cluster ||
            Kind == TokenKind.NonThaiText || Kind == TokenKind.UnknownRun;

        public bool IsBreakableBefore =>
            Kind == TokenKind.DictionaryWord || Kind == TokenKind.Cluster ||
            Kind == TokenKind.NonThaiText || Kind == TokenKind.UnknownRun;

        public override string ToString() => Kind + ":" + Text;
    }
}

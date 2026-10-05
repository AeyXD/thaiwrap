using System;

namespace ThaiWrap
{
    /// <summary>
    /// Character classification for Thai script (U+0E01..U+0E5B) used by the segmenter.
    /// Thai is written without spaces between words, so a line break may only be placed
    /// at word boundaries returned by the segmenter — never inside a character cluster.
    /// </summary>
    public static class ThaiChars
    {
        public const char ZeroWidthSpace = '\u200B';

        public static bool IsThaiRange(char c) => c >= '\u0E01' && c <= '\u0E5B';

        // ั ิ ี ึ ื ุ ู ฺ ็ ่ ้ ๊ ๋ ํ ๎ — vowel/tone marks written above or below the
        // preceding base character; splitting one off renders a broken cluster.
        public static bool IsCombiningMark(char c) =>
            c == '\u0E31' || (c >= '\u0E34' && c <= '\u0E3A') || (c >= '\u0E47' && c <= '\u0E4E');

        // เ แ โ ใ ไ — vowels written *before* their consonant; one must never be left
        // dangling at the end of a line separated from its consonant.
        public static bool IsLeadingVowel(char c) => c >= '\u0E40' && c <= '\u0E44';

        // ๆ (mai yamok) and ฯ (paiyannoi) attach to the preceding word.
        public static bool IsThaiPunctuation(char c) => c == '\u0E46' || c == '\u0E4F';

        public static bool IsWhitespace(char c) => char.IsWhiteSpace(c);

        public static bool IsAlnum(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
            (c >= '\u0E50' && c <= '\u0E59'); // Thai digits ๐-๙

        // A break right after these would leave an opening bracket or quote stranded
        // at the end of a line.
        public static bool NoBreakAfter(char c) =>
            c == '(' || c == '[' || c == '{' || c == '<' || c == '"' || c == '\'' ||
            c == '\u201C' || c == '\u2018';

        // A break right before these would strand closing punctuation at a line start.
        public static bool NoBreakBefore(char c) =>
            c == ')' || c == ']' || c == '}' || c == '>' || c == '"' || c == '\'' ||
            c == ',' || c == '.' || c == '!' || c == '?' || c == ';' || c == ':' ||
            c == '%' || c == '\u201D' || c == '\u2019' || c == '\u2026' ||
            IsThaiPunctuation(c) || IsCombiningMark(c);
    }
}

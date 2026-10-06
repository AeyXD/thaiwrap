using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ThaiWrap
{
    /// <summary>
    /// Segments mixed Thai/non-Thai text into tokens and inserts zero-width spaces
    /// (U+200B) at word boundaries so engines that wrap on whitespace get correct
    /// Thai line breaks without any engine modification.
    /// </summary>
    public sealed class ThaiSegmenter
    {
        private readonly HashSet<string> _words;
        private readonly ThaiWrapOptions _options;

        public ThaiSegmenter(IEnumerable<string> words, ThaiWrapOptions? options = null)
        {
            _options = options ?? new ThaiWrapOptions();
            _words = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in words)
            {
                var t = w?.Trim();
                if (t == null || t.Length == 0) continue;
                if (t.IndexOf(' ') >= 0 || t.IndexOf('\t') >= 0 || t.IndexOf('\r') >= 0 || t.IndexOf('\n') >= 0)
                    continue; // entries containing whitespace can never match space-free Thai text
                if (t.Length < 2) continue; // single characters create noise, not word boundaries
                _words.Add(t);
            }
        }

        public static ThaiSegmenter FromFile(string path, ThaiWrapOptions? options = null)
        {
            return new ThaiSegmenter(File.ReadAllLines(path), options);
        }

        public ThaiWrapOptions Options => _options;
        public int WordCount => _words.Count;

        /// <summary>Tokenizes the full text; tokens concatenate back to the original string.</summary>
        public List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            if (string.IsNullOrEmpty(text)) return tokens;

            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (ThaiChars.IsWhitespace(c))
                {
                    int j = i + 1;
                    while (j < n && ThaiChars.IsWhitespace(text[j])) j++;
                    tokens.Add(new Token(text.Substring(i, j - i), TokenKind.Whitespace));
                    i = j;
                }
                else if (ThaiChars.IsAlnum(c))
                {
                    int j = i + 1;
                    while (j < n && ThaiChars.IsAlnum(text[j])) j++;
                    tokens.Add(new Token(text.Substring(i, j - i), TokenKind.NonThaiText));
                    i = j;
                }
                else if (ThaiChars.IsThaiRange(c))
                {
                    int j = i;
                    while (j < n && ThaiChars.IsThaiRange(text[j])) j++;
                    TokenizeThaiRun(text, i, j, tokens);
                    i = j;
                }
                else
                {
                    int j = i + 1;
                    while (j < n)
                    {
                        char d = text[j];
                        if (ThaiChars.IsWhitespace(d) || ThaiChars.IsAlnum(d) || ThaiChars.IsThaiRange(d)) break;
                        j++;
                    }
                    tokens.Add(new Token(text.Substring(i, j - i), TokenKind.Punctuation));
                    i = j;
                }
            }
            return tokens;
        }

        /// <summary>Greedy longest-match segmentation of one space-free Thai run.</summary>
        private void TokenizeThaiRun(string text, int start, int end, List<Token> tokens)
        {
            var unknown = new List<string>();
            int i = start;
            while (i < end)
            {
                string? match = null;
                int limit = Math.Min(_options.MaxWordLength, end - i);
                for (int l = limit; l >= 2; l--)
                {
                    if (_words.Contains(text.Substring(i, l))) { match = text.Substring(i, l); break; }
                }

                if (match != null)
                {
                    FlushUnknown(tokens, unknown);
                    tokens.Add(new Token(match, TokenKind.DictionaryWord));
                    i += match.Length;
                }
                else
                {
                    // Consume one cluster: base character + following combining marks,
                    // optionally absorbing an attached ๆ / ฯ.
                    int j = i + 1;
                    while (j < end && ThaiChars.IsCombiningMark(text[j])) j++;
                    if (j < end && ThaiChars.IsThaiPunctuation(text[j])) j++;
                    unknown.Add(text.Substring(i, j - i));
                    i = j;
                }
            }
            FlushUnknown(tokens, unknown);
        }

        private void FlushUnknown(List<Token> tokens, List<string> clusters)
        {
            if (clusters.Count == 0) return;
            if (clusters.Count <= _options.UnknownBreakThreshold)
                tokens.Add(new Token(clusters.Count == 1 ? clusters[0] : string.Concat(clusters.ToArray()), TokenKind.UnknownRun));
            else
                foreach (var cl in clusters) tokens.Add(new Token(cl, TokenKind.Cluster));
            clusters.Clear();
        }

        /// <summary>Returns the text with U+200B inserted at safe Thai word boundaries.</summary>
        public string InsertZwsp(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var parts = SplitControlParts(text);
            if (parts.Count == 1 && !parts[0].Masked) return InsertZwspRaw(text);

            // ชิ้นที่ไม่ใช่ mask ถูกตัดคำแบบอิสระ — InsertZwspRaw แทรกเฉพาะระหว่าง token
            // จึงไม่มี ZWSP ใหม่หลุดไปติดขอบ mask และ ZWSP เดิมของผู้ใช้ถูกรักษาไว้ทั้งหมด
            var sb = new StringBuilder(text.Length + 16);
            foreach (var part in parts)
            {
                sb.Append(part.Masked ? part.Text : InsertZwspRaw(part.Text));
            }
            return sb.ToString();
        }

        private string InsertZwspRaw(string text)
        {
            var tokens = Tokenize(text);
            var sb = new StringBuilder(text.Length + tokens.Count);
            for (int k = 0; k < tokens.Count; k++)
            {
                var t = tokens[k];
                sb.Append(t.Text);
                if (k == tokens.Count - 1) break;
                var next = tokens[k + 1];
                if (ShouldInsertBetween(t, next)) sb.Append(ThaiChars.ZeroWidthSpace);
            }
            return sb.ToString();
        }

        /// <summary>Debug view: every token boundary except whitespace joins.</summary>
        public string SegmentToString(string text, string separator = "|")
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var parts = SplitControlParts(text);
            if (parts.Count == 1 && !parts[0].Masked) return SegmentToStringRaw(text, separator);

            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                sb.Append(part.Masked ? part.Text : SegmentToStringRaw(part.Text, separator));
            }
            return sb.ToString();
        }

        private string SegmentToStringRaw(string text, string separator)
        {
            var tokens = Tokenize(text);
            var sb = new StringBuilder();
            for (int k = 0; k < tokens.Count; k++)
            {
                sb.Append(tokens[k].Text);
                if (k < tokens.Count - 1 &&
                    tokens[k].Kind != TokenKind.Whitespace &&
                    tokens[k + 1].Kind != TokenKind.Whitespace)
                    sb.Append(separator);
            }
            return sb.ToString();
        }

        private struct ControlPart
        {
            public string Text;
            public bool Masked;
        }

        /// <summary>
        /// แยกส่วนควบคุมของเกมออกจากข้อความที่ต้องตัดคำ:
        /// {placeholder} และ &lt;rich-text tag&gt; ต้องไม่ถูกแทรก ZWSP เด็ดขาด
        /// (แม้ข้างในมีตัวอักษรไทย) เพราะการแทนค่า/อ้างอิงแท็กจะเสีย
        /// </summary>
        private List<ControlPart> SplitControlParts(string text)
        {
            var parts = new List<ControlPart>();
            if (!_options.ProtectPlaceholders) return new List<ControlPart> { new ControlPart { Text = text } };

            var sb = new StringBuilder();
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (c == '{' || c == '<')
                {
                    char close = c == '{' ? '}' : '>';
                    int j = i + 1;
                    while (j < n && text[j] != close && text[j] != '\n' && text[j] != '\r') j++;
                    if (j < n && text[j] == close) // พบตัวปิดในบรรทัดเดียวกัน → mask ทั้งช่วง
                    {
                        if (sb.Length > 0) { parts.Add(new ControlPart { Text = sb.ToString() }); sb.Length = 0; }
                        parts.Add(new ControlPart { Text = text.Substring(i, j - i + 1), Masked = true });
                        i = j + 1;
                        continue;
                    }
                }
                sb.Append(c);
                i++;
            }
            if (sb.Length > 0) parts.Add(new ControlPart { Text = sb.ToString() });
            return parts;
        }

        private static bool ShouldInsertBetween(Token t, Token next)
        {
            if (!t.IsBreakableAfter || !next.IsBreakableBefore) return false;

            char last = t.Text[t.Text.Length - 1];
            char first = next.Text[0];

            if (ThaiChars.NoBreakAfter(last)) return false;
            if (ThaiChars.IsLeadingVowel(last)) return false; // never strand เ แ โ ใ ไ at line end
            if (ThaiChars.NoBreakBefore(first)) return false;
            return true;
        }
    }
}

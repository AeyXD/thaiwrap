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
        private readonly ThaiWrapOptions _options;
        private readonly TrieNode _root = new TrieNode();
        private readonly int _wordCount;

        private const int ShortWord = 2;
        private const int Inf = int.MaxValue / 2;

        public ThaiSegmenter(IEnumerable<string> words, ThaiWrapOptions? options = null)
        {
            _options = options ?? new ThaiWrapOptions();
            int count = 0;
            foreach (var w in words)
            {
                var t = w?.Trim();
                if (t == null || t.Length == 0) continue;
                if (t.IndexOf(' ') >= 0 || t.IndexOf('\t') >= 0 || t.IndexOf('\r') >= 0 || t.IndexOf('\n') >= 0)
                    continue; // entries containing whitespace can never match space-free Thai text
                if (t.Length < 2) continue; // single characters create noise, not word boundaries
                if (_options.MaxWordLength > 0 && t.Length > _options.MaxWordLength) continue;

                var node = _root;
                foreach (char c in t)
                {
                    node.Children ??= new Dictionary<char, TrieNode>();
                    if (!node.Children.TryGetValue(c, out var next))
                    {
                        next = new TrieNode();
                        node.Children[c] = next;
                    }
                    node = next;
                }
                node.End = true;
                count++;
            }
            _wordCount = count;
        }

        private sealed class TrieNode
        {
            public Dictionary<char, TrieNode>? Children;
            public bool End;
        }

        public static ThaiSegmenter FromFile(string path, ThaiWrapOptions? options = null)
        {
            return new ThaiSegmenter(File.ReadAllLines(path), options);
        }

        public ThaiWrapOptions Options => _options;
        public int WordCount => _wordCount;

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

        /// <summary>
        /// แบ่ง Thai run ด้วย DP (dynamic programming) — หาการตัดคำที่ minimizes
        /// (จำนวนตัวอักษรนอกพจนานุกรม, จำนวนคำ) พร้อมกฎพยัญชนะทวิภาค
        /// อัลกอริทึม port จาก ThaiW3Setup (core/thai_wrap.py, MIT) ซึ่งพิสูจน์แล้วใน production
        /// ว่า precision ของจุดตัด 100% เทียบ newmm
        /// </summary>
        private void TokenizeThaiRun(string text, int start, int end, List<Token> tokens)
        {
            string run = text.Substring(start, end - start);
            int n = run.Length;

            // ok[i] = อนุญาตให้ตัดที่ตำแหน่ง i (ระหว่าง run[i-1] กับ run[i])
            var ok = new bool[n + 1];
            for (int i = 0; i <= n; i++)
                ok[i] = i == 0 || i == n ||
                        (!ThaiChars.NoBreakBeforeSegment(run[i]) && !ThaiChars.NoBreakAfterSegment(run[i - 1]));

            var bestUnknown = new int[n + 1];
            var bestWords = new int[n + 1];
            var prevStart = new int[n + 1];
            var prevIsWord = new bool[n + 1];
            for (int i = 0; i <= n; i++) { bestUnknown[i] = Inf; bestWords[i] = Inf; }
            bestUnknown[0] = 0; bestWords[0] = 0;

            for (int i = 0; i < n; i++)
            {
                if (bestUnknown[i] == Inf || !ok[i]) continue;
                int u = bestUnknown[i], c = bestWords[i];

                // ทางเลือกที่ 1: คำจากพจนานุกรม (เดินตาม trie จากตำแหน่ง i)
                var node = _root;
                int j = i;
                while (j < n && node.Children != null && node.Children.TryGetValue(run[j], out var next))
                {
                    node = next;
                    j++;
                    if (node.End && ok[j])
                    {
                        int nu = u, nw = c + 1;
                        if (nu < bestUnknown[j] || (nu == bestUnknown[j] && nw < bestWords[j]))
                        {
                            bestUnknown[j] = nu; bestWords[j] = nw;
                            prevStart[j] = i; prevIsWord[j] = true;
                        }
                    }
                }

                // ทางเลือกที่ 2: ก้อน unknown จาก i ถึงตำแหน่งถัดไปที่ตัดได้
                int k = i + 1;
                while (!ok[k]) k++;
                int uu = u + (k - i), cw = c + 1;
                if (uu < bestUnknown[k] || (uu == bestUnknown[k] && cw < bestWords[k]))
                {
                    bestUnknown[k] = uu; bestWords[k] = cw;
                    prevStart[k] = i; prevIsWord[k] = false;
                }
            }

            // backtrack → parts
            var parts = new List<string>();
            var isWords = new List<bool>();
            int p = n;
            while (p > 0)
            {
                int s = prevStart[p];
                parts.Add(run.Substring(s, p - s));
                isWords.Add(prevIsWord[p]);
                p = s;
            }
            parts.Reverse();
            isWords.Reverse();

            // รวมก้อน unknown เข้ากับคำสั้นที่ติดกัน (กันจุดตัดที่ดูแปลก เช่น ท|ริ|สส์ → ทริสส์)
            // mirror ของ merge pass ใน ThaiW3Setup core/thai_wrap.py
            string pendingUnknown = "";
            for (int idx = 0; idx < parts.Count; idx++)
            {
                string partText = parts[idx];
                bool word = isWords[idx];
                bool nearUnknown = (idx > 0 && !isWords[idx - 1]) || (idx + 1 < parts.Count && !isWords[idx + 1]);
                bool unknown = !word || (partText.Length <= ShortWord && nearUnknown);

                if (unknown)
                {
                    pendingUnknown += partText;
                }
                else
                {
                    if (pendingUnknown.Length > 0) { AddUnknown(tokens, pendingUnknown); pendingUnknown = ""; }
                    tokens.Add(new Token(partText, TokenKind.DictionaryWord));
                }
            }
            if (pendingUnknown.Length > 0) AddUnknown(tokens, pendingUnknown);
        }

        /// <summary>ก้อน unknown: สั้นเก็บเป็นชิ้นเดียว ยาวเกิน threshold ยอมให้ตัดระดับ cluster กันล้นกรอบ</summary>
        private void AddUnknown(List<Token> tokens, string chunk)
        {
            if (chunk.Length == 0) return;
            if (CountClusters(chunk) <= _options.UnknownBreakThreshold)
            {
                tokens.Add(new Token(chunk, TokenKind.UnknownRun));
                return;
            }
            int i = 0;
            while (i < chunk.Length)
            {
                int j = ClusterEnd(chunk, i);
                tokens.Add(new Token(chunk.Substring(i, j - i), TokenKind.Cluster));
                i = j;
            }
        }

        /// <summary>
        /// จุดจบ cluster ที่ตำแหน่ง i — ใช้กฎขอบเขตเดียวกับ DP: อักขระที่เปิด segment ใหม่ไม่ได้
        /// (สระจ่อย วรรณยุกต์ ๆ ฯ ะ า ำ) ติดกับ cluster ก่อนหน้าเสมอ และถ้า cluster จบที่สระหน้า
        /// (เ แ โ ใ ไ) ต้องกลืนพยัญชนะถัดไปด้วย ไม่ให้สระหน้าค้างท้ายชิ้น
        /// </summary>
        private static int ClusterEnd(string s, int i)
        {
            int j = i + 1;
            bool extended = true;
            while (extended)
            {
                extended = false;
                while (j < s.Length && (ThaiChars.IsCombiningMark(s[j]) || ThaiChars.NoBreakBeforeSegment(s[j]))) j++;
                if (j < s.Length && ThaiChars.IsLeadingVowel(s[j - 1]))
                {
                    j++;
                    extended = true;
                }
            }
            return j;
        }

        private static int CountClusters(string s)
        {
            int count = 0, i = 0;
            while (i < s.Length)
            {
                i = ClusterEnd(s, i);
                count++;
            }
            return count;
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
                if (ShouldInsertBetween(t, next)) sb.Append(_options.BreakChar);
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

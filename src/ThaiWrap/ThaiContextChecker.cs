using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ThaiWrap
{
    public enum SuggestionKind
    {
        /// <summary>พิมพ์สลับ ร↔ล (บุกลุก → บุกรุก) หรือคำผิดทั่วไปที่รู้คำแก้ (รุงแรง → รุนแรง)</summary>
        RalSwap,

        /// <summary>สะกดถูกเป็นคำจริง แต่ผิดความหมายในบริบท (เข่าไปใน → เข้าไปใน)</summary>
        WrongWord
    }

    /// <summary>ข้อเสนอแก้คำหนึ่งจุด — ตำแหน่งอ้างอิงข้อความที่ส่งเข้ามาตรวจ</summary>
    public sealed class Suggestion
    {
        public Suggestion(string wrong, string suggested, int start, SuggestionKind kind)
        {
            Wrong = wrong;
            Suggested = suggested;
            Start = start;
            Kind = kind;
        }

        public string Wrong { get; }
        public string Suggested { get; }
        public int Start { get; }
        public int End => Start + Wrong.Length;
        public SuggestionKind Kind { get; }
    }

    /// <summary>
    /// ข้อยกเว้นของโปรเจกต์ (check_allow.txt วางที่เดียวกับ words_extra.txt) — รูปที่ทีมตรวจแล้วว่า**ถูก**
    /// ชื่อเฉพาะ/ข้อยกเว้นของแต่ละเกมอยู่ที่ไฟล์นี้ ไม่ใส่ในเครื่องมือกลาง · รูปแบบบรรทัด:
    /// <c>คำ</c> = ไม่รายงานรูปนี้เลย · <c>ได้[ลับ]มีด</c> = ไม่รายงาน ลับ เฉพาะในวลีนี้ ·
    /// <c>ม้า&gt;มา</c> = ไม่เสนอ มา แทน ม้า · ขึ้นต้น <c>#</c> หรือว่าง = ข้าม
    /// ทุกบรรทัดควรมีที่มากำกับในคอมเมนต์ (id / บริบท / ต้นฉบับ) — รูปที่ไม่มีที่มาเคยทำให้คำผิดรอด
    /// (จราจล/รำรึก เคยอยู่ใน ALLOW ของ W3)
    /// </summary>
    public sealed class CheckAllow
    {
        private readonly HashSet<string> _forms = new HashSet<string>();
        private readonly HashSet<string> _pairs = new HashSet<string>();
        private readonly Dictionary<string, List<KeyValuePair<string, string>>> _phrases =
            new Dictionary<string, List<KeyValuePair<string, string>>>();

        public int Count { get; private set; }

        public static CheckAllow Load(string path) => Parse(File.ReadAllLines(path));

        public static CheckAllow Parse(IEnumerable<string> lines)
        {
            var allow = new CheckAllow();
            foreach (var line in lines)
            {
                string t = line.Trim();
                if (t.Length == 0 || t[0] == '#') continue;
                int open = t.IndexOf('['), close = t.IndexOf(']');
                int gt = t.IndexOf('>');
                if (open >= 0 && close > open + 1)
                {
                    string word = t.Substring(open + 1, close - open - 1);
                    if (!allow._phrases.TryGetValue(word, out var list))
                        allow._phrases[word] = list = new List<KeyValuePair<string, string>>();
                    list.Add(new KeyValuePair<string, string>(t.Substring(0, open), t.Substring(close + 1)));
                }
                else if (gt > 0 && gt < t.Length - 1)
                {
                    allow._pairs.Add(PairKey(t.Substring(0, gt).Trim(), t.Substring(gt + 1).Trim()));
                }
                else
                {
                    allow._forms.Add(t);
                }
                allow.Count++;
            }
            return allow;
        }

        public bool AllowsForm(string form) => _forms.Contains(form);

        public bool AllowsPair(string wrong, string suggested) => _pairs.Contains(PairKey(wrong, suggested));

        /// <summary>รูป wrong ที่ตำแหน่ง start อยู่ในวลีที่ทีมยืนยันว่าถูกหรือไม่</summary>
        public bool AllowsAt(string wrong, string text, int start)
        {
            if (!_phrases.TryGetValue(wrong, out var list)) return false;
            foreach (var p in list)
            {
                int from = start - p.Key.Length, end = start + wrong.Length;
                if (from < 0 || end + p.Value.Length > text.Length) continue;
                if (string.CompareOrdinal(text, from, p.Key, 0, p.Key.Length) == 0 &&
                    string.CompareOrdinal(text, end, p.Value, 0, p.Value.Length) == 0)
                    return true;
            }
            return false;
        }

        internal static string PairKey(string a, string b) => a + "\u0001" + b;
    }

    /// <summary>
    /// ตัวตรวจคำผิดที่ตัวตรวจคำสะกดทั่วไปจับไม่ได้ เพราะทุกชิ้นเป็นคำจริง — ผลทั้งหมดเป็น**ข้อเสนอ**
    /// ให้คนตรวจบริบทก่อนแก้เสมอ ไม่มีการแก้อัตโนมัติ · พอร์ตจาก ThaiW3Setup (MIT) branch devtools-checkers:
    ///
    /// 1. <see cref="CheckRal"/> — find_ral_swaps.py: token เดี่ยวที่ไม่อยู่ในพจนานุกรมแต่สลับ ร↔ล
    ///    หนึ่งตำแหน่งแล้วเป็นคำ และคำประสม 2–3 token ที่สลับแล้วเป็นคำ (บุก+ลุก → บุกรุก) ·
    ///    กันจับผิด: การ นำหน้าคำถัดไป (ตลอดการเดินทาง ≠ ตลอดกาล) · ...ละ ที่เป็นต้นคำถัดไป (มาละลาย)
    ///    · ไม่...หรอก (คำลงท้ายปฏิเสธ ไม่ใช่ หลอก) · คู่คำที่ต้องดูบริบท (สมุนไหร → สมุนไพร)
    /// 2. <see cref="CheckWrongWord"/> — find_wrong_word.py: ชุดคำสับสน (วรรณยุกต์ต่าง · สระ ั/ะ/า ·
    ///    ร/ล ตัวท้าย) + สถิติคู่คำของ**คลังที่ตรวจเอง** (<see cref="AddCorpus"/>): รายงานเมื่อคำเดิม
    ///    แทบไม่เคยอยู่คู่เพื่อนบ้านนี้ (≤ 1 ครั้ง ทั้งสองด้าน) ขณะที่คู่สับสนอยู่คู่แบบเดียวกันแน่นอน (≥ 5 ครั้ง)
    ///    — ได้ผลเมื่อคลังใหญ่ (ทั้งโฟลเดอร์งานแปล) ไฟล์เล็กไฟล์เดียวแทบไม่มีผล
    ///
    /// ส่งต้นฉบับอังกฤษมาด้วย (ถ้ามี) จะตัดข้อเสนอที่ต้นฉบับขัด — <see cref="ThaiEnglishSense"/> (en_sense.py)
    /// สิ่งที่**ไม่**พอร์ต: ALLOW/CONFIRMED_IN ที่เป็นชื่อเฉพาะหรือคำตัดสินของ W3 (อยู่ใน check_allow.txt
    /// ของแต่ละโปรเจกต์แทน)
    /// ตัดคำด้วย segmenter ของตัวเองที่ไม่รวมกลุ่มวลี (MaxChunk) — ผลไม่ขึ้นกับโหมด break char
    /// </summary>
    public sealed class ThaiContextChecker
    {
        // ---- find_wrong_word: เกณฑ์ ----
        private const int MaxMine = 1;     // คำเดิมแทบไม่เคยอยู่บริบทนี้ (typo ชอบซ้ำกันเองในคลัง)
        private const int MinSupport = 5;  // คู่สับสนต้องอยู่บริบทนี้อย่างน้อยเท่านี้จึงนับว่าแน่นอน
        private const string Tones = "่้๊๋";

        // ---- find_ral_swaps: รูปที่ถูกต้องแต่กลไกจับคร่อมขอบ token (คำประสม/สำนวนทั่วไป ไม่ผูกกับเกมใด)
        private static readonly HashSet<string> RalAllow = new HashSet<string>
        {
            "ลางๆ", "การแปล", "การแปลง", "ทำรายได้", "ทำราย", "การรุก", "ถั่วและ", "โดยลอบ", "ลับได้",
            "ที่รับ", "นางกลาย", "จะกระ", "รับตัว", "ไร่ที่", "ไรเลย", "ว่ากาล", "การลด", "ดำลง", "เป็นลัง",
            "ติดแล้ว", "ที่ล้าง", "ถือพร", "การลัด", "ละดู", "กันไกล", "ลับสัมผัส", "ดูกล", "ดีระ", "ร่วงเลย",
            "ให้พล", "การออก", "ลังแรก", "รูนะ", "การอ",
            // ลากเสียง/สำนวนซ้ำคำ (รออออ, หลบๆ ซ่อนๆ, อร่อยๆ) · คำในพจนานุกรมราชบัณฑิตฯ ที่ dict ยังไม่มี
            "รออออ", "รออ", "หลบๆ", "ร่อยๆ", "อิ่มแปล้", "ขาดรุ่ย", "ทั้งกรม", "หินแรง",
        };

        // คำผิดที่รู้คำแก้ซึ่งไม่ได้มาจากการสลับ — ใช้แทนข้อเสนอของกลไกสลับ
        private static readonly Dictionary<string, string> Hints = new Dictionary<string, string>
        {
            { "รุง", "รุน" }, { "ลักษ์", "ลักษณ์" }, { "ลังค์", "ลังก์" }, { "โคลม", "โคลน" },
            { "อารมเสีย", "อารมณ์เสีย" }, { "โรงละคน", "โรงละคร" }, { "ตระกลู", "ตระกูล" }, { "ร้ำ", "ร่ำ" },
            { "แกรง", "แกร่ง" }, { "เกรา", "เกราะ" }, { "นักเรีย", "นักเรียน" }, { "บรอนด์", "บลอนด์" },
            { "กลู", "กูล" },
        };

        // คู่ token ที่คำแก้ต้องอาศัยบริบท: ไหร หลัง เท่า = ไหร่ แต่ หลัง สมุน = สมุนไพร
        private static readonly Dictionary<string, string> ContextHints = new Dictionary<string, string>
        {
            { CheckAllow.PairKey("เท่า", "ไหร"), "เท่าไหร่" },
            { CheckAllow.PairKey("ประมาณ", "ไหร"), "ประมาณไหร่" },
            { CheckAllow.PairKey("สมุน", "ไหร"), "สมุนไพร" },
        };

        // token ที่ไม่มีบริบทกำกับแล้วห้ามเสนอ (เสนอผิดง่ายกว่าเสนอถูก)
        private static readonly HashSet<string> ContextOnly = new HashSet<string> { "ไหร" };

        // คำลงท้ายที่ตามหลัง ตลอดกาล/เฉพาะกาล ได้ แต่ไม่ใช่คำที่ การ นำหน้า: ตลอดการเลย = ตลอดกาลเลย
        private static readonly HashSet<string> Particles = new HashSet<string>
        {
            "เลย", "นะ", "น่ะ", "ล่ะ", "แล้ว", "ไป", "ด้วย", "จ้ะ", "ค่ะ", "ครับ", "เท่านั้น",
        };

        // find_wrong_word: คู่ที่ใช้ได้ทั้งสองรูปตามสไตล์ภาษาพูด (คำลงท้าย · การสะกดเน้นเสียง)
        private static readonly HashSet<string> WordAllowPairs = BuildPairs(
            "นั่น>นั้น", "นั้น>นั่น", "น่ะ>นะ", "นะ>น่ะ", "ล่ะ>ละ", "ละ>ล่ะ", "นี่>นี้", "นี้>นี่",
            "จ๊ะ>จะ", "จ้ะ>จะ", "มั้ย>ม่าย", "มั้ง>มั่ง", "มั่ง>มั้ง", "เอ่อ>เออ", "เอ้ย>เอ๊ย", "เอ่ย>เอ๊ย",
            "แหง๋>แหง", "วะ>ว่ะ", "ว่ะ>วะ", "อา>อ่า", "ไม๊>ไม่", "นั่นแหละ>นั้นแหละ", "นี่แหละ>นี้แหละ",
            "นี้แหละ>นี่แหละ", "อ่ะ>อะ", "อะ>อ่ะ", "อ้า>อ่า", "ม๊าก>มาก", "เล้ย>เลย", "ว้า>ว่า", "ต๊อง>ต้อง",
            "ดัง>ดั่ง");

        private readonly ThaiSegmenter _seg;
        private readonly CheckAllow _allow;
        private readonly List<string> _laWords;
        private Dictionary<string, List<string>>? _confusions;
        private readonly Dictionary<string, int> _bigrams = new Dictionary<string, int>();

        /// <param name="words">word list เดียวกับที่ใช้ตัดคำ (รวม words_extra แล้ว)</param>
        /// <param name="allow">ข้อยกเว้นของโปรเจกต์ (null = ไม่มี)</param>
        public ThaiContextChecker(IEnumerable<string> words, CheckAllow? allow = null)
        {
            _seg = new ThaiSegmenter(words); // ตัวเลือกปริยาย — ไม่รวมกลุ่มวลี ไม่ normalize
            _allow = allow ?? CheckAllow.Parse(new string[0]);
            _laWords = _seg.EnumerateWords().Where(w => w.StartsWith("ละ", StringComparison.Ordinal) && w.Length >= 4).ToList();
        }

        // ================= ร↔ล =================

        /// <param name="english">ต้นฉบับอังกฤษของข้อความนี้ (ถ้ามี) — ใช้ตัดข้อเสนอที่จับผิดด้วย <see cref="ThaiEnglishSense"/></param>
        public List<Suggestion> CheckRal(string text, string? english = null)
        {
            var hits = new List<Suggestion>();
            foreach (var run in ThaiRuns(text))
            {
                var toks = run.Tokens;
                var starts = run.Starts;
                // พาส 0: ข้อเสนอที่ต้องมีบริบทกำกับ (สมุนไหร → สมุนไพร)
                for (int i = 1; i < toks.Count; i++)
                {
                    if (ContextHints.TryGetValue(CheckAllow.PairKey(toks[i - 1], toks[i]), out var fix))
                        hits.Add(new Suggestion(toks[i - 1] + toks[i], fix, starts[i - 1], SuggestionKind.RalSwap));
                }
                // พาส 1: token เดี่ยว
                for (int i = 0; i < toks.Count; i++)
                {
                    string tok = toks[i];
                    if (tok.Length < 3 || _seg.IsKnownWord(tok) || IsAllowedForm(tok) || ContextOnly.Contains(tok)) continue;
                    string? v = FirstKnownSwap(tok);
                    if (v != null) hits.Add(new Suggestion(tok, Hint(tok, v), starts[i], SuggestionKind.RalSwap));
                }
                // พาส 2: คำประสม 2–3 token ติดกัน
                for (int span = 2; span <= 3; span++)
                {
                    for (int i = 0; i + span <= toks.Count; i++)
                    {
                        bool longEnough = true;
                        for (int k = i; k < i + span; k++) if (toks[k].Length < 2) { longEnough = false; break; }
                        if (!longEnough) continue;
                        string joined = string.Concat(toks.GetRange(i, span).ToArray());
                        if (_seg.IsKnownWord(joined) || IsAllowedForm(joined)) continue;
                        // การ นำหน้าคำถัดไป: ตลอดการเดินทาง เฉพาะการบันทึก ไม่ใช่ ตลอดกาล เฉพาะกาล
                        if (toks[i + span - 1] == "การ" && i + span < toks.Count && !Particles.Contains(toks[i + span])) continue;
                        string? v = FirstKnownSwap(joined);
                        if (v != null) hits.Add(new Suggestion(joined, Hint(joined, v), starts[i], SuggestionKind.RalSwap));
                    }
                }
            }

            var kept = hits
                .Where(h => !StartsLaWord(h, text))       // เอามาละลายน้ำ = มา + ละลาย ไม่ใช่ มาล่ะ
                .Where(h => !NegatedParticle(h, text))    // ไม่ได้เผาหรอก = คำลงท้าย ไม่ใช่ หลอก
                .Where(h => !Allowed(h, text))
                .Where(h => !ThaiEnglishSense.RuledOut(h.Wrong, h.Suggested, english)) // รุกขึ้นเหนือ = push north
                .ToList();
            return DropNested(kept);
        }

        private string? FirstKnownSwap(string tok)
        {
            var chars = tok.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c != 'ร' && c != 'ล') continue; // ร / ล
                chars[i] = c == 'ร' ? 'ล' : 'ร';
                string v = new string(chars);
                chars[i] = c;
                if (_seg.IsKnownWord(v)) return v;
            }
            return null;
        }

        private static string Hint(string wrong, string swapped) => Hints.TryGetValue(wrong, out var h) ? h : swapped;

        private bool IsAllowedForm(string form) => RalAllow.Contains(form) || _allow.AllowsForm(form);

        /// <summary>รูปที่ลงท้าย ละ แต่ ละ นั้นต่อกับข้อความถัดไปเป็นคำ (ละลาย ละเอียด ละคร)</summary>
        private bool StartsLaWord(Suggestion h, string text)
        {
            if (!h.Wrong.EndsWith("ละ", StringComparison.Ordinal)) return false;
            int from = h.End - 2;
            foreach (var w in _laWords)
                if (from + w.Length <= text.Length && string.CompareOrdinal(text, from, w, 0, w.Length) == 0) return true;
            return false;
        }

        /// <summary>...หรอก ที่มี ไม่ นำหน้าในวลีเดียวกัน (ช่วงอักษรไทยติดกันก่อนหน้า) คือคำลงท้ายปฏิเสธ</summary>
        private static bool NegatedParticle(Suggestion h, string text)
        {
            if (!h.Wrong.EndsWith("หรอก", StringComparison.Ordinal)) return false;
            int k = h.Start;
            while (k > 0 && IsThaiBlock(text[k - 1])) k--;
            return text.Substring(k, h.Start - k).IndexOf("ไม่", StringComparison.Ordinal) >= 0;
        }

        // ================= สะกดถูกแต่ผิดบริบท =================

        /// <summary>เพิ่มข้อความเข้าคลังสถิติคู่คำ — เรียกกับ**ทุกข้อความ**ก่อน <see cref="CheckWrongWord"/></summary>
        public void AddCorpus(string text)
        {
            foreach (var run in ThaiRuns(text))
            {
                var toks = run.Tokens;
                for (int i = 0; i + 1 < toks.Count; i++)
                {
                    string key = CheckAllow.PairKey(toks[i], toks[i + 1]);
                    _bigrams.TryGetValue(key, out int n);
                    _bigrams[key] = n + 1;
                }
            }
        }

        /// <param name="english">ต้นฉบับอังกฤษของข้อความนี้ (ถ้ามี) — ขาพาไปไหนก็ไป + "wherever our legs carry us" = ขา ถูก</param>
        public List<Suggestion> CheckWrongWord(string text, string? english = null)
        {
            var confusions = _confusions ??= BuildConfusions();
            var hits = new List<Suggestion>();
            foreach (var run in ThaiRuns(text))
            {
                var toks = run.Tokens;
                if (toks.Count < 2) continue; // ช่วงคำเดียวไม่มีเพื่อนบ้านให้เทียบ
                for (int i = 0; i < toks.Count; i++)
                {
                    string tok = toks[i];
                    if (!confusions.TryGetValue(tok, out var alts)) continue;
                    // ที ท้ายช่วงข้อความ = คำขอร้อง/จังหวะ (ช่วยนางที!) ไม่ใช่ ที่ ที่ตกไม้เอก
                    if (tok == "ที" && i == toks.Count - 1) continue;
                    string prev = i > 0 ? toks[i - 1] : "", next = i + 1 < toks.Count ? toks[i + 1] : "";
                    int mineP = Bigram(prev, tok), mineN = Bigram(tok, next);
                    // เกตใช้ max: การหายากต้องเป็นทั้งสองด้าน หลักฐานด้านเดียวไม่พอฟันธง
                    if (Math.Max(mineP, mineN) > MaxMine) continue;
                    string? best = null;
                    int support = 0;
                    foreach (var alt in alts)
                    {
                        if (WordAllowPairs.Contains(CheckAllow.PairKey(tok, alt)) || _allow.AllowsPair(tok, alt)) continue;
                        int aP = Bigram(prev, alt), aN = Bigram(alt, next);
                        if (mineP <= MaxMine && aP > support) { best = alt; support = aP; }
                        if (mineN <= MaxMine && aN > support) { best = alt; support = aN; }
                    }
                    if (best == null || support < MinSupport) continue;
                    var h = new Suggestion(tok, best, run.Starts[i], SuggestionKind.WrongWord);
                    if (!Allowed(h, text) && !ThaiEnglishSense.RuledOut(tok, best, english)) hits.Add(h);
                }
            }
            return hits;
        }

        private int Bigram(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            _bigrams.TryGetValue(CheckAllow.PairKey(a, b), out int n);
            return n;
        }

        /// <summary>คู่สับสนจากพจนานุกรม: คำจริงที่ต่างกันเพียงวรรณยุกต์ · สระ ั/ะ/า · ร/ล ตัวท้าย</summary>
        private Dictionary<string, List<string>> BuildConfusions()
        {
            var groups = new Dictionary<string, List<string>>();
            foreach (var w in _seg.EnumerateWords())
            {
                if (w.Length > 10 || !AllThai(w)) continue;
                foreach (var key in Skeletons(w))
                {
                    if (!groups.TryGetValue(key, out var members)) groups[key] = members = new List<string>();
                    members.Add(w);
                }
            }
            var sets = new Dictionary<string, HashSet<string>>();
            foreach (var members in groups.Values)
            {
                if (members.Count < 2) continue;
                foreach (var w in members)
                {
                    if (!sets.TryGetValue(w, out var s)) sets[w] = s = new HashSet<string>();
                    foreach (var o in members) if (o != w) s.Add(o);
                }
            }
            // เรียงลำดับคงที่ — เสมอกันแล้วเลือกตัวแรกเสมอ ผลไม่ขึ้นกับลำดับใน hash
            var result = new Dictionary<string, List<string>>();
            foreach (var kv in sets)
            {
                var list = kv.Value.ToList();
                list.Sort(StringComparer.Ordinal);
                result[kv.Key] = list;
            }
            return result;
        }

        private static IEnumerable<string> Skeletons(string word)
        {
            var sb = new System.Text.StringBuilder(word.Length);
            foreach (char c in word) if (Tones.IndexOf(c) < 0) sb.Append(c);
            string toneless = sb.ToString();
            yield return "tone\u0001" + toneless;
            if (word.Length >= 4)
            {
                yield return "vowel\u0001" + toneless.Replace('ะ', 'า').Replace('ั', 'า'); // ะ ั → า
                yield return "final\u0001" + (toneless.EndsWith("ร", StringComparison.Ordinal)
                    ? toneless.Substring(0, toneless.Length - 1) + "ล" : toneless);                     // ร$ → ล
            }
        }

        // ================= ร่วม =================

        private bool Allowed(Suggestion h, string text) =>
            _allow.AllowsForm(h.Wrong) || _allow.AllowsPair(h.Wrong, h.Suggested) || _allow.AllowsAt(h.Wrong, text, h.Start);

        /// <summary>ตัด hit ซ้ำและ hit ที่ซ้อนอยู่ใน hit ที่ยาวกว่า (เด็กกำพล้า ครอบ กำพล้า → รายงานเดียว)</summary>
        private static List<Suggestion> DropNested(List<Suggestion> hits)
        {
            var sorted = hits
                .GroupBy(h => h.Start + "\u0001" + h.Wrong + "\u0001" + h.Suggested).Select(g => g.First())
                .OrderBy(h => h.Start).ThenByDescending(h => h.Wrong.Length)
                .ThenBy(h => h.Wrong, StringComparer.Ordinal).ThenBy(h => h.Suggested, StringComparer.Ordinal)
                .ToList();
            var kept = new List<Suggestion>();
            foreach (var h in sorted)
                if (!kept.Any(k => k.Start <= h.Start && h.End <= k.End && k.Wrong != h.Wrong)) kept.Add(h);
            return kept;
        }

        private sealed class Run
        {
            public readonly List<string> Tokens = new List<string>();
            public readonly List<int> Starts = new List<int>();
        }

        /// <summary>
        /// ตัดคำแยกทีละช่วงอักษรไทย (U+0E00–U+0E7F) — ถ้าตัดทั้งข้อความก้อนเดียว เครื่องหมาย/แท็กที่ติดคำ
        /// (บุกลุก! · &lt;b&gt;เข้า&lt;/b&gt;) จะเกาะเป็น token เดียวแล้วหลุดเงื่อนไขภาษาไทย (บทเรียนจาก W3)
        /// </summary>
        private IEnumerable<Run> ThaiRuns(string text)
        {
            int i = 0, n = text.Length;
            while (i < n)
            {
                if (!IsThaiBlock(text[i])) { i++; continue; }
                int j = i;
                while (j < n && IsThaiBlock(text[j])) j++;
                var run = new Run();
                int pos = i;
                foreach (var t in _seg.Tokenize(text.Substring(i, j - i)))
                {
                    run.Tokens.Add(t.Text);
                    run.Starts.Add(pos);
                    pos += t.Text.Length;
                }
                yield return run;
                i = j;
            }
        }

        private static bool IsThaiBlock(char c) => c >= '฀' && c <= '๿';

        private static bool AllThai(string w)
        {
            foreach (char c in w) if (!IsThaiBlock(c)) return false;
            return true;
        }

        private static HashSet<string> BuildPairs(params string[] pairs)
        {
            var set = new HashSet<string>();
            foreach (var p in pairs)
            {
                int gt = p.IndexOf('>');
                set.Add(CheckAllow.PairKey(p.Substring(0, gt), p.Substring(gt + 1)));
            }
            return set;
        }
    }
}

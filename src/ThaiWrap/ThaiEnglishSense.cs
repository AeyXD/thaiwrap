using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ThaiWrap
{
    /// <summary>
    /// ใช้ภาษาอังกฤษต้นฉบับตัดข้อเสนอของ <see cref="ThaiContextChecker"/> ที่จับผิด — พอร์ตจาก en_sense.py
    /// ของ ThaiW3Setup (MIT, branch devtools-checkers) · คู่สับสนเป็นคำจริงทั้งคู่ (ขา/ข้า, เข่า/เขา, รุก/ลุก)
    /// บริบทไทยอย่างเดียวจึงตัดสินไม่ได้ แต่ต้นฉบับบอกความหมายได้ชัด: "wherever our legs carry us" → ขา ถูก
    ///
    /// หลักตัดสิน (ใช้เมื่อมีภาษาอังกฤษของข้อความนั้นเท่านั้น):
    /// 1. ความหมายของ**คำเดิม**ปรากฏในอังกฤษ → คำเดิมถูก ไม่ต้องเสนอ
    /// 2. ความหมายของ**คำที่เสนอ**ไม่ปรากฏในอังกฤษเลย → ไม่มีหลักฐานให้เปลี่ยน ไม่ต้องเสนอ
    ///
    /// คำที่ไม่มีใน Senses ไม่ถูกตัด (ปล่อยให้คนตรวจตามเดิม) · คำที่ไม่ผูกกับคำอังกฤษใดชัด ๆ (ข้า, ไอ้, ที่,
    /// คำลงท้าย) อย่าใส่ เพราะข้อ 2 จะซ่อนคำผิดจริง · ที่มา: W3 ตรวจ wrong_word 219 จุด + ral_swaps 48 จุด
    /// ทีละจุด (2026-11-24) ได้ wrong_word 219 → 19
    /// </summary>
    public static class ThaiEnglishSense
    {
        private static readonly Dictionary<string, string> Senses = new Dictionary<string, string>
        {
            // wrong_word: คำเดิมที่ถูก (ข้อ 1)
            { "ขา", "legs?|foot|feet|footwork|knees?" },
            { "เข่า", "knees?" },
            { "รู", "holes?|cranny|burrow|bunghole" },
            { "ตั๋ว", "tickets?" },
            { "ท้อง", "bell(y|ies)|stomach|gut" },
            { "แท้ง", "miscarr|child inside|sapped" },
            { "สั่น", "trembl|shak|quiver|shiver" },
            { "ค้น", "search|rummag|examin" },
            { "คุ้ย", "rummag|dig|examin|scour|acorn" },
            { "ซ้อม", "beat|rehears|practi|drill" },
            { "ข้าว", "grain|rice|food|mouths?|bread" },
            { "ไอ", "cough|vapou?r|steam|fume" },
            { "สิง", "possess|inhabit" },
            { "ป้า", "aunt" },
            { "ล่อง", "sail|float|drift" },
            { "บิ่น", "chipped" },
            { "อาน", "saddle" },
            { "หม้อ", @"pots?\b|cauldron" },
            { "ผา", "cliff|crag|bluff" },
            { "ผ่า", "cut|slice|split|incision" },
            { "บ่น", "nag|complain|grumbl|whin|curs" },
            { "สร่าง", "sober" },
            { "ต้น", "trees?|willow|stalk|oak|plant" },
            { "สู้", "fight|bout|battle|duel|combat" },
            { "ร่าย", "curse|spell|utter|incant" },
            { "ร้าย", "terrible|wicked|evil|vicious" },
            { "ลา", "farewell|goodbye|leave|resign|gave up" },
            { "ห่าน", "goose|geese" },
            { "ยื่น", "file|submit|hand|offer" },
            { "ย้าย", "move|moved|transfer|relocat|pack" },
            { "ส่อง", "watch|peek|peep|look" },
            { "ขว้าง", "throw|hurl|toss" },
            { "หลั่ง", "spil|shed" },
            { "ชา", "numb" },
            { "แก่", "old|elder|age" },
            { "ยั้ง", "restrain|resist|hold back|refrain" },
            { "กลั้ว", "rins|gargl|wash" },
            { "เสียง", "voice|sound|noise" },
            { "คาง", "chin" },
            { "รอย", "wounds?|marks?|traces?|tracks?" },
            { "หมู", "pigs?|hogs?|boar|swine" },
            { "ปา", "throw" },
            { "ท่า", "stance|pose|horseback|position" },
            { "ทา", "smear|rub|apply" },
            { "มั่น", "sure|firm|confiden|tight|grip" },
            { "ล่ม", "sink|sunk|capsiz|collaps|fall" },
            { "กั้น", "separat|block|wall|ward|barrier|impermeab|veil|apotrop|keep" },
            { "ทั่ง", "anvil" },
            { "แท่น", "pedestal|altar|plinth|dais" },
            { "ย่าม", "sack|bag|pouch" },
            { "เดี่ยว", "single|toe-to-toe|solo|alone" },
            { "พลัด", "fell|fall" },
            { "ฝั่ง", "shore|coast|bank" },
            { "ขั้ว", "pole|polar" },
            { "รั้ง", "held|hold|grab|restrain|took" },
            { "หนี้", "debts?" },
            { "ย้อม", "dye|colou?r|highlight" },
            { "ใส", "clear|transparent" },
            { "คอย", "wait" },
            { "ส้ม", "citrus|orange" },
            { "ซุ้ม", "alcove|arbou?r|bower" },
            { "สวน", "garden|orchard|grove" },
            { "แห้ง", "dried|dry" },
            { "ร้าง", "ruin|abandon|deserted" },
            { "ทาน", "alms" },
            { "หลาบ", "welt|teach|lesson" },
            // ral_swaps: ความหมายของคำที่เสนอ (ข้อ 2) — ไม่มีในอังกฤษ = ไม่มีหลักฐานให้เปลี่ยน
            { "ลุกขึ้น", "stand|get up|got up|rise|rose|risen" },
            { "แรก", "first|start|beginning|outset" },
            { "ลัก", "steal|stole|theft|thie" },
            { "รับ", "receiv|accept|got|get|take|took" },
            { "ลอง", "try|tried|attempt|taste" },
            { "กลับมา", "return|back|came back" },
            { "กูล", "ghoul" },
            { "ลองของ", "test|try|challenge" },
            { "รายตัว", "each|individual|one by one" },
            { "ทางรอด", "survive|escape|way out" },
            { "ลอดผ่าน", "crawl|squeeze|pass under|through" },
            { "ร่ำไป", "always|constantly|forever" },
            { "ระคน", "mix|mingl|blend" },
            { "ระแบบ", "system" },
            { "สามล้อ", "tricycle|rickshaw" },
            { "ไล่เลียง", "question|interrogat|grill" },
            { "ลือเลื่อง", "famous|renown|legendary" },
            // คำที่เสนอบ่อยใน wrong_word — ความหมายไม่มีในอังกฤษ = เครื่องมือเดาจากบริบทไทยล้วน
            { "ใช้", "use|using|wield|spend|spent|apply|employ" },
            { "รู้", "know|knew|aware|understand|realiz|learn|tell|told|wonder" },
            { "ร่วม", "join|together|share|participat|along" },
            { "ม้า", "horse|mare|steed|mount|stallion|roach" },
            { "ยอม", "agree|allow|let|surrender|yield|give in|accept|consent|permit" },
            { "พ่อ", "father|dad|pa|da|sire|papa" },
            { "พอ", "enough|as good as|about as|suffic" },
            { "แม่", "mother|mum|mom|ma|mama" },
            { "แม้", "even|though|although|while" },
            { "สี่", "four|quarter" },
            { "สี", "colou?r|paint|white|red|blue|green|black|hue" },
            { "ไหล่", "shoulder" },
            { "เหล้า", "wine|ale|drink|booze|vodka|liquor|mead|spirits|beer" },
            { "ค่ำ", "evening|night|dusk" },
            { "ยิง", "shoot|shot|fire|arrow" },
            { "ล้อ", "wheel|roll|cart|wagon" },
            { "ใคร่", "curious|wish|desire|lust|eager" },
            { "สั่ง", "order|command|instruct" },
            { "ห้า", "five|fifth" },
            { "หา", "seek|find|look|search|fetch" },
            { "บาง", "thin|some" },
            { "บ้าง", "some|any|what" },
            { "นำ", "lead|led|bring|brought|guide" },
            { "น้ำ", "water|element|liquid|drink|stream|river" },
            { "ทีนี้", "now|then|so" },
            { "ที่นี่", "here" },
            { "หมู่", "group|among|village|all of" },
            { "ว่าง", "free|available|busy|empty|spare|vacant" },
            { "วาง", "put|place|set|lay|down" },
            { "เขา", "he|him|his|they|them|she|her|horns?" },
            { "ไว", "quick|fast|swift|soon|rapid" },
            { "บาน", "mirrors?|doors?|windows?|blossom|bloom|panes?" },
            { "ขี่", "ride|rode|riding|mount" },
            { "แทง", "stab|stuck|pierc|thrust|impal" },
            { "ปรับ", "tinker|adjust|tune|fix|modif" },
            { "พลาง", "while|shrug" },
            { "เร้า", "arous|excit|stimul|passion" },
            { "สงสาร", "pity|sorry|compassion" },
        };

        private static readonly Dictionary<string, Regex> Compiled = Senses.ToDictionary(kv => kv.Key, kv => Compile(kv.Value));

        // สระบน/ล่าง/วรรณยุกต์ที่ต่อท้ายแปลว่าเป็นคนละคำ (หมู ใน หมู่)
        private static readonly Regex Mark = new Regex("^[ัิ-ฺ็-๎]");

        /// <summary>ขึ้นต้นคำเสมอ (trembl → trembling) แต่คำสั้น ≤ 3 ตัวต้องจบคำด้วย (pa ไม่ใช่ past, so ไม่ใช่ some)</summary>
        private static Regex Compile(string pattern)
        {
            var alts = pattern.Split('|').Select(a => Regex.IsMatch(a, "^[a-z]{1,3}$") ? a + @"\b" : a).ToArray();
            return new Regex(@"\b(?:" + string.Join("|", alts) + ")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// regex ความหมายของฝั่งนี้ — ข้อเสนอ ร↔ล เป็นช่วงคำ (แต่แลก → แต่แรก) จึงยอมให้คำใน Senses ยาว ≥ 3 อักษร
        /// อยู่ข้างในได้ (แรก ใน แต่แรก) ถ้าไม่มีสระ/วรรณยุกต์ต่อท้าย (หมู ใน หมู่ ไม่ใช่คำนั้น)
        /// ส่วนคำ 2 อักษรต้องตรงทั้งคำ (ลา ใน ลาเพ ไม่ใช่ ลา)
        /// </summary>
        private static List<Regex> Keys(string side, string against)
        {
            if (Compiled.TryGetValue(side, out var exact)) return new List<Regex> { exact };
            var outList = new List<Regex>();
            foreach (var kv in Compiled)
            {
                string w = kv.Key;
                int k = side.IndexOf(w, System.StringComparison.Ordinal);
                if (w.Length >= 3 && k >= 0 && against.IndexOf(w, System.StringComparison.Ordinal) < 0 &&
                    !Mark.IsMatch(side.Substring(k + w.Length)))
                    outList.Add(kv.Value);
            }
            return outList;
        }

        /// <summary>true เมื่ออังกฤษยืนยันว่าคำเดิมถูก หรือไม่มีความหมายของคำที่เสนอเลย (ว่าง = ไม่ตัดสิน)</summary>
        public static bool RuledOut(string word, string suggested, string? english)
        {
            if (string.IsNullOrEmpty(english)) return false;
            var mine = Keys(word, suggested);
            if (mine.Any(r => r.IsMatch(english!))) return true;
            var other = Keys(suggested, word);
            return other.Count > 0 && !other.Any(r => r.IsMatch(english!));
        }
    }
}

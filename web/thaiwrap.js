'use strict';
// JavaScript port of thaiwrap (src/ThaiWrap). Keep in sync with the C# implementation:
// same dictionary cleaning rules, same options, same insertion guards.
// ตรงกับ C# ณ v0.9.0 (normalize · โหมด space + กลุ่มวลี · min-len · words_extra · nobreak · yamok-nbsp)
// — ตรวจ parity กับ CLI จริงด้วย tools/js-parity.sh (รันใน CI)

const ZWSP = '\u200B';
const HAIRSPACE = '\u200A';
const NBSP = '\u00A0';

const ThaiChars = {
  isThaiRange(c) { const x = c.codePointAt(0); return x >= 0x0E01 && x <= 0x0E5B; },
  isCombiningMark(c) {
    const x = c.codePointAt(0);
    return x === 0x0E31 || (x >= 0x0E34 && x <= 0x0E3A) || (x >= 0x0E47 && x <= 0x0E4E);
  },
  isLeadingVowel(c) { const x = c.codePointAt(0); return x >= 0x0E40 && x <= 0x0E44; },
  isThaiPunctuation(c) { const x = c.codePointAt(0); return x === 0x0E46 || x === 0x0E2F; },
  // กฎแบ่ง segment ของ DP: ห้ามขึ้นต้น segment ด้วย ฯ/ะ/ั/า/ำ/สระ-วรรณยุกต์จ่อย และช่วง ๆ ๅ ็ ่-๎
  noBreakBeforeSegment(c) {
    const x = c.codePointAt(0);
    return x === 0x0E2F || (x >= 0x0E30 && x <= 0x0E3A) || (x >= 0x0E45 && x <= 0x0E4E);
  },
  noBreakAfterSegment(c) { return this.isLeadingVowel(c); },
  isWhitespace(c) { return /\s/.test(c); },
  isAlnum(c) {
    const x = c.codePointAt(0);
    return (x >= 48 && x <= 57) || (x >= 97 && x <= 122) || (x >= 65 && x <= 90) ||
      (x >= 0x0E50 && x <= 0x0E59); // Thai digits ๐-๙
  },
  noBreakAfterChars: ['(', '[', '{', '<', '"', "'", '\u201C', '\u2018'],
  noBreakBeforeChars: [')', ']', '}', '>', '"', "'", ',', '.', '!', '?', ';', ':', '%', '\u201D', '\u2019', '\u2026'],
  noBreakAfter(c) { return this.noBreakAfterChars.includes(c); },
  noBreakBefore(c) { return this.noBreakBeforeChars.includes(c) || this.isThaiPunctuation(c) || this.isCombiningMark(c); },
};

const TokenKind = {
  DictionaryWord: 'DictionaryWord',
  UnknownRun: 'UnknownRun',
  Cluster: 'Cluster',
  NonThaiText: 'NonThaiText',
  Punctuation: 'Punctuation',
  Whitespace: 'Whitespace',
};
const BREAKABLE = new Set([TokenKind.DictionaryWord, TokenKind.Cluster, TokenKind.NonThaiText, TokenKind.UnknownRun]);
const THAI_KINDS = new Set([TokenKind.DictionaryWord, TokenKind.UnknownRun, TokenKind.Cluster]);

// --- ยุบรูปพิมพ์ผิดก่อนตัดคำ — mirror ของ C# ThaiNormalizer.Normalize (กฎและเหตุผลดูที่ C#) ---
const ThaiNormalizer = (() => {
  const FIXABLE = '\u0E31\u0E34\u0E35\u0E36\u0E37\u0E38\u0E39\u0E47\u0E48\u0E49\u0E4A\u0E4B\u0E4C';
  const E = '\u0E40', AE = '\u0E41', NIKHAHIT = '\u0E4D', AA = '\u0E32', AM = '\u0E33';
  const isVowelAbove = c => c === '\u0E31' || (c >= '\u0E34' && c <= '\u0E37');
  const isTone = c => c >= '\u0E48' && c <= '\u0E4B';
  const isFixable = c => FIXABLE.includes(c);
  const isBaseConsonant = c => c >= '\u0E01' && c <= '\u0E2A';
  const isLeadingVowel = c => c >= '\u0E40' && c <= '\u0E44';

  // เเ → แ ทีละคู่จากซ้าย ไม่ซ้อนทับ
  const fixDoubleE = t => t.split(E + E).join(AE);

  // ํ (+วรรณยุกต์คั่น) + า → (วรรณยุกต์) + ำ
  function composeSaraAm(t) {
    if (!t.includes(NIKHAHIT)) return t;
    let out = '';
    for (let i = 0; i < t.length; i++) {
      if (t[i] === NIKHAHIT) {
        if (t[i + 1] === AA) { out += AM; i++; continue; }
        if (i + 2 < t.length && isTone(t[i + 1]) && t[i + 2] === AA) { out += t[i + 1] + AM; i += 2; continue; }
      }
      out += t[i];
    }
    return out;
  }

  // run ของมาร์ค: ถ้าทั้ง run เป็นสระบน+วรรณยุกต์ → สระบนก่อน วรรณยุกต์ทีหลัง (stable) แล้วยุบตัวซ้ำ
  function canonicalRun(run) {
    if (run.length === 1) return run;
    const dedupe = cs => cs.filter((c, k) => k === 0 || c !== cs[k - 1]).join('');
    const cs = [...run];
    if (cs.every(c => isVowelAbove(c) || isTone(c)))
      return dedupe(cs.filter(isVowelAbove)) + dedupe(cs.filter(isTone));
    return dedupe(cs);
  }

  function canonicalizeMarkRuns(t) {
    let needed = false;
    for (let i = 1; i < t.length && !needed; i++) {
      const a = t[i - 1], b = t[i];
      if (isFixable(a) && isFixable(b) && (a === b || (isTone(a) && isVowelAbove(b)))) needed = true;
    }
    if (!needed) return t;
    let out = '', i = 0;
    while (i < t.length) {
      if (!isFixable(t[i])) { out += t[i]; i++; continue; }
      let j = i;
      while (j < t.length && isFixable(t[j])) j++;
      out += canonicalRun(t.slice(i, j));
      i = j;
    }
    return out;
  }

  // ไ่ม่ → ไม่: วรรณยุกต์หลงหลังสระหน้า เมื่อพยัญชนะ (+สระบน) ถัดไปมีวรรณยุกต์ถูกอยู่แล้ว
  function strayToneFixable(t, tonePos) {
    let k = tonePos + 1;
    if (k >= t.length || !isBaseConsonant(t[k])) return false;
    k++;
    if (k < t.length && isVowelAbove(t[k])) k++;
    return k < t.length && isTone(t[k]);
  }

  function removeStrayTone(t) {
    let out = '';
    for (let i = 0; i < t.length; i++) {
      if (isLeadingVowel(t[i]) && i + 1 < t.length && isTone(t[i + 1]) && strayToneFixable(t, i + 1)) {
        out += t[i]; i++; continue;
      }
      out += t[i];
    }
    return out;
  }

  return {
    normalize(text) {
      if (!text) return text;
      return removeStrayTone(canonicalizeMarkRuns(composeSaraAm(fixDoubleE(text))));
    },
  };
})();

// --- รวมคำเป็นกลุ่มวลีสำหรับโหมดช่องว่างจริง — mirror ของ C# ThaiChunker (พอร์ตจาก ThaiW1Setup) ---
const ThaiChunker = (() => {
  // C# คิดต้นทุนเป็น float — Math.fround ทุกขั้นให้ปัดแบบเดียวกัน (เสมอกันแล้วเลือกต่างกันไม่ได้)
  const f = Math.fround;
  const CHUNK_HARD = 8, BASE = 1.0, BIND = 4.0, GOOD_BEFORE = f(0.7), OVERFLOW = f(0.4);
  const BIND_NEXT = new Set(['ผู้', 'การ', 'ความ', 'นัก', 'พวก', 'ชาว', 'หมู่', 'เหล่า', 'ช่าง', 'ใน', 'กับ', 'แก่', 'แห่ง',
    'จาก', 'ของ', 'ที่', 'ซึ่ง', 'อัน', 'และ', 'แต่', 'หรือ', 'เพื่อ', 'ให้', 'โดย', 'ถึง', 'ว่า',
    'เมื่อ', 'ถ้า', 'หาก', 'แม้', 'เพราะ', 'จึง', 'จน', 'ไม่', 'จะ', 'ก็', 'ต้อง', 'ยัง', 'เคย',
    'กำลัง', 'คง', 'อาจ', 'ควร', 'เป็น', 'คือ', 'ไร้', 'ทุก', 'แต่ละ', 'บาง', 'หลาย', 'สู่', 'ต่อ',
    'ด้วยการ', 'ทั้ง', 'ตาม']);
  const BIND_PREV = new Set(['ได้', 'แล้ว', 'ไป', 'มา', 'อยู่', 'ด้วย', 'เลย', 'กัน', 'ขึ้น', 'ลง', 'เอง', 'นะ', 'ครับ',
    'ค่ะ', 'คะ', 'จ้ะ', 'สิ', 'ล่ะ', 'หรอก', 'เถอะ', 'นั้น', 'นี้', 'โน้น', 'มาก', 'ไหม', 'เท่านั้น',
    'ที่สุด', 'ไว้', 'เสีย']);
  const GOOD = new Set(['และ', 'แต่', 'หรือ', 'ที่', 'ซึ่ง', 'ใน', 'กับ', 'ของ', 'เพื่อ', 'จาก', 'ว่า', 'เมื่อ', 'ถ้า',
    'หาก', 'แม้', 'เพราะ', 'จึง', 'จน', 'โดย', 'ให้', 'แห่ง', 'สำหรับ']);

  function breakCost(parts, k) {
    let c = BASE;
    if (BIND_NEXT.has(parts[k - 1])) c = f(c + BIND);
    if (BIND_PREV.has(parts[k])) c = f(c + BIND);
    if (GOOD.has(parts[k])) c = f(c - GOOD_BEFORE);
    return c;
  }

  return {
    chunkInPlace(tokens, start, end, maxChunk) {
      const n = end - start;
      if (n < 2) return;
      const parts = [], lengths = [0];
      for (let i = 0; i < n; i++) {
        parts.push(tokens[start + i].text);
        lengths.push(lengths[i] + parts[i].length);
      }
      const best = new Array(n + 1).fill(Infinity), prev = new Array(n + 1).fill(0);
      best[0] = 0;
      for (let j = 1; j <= n; j++) {
        for (let i = j - 1; i >= 0; i--) {
          const count = j - i, len = lengths[j] - lengths[i];
          if (count > 1 && len > maxChunk + CHUNK_HARD) break;
          const span = len <= maxChunk ? 0 : f((len - maxChunk) * OVERFLOW);
          const cost = f(f(best[i] + span) + (i > 0 ? breakCost(parts, i) : 0));
          if (cost < best[j]) { best[j] = cost; prev[j] = i; }
        }
      }
      const merged = [];
      for (let k = n; k > 0;) {
        const i = prev[k];
        let allWords = true;
        for (let t = i; t < k; t++) if (tokens[start + t].kind !== TokenKind.DictionaryWord) { allWords = false; break; }
        merged.push({ text: parts.slice(i, k).join(''), kind: allWords ? TokenKind.DictionaryWord : TokenKind.UnknownRun });
        k = i;
      }
      merged.reverse();
      tokens.splice(start, n, ...merged);
    },
  };
})();

class ThaiSegmenter {
  constructor(words, options) {
    this.options = Object.assign(
      {
        maxWordLength: 0, unknownBreakThreshold: 8, protectPlaceholders: true, breakChar: ZWSP,
        maxChunk: 0, minThaiLength: 0, noBreakPhrases: null, normalize: false, yamokNbsp: false,
      },
      options);
    this.words = new Set();
    this._root = { children: new Map(), end: false };
    for (const w of words) {
      const t = (w || '').trim();
      if (!t) continue;
      if (/\s/.test(t)) continue; // entries containing whitespace can never match space-free Thai text
      if (t.length < 2) continue;
      if (this.options.maxWordLength > 0 && t.length > this.options.maxWordLength) continue;
      this.words.add(t);
      let node = this._root;
      for (const ch of t) {
        if (!node.children.has(ch)) node.children.set(ch, { children: new Map(), end: false });
        node = node.children.get(ch);
      }
      node.end = true;
    }
  }

  tokenize(text) {
    const tokens = [];
    if (!text) return tokens;
    let i = 0;
    const n = text.length;
    while (i < n) {
      const c = text[i];
      if (ThaiChars.isWhitespace(c)) {
        let j = i + 1;
        while (j < n && ThaiChars.isWhitespace(text[j])) j++;
        tokens.push({ text: text.slice(i, j), kind: TokenKind.Whitespace });
        i = j;
      } else if (ThaiChars.isAlnum(c)) {
        let j = i + 1;
        while (j < n && ThaiChars.isAlnum(text[j])) j++;
        tokens.push({ text: text.slice(i, j), kind: TokenKind.NonThaiText });
        i = j;
      } else if (ThaiChars.isThaiRange(c)) {
        let j = i;
        while (j < n && ThaiChars.isThaiRange(text[j])) j++;
        this._tokenizeThaiRun(text, i, j, tokens);
        i = j;
      } else {
        let j = i + 1;
        while (j < n) {
          const d = text[j];
          if (ThaiChars.isWhitespace(d) || ThaiChars.isAlnum(d) || ThaiChars.isThaiRange(d)) break;
          j++;
        }
        tokens.push({ text: text.slice(i, j), kind: TokenKind.Punctuation });
        i = j;
      }
    }
    return tokens;
  }

  // DP segmentation — mirror ของ C# (port จาก ThaiW3Setup core/thai_wrap.py, MIT)
  _tokenizeThaiRun(text, start, end, tokens) {
    const runStart = tokens.length; // ขอบเขต token ของ run นี้ — ใช้ตอนรวมกลุ่มวลี (maxChunk)
    const run = text.slice(start, end);
    const n = run.length;
    const INF = 0x3fffffff;

    const ok = new Array(n + 1);
    for (let i = 0; i <= n; i++)
      ok[i] = i === 0 || i === n ||
        (!ThaiChars.noBreakBeforeSegment(run[i]) && !ThaiChars.noBreakAfterSegment(run[i - 1]));

    const bestU = new Array(n + 1).fill(INF);
    const bestW = new Array(n + 1).fill(INF);
    const prevStart = new Array(n + 1).fill(0);
    const prevIsWord = new Array(n + 1).fill(false);
    bestU[0] = 0; bestW[0] = 0;

    for (let i = 0; i < n; i++) {
      if (bestU[i] === INF || !ok[i]) continue;
      const u = bestU[i], c = bestW[i];

      let node = this._root, j = i;
      while (j < n && node.children.has(run[j])) {
        node = node.children.get(run[j]);
        j++;
        if (node.end && ok[j]) {
          const nu = u, nw = c + 1;
          if (nu < bestU[j] || (nu === bestU[j] && nw < bestW[j])) {
            bestU[j] = nu; bestW[j] = nw; prevStart[j] = i; prevIsWord[j] = true;
          }
        }
      }

      let k = i + 1;
      while (!ok[k]) k++;
      const uu = u + (k - i), cw = c + 1;
      if (uu < bestU[k] || (uu === bestU[k] && cw < bestW[k])) {
        bestU[k] = uu; bestW[k] = cw; prevStart[k] = i; prevIsWord[k] = false;
      }
    }

    const parts = [], isWords = [];
    for (let p = n; p > 0;) {
      const s = prevStart[p];
      parts.push(run.slice(s, p));
      isWords.push(prevIsWord[p]);
      p = s;
    }
    parts.reverse(); isWords.reverse();

    // merge pass: ก้อน unknown กับคำสั้นที่ติดกัน (ท|ริ|สส์ → ทริสส์)
    let pending = '';
    for (let idx = 0; idx < parts.length; idx++) {
      const partText = parts[idx], word = isWords[idx];
      const nearUnknown = (idx > 0 && !isWords[idx - 1]) || (idx + 1 < parts.length && !isWords[idx + 1]);
      const unknown = !word || (partText.length <= 2 && nearUnknown);
      if (unknown) {
        pending += partText;
      } else {
        if (pending) { this._addUnknown(tokens, pending); pending = ''; }
        tokens.push({ text: partText, kind: TokenKind.DictionaryWord });
      }
    }
    if (pending) this._addUnknown(tokens, pending);

    if (this.options.maxChunk > 0 && tokens.length - runStart >= 2)
      ThaiChunker.chunkInPlace(tokens, runStart, tokens.length, this.options.maxChunk);
  }

  _addUnknown(tokens, chunk) {
    if (chunk.length === 0) return;
    if (this._countClusters(chunk) <= this.options.unknownBreakThreshold) {
      tokens.push({ text: chunk, kind: TokenKind.UnknownRun });
      return;
    }
    let i = 0;
    while (i < chunk.length) {
      const j = this._clusterEnd(chunk, i);
      tokens.push({ text: chunk.slice(i, j), kind: TokenKind.Cluster });
      i = j;
    }
  }

  // กฎขอบเขตเดียวกับ DP — mirror ของ C# ClusterEnd
  _clusterEnd(s, i) {
    let j = i + 1;
    let extended = true;
    while (extended) {
      extended = false;
      while (j < s.length && (ThaiChars.isCombiningMark(s[j]) || ThaiChars.noBreakBeforeSegment(s[j]))) j++;
      if (j < s.length && ThaiChars.isLeadingVowel(s[j - 1])) {
        j++;
        extended = true;
      }
    }
    return j;
  }

  _countClusters(s) {
    let count = 0, i = 0;
    while (i < s.length) {
      i = this._clusterEnd(s, i);
      count++;
    }
    return count;
  }

  insertZwsp(text) {
    if (!text) return text;
    if (this.options.minThaiLength > 0 && !meetsMinThaiLength(text, this.options.minThaiLength)) return text;
    const raw = t => this._insertZwspRaw(t);
    const parts = this._splitControlParts(text);
    if (parts.length === 1 && !parts[0].masked) return this._processNormal(text, raw);
    // ชิ้นที่ไม่ใช่ mask ถูกตัดคำแบบอิสระ — ไม่มี ZWSP ใหม่หลุดไปติดขอบ mask
    // และ ZWSP เดิมของผู้ใช้ถูกรักษาไว้ (mirror ของ C#)
    let out = '';
    for (const p of parts) {
      out += p.masked ? p.text : this._processNormal(p.text, raw);
    }
    return out;
  }

  // normalize ก่อน แล้วจึงแยกวลีห้ามตัด แล้วไม้ยมก — mirror ของ C# ProcessNormal
  _processNormal(text, raw) {
    if (this.options.normalize) text = ThaiNormalizer.normalize(text);
    const result = this._wrapNoBreak(text, raw);
    return this.options.yamokNbsp ? keepYamokOnWord(result) : result;
  }

  // วลีห้ามตัด: ช่วงวลีส่งออกตรงๆ (ช่องว่าง → NBSP) ไม่มีจุดตัดภายใน · จับทั้งรูป space และ NBSP (idempotent)
  // · วลียาวก่อน — mirror ของ C# WrapNoBreak (phrases ต้องเรียงยาว→สั้นแล้ว ดู loadNoBreakPhrases)
  _wrapNoBreak(text, raw) {
    const phrases = this.options.noBreakPhrases;
    if (!phrases || phrases.length === 0) return raw(text);
    let out = '', norm = '', i = 0;
    while (i < text.length) {
      let hit = null, nbspForm = false;
      for (const ph of phrases) {
        if (text.startsWith(ph, i)) { hit = ph; break; }
        const nbv = ph.split(' ').join(NBSP);
        if (nbv !== ph && text.startsWith(nbv, i)) { hit = nbv; nbspForm = true; break; }
      }
      if (hit === null) { norm += text[i]; i++; continue; }
      if (norm) { out += raw(norm); norm = ''; }
      out += nbspForm ? hit : hit.split(' ').join(NBSP);
      i += hit.length;
    }
    if (norm) out += raw(norm);
    return out;
  }

  _insertZwspRaw(text) {
    const tokens = this.tokenize(text);
    const br = this.options.breakChar || ZWSP;
    let out = '';
    for (let k = 0; k < tokens.length; k++) {
      out += tokens[k].text;
      if (k === tokens.length - 1) break;
      if (this._shouldInsertBetween(tokens[k], tokens[k + 1])) out += br;
    }
    return out;
  }

  segmentToString(text, sep) {
    sep = sep || '|';
    if (!text) return text;
    const raw = t => this._segmentToStringRaw(t, sep);
    const parts = this._splitControlParts(text);
    if (parts.length === 1 && !parts[0].masked) return this._processNormal(text, raw);
    let out = '';
    for (const p of parts) {
      out += p.masked ? p.text : this._processNormal(p.text, raw);
    }
    return out;
  }

  _segmentToStringRaw(text, sep) {
    const tokens = this.tokenize(text);
    let out = '';
    for (let k = 0; k < tokens.length; k++) {
      out += tokens[k].text;
      if (k < tokens.length - 1 &&
        tokens[k].kind !== TokenKind.Whitespace && tokens[k + 1].kind !== TokenKind.Whitespace) out += sep;
    }
    return out;
  }

  // แยกส่วนควบคุม {placeholder} / <tag> ออกก่อนตัดคำ — mirror ของ C# SplitControlParts
  _splitControlParts(text) {
    if (!this.options.protectPlaceholders) return [{ text: text, masked: false }];
    const parts = [];
    let cur = '';
    let i = 0;
    const n = text.length;
    while (i < n) {
      const c = text[i];
      if (c === '{' || c === '<') {
        const close = c === '{' ? '}' : '>';
        let j = i + 1;
        while (j < n && text[j] !== close && text[j] !== '\n' && text[j] !== '\r') j++;
        if (j < n && text[j] === close) {
          if (cur) { parts.push({ text: cur, masked: false }); cur = ''; }
          parts.push({ text: text.slice(i, j + 1), masked: true });
          i = j + 1;
          continue;
        }
      }
      cur += c;
      i++;
    }
    if (cur) parts.push({ text: cur, masked: false });
    return parts;
  }

  _shouldInsertBetween(t, next) {
    // โหมดช่องว่างจริง: แทรกเฉพาะรอยต่อคำไทย-คำไทย (ตามพฤติกรรม ThaiW1Setup)
    if (this.options.breakChar === ' ' && (!THAI_KINDS.has(t.kind) || !THAI_KINDS.has(next.kind))) return false;
    if (!BREAKABLE.has(t.kind) || !BREAKABLE.has(next.kind)) return false;
    const last = t.text[t.text.length - 1];
    const first = next.text[0];
    if (ThaiChars.noBreakAfter(last)) return false;
    if (ThaiChars.isLeadingVowel(last)) return false; // never strand เ แ โ ใ ไ at line end
    if (ThaiChars.noBreakBefore(first)) return false;
    return true;
  }
}

// ข้อความผ่านเกณฑ์ความยาวขั้นต่ำหรือไม่ — รายบรรทัด (\n จริงและ literal "\n") · mirror ของ C# MeetsMinThaiLength
function meetsMinThaiLength(text, min) {
  let count = 0;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c === '\n') {
      if (count >= min) return true;
      count = 0;
    } else if (c === '\\' && text[i + 1] === 'n') {
      if (count >= min) return true;
      count = 0;
      i++;
    } else if (ThaiChars.isThaiRange(c)) {
      count++;
    }
  }
  return count >= min;
}

// ช่วงช่องว่างที่ติดหน้า ๆ: U+0020 → NBSP ตัวต่อตัว · ZWSP/hairspace ตัดทิ้ง — mirror ของ C# KeepYamokOnWord
function keepYamokOnWord(text) {
  if (!text.includes('\u0E46')) return text;
  const isSp = c => c === ' ' || c === HAIRSPACE || c === ZWSP;
  let out = '', i = 0;
  while (i < text.length) {
    if (!isSp(text[i])) { out += text[i]; i++; continue; }
    let j = i;
    while (j < text.length && isSp(text[j])) j++;
    const beforeYamok = text[j] === '\u0E46';
    for (let k = i; k < j; k++) {
      if (!beforeYamok) out += text[k];
      else if (text[k] === ' ') out += NBSP;
    }
    i = j;
  }
  return out;
}

// อ่านรายการวลีห้ามตัด (บรรทัดละวลี · # = คอมเมนต์) เรียงยาว→สั้น — mirror ของ C# LoadNoBreakPhrases
function loadNoBreakPhrases(text) {
  const phrases = (text || '').split(/\r?\n/).map(l => l.trim()).filter(l => l && l[0] !== '#');
  return phrases.map((p, i) => [p, i]).sort((a, b) => b[0].length - a[0].length || a[1] - b[1]).map(x => x[0]);
}

// word list หลัก + words_extra ("-คำ" = ลบ · # = คอมเมนต์ · อื่นๆ = เพิ่ม) — mirror ของ C# MergeWordFiles
function mergeWordLists(mainWords, extraText) {
  const mainSet = new Set(mainWords.map(w => w.trim()));
  const removed = new Set(), added = [], addedSet = new Set();
  for (const line of (extraText || '').split(/\r?\n/)) {
    const t = line.trim();
    if (!t || t[0] === '#') continue;
    if (t[0] === '-' && t.length > 1) { removed.add(t.slice(1)); continue; }
    if (!mainSet.has(t) && !addedSet.has(t)) { addedSet.add(t); added.push(t); }
  }
  const words = added.concat(mainWords.filter(w => !removed.has(w.trim())));
  return { words, added: added.length, removed: removed.size };
}

// --- naive game-engine wrap simulation (canvas) ---
// Wraps text using ONLY the engine's break characters (space + its invisible break char:
// ZWSP for TMP-like engines, hair space for Scaleform, nothing extra for Aurora), exactly
// like engines that split on whitespace. NBSP is never a break. A unit wider than the box
// is hard-cut per character (highlighted red) — this is what those engines do to Thai today.
function simulateEngineWrap(ctx, text, maxWidth, breakChars) {
  breakChars = breakChars || [' ', ZWSP];
  const units = []; // {t, space?, zwsp?}
  let cur = '';
  for (const ch of text) {
    if (breakChars.includes(ch)) {
      if (cur) { units.push({ t: cur }); cur = ''; }
      units.push(ch === ' ' ? { t: '', space: true } : { t: '', zwsp: true });
    } else cur += ch;
  }
  if (cur) units.push({ t: cur });

  const lines = []; // [{segments: [{t, hard}]}]
  let segments = [];
  let lineW = 0;
  let spacePending = false;
  let hardCuts = 0;

  const newLine = () => { lines.push({ segments }); segments = []; lineW = 0; spacePending = false; };

  for (const u of units) {
    if (u.space) { if (segments.length || lineW > 0) spacePending = true; continue; }
    if (u.zwsp) continue; // break opportunity only
    const w = ctx.measureText(u.t).width;
    const lead = spacePending && segments.length ? ' ' : '';
    if (lineW + ctx.measureText(lead + u.t).width <= maxWidth) {
      if (lead) segments.push({ t: ' ', hard: false });
      segments.push({ t: u.t, hard: false });
      lineW += ctx.measureText(lead + u.t).width;
      spacePending = false;
    } else {
      if (segments.length) newLine();
      if (w <= maxWidth) {
        segments.push({ t: u.t, hard: false });
        lineW = w;
      } else {
        // hard cut per character, like a naive engine
        let piece = '';
        for (const ch of u.t) {
          if (ctx.measureText(piece + ch).width > maxWidth && piece) {
            segments.push({ t: piece, hard: true });
            hardCuts++;
            newLine();
            piece = ch;
          } else piece += ch;
        }
        segments.push({ t: piece, hard: true });
        hardCuts++;
        lineW = ctx.measureText(piece).width;
      }
    }
  }
  if (segments.length) lines.push({ segments });
  return { lines, hardCuts };
}

// ใช้จาก node ได้ (tools/js-parity.sh) — ในเบราว์เซอร์ไม่มี module จึงข้าม
if (typeof module !== 'undefined' && module.exports) {
  module.exports = { ThaiSegmenter, ThaiNormalizer, ThaiChars, TokenKind, ZWSP, HAIRSPACE, NBSP,
    loadNoBreakPhrases, mergeWordLists, meetsMinThaiLength, keepYamokOnWord };
}

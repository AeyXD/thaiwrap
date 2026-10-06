'use strict';
// JavaScript port of thaiwrap (src/ThaiWrap). Keep in sync with the C# implementation:
// same dictionary cleaning rules, same options, same insertion guards.

const ZWSP = '\u200B';

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

class ThaiSegmenter {
  constructor(words, options) {
    this.options = Object.assign(
      { maxWordLength: 0, unknownBreakThreshold: 8, protectPlaceholders: true, breakChar: ZWSP },
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
  }

  _addUnknown(tokens, chunk) {
    if (chunk.length === 0) return;
    if (this._countClusters(chunk) <= this.options.unknownBreakThreshold) {
      tokens.push({ text: chunk, kind: TokenKind.UnknownRun });
      return;
    }
    let i = 0;
    while (i < chunk.length) {
      let j = i + 1;
      while (j < chunk.length && ThaiChars.isCombiningMark(chunk[j])) j++;
      if (j < chunk.length && ThaiChars.isThaiPunctuation(chunk[j])) j++;
      tokens.push({ text: chunk.slice(i, j), kind: TokenKind.Cluster });
      i = j;
    }
  }

  _countClusters(s) {
    let count = 0, i = 0;
    while (i < s.length) {
      let j = i + 1;
      while (j < s.length && ThaiChars.isCombiningMark(s[j])) j++;
      if (j < s.length && ThaiChars.isThaiPunctuation(s[j])) j++;
      count++;
      i = j;
    }
    return count;
  }

  insertZwsp(text) {
    if (!text) return text;
    const parts = this._splitControlParts(text);
    if (parts.length === 1 && !parts[0].masked) return this._insertZwspRaw(text);
    // ชิ้นที่ไม่ใช่ mask ถูกตัดคำแบบอิสระ — ไม่มี ZWSP ใหม่หลุดไปติดขอบ mask
    // และ ZWSP เดิมของผู้ใช้ถูกรักษาไว้ (mirror ของ C#)
    let out = '';
    for (const p of parts) {
      out += p.masked ? p.text : this._insertZwspRaw(p.text);
    }
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
    const parts = this._splitControlParts(text);
    if (parts.length === 1 && !parts[0].masked) return this._segmentToStringRaw(text, sep);
    let out = '';
    for (const p of parts) {
      out += p.masked ? p.text : this._segmentToStringRaw(p.text, sep);
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
    if (!BREAKABLE.has(t.kind) || !BREAKABLE.has(next.kind)) return false;
    const last = t.text[t.text.length - 1];
    const first = next.text[0];
    if (ThaiChars.noBreakAfter(last)) return false;
    if (ThaiChars.isLeadingVowel(last)) return false; // never strand เ แ โ ใ ไ at line end
    if (ThaiChars.noBreakBefore(first)) return false;
    return true;
  }
}

// --- naive game-engine wrap simulation (canvas) ---
// Wraps text using ONLY spaces and ZWSP as break opportunities, exactly like engines
// that split on whitespace. A unit wider than the box is hard-cut per character
// (highlighted red) — this is what those engines do to Thai today.
function simulateEngineWrap(ctx, text, maxWidth) {
  const units = []; // {t, space?, zwsp?}
  let cur = '';
  for (const ch of text) {
    if (ch === ' ' || ch === ZWSP) {
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

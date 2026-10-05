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
  isThaiPunctuation(c) { const x = c.codePointAt(0); return x === 0x0E46 || x === 0x0E4F; },
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
const BREAKABLE = new Set([TokenKind.DictionaryWord, TokenKind.Cluster, TokenKind.NonThaiText]);

class ThaiSegmenter {
  constructor(words, options) {
    this.options = Object.assign({ maxWordLength: 12, unknownBreakThreshold: 8 }, options);
    this.words = new Set();
    for (const w of words) {
      const t = (w || '').trim();
      if (!t) continue;
      if (/\s/.test(t)) continue; // entries containing whitespace can never match space-free Thai text
      if (t.length < 2) continue;
      this.words.add(t);
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

  _tokenizeThaiRun(text, start, end, tokens) {
    const unknown = [];
    let i = start;
    while (i < end) {
      let match = null;
      const limit = Math.min(this.options.maxWordLength, end - i);
      for (let l = limit; l >= 2; l--) {
        const cand = text.substr(i, l);
        if (this.words.has(cand)) { match = cand; break; }
      }
      if (match !== null) {
        this._flushUnknown(tokens, unknown);
        tokens.push({ text: match, kind: TokenKind.DictionaryWord });
        i += match.length;
      } else {
        let j = i + 1;
        while (j < end && ThaiChars.isCombiningMark(text[j])) j++;
        if (j < end && ThaiChars.isThaiPunctuation(text[j])) j++;
        unknown.push(text.slice(i, j));
        i = j;
      }
    }
    this._flushUnknown(tokens, unknown);
  }

  _flushUnknown(tokens, clusters) {
    if (clusters.length === 0) return;
    if (clusters.length <= this.options.unknownBreakThreshold) {
      tokens.push({ text: clusters.join(''), kind: TokenKind.UnknownRun });
    } else {
      for (const cl of clusters) tokens.push({ text: cl, kind: TokenKind.Cluster });
    }
    clusters.length = 0;
  }

  insertZwsp(text) {
    const tokens = this.tokenize(text);
    let out = '';
    for (let k = 0; k < tokens.length; k++) {
      out += tokens[k].text;
      if (k === tokens.length - 1) break;
      if (this._shouldInsertBetween(tokens[k], tokens[k + 1])) out += ZWSP;
    }
    return out;
  }

  segmentToString(text, sep) {
    sep = sep || '|';
    const tokens = this.tokenize(text);
    let out = '';
    for (let k = 0; k < tokens.length; k++) {
      out += tokens[k].text;
      if (k < tokens.length - 1 &&
        tokens[k].kind !== TokenKind.Whitespace && tokens[k + 1].kind !== TokenKind.Whitespace) out += sep;
    }
    return out;
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

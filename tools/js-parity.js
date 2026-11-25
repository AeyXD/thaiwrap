'use strict';
// เทียบผล web/thaiwrap.js กับผลของ CLI C# (ไฟล์ expected) ทีละบรรทัด — เรียกผ่าน tools/js-parity.sh
// ใช้: node tools/js-parity.js <data-dir> <input.txt> <expected.txt> [ตัวเลือกแบบเดียวกับ CLI...]
// data-dir ต้องมี words_th.txt · มี words_extra.txt / nobreak.txt ก็ใช้เหมือน CLI
const fs = require('fs');
const path = require('path');
const tw = require('../web/thaiwrap.js');

const [dataDir, inputPath, expectedPath, ...flags] = process.argv.slice(2);
const read = p => fs.readFileSync(p, 'utf8');
const readIf = p => (fs.existsSync(p) ? read(p) : null);

const options = {};
let mode = 'zwsp', chunkSet = false, minLenSet = false;
for (let i = 0; i < flags.length; i++) {
  const a = flags[i];
  if (a === '--break-char') {
    const v = flags[++i];
    options.breakChar = v === 'space' ? ' ' : v === 'hairspace' ? tw.HAIRSPACE : tw.ZWSP;
    if (v === 'space') { if (!chunkSet) options.maxChunk = 12; if (!minLenSet) options.minThaiLength = 40; }
  } else if (a === '--chunk') { options.maxChunk = +flags[++i]; chunkSet = true; }
  else if (a === '--min-len') { options.minThaiLength = +flags[++i]; minLenSet = true; }
  else if (a === '--normalize') options.normalize = true;
  else if (a === '--yamok-nbsp') options.yamokNbsp = true;
  else if (a === '-m') mode = flags[++i];
  else throw new Error('ตัวเลือกที่ js-parity ยังไม่รองรับ: ' + a);
}

let words = read(path.join(dataDir, 'words_th.txt')).split(/\r?\n/);
const extra = readIf(path.join(dataDir, 'words_extra.txt'));
if (extra !== null) words = tw.mergeWordLists(words, extra).words;
const nobreak = readIf(path.join(dataDir, 'nobreak.txt'));
if (nobreak !== null) options.noBreakPhrases = tw.loadNoBreakPhrases(nobreak);

const seg = new tw.ThaiSegmenter(words, options);
const transform = mode === 'sep' ? t => seg.segmentToString(t) : t => seg.insertZwsp(t);

const inputs = read(inputPath).split('\n');
const expected = read(expectedPath).split('\n');
const show = s => s.replace(/[​  ]/g, c => ({ '​': '·', ' ': '¦', ' ': '⍽' })[c]);

let diffs = 0;
if (inputs.length !== expected.length) {
  console.log(`  จำนวนบรรทัดไม่เท่ากัน: input ${inputs.length} / C# ${expected.length}`);
  diffs++;
}
for (let i = 0; i < Math.min(inputs.length, expected.length); i++) {
  const js = transform(inputs[i]);
  if (js === expected[i]) continue;
  if (diffs++ < 5) console.log(`  บรรทัด ${i + 1}\n    C#: ${show(expected[i])}\n    JS: ${show(js)}`);
}
const label = flags.join(' ') || '(default)';
console.log((diffs ? 'FAIL ' : 'ok   ') + label + (fs.existsSync(path.join(dataDir, 'nobreak.txt')) ? ' +nobreak/words_extra' : '') +
  (diffs ? ` — ต่าง ${diffs} บรรทัด` : ` — ${inputs.length} บรรทัดตรงกัน`));
process.exit(diffs ? 1 : 0);

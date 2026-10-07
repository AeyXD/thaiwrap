# thaiwrap

[![CI](https://github.com/AeyXD/thaiwrap/actions/workflows/ci.yml/badge.svg)](https://github.com/AeyXD/thaiwrap/actions/workflows/ci.yml)

ทำให้ข้อความไทยในเกมตัดบรรทัดถูกจุด — โดยไม่ต้องแก้เกมเลย

ภาษาไทยเขียนโดยไม่เว้นวรรคระหว่างคำ แต่เอนจินเกมส่วนใหญ่ (Unity, Unreal, ฯลฯ)
ตัดบรรทัดที่ "ช่องว่าง" เท่านั้น ผลคือข้อความไทยถูกตัดกลางคำหรือล้นกรอบ

thaiwrap แก้ที่ตัวข้อความ: ตัดคำด้วยพจนานุกรมแล้วแทรก **Zero-Width Space (U+200B)**
ที่รอยต่อระหว่างคำ เอนจินที่ตัดบรรทัดที่ช่องว่างจะเห็น ZWSP เป็นจุดตัดโดยอัตโนมัติ

```
"กระบี่อันเป็นเลิศ" → ตัดคำ → กระบี่|อัน|เป็น|เลิศ → "กระบี่​อัน​เป็น​เลิศ" (แทรก ZWSP)
```

## องค์ประกอบ

| ส่วน | ที่อยู่ | คำอธิบาย |
|---|---|---|
| Core library (C#) | `src/ThaiWrap` | netstandard2.0 — ตัดคำด้วย **DP optimal segmentation** + แทรกตัวตัดบรรทัด, ไม่มี dependency |
| Formats library | `src/ThaiWrap.Formats` | csv/tsv/json/po/keyvalue — parser อนุรักษ์เดิม ไม่มี dependency |
| CLI | `src/ThaiWrap.Cli` | dry-run diff, เลือกคอลัมน์, โหมด folder, BOM/CRLF |
| Tests | `tests/ThaiWrap.Tests` | invariant checks + ชุดประโยคเกม 39 ประโยค |
| เว็บทดสอบ visual | `web/` | จำลองเอนจินเกมด้วย canvas เทียบก่อน/หลัง + `font_probe.html` |
| BepInEx plugin | `src/ThaiWrap.BepInEx` | hook TextMeshPro/UGUI ตอนรัน (net35, ไม่มี dependency) — แพ็กด้วย `tools/package-plugin.sh` |
| Font patcher | `tools/font_zwsp_patch.py` | เติม glyph ZWSP เปล่าให้ฟอนต์ TrueType ที่ไม่มี |
| Godot compat test | `tools/godot-compat/` | สคริปต์ทดสอบ headless รันซ้ำได้ |
| Word list | `data/words_th.txt` | 65,758 คำ — union ของ [pythainlp](https://github.com/PyThaiNLP/pythainlp) (Apache-2.0) + ICU thaidict (Unicode License) |

## ใช้งาน CLI

```bash
cd thaiwrap
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"

# ดูผลตัดคำเป็นคำๆ
dotnet run --project src/ThaiWrap.Cli -- samples/game_sentences.txt -m sep | head

# ไฟล์งานแปล: csv / tsv / json / po / key=value (XUnity) — ดูก่อนด้วย dry-run เสมอ
dotnet run --project src/ThaiWrap.Cli -- data/items.csv --columns thai --dry-run
dotnet run --project src/ThaiWrap.Cli -- data/items.csv --columns thai -o data/items.out.csv
dotnet run --project src/ThaiWrap.Cli -- data/ -r                 # ทั้งโฟลเดอร์ → สร้าง *.zwsp.*
dotnet run --project src/ThaiWrap.Cli -- th.txt -f keyvalue -o th.out.txt
```

Options หลัก: `-f auto|text|csv|tsv|json|po|keyvalue` · `--columns "1,3" หรือ "ชื่อ"` · `--json-keys "desc,tooltip"` · `--break-char zwsp|hairspace` · `--dry-run` (diff, ZWSP แสดงเป็น ·) · `-r` · `--inplace` (+.bak) · `--maxword` (0=ไม่จำกัด) · `--unknown-threshold` · `--stats`

**เกม Scaleform/Flash (เช่น Witcher 3)**: ZWSP ไม่เป็นจุดตัด ต้องใช้ `--break-char hairspace` (U+200A) และฟอนต์ต้องมี glyph ว่างที่ U+200A — ดูวิธีเต็มใน [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md)

## อัลกอริทึมตัดคำ

**DP optimal segmentation** — หาการตัดคำที่ minimizes (จำนวนตัวอักษรนอกพจนานุกรม, จำนวนคำ) ภายใต้กฎพยัญชนะทวิภาคของไทย (ห้ามแยกสระ/วรรณยุกต์ออกจากพยัญชนะ ห้ามทิ้งสระหน้าท้ายบรรทัด ห้ามตัดก่อน ๆ/ฯ)

- port จาก [ThaiW3Setup](https://github.com/AeyXD/ThaiW3Setup) (`core/thai_wrap.py`, MIT) ซึ่งใช้จริงใน production กับซับไทย The Witcher 3
- คุณภาพ (เทียบ pythainlp/newmm): **precision 1.000 · recall 0.773 · F1 0.872** บนชุดตัวอย่าง 40 ประโยคเกมที่วัด (เดิม greedy: P 0.936 · F1 0.866) — *ผลวัดบนชุดตัวอย่างเพื่อเปรียบเทียบอัลกอริทึม ไม่ใช่การรับประกันว่าถูกทุกข้อความ*
- ตรวจสอบการ port: output ตรงกับต้นฉบับ Python เป๊ะ 41/42 ประโยค (ตัวที่ต่างคือเคส `{placeholder}` ที่ thaiwrap ปกป้องทั้งก้อนโดยเจตนา)

ความปลอดภัย:
- **JSON**: key ไม่ถูกแตะเสมอ · string **value** ถูกแปลงทุกตัวโดย default — ถ้าไฟล์มี value ที่เป็น ID/รหัส ให้ระบุ `--json-keys` เฉพาะ field ข้อความ
- **CSV/TSV**: default แปลงทุกคอลัมน์ — ไฟล์ที่มีคอลัมน์ ID ควรระบุ `--columns` เสมอ
- `{placeholder}` เช่น `{ชื่อผู้เล่น}` และแท็ก rich-text `<color=red>…</color>` / `<link="…">` **ไม่ถูกแทรก ZWSP ทั้งช่วง** แม้ข้างในมีตัวไทย
- BOM และ CRLF คงเดิม · รันซ้ำไม่เพิ่ม ZWSP ซ้ำ (idempotent) · โหมด folder ข้ามไฟล์ที่ไม่เปลี่ยน และ `--inplace` จะไม่ทับ `.bak` เดิม (สำรองไว้ตั้งแต่รอบแรก)

## เว็บทดสอบ visual

**Demo ออนไลน์: https://aeyxd.github.io/thaiwrap/**

หรือรัน local:

```bash
cd web && python3 -m http.server 8741
# เปิด http://127.0.0.1:8741/
```

แผงซ้าย = ข้อความไทยในเอนจินที่ตัดที่ช่องว่างเท่านั้น (ตัวสีแดง = โดนตัดกลางคำ)
แผงขวา = ข้อความเดียวกันหลังผ่าน thaiwrap

## กฎความปลอดภัยของการแทรก ZWSP

- ไม่แทรกก่อนสระ/วรรณยุกต์ที่ประกอบกับพยัญชนะก่อนหน้า (ั ิ ี ึ ื ุ ู ็ ่ ้ ๊ ๋ ํ ๎)
- ไม่แทรกหลังสระหน้า (เ แ โ ใ ไ) ที่ยังไม่มีพยัญชนะตาม
- ไม่แทรกก่อน ๆ (U+0E46 ไม้ยมก) และ ฯ (U+0E2F ไปยาลน้อย)
- ไม่แทรกทั้งช่วง `{placeholder}` และแท็ก rich-text `<...>` (ปิดได้ด้วย option `ProtectPlaceholders`)
- ไม่แทรกติดช่องว่างเดิม, ต้น/ท้ายข้อความ, ก่อนเครื่องหมายปิด หรือหลังเครื่องหมายเปิด
- ตัวเลข/อังกฤษ (`25%`, `150/200`, `XP`) คงเป็นก้อนเดียว
- ชื่อเฉพาะ/คำนอกพจนานุกรมที่สั้นกว่า 8 cluster ถือเป็นก้อนเดียว ยาวกว่านั้นจึงยอมให้ตัดระดับ cluster (กันล้นกรอบ)

## สถานะ

- **Phase 0 (พิสูจน์ไอเดีย): เสร็จ** — ดูผลที่ [docs/PHASE0.md](docs/PHASE0.md)
- **อัปเกรดใหญ่ (จากความร่วมมือกับ [ThaiW3Setup](https://github.com/AeyXD/ThaiW3Setup))**: อัลกอริทึม DP · dict รวม 65,758 คำ · รองรับ Scaleform ผ่าน `--break-char hairspace`
- **Phase 1 (ตาราง compatibility): เสร็จ** — ดูที่ [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md)
  - TextMeshPro, Ren'Py รับ ZWSP ✅ · Godot ตัดไทย native อยู่แล้ว · RPG Maker MZ ต้องแก้ที่ plugin
  - Font audit: Sarabun/Kanit/Prompt/Mitr/Trirong **ไม่มี glyph ZWSP** → ใช้ `tools/font_zwsp_patch.py` เติมให้
- **Phase 2 (CLI ใช้งานจริง): เสร็จ** — ดูที่ [docs/PHASE2.md](docs/PHASE2.md)
  - รองรับ csv/tsv/json/po/key-value · dry-run diff · เลือกคอลัมน์ · BOM/CRLF คงเดิม · idempotent · โหมด folder
  - เหลือ: pilot กับทีมแปลจริง 1 ทีม
- **Phase 3 (BepInEx plugin): โค้ดเสร็จ** — ดูที่ [docs/PHASE3.md](docs/PHASE3.md)
  - hook TextMeshPro/UGUI ตอนรันด้วย Harmony + cache + config · net35 DLL เดียวจบไม่มี dependency
  - แพ็กติดตั้งด้วย `./tools/package-plugin.sh` · เหลือ: ทดสอบในเกม Unity จริง

## License

- โค้ด thaiwrap: เตรียมเปิดเป็น open source (MIT)
- Word list: มาจาก pythainlp corpus — Apache License 2.0

# Phase 2 Report — CLI ใช้งานจริงกับไฟล์งานแปล

วันที่: 2026-10-05 · สถานะ: **เสร็จ** · เทส **38/38 ผ่าน**

## สิ่งที่เพิ่ม

### 1. รองรับไฟล์งานแปลจริง 5 ฟอร์แมต (`src/ThaiWrap.Formats/`)

| ฟอร์แมต | วิธีประมวลผล | สิ่งที่ไม่ถูกแตะ |
|---|---|---|
| **csv / tsv** (RFC4180) | เฉพาะคอลัมน์ที่ระบุ (`--columns "1,3"` หรือ `"ชื่อคอลัมน์"`) | คอลัมน์ ID/key, แถว header (เมื่อเลือกตามชื่อ) |
| **json** | แก้เฉพาะเนื้อหา string ตำแหน่ง "ค่า" — ตัว rewriter อนุรักษ์เดิม | keys (byte-identical), ลำดับ, indent, string ที่ไม่มีไทย |
| **po** (gettext) | เฉพาะ msgstr / msgstr[n] | msgid, คอมเมนต์, entry ที่ obsolete (#~), header metadata |
| **keyvalue** (XUnity-style `original=translation`) | เฉพาะฝั่งขวาของ `=` ตัวแรก | key, บรรทัด `#` |
| **text** | ทุกบรรทัด (เดิมจาก Phase 0) | — |

### 2. ความปลอดภัยของ pipeline

- **`--dry-run`** — แสดง diff รายบรรทัด (ZWSP แสดงเป็น `·`) โดยไม่เขียนไฟล์ใดๆ
- **เลือกคอลัมน์/ฟิลด์** — กัน ZWSP ปนเข้าไปใน string ID (เช่น `item_sword_desc` ไม่มีวันถูกแตะ)
- **BOM ถูกรักษา** — อ่านแล้วเขียนกลับด้วย encoding/BOM เดิม (UTF-8 BOM, UTF-16 LE/BE)
- **Line ending ถูกรักษา** — CRLF ของ csv, terminator รายบรรทัดของ po/keyvalue
- **Idempotent** — รันซ้ำกับไฟล์ที่มี ZWSP แล้ว = ไม่เปลี่ยนแปลง (ZWSP ทำหน้าที่เป็น barrier ใน tokenizer) ทดสอบแล้ว
- **โหมด folder** — default เขียนเป็น `<ชื่อ>.zwsp.<ext>` ข้างไฟล์เดิม (ไม่มีทางทำลายต้นฉบับ), `--inplace` เขียนทับพร้อมสำรอง `.bak` อัตโนมัติ

### 3. ตัวอย่างการใช้งานจริง

```bash
# ดูก่อนว่าจะเปลี่ยนอะไรบ้าง (แนะนำ: ทำทุกครั้งก่อนเขียนจริง)
dotnet run --project src/ThaiWrap.Cli -- data/items.csv --columns thai --dry-run

# เขียนไฟล์ใหม่
dotnet run --project src/ThaiWrap.Cli -- data/items.csv --columns thai -o data/items.out.csv

# ทั้งโฟลเดอร์ (สร้างไฟล์ .zwsp. ข้างเดิม ไม่ทับต้นฉบับ)
dotnet run --project src/ThaiWrap.Cli -- data/ -r

# XUnity.AutoTranslator: Text\en.txt → th.txt เสร็จแล้ว
dotnet run --project src/ThaiWrap.Cli -- th.txt --format keyvalue -o th.out.txt
```

## ผลเทส

- 38/38 checks ผ่าน (19 ของ core เดิม + 19 ใหม่ของ formats)
- CSV: key/en คงเดิม 100%, ฟิลด์มี comma ยังเป็นฟิลด์เดียว, CRLF คงเดิม
- JSON: keys byte-identical, ค่า `\uXXXX` escape ถูก decode→แปลง→เข้ารหัสใหม่ถูกต้อง, โครงสร้าง valid
- PO: msgid คงเดิมทุกบรรทัด, multiline msgstr ถูกรวมและแปลงถูก
- End-to-end: dry-run diff, folder mode (-r), BOM preservation, idempotency — ผ่านทั้งหมด

## เหลือทำ (นอกเฟสนี้)

- หาทีมแปลจริง 1 ทีม มา pilot กับเกมจริง 1 เกม (เกณฑ์จบ Phase 2 เต็มรูปแบบ — ต้องการผู้ใช้จริง)
- เพิ่ม `.xls/.xlsx`, `.locres`, `.yaml` ตามความต้องการของทีมที่มาใช้
- Phase 3: BepInEx runtime plugin สำหรับเกม Unity ที่ข้อความโหลดตอนรัน

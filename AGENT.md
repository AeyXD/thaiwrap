# AGENT.md — คำแนะนำสำหรับ AI agent ที่ทำงานใน thaiwrap

ตัดคำไทยด้วยพจนานุกรม แล้วแทรก ZWSP (U+200B) หรือ hairspace (U+200A) ที่รอยต่อคำ
เพื่อให้เอนจินเกมที่ตัดบรรทัดที่ช่องว่างตัดภาษาไทยถูกจุด — โดยไม่ต้องแก้เกม

- ตอบ/เขียนเอกสารเป็นภาษาไทย · สถานะงานล่าสุดอยู่ที่ `.handover/HANDOVER.md`
  (git-excluded — ถ้าไม่มีไฟล์นั้นแปลว่า clone ใหม่ ให้อ่าน `README.md` + `docs/` ก่อน)
- หลักการใหญ่ของโปรเจกต์: **อนุรักษ์ข้อมูลเดิมเสมอ** — parser และ output ต้อง
  preserve ส่วนที่ไม่แตะ (BOM/CRLF/คอลัมน์อื่น/key ของ JSON) · ผู้ใช้ดูผลด้วย
  `--dry-run` ก่อนเขียนทับจริงทุกครั้ง

## สภาพแวดล้อม / คำสั่งหลัก

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"

dotnet test tests/ThaiWrap.Tests                                # เทส — ต้องผ่านก่อนจบทุกครั้ง
dotnet run --project src/ThaiWrap.Cli -- samples/game_sentences.txt -m sep
dotnet run --project src/ThaiWrap.Cli -- tests/samples/items.csv --columns thai --dry-run
```

- Target: Core = netstandard2.0, Plugin(BepInEx) = net35 — **ห้ามเพิ่ม dependency**
  ในสองตัวนี้ · CLI/Formats ก็ไม่มี dependency เช่นกัน
- CI รันบน ubuntu + windows จริง — อย่าใช้ API เฉพาะ macOS

## โครงสร้าง

| ที่อยู่ | หน้าที่ |
|---|---|
| `src/ThaiWrap/ThaiSegmenter.cs` | หัวใจ: DP optimal segmentation + trie + กฎพยัญชนะทวิภาค |
| `src/ThaiWrap.Formats/` | parser csv/tsv/json/po/keyvalue — อนุรักษ์เดิม |
| `src/ThaiWrap.Cli/` | CLI + options |
| `tests/ThaiWrap.Tests/` | invariant + ชุดประโยคเกม |
| `data/words_th.txt` | พจนานุกรม 65,758 คำ |
| `docs/COMPATIBILITY.md` | ความเข้ากันได้เอนจิน/ฟอนต์ (ZWSP vs hairspace) |

## ข้อห้าม / ข้อควรระวัง

1. **Port parity** — `ThaiSegmenter.cs` port จาก `ThaiW3Setup/core/thai_wrap.py` (MIT)
   เมื่อแก้ segmentation: หรือ sync สองฝั่งให้ตรงกัน หรือระบุในคอมเมนต์/เทสว่าเหตุใด
   แตกต่าง · มาตรฐานเทียบ output 41/42 ประโยค (ต่างเฉพาะเคส placeholder ที่ตั้งใจปกป้อง)
2. **พจนานุกรม** = union ของ pythainlp (Apache-2.0) + ICU thaidict (Unicode License) —
   เครดิตอยู่ใน `LICENSE`/`NOTICE` ต้องรักษาไว้ตรงจริงทุกครั้งที่แก้ `data/words_th.txt`
   · ห้ามใส่ชื่อเฉพาะจากเกม/โปรเจกต์ใดเข้า dict กลาง — ผู้ใช้เพิ่มเองผ่านไฟล์
   `words_extra.txt` วางข้าง word list (CLI/plugin หาใช้เองอัตโนมัติ · รองรับ `-คำ` = ลบ
   คำที่แย่งพยางค์) · หาคำที่ควรเพิ่มจากรายงาน `--stats` "คำนอกพจนานุกรมที่พบบ่อย"
   · บทเรียน (ผู้ใช้ยืนยัน 2026-10-29): อย่าเชื่อรายการ "คำแท้ที่ dict ไม่มี" จาก ALLOW
     ของ W3 ตรง ๆ — จราจล/รำรึก/รึก ทั้งชุดเป็น typo ที่ทีมอนุมัติไว้ (ของจริง: จราจร
     จลาจล รำลึก ลึก — dict มีครบแล้ว) · จะเติม dict กลางต้องยืนยันกับพจนานุกรม
     มาตรฐาน + ผู้ใช้ก่อน พร้อมบันทึกที่มา
3. **ข้อความไทยจริงสกปรก** — ตระกูล `เ+เ` แทน `แ`, วรรณยุกต์ซ้อน, ลำดับมาร์คผิด
   ทำให้คำกลายเป็น unknown แล้ว merge pass เกาะกินคำข้าง ๆ (จุดตัดเพี้ยนทั้งย่อหน้า)
   เมื่อทำฟีเจอร์ normalize/พบปัญหา segmentation ให้นึกถึงตระกูลนี้ก่อนเตะอัลกอริทึม
4. **ไฟล์ binary ใน `publish/`** ถูก commit ไว้ — เจอ modified อย่า `git add .`
   ให้ดูทีละไฟล์ว่าตั้งใจ
5. Scaleform/Flash (เช่น Witcher 3) ไม่นับ ZWSP เป็นจุดตัด — ใช้ hairspace
   และฟอนต์ต้องมี glyph ว่างที่ U+200A (ดู `docs/COMPATIBILITY.md`)

## ธรรมเนียม

- เทสใหม่ประกอบทุกฟีเจอร์/ทุก bugfix — ใส่ประโยคจริงที่เคยพลาดเข้าชุดเทสเสมอ
- ข้อความอินพุตเป็นของผู้ใช้: default ต้องปลอดภัยที่สุด (ไม่เขียนทับโดยไม่ระบุ -o,
  `--inplace` สร้าง .bak เสมอ)
- จบ session ทำงาน → อัปเดต `.handover/HANDOVER.md` (สรุปสิ่งที่ทำ/ค้าง พร้อม commit hash)

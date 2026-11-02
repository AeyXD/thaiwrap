# AGENTS.md — คำแนะนำสำหรับ AI agent ที่ทำงานใน thaiwrap

ตัดคำไทยด้วยพจนานุกรม แล้วแทรก ZWSP (U+200B) หรือ hairspace (U+200A) ที่รอยต่อคำ
เพื่อให้เอนจินเกมที่ตัดบรรทัดที่ช่องว่างตัดภาษาไทยถูกจุด — โดยไม่ต้องแก้เกม

- ตอบ/เขียนเอกสารเป็นภาษาไทย · สถานะงานล่าสุดอยู่ที่ `.handover/HANDOVER.md`
  — **HANDOVER อยู่ใน repo private รวม `AeyXD/handover-notes`** (`.handover` เป็น symlink →
  `../handover-notes/thaiwrap` ไม่อยู่ใน git ของ repo นี้) · อัปเดต/commit/push HANDOVER **ที่ repo รวม**
  ตามโครงใน `../handover-notes/GUIDE.md` · เริ่ม-จบ session ให้ pull/push ทั้งสอง repo
  · ถ้าไม่มี symlink = เครื่องใหม่ ดูวิธีตั้งใน README ของ repo รวม
- หลักการใหญ่ของโปรเจกต์: **อนุรักษ์ข้อมูลเดิมเสมอ** — parser และ output ต้อง
  preserve ส่วนที่ไม่แตะ (BOM/CRLF/คอลัมน์อื่น/key ของ JSON) · ผู้ใช้ดูผลด้วย
  `--dry-run` ก่อนเขียนทับจริงทุกครั้ง

## สภาพแวดล้อม / คำสั่งหลัก

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"

dotnet run --project tests/ThaiWrap.Tests -- data/words_th.txt  # เทส — ต้องจบด้วย "ALL n CHECKS PASSED"
dotnet run --project src/ThaiWrap.Cli -- samples/game_sentences.txt -m sep
dotnet run --project src/ThaiWrap.Cli -- tests/samples/items.csv --columns thai --dry-run
```

- **อย่าใช้ `dotnet test`** — โปรเจกต์เทสเป็น console runner (ไม่ใช่ xunit) เดิมคำสั่งนี้
  จบ exit 0 โดยไม่รันเทสสักข้อ ตอนนี้ csproj บังคับให้ error พร้อมบอกคำสั่งที่ถูก ·
  ใช้คำสั่งเดียวกับ CI ด้านบน (exit != 0 เมื่อมี check ใด fail)

- Target: Core = netstandard2.0, Plugin(BepInEx) = net35 — **ห้ามเพิ่ม dependency**
  ในสองตัวนี้ · CLI/Formats ก็ไม่มี dependency เช่นกัน
- CI รันบน ubuntu + windows จริง — อย่าใช้ API เฉพาะ macOS

## โครงสร้าง

| ที่อยู่ | หน้าที่ |
|---|---|
| `src/ThaiWrap/ThaiSegmenter.cs` | หัวใจ: DP optimal segmentation + trie + กฎพยัญชนะทวิภาค + วลีห้ามตัด (nobreak) |
| `src/ThaiWrap/ThaiNormalizer.cs` | `--normalize` ยุบรูปพิมพ์ผิด (เเ→แ, มาร์คซ้อน/ลำดับผิด) |
| `src/ThaiWrap/ThaiChunker.cs` | รวมคำเป็นกลุ่มวลีสำหรับโหมด space (พอร์ตจาก ThaiW1Setup) |
| `src/ThaiWrap.Formats/` | parser csv/tsv/json/po/keyvalue — อนุรักษ์เดิม |
| `src/ThaiWrap.Cli/` | CLI + options |
| `src/ThaiWrap.BepInEx/` | plugin hook TMP/UGUI ตอนเกมรัน (net35) · แผนทดสอบ `docs/PLUGIN-TEST-PLAN.md` |
| `tests/ThaiWrap.Tests/` | invariant + ชุดประโยคเกม (console runner) |
| `data/words_th.txt` | พจนานุกรม (65,758 คำ ณ v0.9.0) |
| `nobreak.txt` · `words_extra.txt` | วลีห้ามตัด · คำเฉพาะเกม — ไฟล์ของผู้ใช้ วางข้าง word list (ไม่อยู่ใน repo) |
| `web/` | หน้าทดสอบ visual — `thaiwrap.js` เป็น **JS port ของ core** ณ v0.4.0 (ยังไม่มี normalize/โหมด space/words_extra/nobreak · ไม่มีเทสใน CI) |
| `tools/` | สคริปต์ package plugin / patch ฟอนต์ / ทดสอบ Godot |
| `publish/` | binary single-file ของ CLI ต่อ RID (commit ไว้ใน repo) |
| `docs/COMPATIBILITY.md` | ความเข้ากันได้เอนจิน/ฟอนต์ (ZWSP vs hairspace · NBSP สำหรับวลีห้ามตัด) |

## ข้อห้าม / ข้อควรระวัง

1. **Port parity** — `ThaiSegmenter.cs` port จาก `ThaiW3Setup/core/thai_wrap.py` (MIT)
   เมื่อแก้ segmentation: หรือ sync สองฝั่งให้ตรงกัน หรือระบุในคอมเมนต์/เทสว่าเหตุใด
   แตกต่าง · มาตรฐานเทียบ output 41/42 ประโยค (วัดตอน port · ต่างเฉพาะเคส placeholder
   ที่ตั้งใจปกป้อง) · แก้ core แล้วต้องดู `web/thaiwrap.js` ด้วย — ตอนนี้ตามหลัง C# อยู่
   (ดูตารางโครงสร้าง) ถ้าไม่ sync ให้ระบุในคอมมิตว่าฟีเจอร์ไหนยังไม่มีฝั่งเว็บ
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
   ให้ดูทีละไฟล์ว่าตั้งใจ · rebuild เฉพาะตอนออกเวอร์ชัน (commit `vX.Y.Z`) ทั้ง 3 RID
   (`linux-x64` `osx-arm64` `win-x64`) ด้วย `dotnet publish src/ThaiWrap.Cli -c Release -r <RID>`
   (csproj ตั้ง single-file และคัด `data/words_th.txt` ไปที่ `data/` ให้เองแล้ว)
5. Scaleform/Flash (เช่น Witcher 3) ไม่นับ ZWSP เป็นจุดตัด — ใช้ hairspace
   และฟอนต์ต้องมี glyph ว่างที่ U+200A (ดู `docs/COMPATIBILITY.md`)
6. **วลีห้ามตัด (`nobreak.txt`, v0.9.0)** — ครอบวลีทั้งก้อนแบบ mask ไม่มีจุดตัดใดภายใน
   และแทนช่องว่างกลางวลีด้วย NBSP (U+00A0) · การแทนแค่ช่องว่าง→NBSP ไม่พอ เพราะคำ
   unknown ในวลียังถูกแยกได้ · ต้อง idempotent (จับทั้งรูป space/NBSP) · เอนจินที่ตัดเฉพาะ
   U+0020 + ฟอนต์สเปซกว้าง 0 ต้องใช้วิธี wide glyph แทน (ดูตารางใน `docs/COMPATIBILITY.md`)

## ธรรมเนียม

- เทสใหม่ประกอบทุกฟีเจอร์/ทุก bugfix — ใส่ประโยคจริงที่เคยพลาดเข้าชุดเทสเสมอ
- ข้อความอินพุตเป็นของผู้ใช้: default ต้องปลอดภัยที่สุด (ไม่เขียนทับโดยไม่ระบุ -o,
  `--inplace` สร้าง .bak เสมอ)
- จบ session ทำงาน → อัปเดต `.handover/HANDOVER.md` (สรุปสิ่งที่ทำ/ค้าง พร้อม commit hash
  และแก้วันที่ "อัปเดต" หัวไฟล์ให้ตรง) — เขียนผ่าน symlink แล้ว commit+push ที่ repo รวม
  ตามเช็กลิสต์ท้าย GUIDE
- ตัวเลขในเอกสาร (จำนวนคำ/จำนวนเทส) ให้ระบุว่าวัด ณ เวอร์ชันไหน — ค่าจริงดูจากคำสั่ง ไม่ใช่จากเอกสาร

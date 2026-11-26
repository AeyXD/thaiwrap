# AGENTS.md — คำแนะนำสำหรับ AI agent ที่ทำงานใน thaiwrap

ตัดคำไทยด้วยพจนานุกรม แล้วแทรกจุดตัดบรรทัดที่รอยต่อคำ ให้เอนจินเกมตัดภาษาไทยถูกจุด
โดยไม่ต้องแก้เกม — เลือกตัวคั่นตามเอนจิน (ดูตารางเต็มใน `docs/COMPATIBILITY.md`):
ZWSP ปกติ · hairspace สำหรับ Scaleform (W3) · ช่องว่างจริงแบบกลุ่มวลีสำหรับ Aurora (W1)
· วลีห้ามตัดกลางวลีใช้ NBSP (`nobreak.txt`)

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
dotnet run --project src/ThaiWrap.Cli -- tests/samples/items.csv --mispaste   # ตรวจวางผิดแถว (csv/tsv)
dotnet run --project src/ThaiWrap.Cli -- tests/samples -r --placeholders; echo $? # {0} %s <tag> เทียบต้นฉบับ: exit 1 ถ้าพบ
dotnet run --project src/ThaiWrap.Cli -- data/ -r --check ; echo $?           # lint: exit 1 ถ้าไทยยังไม่แตรป

./tools/fetch-bepinex.sh                       # ครั้งแรก — ดาวน์โหลด BepInEx refs (lib/ ไม่ commit)
dotnet build src/ThaiWrap.BepInEx -c Release   # plugin — ต้อง 0 warning 0 error
./tools/js-parity.sh                           # web/thaiwrap.js ต้องตรงกับ CLI C# (ต้องมี node)
```

- **อย่าใช้ `dotnet test`** — โปรเจกต์เทสเป็น console runner (ไม่ใช่ xunit) เดิมคำสั่งนี้
  จบ exit 0 โดยไม่รันเทสสักข้อ ตอนนี้ csproj บังคับให้ error พร้อมบอกคำสั่งที่ถูก ·
  ใช้คำสั่งเดียวกับ CI ด้านบน (exit != 0 เมื่อมี check ใด fail)

- Target: Core = netstandard2.0, Plugin(BepInEx) = net35 — **ห้ามเพิ่ม dependency**
  ในสองตัวนี้ · CLI/Formats ก็ไม่มี dependency เช่นกัน · ระวัง API ที่ net35/netstandard2.0
  ไม่มี เช่น `string.StartsWith(value, startIndex, comparison)` (net35 ไม่มี overload นี้ —
  เคยชนแล้ว ต้องเขียน helper เอง)
- CI รันบน ubuntu + windows จริง — อย่าใช้ API เฉพาะ macOS

## โครงสร้าง

| ที่อยู่ | หน้าที่ |
|---|---|
| `src/ThaiWrap/ThaiSegmenter.cs` | หัวใจ: DP optimal segmentation + trie + กฎพยัญชนะทวิภาค + วลีห้ามตัด (nobreak) + รวม `words_extra` |
| `src/ThaiWrap/ThaiEnglishSense.cs` | ตัดข้อเสนอของตัวตรวจคำผิดด้วยต้นฉบับอังกฤษ (พอร์ต en_sense.py ของ W3) — CLI หาคู่อังกฤษจาก csv/tsv (เซลล์ละตินมากสุด หรือ `--source-column`) · po msgid · key=value · ข้อความที่มีตัวเลข/ขีดล่างไม่นับเป็นอังกฤษ (กันคอลัมน์ ID ซ่อนคำผิดจริง) |
| `src/ThaiWrap/ThaiContextChecker.cs` | ตัวตรวจคำผิดตามบริบทใน `--stats` (ร↔ล + สะกดถูกแต่ผิดบริบท) — พอร์ต find_ral_swaps / find_wrong_word ของ W3 ผลตรง Python ทุกจุดบนคลัง W3 · ข้อยกเว้นเฉพาะเกมอยู่ใน `check_allow.txt` ของโปรเจกต์ ไม่ใส่ในโค้ด |
| `src/ThaiWrap/PlaceholderChecker.cs` | `--placeholders` เทียบ `{…}` `<…>` printf ในคำแปลกับต้นฉบับ (multiset ไม่สนลำดับ) · `FindControlEnd` เป็นนิยามส่วนควบคุมที่ใช้ร่วมกับ `SplitControlParts` ของตัวตัดคำ — แก้ที่เดียว |
| `src/ThaiWrap/ThaiNormalizer.cs` | `--normalize` ยุบรูปพิมพ์ผิด (เเ→แ, มาร์คซ้อน/ลำดับผิด) |
| `src/ThaiWrap/ThaiChunker.cs` | รวมคำเป็นกลุ่มวลีสำหรับโหมด space (พอร์ตจาก ThaiW1Setup) |
| `src/ThaiWrap.Formats/` | parser csv/tsv/json/po/keyvalue — อนุรักษ์เดิม · `MispasteDetector` (--mispaste) · `TableColumns` (--source-column/--columns ของโหมดตรวจ — ไม่ระบุ = เดาจากเนื้อหาแบบเดิม) · `JsonPathPattern` (--json-path แบบ JSON Pointer + `*`/`**`) |
| `src/ThaiWrap.Cli/` | CLI + options |
| `src/ThaiWrap.BepInEx/` | plugin hook TMP/UGUI ตอนเกมรัน (net35) · แผนทดสอบ `docs/PLUGIN-TEST-PLAN.md` |
| `tests/ThaiWrap.Tests/` | invariant + ชุดประโยคเกม — 240 checks ณ หลัง v0.12.1 (console runner) |
| `data/words_th.txt` | พจนานุกรม (65,758 คำ ณ v0.12.1) |
| `nobreak.txt` · `words_extra.txt` · `check_allow.txt` | วลีห้ามตัด · คำเฉพาะเกม · ข้อยกเว้นตัวตรวจคำผิด — ไฟล์ของผู้ใช้ วางข้าง word list (ไม่อยู่ใน repo) |
| `web/` | หน้าทดสอบ visual — `thaiwrap.js` เป็น **JS port ของ core** ตรงกับ C# ณ v0.12.1 ทุกตัวเลือกตัดคำ (ไม่รวม `--stats`/ร↔ล/mispaste/placeholders) · ตรวจ parity ใน CI ด้วย `tools/js-parity.sh` · push `web/**` = deploy GitHub Pages อัตโนมัติ |
| `tools/` | `publish.sh` (rebuild `publish/` 3 RID · `--zip` = zip CLI ไว้แนบ Release) · `package-plugin.sh` (zip plugin) · `fetch-bepinex.sh` (refs สำหรับ build plugin) · `js-parity.sh` · patch ฟอนต์ · ทดสอบ Godot |
| `publish/` | binary single-file ของ CLI ต่อ RID (commit ไว้ใน repo) |
| `docs/COMPATIBILITY.md` | ความเข้ากันได้เอนจิน/ฟอนต์ (ZWSP vs hairspace · NBSP สำหรับวลีห้ามตัด) |

## ข้อห้าม / ข้อควรระวัง

1. **Port parity** — `ThaiSegmenter.cs` port จาก `ThaiW3Setup/core/thai_wrap.py` (MIT)
   เมื่อแก้ segmentation: หรือ sync สองฝั่งให้ตรงกัน หรือระบุในคอมเมนต์/เทสว่าเหตุใด
   แตกต่าง · มาตรฐานเทียบ output 41/42 ประโยค (วัดตอน port · ต่างเฉพาะเคส placeholder
   ที่ตั้งใจปกป้อง) · แก้ core แล้วต้องแก้ `web/thaiwrap.js` ตามด้วย — `tools/js-parity.sh`
   (CI) เทียบผล JS กับ CLI จริงหลายชุดตัวเลือก จะ fail ถ้าไม่ตาม · ตัวเลือกใหม่ต้องเพิ่มใน
   `tools/js-parity.js` + ชุดใน `.sh` และเคสใน `tests/samples/parity.txt` ด้วย
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
   ให้ดูทีละไฟล์ว่าตั้งใจ · rebuild เฉพาะตอนออกเวอร์ชัน (commit `vX.Y.Z`) ด้วย
   `./tools/publish.sh` (ทั้ง 3 RID `linux-x64` `osx-arm64` `win-x64` + รัน binary ของเครื่องนี้จริง)
   · build ไม่ deterministic — source เดิมก็ได้ไบต์ต่าง ถ้าไม่ได้ออกเวอร์ชันให้ `git checkout -- publish/`
5. Scaleform/Flash (เช่น Witcher 3) ไม่นับ ZWSP เป็นจุดตัด — ใช้ hairspace
   และฟอนต์ต้องมี glyph ว่างที่ U+200A (ดู `docs/COMPATIBILITY.md`) · ช่องว่างจริงหน้า ๆ
   ("ต่าง ๆ") ยังเป็นจุดตัด → ต้องเป็น NBSP (`--yamok-nbsp` / plugin `YamokNbsp` · opt-in)
   และฟอนต์ต้องวาด NBSP กว้างเท่าช่องว่าง · เปิดแล้ว round-trip "ลบ ZWSP = ต้นฉบับ" ไม่จริงอีก
   (space→NBSP) เหมือน `--normalize`
6. **วลีห้ามตัด (`nobreak.txt`, v0.9.0)** — ครอบวลีทั้งก้อนแบบ mask ไม่มีจุดตัดใดภายใน
   และแทนช่องว่างกลางวลีด้วย NBSP (U+00A0) · การแทนแค่ช่องว่าง→NBSP ไม่พอ เพราะคำ
   unknown ในวลียังถูกแยกได้ · ต้อง idempotent (จับทั้งรูป space/NBSP) · เอนจินที่ตัดเฉพาะ
   U+0020 + ฟอนต์สเปซกว้าง 0 ต้องใช้วิธี wide glyph แทน (ดูตารางใน `docs/COMPATIBILITY.md`)
7. **โหมด `--break-char space` ไม่ idempotent** — ช่องว่างจริงของทีมแปลแยกจากจุดตัดที่แทรก
   ไม่ได้ รันซ้ำบนไฟล์ที่แปลงแล้วจะเพิ่มช่องว่างซ้ำ — ต้องรันจากต้นฉบับสะอาดหรือ `--dry-run`
   ก่อนเสมอ (W1 แก้เรื่องนี้ด้วย WIDE_SPACE PUA ที่ระดับ pipeline เฉพาะเกม)

## ธรรมเนียม

- เทสใหม่ประกอบทุกฟีเจอร์/ทุก bugfix — ใส่ประโยคจริงที่เคยพลาดเข้าชุดเทสเสมอ
- ข้อความอินพุตเป็นของผู้ใช้: default ต้องปลอดภัยที่สุด (ไม่เขียนทับโดยไม่ระบุ -o,
  `--inplace` สร้าง .bak เสมอ)
- จบ session ทำงาน → อัปเดต `.handover/HANDOVER.md` (สรุปสิ่งที่ทำ/ค้าง พร้อม commit hash
  และแก้วันที่ "อัปเดต" หัวไฟล์ให้ตรง) — เขียนผ่าน symlink แล้ว commit+push ที่ repo รวม
  ตามเช็กลิสต์ท้าย GUIDE
- **ออกเวอร์ชัน `vX.Y.Z`** (แนวทางจาก v0.8.0): bump `PluginVersion` ใน `ThaiWrapPlugin.cs`
  ให้ตรงแท็ก → เทสผ่าน → `./tools/publish.sh --zip` → commit "vX.Y.Z: …" (เฉพาะไฟล์ใน
  `publish/`) → `./tools/package-plugin.sh` → แท็ก + GitHub Release ภาษาไทย แนบ `dist/` 4 ไฟล์
  (`thaiwrap-cli-<rid>.zip` ×3 + `ThaiWrap-BepInEx.zip`) → release notes ต้องระบุสถานะ plugin ตรงๆ
  (ตอนนี้คือ "ยังไม่ได้ทดสอบในเกมจริง" จนกว่าแผน `docs/PLUGIN-TEST-PLAN.md` จะผ่าน)
- ตัวเลขในเอกสาร (จำนวนคำ/จำนวนเทส) ให้ระบุว่าวัด ณ เวอร์ชันไหน — ค่าจริงดูจากคำสั่ง ไม่ใช่จากเอกสาร

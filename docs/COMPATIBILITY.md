# ตาราง Compatibility — ZWSP กับ text stack ของเกม (Phase 1)

วันที่: 2026-10-05 · ทดสอบ/วิเคราะห์บน: macOS (arm64), Godot 4.7.2, uGUI 2.0 source, Ren'Py (main branch)

## สรุปตาราง

| Text stack | รับ ZWSP เป็นจุดตัด? | ตัดไทยโดยไม่มี ZWSP? | สรุปสำหรับทีมแปล |
|---|---|---|---|
| **เอนจินที่ตัดบรรทัดที่ช่องว่างเท่านั้น** (กลุ่มเป้าหมายหลักของ thaiwrap) | ✅ โดยนิยาม | ❌ ตัดกลางคำ/ล้นกรอบ | **ใช้ thaiwrap แล้วปัญหาหาย** (ดู Phase 0: 8→0 จุดตัดกลางคำ) |
| **TextMeshPro** (uGUI 2.0 / Unity 6+) | ✅ | ❌ | ใช้ได้เลย — แทรก ZWSP ใน string ก่อน set text |
| **Ren'Py 8.x** (โหมด default `western`, `unicode`, `anywhere`) | ✅ | ❌ (ยกเว้นตั้ง `language "thaic90"`) | ใช้ได้เลย หรือลองโหมด `thaic90` ในตัว |
| **Godot 4.x** (text_server_adv) | ไม่กระทบ layout | ✅ ตัดไทย native ได้ | ZWSP ปลอดภัยแต่ไม่จำเป็น; คุณภาพจุดตัดดู issue #99474 |
| **เบราว์เซอร์ / canvas (ICU, CoreText)** | ✅ | ✅ ตัดไทย native | ใช้ได้ทั้งสองกรณี |
| **RPG Maker MZ** (canvas, ตัดระดับตัวอักษร) | ❌ ไม่มีผล (ไม่ใช้ ZWSP ตัดสินจุดตัด) | ตัดกลางคำ/กลาง cluster อยู่แล้ว | ZWSP ไม่ช่วย — ต้องแก้ที่ตัว plugin จัดการ wrap เอง |
| **Unity UGUI Legacy Text** | ยังไม่ได้ทดสอบ | ❌ (ตัดที่ space) | ต้องทดสอบเพิ่ม |
| **Unreal (Slate/UMG)** | ยังไม่ได้ทดสอบ | ไม่ทราบ | ต้องทดสอบเพิ่ม (UE มี ICUBreakIterator ในเอนจิน แต่ไม่ทราบพาธที่ UMG ใช้จริง) |

## หลักฐานแต่ละแถว

### TextMeshPro (Unity) — ✅ รับ ZWSP

วิเคราะห์จาก source ของ [uGUI 2.0](https://github.com/Unity-Technologies/uGUI) (`com.unity.ugui/Runtime/TMP/TextMeshPro.cs`):

```csharp
// ลูป word wrapping (ราวบรรทัด 4208):
if ((isWhiteSpace || charCode == 0x200B || ...) && ...)
{
    isFirstWordOfLine = false;
    shouldSaveHardLineBreak = true;   // ← ZWSP = จุด save break เหมือน space
}
```

และ ZWSP ถูกกันออกจากการ render เป็น glyph (ราวบรรทัด 3134) — ไม่โชว์ tofu แม้ font atlas ไม่มี glyph

ข้อควรระวัง: [TMP_InputField มี bug เก็บ ZWSP ติดไปใน text property](https://issuetracker.unity.com) — ถ้าข้อความไหลเข้า input field ให้ strip ZWSP ก่อน

### Ren'Py — ✅ รับ ZWSP

วิเคราะห์จาก source ของ [renpy](https://github.com/renpy/renpy) (`renpy/text/textsupport.pyx`):

```python
def annotate_western(list glyphs):        # ← โหมด default ของภาษา western
    ...
    elif g.character == 0x20 or g.character == 0x200b:
        g.split = SPLIT_INSTEAD           # ← ตัดที่ ZWSP เหมือน space เป๊ะ
```

- โหมด `unicode`/`eastasian` ใช้ UAX #14 (`linebreak.pxi` มี class BC_ZW = break หลัง ZW ตาม rule LB8)
- โหมด `anywhere` ก็จัดการ 0x200b เป็น SPLIT_INSTEAD เหมือนกัน
- **โบนัส**: Ren'Py มี `language "thaic90"` ซึ่งเป็นอัลกอริทึมตัดไทยในตัว — ทีมแปล Ren'Py อาจไม่ต้องใช้ thaiwrap เลยถ้าเปิดโหมดนี้ได้
- การ render: `renpy/text/font.py` ลงทะเบียน ZWSP เป็น surface กว้าง 0 เอง → ไม่ tofu แม้ฟอนต์ไม่มี glyph

### Godot 4.7.2 — ตัดไทย native ได้, ZWSP ปลอดภัย

Runtime test จริง headless (`tools/godot-compat/thai_test2.gd`, SystemFont Thonburi, ฟอนต์ 20px):

```
ข้อความไทยไม่มี space เลย, กล่องกว้าง 200px:
  plain (ไม่มี ZWSP):  lines=3  bbox=190px   ← ตัดเองได้ ไม่ล้น
  zwsp  (มี ZWSP):     lines=3  bbox=190px   ← layout ไม่เปลี่ยน
```

แปลว่า text_server_adv ของ Godot จัดการภาษาไทยเองอยู่แล้ว การแทรก ZWSP ไม่ทำอันตราย
ข้อจำกัดคุณภาพ: [issue #99474](https://github.com/godotengine/godot/issues/99474) — จุดตัดบางจุดทำให้สระ/วรรณยุกต์แสดงเพี้ยน (ยังเปิดอยู่ ณ Godot 4.7)

### RPG Maker MZ — ZWSP ไม่ช่วย

MZ วาดข้อความผ่าน canvas และตัดบรรทัดแบบระดับตัวอักษร (ไม่สน lookup จุดตัดพิเศษ) ผลคือ:
- การตัดกลางคำ/กลาง cluster เกิดอยู่แล้วตั้งแต่ต้น ไม่ว่าจะมี ZWSP หรือไม่
- ทางแก้ที่ถูกคือ plugin ที่เปลี่ยนตัว wrap ของ message window (หรือเว้นวรรคจริง) — ไม่ใช่การแทรก ZWSP
- ด้าน render ปลอดภัย: ทดสอบ canvas บน macOS พบว่า ZWSP กว้าง 0 และไม่มีหมึกแม้ฟอนต์ไม่มี glyph (ด้านล่าง)

### Canvas / ฟอนต์ (probe จริงผ่าน `web/font_probe.html`)

| ฟอนต์ | กว้าง ZWSP | หมึกที่วาด |
|---|---|---|
| Sarabun เดิม (ไม่มี glyph ZWSP) | 0.0px | 0 พิกเซล (มองไม่เห็น) |
| Sarabun ที่ผ่าน `tools/font_zwsp_patch.py` | 0.0px | 0 พิกเซล |

บน macOS ตัว shaper (CoreText) ทำ font fallback ให้เองจึงปลอดภัยแม้ไม่ patch —
แต่**เอนจินที่ดึง glyph ตรงจาก cmap โดยไม่มี fallback** (เช่น บางระบบ bitmap font) จะเสี่ยง tofu → แนะนำ patch ฟอนต์ทุกครั้งเพื่อกันเหตุไม่คาดฝัน (ไม่มีค่าใช้จ่าย)

## Font audit — glyph ZWSP ในฟอนต์ไทย (31 ตัว)

| กลุ่ม | มี ZWSP | ไม่มี |
|---|---|---|
| Google Fonts ยอดนิยม (7) | Noto Sans Thai, IBM Plex Sans Thai | **Sarabun, Kanit, Prompt, Mitr, Trirong** |
| ฟอนต์ระบบ macOS (24) | Thonburi (ทุกน้ำหนัก), Tahoma, Silom, Ayuthaya, Krungthep, Sathu, Arial Unicode MS, Microsoft Sans Serif | **Sukhumvit Set (ทุกน้ำหนัก), Kanit** |

ฟอนต์ที่มี ZWSP ล้วนเป็น glyph เปล่า advance=0 ที่ถูกต้อง (ยกเว้น .LastResort ที่เป็น glyph กล่อง error ของระบบ)

→ ใช้ `tools/font_zwsp_patch.py` เติม glyph เปล่าให้ฟอนต์ใดก็ได้ (TrueType):

```bash
pip install fonttools
python3 tools/font_zwsp_patch.py Sarabun-Regular.ttf        # → Sarabun-Regular-zwsp.ttf
python3 tools/font_zwsp_patch.py โฟลเดอร์ฟอนต์ทั้งหมด/
```

## วิธีทดสอบซ้ำ

- Godot: สคริปต์อยู่ที่ `tools/godot-compat/` (สร้าง project.godot ว่างๆ + `godot --headless --path <dir> --script thai_test2.gd`)
- Canvas/font probe: `cd web && python3 -m http.server 8741` → เปิด `http://127.0.0.1:8741/font_probe.html`
- Source analysis: uGUI `TextMeshPro.cs`, renpy `textsupport.pyx` (clone --depth 1 ทั้งสอง repo)

## ช่องว่างที่เหลือ (ทดสอบเพิ่มในอนาคต)

1. **Unity UGUI Legacy Text** + ยืนยัน TMP ใน Unity เวอร์ชันเก่ากว่า (TMP 1.x/2.x ยุค 2019–2021) — ต้องมี Unity editor จริง
2. **Unreal Engine** Slate/UMG — ต้องมีโปรเจกต์ UE จริง
3. **RPG Maker MZ** บน Windows + ฟอนต์สะกด tofu จริงในเอนจิน bitmap อื่นๆ
4. Godot บน text_server_fallback (ตัว server อีกตัวของ Godot) และเวอร์ชันก่อน 4.0

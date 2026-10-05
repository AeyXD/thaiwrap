# Phase 3 Report — BepInEx Plugin สำหรับเกม Unity

วันที่: 2026-10-05 · สถานะ: **โค้ดเสร็จ + build ผ่าน + โลจิกผ่านเทส 45/45** · การทดสอบในเกมจริง = ขั้นถัดไป

## แนวทาง

สำหรับเกม Unity ที่ข้อความโหลด/ประกอบตอนรัน (dynamic text, การ์ด, ควิว, string concat)
การแทรก ZWSP ลงไฟล์ล่วงหน้าไม่พอ — ต้องแทรกตอนข้อความถูกส่งเข้า text component:

```
เกมเรียก tmp.text = "..."  →  Harmony prefix ดัก  →  ตัดคำ+แทรก ZWSP (มี cache)  →  TMP แสดงผลตัดบรรทัดถูกจุด
```

## สิ่งที่สร้าง

**`src/ThaiWrap.BepInEx/`** — plugin เดียวจบ ไม่มี dependency ภายนอกตอนรัน

| การตัดสินใจ | เหตุผล |
|---|---|
| target **net35** | รันได้กับ BepInEx 5 + Unity Mono ทุกรุ่น (runtime ย้อนหลัง compatible เสมอ) |
| **source-link** core เข้า DLL เดียว | net35 อ้าง netstandard2.0 ไม่ได้ — รวมซอร์สแทน; core เขียนด้วย API ที่มีใน net35 ทั้งหมด (แก้จุดเดียว: `string.Concat(IEnumerable)` → `.ToArray()`) |
| ค้นหา text component **ด้วยชื่อตอนรัน** (`AccessTools.TypeByName`) | ไม่ผูกกับ TMP/UGUI ตอน compile — DLL เดียวใช้ได้ทุกเกมไม่สน Unity เวอร์ชัน; เกมไม่มี TMP ก็ไม่พัง |
| hook `set_text` ผ่าน **Harmony prefix แก้ `ref __0`** | แก้ค่าก่อนเข้า setter จริง ไม่เกิด recursion |
| **cache 4096 รายการ** + fast-reject (ไม่มีไทย = return เลย) | เกม set ข้อความเดิมซ้ำๆ ทุกเฟรม → cache hit; string ละติน/ตัวเลขไม่เสียค่า segmentation เลย |
| word list โหลดจากไฟล์ข้าง DLL | แก้พจนานุกรมได้โดยไม่ต้อง compile ใหม่ + ทีมแปลเพิ่มศัพท์เกมได้เอง |
| config ผ่าน BepInEx Config | Enabled / MaxWordLength / UnknownBreakThreshold — ปรับได้ระหว่างเล่น |

Hook targets: `TMPro.TMP_Text`, `TMPro.TextMeshProUGUI`, `TMPro.TextMeshPro`, `UnityEngine.UI.Text`
(patch เฉพาะที่พบจริงในเกม, ข้ามตัวที่เป็น abstract)

## ผลเทส

เทส runtime logic ผ่านชุดเดิม (45/45 — เพิ่ม 7 ข้อของ filter):
- fast-reject ข้อความไม่มีไทย · cache hit เมื่อ set ซ้ำ · idempotent
- **typewriter simulation**: ป้อนข้อความทีละตัวอักษร (จำลองเกมเปิด dialogue แบบทยอยเผย) — ทุกขั้น round-trip ถูก และข้อความเต็มได้ ZWSP ครบ

## สิ่งที่ยังต้องทำ / ข้อจำกัดที่รู้

1. **ทดสอบในเกมจริง** — ต้องมีเกม Unity + BepInEx 5 ติดตั้งจริง (โค้ดพร้อม แพ็กผ่าน `tools/package-plugin.sh`)
2. **IL2CPP ยังไม่รองรับ** — ต้องใช้แนวทาง BepInEx 6/Interop ต่างกันเยอะ (ทำเฟสหน้าถ้ามีดีมานด์)
3. **TMP_InputField**: การพิมพ์ไทยในช่องกรอกข้อความอาจเจอตำแหน่ง caret เพี้ยน (display ถูก transform แต่ค่าภายในไม่ได้แปลง) — ถ้าเจอให้ปิด Enabled ชั่วคราว แล้วรายงาน issue
4. เกมที่ set ข้อความผ่าน `SetText(char[])` หรือ mesh ตรงๆ ยังไม่ถูกดัก (เพิ่มได้ถ้าเจอจริง)

## ติดตั้ง (สรุป)

```bash
./tools/package-plugin.sh          # สร้าง dist/ThaiWrap/ + zip
# คัดลอก dist/ThaiWrap → <เกม>/BepInEx/plugins/
```

ดูคู่มือเต็มใน `dist/ThaiWrap/INSTALL-TH.txt` หลังแพ็ก

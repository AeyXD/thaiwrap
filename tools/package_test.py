"""package_test.py — ทดสอบ CLI แบบแพ็กเกจจริงจากโฟลเดอร์ชั่วคราว (รันซ้ำได้ใน CI และเครื่อง local)

ใช้กับ: ไบนารีที่ publish แล้ว (single-file) — จำลองสิ่งที่ผู้ใช้ zip ได้รับจริง
รัน: python3 tools/package_test.py <โฟลเดอร์ที่มี ThaiWrap.Cli[.exe] และ data/words_th.txt>

ตรวจ:
 1. --help ออก exit 0
 2. json: ประมวลผลได้ + parser อิสระ (json.load) ยืนยันโครงสร้าง/ค่า + ZWSP อยู่ใน field เป้าหมาย
 3. CP874 ไม่มี BOM: exit 2 + ไม่เขียน output (ไม่ยอมแทนอักขระเงียบๆ)
 4. csv: record ว่างท้ายไฟล์ ("") ไม่หาย ไปกลับครบ
 5. hairspace: แทรก U+200A ไม่ปน ZWSP + สถิติรายงาน hairspace+N
 6. --inplace สองรอบ: .bak เก็บต้นฉบับไว้ + รอบสองข้ามไฟล์
"""
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

# Windows console อาจเป็น cp1252 — บังคับ stdout เป็น UTF-8 ไม่งั้น print ข้อความไทยพังกลางทาง
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ZWSP = "\u200b"
HAIR = "\u200a"


def run(binary, *args, cwd):
    return subprocess.run([str(binary), *[str(a) for a in args]], cwd=str(cwd),
                          capture_output=True, text=True, encoding="utf-8", errors="replace")


def main() -> int:
    pkg = Path(sys.argv[1] if len(sys.argv) > 1 else "publish/out").resolve()
    binary = pkg / ("ThaiWrap.Cli.exe" if sys.platform == "win32" else "ThaiWrap.Cli")
    if not binary.exists():
        print(f"FAIL ไม่พบ {binary}")
        return 1
    if not (pkg / "data" / "words_th.txt").exists():
        print(f"FAIL แพ็กเกจไม่มี data/words_th.txt — ผู้ใช้รันเดี่ยวไม่ได้")
        return 1

    failures = []

    def check(name, cond, detail=""):
        print(("  ok  " if cond else "FAIL  ") + name + (f"  [{detail}]" if detail and not cond else ""))
        if not cond:
            failures.append(name)

    work = Path(tempfile.mkdtemp(prefix="thaiwrap-pkgtest-"))
    try:
        # 1. help
        r = run(binary, "--help", cwd=work)
        check("--help ออก exit 0", r.returncode == 0 and "usage" in r.stdout.lower(), f"exit={r.returncode}")

        # 2. json ด้วย parser อิสระ
        src = work / "d.json"
        src.write_text(json.dumps({"items": {"sword": {"name": "ดาบแห่งรุ่งอรุณ", "desc": "ดาบที่ตกทอดมาแต่โบราณ"}},
                                   "lines": ["นักเดินทางจากแดนไกล"]}, ensure_ascii=False), encoding="utf-8")
        out = work / "d.out.json"
        r = run(binary, src, "-o", out, cwd=work)
        data = json.loads(out.read_text(encoding="utf-8"))  # parser อิสระ: ไม่ใช่โค้ดของเรา
        name_back = data["items"]["sword"]["name"].replace(ZWSP, "")
        check("json: โครงสร้าง parse ได้ ครบทุก key", name_back == "ดาบแห่งรุ่งอรุณ" and "lines" in data)
        check("json: ZWSP อยู่ในค่าไทย ไม่อยู่ใน key", ZWSP in data["items"]["sword"]["desc"] and ZWSP not in "".join(data.keys()))

        # 3. CP874 ไม่มี BOM → ปฏิเสธ
        bad = work / "cp874.txt"
        bad.write_bytes(b"\xbe\xd4\xb4\xd2\xcb")  # "ทดสอบ" ใน CP874
        badout = work / "cp874.out"
        r = run(binary, bad, "-o", badout, cwd=work)
        check("CP874: exit 2 + ข้อความบอกทางแก้", r.returncode == 2 and "UTF-8" in r.stderr)
        check("CP874: ไม่เขียน output ของเสีย", not badout.exists())

        # 4. csv record ว่างท้ายไฟล์
        csv_in = work / "t.csv"
        csv_in.write_bytes("thai\r\n\"\"".encode("utf-8"))
        csv_out = work / "t.out.csv"
        run(binary, csv_in, "--columns", "thai", "-o", csv_out, cwd=work)
        check("csv: record ว่างท้ายไฟล์ไม่หาย", csv_out.read_bytes() == "thai\r\n\"\"".encode("utf-8"),
              repr(csv_out.read_bytes()))

        # 5. hairspace + สถิติ
        txt = work / "hs.txt"
        txt.write_text("นักเดินทางจากแดนไกลมาถึงหมู่บ้าน", encoding="utf-8")
        r = run(binary, txt, "--break-char", "hairspace", "--stats", "-o", work / "hs.out", cwd=work)
        hs_out = (work / "hs.out").read_text(encoding="utf-8")
        check("hairspace: แทรก U+200A ไม่ปน ZWSP", HAIR in hs_out and ZWSP not in hs_out)
        check("hairspace: สถิติรายงานตามตัวที่เลือก", "hairspace+" in r.stderr and "hairspace+0" not in r.stderr,
              r.stderr.strip().splitlines()[-1] if r.stderr else "")

        # 6. --inplace สองรอบ + backup (แยกโฟลเดอร์เฉพาะ กัน run ตายเพราะ cp874.txt ในโฟลเดอร์เดียวกัน)
        folder = work / "inplace"
        (folder / "sub").mkdir(parents=True)
        original = "หินฟื้นฟูเลือด ฟื้นฟูพลังชีวิต 150 หน่วย"
        (folder / "sub" / "items.txt").write_text(original, encoding="utf-8")
        r1 = run(binary, folder, "--inplace", "-r", cwd=work)
        first_content = (folder / "sub" / "items.txt").read_text(encoding="utf-8")
        r2 = run(binary, folder, "--inplace", "-r", cwd=work)
        check("inplace: แทรกจริงรอบแรก", ZWSP in first_content)
        check("inplace: .bak เก็บต้นฉบับแท้", (folder / "sub" / "items.txt.bak").read_text(encoding="utf-8") == original)
        check("inplace: รอบสองข้ามไฟล์ (ไม่ทับ backup)", "ไม่เปลี่ยนแปลง" in r2.stderr)
        check("inplace: .bak ยังเป็นต้นฉบับหลังรอบสอง", (folder / "sub" / "items.txt.bak").read_text(encoding="utf-8") == original)
    finally:
        shutil.rmtree(work, ignore_errors=True)

    print()
    if failures:
        print(f"{len(failures)} จากชุด FAIL: " + ", ".join(failures))
        return 1
    print("PACKAGE TESTS ALL PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())

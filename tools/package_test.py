"""package_test.py — ทดสอบ CLI แบบแพ็กเกจจริงจากโฟลเดอร์ชั่วคราว (รันซ้ำได้ใน CI และเครื่อง local)

ใช้กับ: ไบนารีที่ publish แล้ว (single-file) — จำลองสิ่งที่ผู้ใช้ zip ได้รับจริง
รัน: python3 tools/package_test.py <โฟลเดอร์ที่มี ThaiWrap.Cli[.exe] และ data/words_th.txt>

ตรวจ:
 1. --help ออก exit 0
 2. json: ประมวลผลได้ + parser อิสระ (json.load) ยืนยันโครงสร้าง/ค่า + ZWSP อยู่ใน field เป้าหมาย
 3. CP874 ไม่มี BOM: exit 2 + ไม่เขียน output (ไม่ยอมแทนอักขระเงียบๆ) · มี BOM แต่ข้อมูลเสีย (UTF-8/UTF-16) ก็เช่นกัน
 4. csv: record ว่างท้ายไฟล์ ("") ไม่หาย ไปกลับครบ · field ที่ไม่แตะคง quote เดิม + --check ไม่ฟ้องผิด
 5. hairspace: แทรก U+200A ไม่ปน ZWSP + สถิติรายงาน hairspace+N
 6. --inplace สองรอบ: .bak เก็บต้นฉบับไว้ + รอบสองข้ามไฟล์
 7. --placeholders: placeholder หาย/เกิน → exit 1 + รายงานแถว · ถูกครบ → exit 0 · ไม่เขียนไฟล์ · เคสรีวิว v0.12.0 (#13–#15)
 8. --source-column/--columns เลือกคู่ต้นฉบับ–คำแปลเอง (คอลัมน์หมายเหตุอังกฤษไม่ถูกเดาเป็นต้นฉบับ) · error ชื่อคอลัมน์ = exit 2
    · --json-path /items/*/desc แปลงเฉพาะ path ที่เลือก
 9. --stats คู่ --dry-run / --check: มีรายงานสถิติ (เดิมเงียบ) และไม่เขียนไฟล์
10. เลขคอลัมน์เกินตาราง (#16): --placeholders/--check/-o ต้อง exit 2 + บอกจำนวนคอลัมน์ · ไม่เขียน output
11. --report (#9): JSON/HTML รวมผลตัวตรวจ · ไฟล์แปลไม่ถูกแตะ · exit 1 เมื่อมี error · --check ทำให้ไม่แตรป = error
    · รายงานเก่าในโฟลเดอร์ไม่ถูกตรวจซ้ำ · เวอร์ชันในรายงาน = PluginVersion · ค่าผิด (-o/นามสกุล) = exit 2
"""
import json
import re
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
        for name, raw in (("UTF-8 BOM", b"\xef\xbb\xbfabc\xff"), ("UTF-16 LE BOM", b"\xff\xfeA\x00\x01")):
            bad = work / "badbom.txt"
            bad.write_bytes(raw)
            badout = work / "badbom.out"
            r = run(binary, bad, "-o", badout, cwd=work)
            check(f"{name} + ข้อมูลเสีย: exit 2 + ไม่เขียน output", r.returncode == 2 and not badout.exists(),
                  f"exit={r.returncode}")
            bad.unlink()

        # 4. csv record ว่างท้ายไฟล์
        csv_in = work / "t.csv"
        csv_in.write_bytes("thai\r\n\"\"".encode("utf-8"))
        csv_out = work / "t.out.csv"
        run(binary, csv_in, "--columns", "thai", "-o", csv_out, cwd=work)
        check("csv: record ว่างท้ายไฟล์ไม่หาย", csv_out.read_bytes() == "thai\r\n\"\"".encode("utf-8"),
              repr(csv_out.read_bytes()))
        quoted = b'id,thai\n"001","Hello"\n'
        csv_in.write_bytes(quoted)
        run(binary, csv_in, "--columns", "thai", "-o", csv_out, cwd=work)
        check("csv: field ที่ไม่แตะคง quote เดิม", csv_out.read_bytes() == quoted, repr(csv_out.read_bytes()))
        r = run(binary, csv_in, "--columns", "thai", "--check", cwd=work)
        check("csv: --check ไม่ฟ้องไฟล์ที่ไม่มีไทย", r.returncode == 0, f"exit={r.returncode}")

        # 5. hairspace + สถิติ
        txt = work / "hs.txt"
        txt.write_text("นักเดินทางจากแดนไกลมาถึงหมู่บ้าน", encoding="utf-8")
        r = run(binary, txt, "--break-char", "hairspace", "--stats", "-o", work / "hs.out", cwd=work)
        hs_out = (work / "hs.out").read_text(encoding="utf-8")
        check("hairspace: แทรก U+200A ไม่ปน ZWSP", HAIR in hs_out and ZWSP not in hs_out)
        check("hairspace: สถิติรายงานตามตัวที่เลือก", "hairspace+" in r.stderr and "hairspace+0" not in r.stderr,
              r.stderr.strip().splitlines()[-1] if r.stderr else "")

        # 7. --placeholders (ไม่ต้องใช้ word list · ไม่เขียนไฟล์)
        ph_dir = work / "ph"
        ph_dir.mkdir()
        ph = ph_dir / "ph.csv"
        ph.write_text("id,en,thai\nitem_01,You found {0} gold,คุณพบทอง {gold} เหรียญ\n"
                      "item_02,Deals {0} to <b>{1}</b>,สร้าง {0} แก่ <b>{1}</b>\n", encoding="utf-8")
        r = run(binary, ph, "--placeholders", cwd=work)
        check("placeholders: พบจุดผิด exit 1 + รายงานแถว 2", r.returncode == 1 and "แถว 2" in r.stderr and "{gold}" in r.stderr,
              f"exit={r.returncode}")
        # รีวิว v0.12.0: #13 ต้นฉบับ {0} ล้วนต้องถูกเลือก · #14 printf สลับชนิด · #15 แท็กปิดข้ามชั้น
        for name, row, expect in (
            ("#13 ต้นฉบับ {0} ล้วน", "{0},สวัสดี {1}", "{1}"),
            ("#14 printf สลับชนิด", "Name %s count %d,จำนวน %d ชื่อ %s", "printf"),
            ("#15 แท็กปิดข้ามชั้น", "<b><i>Hello</i></b>,<b><i>สวัสดี</b></i>", "ข้ามชั้น"),
        ):
            ph.write_text("en,th\n" + row + "\n", encoding="utf-8")
            r = run(binary, ph, "--placeholders", cwd=work)
            check(f"placeholders {name}: exit 1 + รายงาน", r.returncode == 1 and "เทียบ 1 คู่" in r.stderr and expect in r.stderr,
                  f"exit={r.returncode} {r.stderr.strip().splitlines()[-1] if r.stderr else ''}")
        ph.write_text("id,en,thai\nitem_02,Deals {0} to <b>{1}</b>,สร้าง {0} แก่ <b>{1}</b>\n", encoding="utf-8")
        r = run(binary, ph, "--placeholders", cwd=work)
        check("placeholders: ถูกครบ exit 0 + ไม่สร้างไฟล์", r.returncode == 0 and sorted(p.name for p in ph_dir.iterdir()) == ["ph.csv"],
              f"exit={r.returncode}")

        # 8. เลือกคอลัมน์ต้นฉบับ/คำแปลเอง + --json-path
        ph.write_text("id,en,note,th\nq1,Get {count} gold,Translator note: a much longer English comment about gold,ได้ทอง {count}\n",
                      encoding="utf-8")
        r = run(binary, ph, "--placeholders", cwd=work)
        check("source-column: ไม่ระบุ = เดาคอลัมน์ note ผิด (ฟ้อง เกิน)", r.returncode == 1 and "เกิน {count}" in r.stderr, f"exit={r.returncode}")
        r = run(binary, ph, "--placeholders", "--source-column", "en", "--columns", "th", cwd=work)
        check("source-column: ระบุ en/th = ผ่าน exit 0", r.returncode == 0 and "เทียบ 1 คู่" in r.stderr,
              f"exit={r.returncode} {r.stderr.strip().splitlines()[-1] if r.stderr else ''}")
        r = run(binary, ph, "--placeholders", "--source-column", "bogus", cwd=work)
        check("source-column: ชื่อไม่พบ = exit 2 + บอกชื่อที่มี", r.returncode == 2 and "id, en, note, th" in r.stderr, f"exit={r.returncode}")
        jp = work / "jp.json"
        jp.write_text('{"items":[{"name":"ดาบเหล็กกล้า","desc":"ดาบคมกริบจากแดนไกล"}],"menu.start":"เริ่มเกมใหม่"}', encoding="utf-8")
        r = run(binary, jp, "--json-path", "/items/*/desc", "-o", work / "jp.out.json", cwd=work)
        jdata = json.loads((work / "jp.out.json").read_text(encoding="utf-8"))
        check("json-path: แปลงเฉพาะ /items/*/desc",
              r.returncode == 0 and ZWSP in jdata["items"][0]["desc"] and ZWSP not in jdata["items"][0]["name"]
              and ZWSP not in jdata["menu.start"], f"exit={r.returncode}")

        # 9. --stats ต้องรายงานแม้ใช้คู่ --dry-run/--check (เดิมเก็บสถิติหลังจุดเขียนไฟล์ จึงเงียบ)
        st_dir = work / "st"
        st_dir.mkdir()
        (st_dir / "st.txt").write_text("วิทเชอร์ไปหาวิทเชอร์ เเท้จริง\nวิทเชอร์มา\n", encoding="utf-8")
        for flag in ("--dry-run", "--check"):
            r = run(binary, st_dir, "--stats", flag, cwd=work)
            check(f"stats {flag}: มีรายงานคำนอกพจนานุกรม + เตือนรูปพิมพ์ผิด + ไม่เขียนไฟล์",
                  "คำนอกพจนานุกรมที่พบบ่อย" in r.stderr and "พบรูปพิมพ์ผิด" in r.stderr
                  and sorted(p.name for p in st_dir.iterdir()) == ["st.txt"], f"exit={r.returncode}")

        # 10. #16 เลขคอลัมน์เกินตาราง — เดิมโหมดตรวจเทียบ 0 คู่แล้ว exit 0 เหมือนผ่าน
        c16_dir = work / "c16"
        c16_dir.mkdir()
        c16 = c16_dir / "c16.csv"
        c16.write_text("en,th\nHello {name},สวัสดี {wrong}\n", encoding="utf-8")
        for args in (("--placeholders", "--source-column", "999", "--columns", "2"),
                     ("--placeholders", "--source-column", "1", "--columns", "999"),
                     ("--columns", "999", "--check"),
                     ("--columns", "999", "-o", str(c16_dir / "out.csv"))):
            r = run(binary, c16, *args, cwd=work)
            check(f"#16 {' '.join(args[:3])}…: exit 2 + บอกจำนวนคอลัมน์ + ไม่เขียนไฟล์",
                  r.returncode == 2 and "มีแค่ 2 คอลัมน์" in r.stderr and sorted(p.name for p in c16_dir.iterdir()) == ["c16.csv"],
                  f"exit={r.returncode}")

        # 11. --report — รายงาน QA ไม่เขียนไฟล์แปล
        rp_dir = work / "report"
        rp_dir.mkdir()
        rp_csv = rp_dir / "loc.csv"
        rp_text = ("id,en,th\n1,Hello <b>{name}</b>,สวัสดี <b>{nmae}</b>\n"
                   "2,The army invades the city,กองทัพบุกลุกเมือง เเละเผาทำลายทุกสิ่ง\n")
        rp_csv.write_text(rp_text, encoding="utf-8")
        r = run(binary, rp_dir, "--report", rp_dir / "qa.json", cwd=work)
        rep = None
        try:
            rep = json.loads((rp_dir / "qa.json").read_text(encoding="utf-8"))
        except (OSError, ValueError) as e:
            check("report: เขียน qa.json ที่ json.load อ่านได้", False, str(e))
        if rep is not None:
            by = rep["summary"]["byCheck"]
            check("report json: exit 1 (มี placeholder ผิด) + รวม placeholders/unwrapped/typo/ral-swap",
                  r.returncode == 1 and by.get("placeholders") == 1 and by.get("unwrapped") == 1
                  and by.get("typo") == 1 and by.get("ral-swap") == 1, f"exit={r.returncode} {by}")
            ph = [f for f in rep["findings"] if f["check"] == "placeholders"]
            check("report json: placeholder บอกแถว + ต้นฉบับ/คำแปลครบ",
                  ph and ph[0]["row"] == 2 and ph[0]["translation"] == "สวัสดี <b>{nmae}</b>", str(ph))
            plugin_src = Path(__file__).resolve().parent.parent / "src" / "ThaiWrap.BepInEx" / "ThaiWrapPlugin.cs"
            if plugin_src.exists():
                m = re.search(r'PluginVersion = "([^"]+)"', plugin_src.read_text(encoding="utf-8"))
                check("report json: version = PluginVersion (bump <Version> ใน ThaiWrap.Cli.csproj ด้วย)",
                      m is not None and rep["version"] == m.group(1), f"report={rep['version']} plugin={m and m.group(1)}")
        check("report: ไฟล์แปลไม่ถูกแตะ", rp_csv.read_text(encoding="utf-8") == rp_text)
        r = run(binary, rp_dir, "--report", rp_dir / "qa.html", cwd=work)
        html = (rp_dir / "qa.html").read_text(encoding="utf-8") if (rp_dir / "qa.html").exists() else ""
        check("report html: escape แท็ก + ไม่ตรวจ qa.json ของรอบก่อนเป็นไฟล์แปล",
              r.returncode == 1 and "&lt;b&gt;{nmae}&lt;/b&gt;" in html and "<b>{nmae}" not in html
              and "qa.json" not in html and "รายงาน QA: 1 ไฟล์" in r.stderr, f"exit={r.returncode}")
        check("report: โฟลเดอร์มีแค่ไฟล์แปล + รายงาน 2 ไฟล์",
              sorted(p.name for p in rp_dir.iterdir()) == ["loc.csv", "qa.html", "qa.json"])
        ok_csv = rp_dir / "ok" / "ok.csv"
        ok_csv.parent.mkdir()
        ok_csv.write_text("en,th\nGood morning {name},สวัสดีตอนเช้า {name}\n", encoding="utf-8")
        r = run(binary, ok_csv, "--report", "-", cwd=work)
        r2 = run(binary, ok_csv, "--report", "-", "--check", cwd=work)
        try:
            ok_rep, ok_rep2 = json.loads(r.stdout), json.loads(r2.stdout)
        except ValueError:
            ok_rep = ok_rep2 = None
        check("report -: JSON ออก stdout · ไม่แตรปเป็น info (exit 0) · ใส่ --check แล้วเป็น error (exit 1)",
              ok_rep is not None and r.returncode == 0 and ok_rep["summary"]["errors"] == 0
              and r2.returncode == 1 and ok_rep2["summary"]["errors"] == 1, f"exit={r.returncode}/{r2.returncode}")
        for args in (("--report", "qa.txt"), ("--report", "x.json", "-o", "out.csv"), ("--report", "x.json", "--inplace")):
            r = run(binary, ok_csv, *args, cwd=rp_dir / "ok")
            check(f"report {' '.join(args[2:]) or args[1]}: exit 2 + ไม่เขียนอะไร",
                  r.returncode == 2 and sorted(p.name for p in (rp_dir / "ok").iterdir()) == ["ok.csv"], f"exit={r.returncode}")

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

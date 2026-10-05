#!/usr/bin/env python3
"""font_zwsp_patch.py — เติม glyph ZWSP (U+200B) ที่ว่างเปล่าให้ฟอนต์ไทย

ฟอนต์ไทยยอดนิยมหลายตัว (Sarabun, Kanit, Prompt, Mitr, Trirong) ไม่มี glyph ที่ U+200B
เอนจินที่วาด glyph ตรงจาก cmap โดยไม่มี font fallback จะเห็น ZWSP เป็นกล่อง tofu
เครื่องมือนี้เติม glyph เปล่า advance=0 ให้ ใช้คู่กับ thaiwrap ได้ทันที

ใช้งาน:
    python3 font_zwsp_patch.py ในฟอนต์.ttf [-o ผลลัพธ์.ttf]
    python3 font_zwsp_patch.py โฟลเดอร์/ -o โฟลเดอร์ผลลัพธ์/

รองรับ TrueType outlines (รวม variable TTF) — CFF/OTF ยังไม่รองรับ
ต้องมี: pip install fonttools
"""
import argparse
import os
import sys

try:
    from fontTools.ttLib import TTFont, getTableModule
except ImportError:
    sys.exit("ต้องติดตั้ง fonttools ก่อน: pip install fonttools")

ZWSP = 0x200B
GLYPH_NAME = "zwsp-empty"


def patch_font(src: str, dst: str) -> str:
    font = TTFont(src)
    cmap_tables = font["cmap"].tables
    best = font.getBestCmap()

    if best is None:
        return "ข้าม: ไม่มี unicode cmap"
    if ZWSP in best:
        return "ข้าม: มี glyph ZWSP อยู่แล้ว"

    if "glyf" not in font:
        return "ข้าม: ไม่ใช่ TrueType outlines (CFF/OTF ยังไม่รองรับ)"

    # glyph เปล่าใหม่
    glyf_table = font["glyf"]
    glyph = getTableModule("glyf").Glyph()
    glyf_table.glyphs[GLYPH_NAME] = glyph
    font.glyphOrder.append(GLYPH_NAME)
    font["hmtx"].metrics[GLYPH_NAME] = (0, 0)

    # เพิ่มเข้าทุก unicode subtable ที่ครอบคลุมระยะ BMP (format 4)
    patched = 0
    for sub in cmap_tables:
        if sub.isUnicode() and sub.format in (4, 6, 12):
            sub.cmap[ZWSP] = GLYPH_NAME
            patched += 1
    if patched == 0:
        return "ข้าม: ไม่พบ subtable ที่แก้ไขได้"

    # loca จะถูกคำนวณใหม่ตอน save (recalcBBoxes=True ปกติ)
    font.save(dst)
    return f"เติมแล้ว (แก้ {patched} subtable) -> {dst}"


def main():
    ap = argparse.ArgumentParser(description="เติม glyph ZWSP เปล่าให้ฟอนต์")
    ap.add_argument("input", help="ไฟล์ .ttf หรือโฟลเดอร์")
    ap.add_argument("-o", "--out", help="ไฟล์/โฟลเดอร์ผลลัพธ์ (default: <name>-zwsp.ttf)")
    args = ap.parse_args()

    if os.path.isdir(args.input):
        out_dir = args.out or args.input
        os.makedirs(out_dir, exist_ok=True)
        for name in sorted(os.listdir(args.input)):
            if not name.lower().endswith(".ttf"):
                continue
            src = os.path.join(args.input, name)
            dst = os.path.join(out_dir, name[:-4] + "-zwsp.ttf")
            print(f"{name}: {patch_font(src, dst)}")
    else:
        dst = args.out or args.input[:-4] + "-zwsp.ttf"
        print(f"{os.path.basename(args.input)}: {patch_font(args.input, dst)}")


if __name__ == "__main__":
    main()

using System;
using System.IO;
using System.Text;

namespace ThaiWrap.Formats
{
    /// <summary>
    /// อ่านไฟล์ข้อความแบบปลอดภัย: ตรวจ BOM (UTF-8 / UTF-16 LE / UTF-16 BE) ก่อน
    /// ทั้งแบบมี BOM และไม่มี BOM (ไม่มี = UTF-8) ถอดรหัสแบบเข้มงวด — ไฟล์ที่มี byte ถอดรหัสไม่ได้
    /// (เช่น TIS-620/CP874) จะโยน IOException ทันที แทนที่จะ "ยอมแทนอักขระ" แล้วเขียนข้อความเสีย
    /// ออกไปเงียบๆ
    /// </summary>
    public static class TextFileEncoding
    {
        public static (string Text, Encoding Enc) Decode(byte[] bytes)
        {
            // มี BOM ก็ต้องถอดแบบเข้มงวดเหมือนกัน — เดิมใช้ Encoding.UTF8/Unicode ที่แทน byte เสียด้วย
            // U+FFFD เงียบๆ แล้วเขียนทับออกไป (byte ค้างท้ายไฟล์ UTF-16 / byte FF ในไฟล์ UTF-8 BOM)
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return (Strict(new UTF8Encoding(false, true), bytes, 3, "UTF-8"), new UTF8Encoding(true));
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return (Strict(new UnicodeEncoding(false, false, true), bytes, 2, "UTF-16 LE"), Encoding.Unicode);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return (Strict(new UnicodeEncoding(true, false, true), bytes, 2, "UTF-16 BE"), Encoding.BigEndianUnicode);

            try
            {
                return (new UTF8Encoding(false, true).GetString(bytes), new UTF8Encoding(false));
            }
            catch (DecoderFallbackException)
            {
                throw new IOException(
                    "ไฟล์ไม่ใช่ UTF-8 ที่ถูกต้อง (ไม่มี BOM และมี byte ที่ถอดรหัสไม่ได้ — อาจเป็น TIS-620/CP874 หรือ ANSI) " +
                    "กรุณาแปลงเป็น UTF-8 ก่อน เช่น เปิดใน editor แล้ว Save As แบบ UTF-8 — " +
                    "การอ่านแบบยอมแทนอักขระจะทำให้ข้อความเสียหายโดยไม่สามารถกู้คืน");
            }
        }

        private static string Strict(Encoding enc, byte[] bytes, int offset, string name)
        {
            if ((enc is UnicodeEncoding) && (bytes.Length - offset) % 2 != 0)
                throw Corrupt(name, "จำนวน byte เป็นเลขคี่ — มี byte ค้างท้ายไฟล์");
            try
            {
                return enc.GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException ex)
            {
                string at = ex.Index >= 0 ? " ราวตำแหน่ง byte " + (offset + ex.Index) : "";
                throw Corrupt(name, "มี byte ที่ถอดรหัสไม่ได้" + at);
            }
        }

        private static IOException Corrupt(string name, string detail) =>
            new IOException(
                "ไฟล์ขึ้นต้นด้วย BOM ของ " + name + " แต่ข้อมูลเสีย (" + detail + ") — " +
                "ไม่อ่านต่อเพราะการแทนอักขระที่เสียจะทำให้ข้อความเสียหายเงียบๆ กรุณาตรวจ/บันทึกไฟล์ใหม่ให้ถูก encoding");
    }
}

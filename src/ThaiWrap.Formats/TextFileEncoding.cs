using System;
using System.IO;
using System.Text;

namespace ThaiWrap.Formats
{
    /// <summary>
    /// อ่านไฟล์ข้อความแบบปลอดภัย: ตรวจ BOM (UTF-8 / UTF-16 LE / UTF-16 BE) ก่อน
    /// ถ้าไม่มี BOM จะถอดรหัสเป็น UTF-8 แบบเข้มงวด — ไฟล์ที่มี byte ที่ไม่ใช่ UTF-8 ที่ถูกต้อง
    /// (เช่น TIS-620/CP874) จะโยน IOException ทันที แทนที่จะ "ยอมแทนอักขระ" แล้วเขียนข้อความเสีย
    /// ออกไปเงียบๆ
    /// </summary>
    public static class TextFileEncoding
    {
        public static (string Text, Encoding Enc) Decode(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true));
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode);

            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            try
            {
                return (strict.GetString(bytes), new UTF8Encoding(false));
            }
            catch (DecoderFallbackException)
            {
                throw new IOException(
                    "ไฟล์ไม่ใช่ UTF-8 ที่ถูกต้อง (ไม่มี BOM และมี byte ที่ถอดรหัสไม่ได้ — อาจเป็น TIS-620/CP874 หรือ ANSI) " +
                    "กรุณาแปลงเป็น UTF-8 ก่อน เช่น เปิดใน editor แล้ว Save As แบบ UTF-8 — " +
                    "การอ่านแบบยอมแทนอักขระจะทำให้ข้อความเสียหายโดยไม่สามารถกู้คืน");
            }
        }
    }
}

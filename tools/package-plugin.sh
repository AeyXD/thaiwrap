#!/usr/bin/env bash
# แพ็กเกจ plugin พร้อมติดตั้ง: dist/ThaiWrap/ (DLL + word list + คู่มือสั้น) และ zip
set -euo pipefail
cd "$(dirname "$0")/.."

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

./tools/fetch-bepinex.sh
dotnet build src/ThaiWrap.BepInEx/ThaiWrap.BepInEx.csproj -c Release

rm -rf dist
mkdir -p dist/ThaiWrap
cp src/ThaiWrap.BepInEx/bin/Release/ThaiWrap.BepInEx.dll dist/ThaiWrap/
cp data/words_th.txt dist/ThaiWrap/

cat > dist/ThaiWrap/INSTALL-TH.txt <<'EOF'
ThaiWrap — ติดตั้งลงเกม Unity (BepInEx 5)

1. ติดตั้ง BepInEx 5 ลงเกมก่อน (https://github.com/BepInEx/BepInEx/releases — เลือก x64/x86 ตามเกม)
   แตกไฟล์ทับโฟลเดอร์เกม แล้วเปิดเกมครั้งหนึ่งให้ BepInEx สร้างโฟลเดอร์
2. คัดลอกโฟลเดอร์ ThaiWrap ทั้งโฟลเดอร์ไปไว้ที่ <เกม>/BepInEx/plugins/
3. เปิดเกม — ข้อความไทยจะถูกตัดคำและแทรก ZWSP อัตโนมัติตอนแสดงผล
   (รองรับ TextMeshPro และ UnityEngine.UI.Text)

หมายเหตุ:
- ปรับแต่งได้ที่ BepInEx/config/BepInEx.cfg ของ thaiwrap (เปิด/ปิด, ความยาวคำ)
- ถ้าฟอนต์เกมไม่มี glyph ZWSP และแสดงเป็นกล่อง ให้ patch ฟอนต์ด้วย
  tools/font_zwsp_patch.py จาก repo ก่อน
- ยังไม่รองรับเกม IL2CPP (BepInEx 6) ในเวอร์ชันนี้
EOF

cd dist && zip -qr ThaiWrap-BepInEx.zip ThaiWrap/
echo "สำเร็จ: dist/ThaiWrap/ และ dist/ThaiWrap-BepInEx.zip"
ls -la ThaiWrap/

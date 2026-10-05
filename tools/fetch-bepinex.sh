#!/usr/bin/env bash
# ดาวน์โหลด BepInEx 5 reference assemblies สำหรับ compile plugin
# ผลลัพธ์: lib/BepInEx.dll, lib/0Harmony.dll (ไม่ถูก commit — อยู่ใน .gitignore)
set -euo pipefail
cd "$(dirname "$0")/.."

BEPINEX_VERSION="5.4.22"
mkdir -p lib /tmp/bepinex_dl

if [ -f lib/BepInEx.dll ] && [ -f lib/0Harmony.dll ]; then
  echo "lib/ มี DLL อยู่แล้ว (BepInEx $BEPINEX_VERSION) — ข้าม"
  exit 0
fi

curl -sL -o /tmp/bepinex_dl/bepinex.zip \
  "https://github.com/BepInEx/BepInEx/releases/download/v${BEPINEX_VERSION}/BepInEx_x64_${BEPINEX_VERSION}.0.zip"

unzip -q -o /tmp/bepinex_dl/bepinex.zip "BepInEx/core/*" -d /tmp/bepinex_dl/
cp /tmp/bepinex_dl/BepInEx/core/BepInEx.dll lib/
cp /tmp/bepinex_dl/BepInEx/core/0Harmony.dll lib/
echo "โหลดแล้ว: $(ls -la lib/ | tail -2)"

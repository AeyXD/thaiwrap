#!/usr/bin/env bash
# rebuild binary CLI ใน publish/ ทั้ง 3 RID (ทำตอนออกเวอร์ชันเท่านั้น — publish/ ถูก commit ไว้ใน repo)
# ใช้: ./tools/publish.sh          → publish/<rid>/
#      ./tools/publish.sh --zip    → + dist/thaiwrap-cli-<rid>.zip สำหรับแนบ GitHub Release
# build ไม่ deterministic: source เดิมก็ได้ binary/pdb ต่างไบต์ทุกครั้ง — ถ้าไม่ได้ออกเวอร์ชัน
# ให้ `git checkout -- publish/` คืนของเดิม อย่า commit
set -euo pipefail
cd "$(dirname "$0")/.."

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

zip=false
case "${1:-}" in
  --zip) zip=true ;;
  "") ;;
  *) echo "ใช้: $0 [--zip]" >&2; exit 2 ;;
esac

RIDS=(linux-x64 osx-arm64 win-x64)

for rid in "${RIDS[@]}"; do
  # ลบของเดิมก่อน — กันไฟล์ค้างจาก build เก่าหลุดเข้า commit/zip
  rm -rf "publish/$rid"
  dotnet publish src/ThaiWrap.Cli -c Release -r "$rid" --self-contained -o "publish/$rid" -v q -nologo >/dev/null
  exe="publish/$rid/ThaiWrap.Cli"
  [ "$rid" = win-x64 ] && exe="$exe.exe"
  [ -f "$exe" ] && [ -f "publish/$rid/data/words_th.txt" ] || { echo "publish $rid ไม่ครบ: ไม่มี $exe หรือ data/words_th.txt" >&2; exit 1; }
  echo "publish/$rid: $(du -h "$exe" | cut -f1) $(basename "$exe")"
done

# รัน binary ของเครื่องนี้จริงหนึ่งตัว (ตัวอื่นรันใน CI package-test)
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) native=publish/linux-x64/ThaiWrap.Cli ;;
  Darwin-arm64) native=publish/osx-arm64/ThaiWrap.Cli ;;
  MINGW*|MSYS*|CYGWIN*) native=publish/win-x64/ThaiWrap.Cli.exe ;;
  *) native="" ;;
esac
if [ -n "$native" ]; then
  out="$(printf 'ทดสอบการตัดคำภาษาไทย\n' | "$native" --words "$(dirname "$native")/data/words_th.txt" 2>/dev/null)"
  case "$out" in
    *$'​'*) echo "รัน $native ได้: แทรก ZWSP แล้ว" ;;
    *) echo "รัน $native แล้วไม่มี ZWSP ในผลลัพธ์: $out" >&2; exit 1 ;;
  esac
fi

if $zip; then
  mkdir -p dist
  for rid in "${RIDS[@]}"; do
    rm -f "dist/thaiwrap-cli-$rid.zip"
    (cd "publish/$rid" && zip -qr "../../dist/thaiwrap-cli-$rid.zip" .)
    echo "dist/thaiwrap-cli-$rid.zip"
  done
fi

echo "เสร็จ — ตรวจ git status ทีละไฟล์ก่อน commit (ห้าม git add .)"

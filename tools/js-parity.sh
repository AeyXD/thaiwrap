#!/usr/bin/env bash
# เทียบ web/thaiwrap.js กับ CLI C# จริง — ทุกชุดตัวเลือกต้องได้ผลตรงกันทุกบรรทัด
# (กัน JS port ตามหลัง C# เงียบๆ อีก) · ต้องมี dotnet และ node
set -euo pipefail
cd "$(dirname "$0")/.."

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

cat samples/game_sentences.txt tests/samples/parity.txt > "$work/input.txt"

# plain = word list อย่างเดียว · files = มี nobreak.txt + words_extra.txt วางข้างกัน
mkdir -p "$work/plain/data" "$work/files/data"
cp data/words_th.txt "$work/plain/data/"
cp data/words_th.txt "$work/files/data/"
printf '# วลีทดสอบ\nเดอะ วิทเชอร์\nลา วาเล็ตต์\nอาร์ยัน ลา วาเล็ตต์\nทริสส์ เมอริโกลด์\n' > "$work/files/data/nobreak.txt"
printf '# คำเฉพาะเกม\nทริสส์\nเยนเนเฟอร์\nโนวิกราด\n-ใช้การ\n' > "$work/files/data/words_extra.txt"

dotnet build src/ThaiWrap.Cli -c Release -v q -nologo >/dev/null
dll="$PWD/src/ThaiWrap.Cli/bin/Release/net10.0/ThaiWrap.Cli.dll"

fail=0
n=0
check() {
  local dir=$1; shift
  n=$((n + 1))
  local out="$work/out$n.txt"
  (cd "$work/$dir" && dotnet "$dll" "$work/input.txt" --words data/words_th.txt -o "$out" "$@" >/dev/null 2>&1)
  node tools/js-parity.js "$work/$dir/data" "$work/input.txt" "$out" "$@" || fail=1
}

check plain
check plain -m sep
check plain --break-char hairspace
check plain --break-char space
check plain --break-char space --chunk 8 --min-len 0
check plain --normalize
check plain --yamok-nbsp
check plain --normalize --yamok-nbsp --break-char hairspace
check files
check files --normalize --yamok-nbsp
check files --break-char space -m sep

if [ "$fail" -ne 0 ]; then
  echo "js-parity: web/thaiwrap.js ไม่ตรงกับ C# — แก้ JS ให้ตามหรือระบุเหตุผลที่ตั้งใจต่าง" >&2
  exit 1
fi
echo "js-parity: ครบ $n ชุดตัวเลือก ตรงกับ C# ทุกบรรทัด"

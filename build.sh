#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
if ! command -v dotnet >/dev/null; then
  echo 'ERROR: .NET SDK 10 is required to build. See https://dotnet.microsoft.com/download/dotnet/10.0' >&2
  exit 1
fi
if ! dotnet --list-sdks | grep -q '^10\.'; then
  echo 'ERROR: .NET SDK 10 was not detected.' >&2
  exit 1
fi
PROJECT=src/Novalist.AiBridge/Novalist.AiBridge.csproj
OUT=dist/com.whscott.novalist-ai-bridge
mkdir -p "$OUT"
dotnet build "$PROJECT" -c Release
cp src/Novalist.AiBridge/bin/Release/net10.0/Novalist.AiBridge.dll "$OUT/"
cp src/Novalist.AiBridge/extension.json "$OUT/"
mkdir -p "$OUT/web"
cp src/Novalist.AiBridge/web/settings.html "$OUT/web/"
cp src/Novalist.AiBridge/web/review.html "$OUT/web/"
# No duplicate Novalist.Sdk.dll: the host provides SDK 15.
rm -f "$OUT/Novalist.Sdk.dll"
printf '\nBuilt extension folder: %s\n' "$(pwd)/$OUT"
echo 'Novalist -> Go -> Extensions -> Installed -> Install from Folder -> select that folder.'

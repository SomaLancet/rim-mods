#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
RIMWORLD="${RIMWORLD:-$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Contents/Resources/Data/Managed}"
HARMONY="${HARMONY:-$HOME/Library/Application Support/Steam/steamapps/workshop/content/294100/2009463077/Current/Assemblies/0Harmony.dll}"
NETSTANDARD="${NETSTANDARD:-/opt/homebrew/lib/mono/4.7.2-api/Facades/netstandard.dll}"
OUT="$ROOT/1.6/Assemblies/RWUPortraitsOfTheRimFix.dll"

mkdir -p "$(dirname "$OUT")"

csc -target:library -langversion:7.3 -nologo \
  -out:"$OUT" \
  -r:"$RIMWORLD/Assembly-CSharp.dll" \
  -r:"$RIMWORLD/UnityEngine.CoreModule.dll" \
  -r:"$RIMWORLD/UnityEngine.IMGUIModule.dll" \
  -r:"$RIMWORLD/UnityEngine.TextRenderingModule.dll" \
  -r:"$HARMONY" \
  -r:"$NETSTANDARD" \
  "$ROOT/Source/RWUPortraitsOfTheRimFix/RWUPortraitsOfTheRimFix.cs"

echo "$OUT"

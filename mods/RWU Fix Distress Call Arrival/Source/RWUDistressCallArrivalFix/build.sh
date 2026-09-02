#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
RIMWORLD_MANAGED="${RIMWORLD_MANAGED:-/Users/dieruki/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Contents/Resources/Data/Managed}"
HARMONY_ASSEMBLY="${HARMONY_ASSEMBLY:-/Users/dieruki/Library/Application Support/Steam/steamapps/workshop/content/294100/2009463077/Current/Assemblies/0Harmony.dll}"
OUT="$ROOT/1.6/Assemblies/RWUDistressCallArrivalFix.dll"

mkdir -p "$(dirname "$OUT")"

csc -target:library -langversion:7.3 -nologo \
  -out:"$OUT" \
  -r:"$RIMWORLD_MANAGED/Assembly-CSharp.dll" \
  -r:"$RIMWORLD_MANAGED/UnityEngine.CoreModule.dll" \
  -r:"$RIMWORLD_MANAGED/netstandard.dll" \
  -r:"$HARMONY_ASSEMBLY" \
  "$ROOT/Source/RWUDistressCallArrivalFix/RWUDistressCallArrivalFix.cs"

echo "$OUT"

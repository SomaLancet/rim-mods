#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
RIMWORLD="${RIMWORLD:-$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Contents/Resources/Data/Managed}"
RJW="${RJW:-$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods/rjw/1.6/Assemblies/RJW.dll}"
OUT="$ROOT/1.6/Assemblies/RWUEqualRJWSelection.dll"

mkdir -p "$(dirname "$OUT")"

csc -target:library -langversion:7.3 -nologo \
  -out:"$OUT" \
  -r:"$RIMWORLD/Assembly-CSharp.dll" \
  -r:"$RIMWORLD/UnityEngine.CoreModule.dll" \
  -r:"$RIMWORLD/netstandard.dll" \
  -r:"$RJW" \
  "$ROOT/Source/RWUEqualRJWSelection/RWUEqualRJWSelection.cs"

echo "$OUT"

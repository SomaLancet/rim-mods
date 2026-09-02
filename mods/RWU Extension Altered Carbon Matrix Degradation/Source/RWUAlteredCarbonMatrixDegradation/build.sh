#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
RIMWORLD="${RIMWORLD:-$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Contents/Resources/Data/Managed}"
ALTERED_CARBON="${ALTERED_CARBON:-$HOME/Library/Application Support/Steam/steamapps/workshop/content/294100/2196278117/1.6/Assemblies/AlteredCarbon.dll}"
OUT="$ROOT/1.6/Assemblies/RWUAlteredCarbonMatrixDegradation.dll"

mkdir -p "$(dirname "$OUT")"

csc -target:library -langversion:7.3 -nologo \
  -out:"$OUT" \
  -r:"$RIMWORLD/Assembly-CSharp.dll" \
  -r:"$RIMWORLD/UnityEngine.CoreModule.dll" \
  -r:"$RIMWORLD/netstandard.dll" \
  -r:"$ALTERED_CARBON" \
  "$ROOT/Source/RWUAlteredCarbonMatrixDegradation/RWUAlteredCarbonMatrixDegradation.cs"

echo "$OUT"

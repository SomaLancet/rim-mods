#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
RIMWORLD="${RIMWORLD:-$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Contents/Resources/Data/Managed}"
INJECT_GENES="${INJECT_GENES:-$HOME/Library/Application Support/Steam/steamapps/workshop/content/294100/2918091446/1.6/Assemblies/UseGene.dll}"
OUT="$ROOT/1.6/Assemblies/RWUInjectGenesTargeting.dll"

mkdir -p "$(dirname "$OUT")"

csc -target:library -langversion:7.3 -nologo \
  -out:"$OUT" \
  -r:"$RIMWORLD/Assembly-CSharp.dll" \
  -r:"$RIMWORLD/UnityEngine.CoreModule.dll" \
  -r:"$RIMWORLD/netstandard.dll" \
  -r:"$INJECT_GENES" \
  "$ROOT/Source/RWUInjectGenesTargeting/RWUInjectGenesTargeting.cs"

echo "$OUT"

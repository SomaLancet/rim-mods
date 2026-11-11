#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
game="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld"
version="${1:?Specify 1.6 or 1.7}"
case "$version" in
  1.6)
    managed="${RIMWORLD_MANAGED:-$game/RimWorldMac.app/Contents/Resources/Data/Managed}"
    refs=(-r:"$managed/UnityEngine.CoreModule.dll" -r:"$managed/netstandard.dll")
    ;;
  1.7)
    managed="${RIMWORLD_MANAGED:-$game/RimWorld.app/Contents/MacOS}"
    refs=(-noconfig -nostdlib+)
    for name in System.Private.CoreLib System.Runtime System.Collections System.Linq UnityStubs; do
      refs+=(-r:"$managed/$name.dll")
    done
    ;;
  *) echo "Unsupported RimWorld version: $version" >&2; exit 1 ;;
esac

test -f "$managed/Assembly-CSharp.dll" || {
  echo "Assembly-CSharp.dll missing in $managed. Set RIMWORLD_MANAGED to the game's managed assemblies." >&2
  exit 1
}
output="$root/$version/Assemblies/RWUGeneWorkstation.dll"
mkdir -p "$(dirname "$output")"
staging="$(mktemp -d "$root/$version/.build.XXXXXX")"
trap 'rm -rf "$staging"' EXIT
staged_dll="$staging/${output##*/}"
csc -nologo -langversion:7.3 -target:library -out:"$staged_dll" \
  -r:"$managed/Assembly-CSharp.dll" "${refs[@]}" "$root/Source/GeneWorkstation.cs"
mv -f "$staged_dll" "$output"
echo "$output"

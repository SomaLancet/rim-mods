#!/usr/bin/env python3
"""Collect read-only evidence for an RWU mod audit."""

from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


DEFAULT_RIMWORLD = Path(
    "/Users/dieruki/Library/Application Support/Steam/steamapps/common/"
    "RimWorld/RimWorldMac.app"
)
DEFAULT_CONFIG = Path(
    "/Users/dieruki/Library/Application Support/RimWorld/Config/ModsConfig.xml"
)
IGNORED_TARGETS = {
    "brrainz.harmony",
    "ludeon.rimworld",
    "ludeon.rimworld.royalty",
    "ludeon.rimworld.ideology",
    "ludeon.rimworld.biotech",
    "ludeon.rimworld.anomaly",
    "ludeon.rimworld.odyssey",
}


def text_at(root: ET.Element, path: str) -> str:
    node = root.find(path)
    return (node.text or "").strip() if node is not None else ""


def list_at(root: ET.Element, path: str) -> list[str]:
    node = root.find(path)
    if node is None:
        return []
    values = []
    for child in node.findall("li"):
        package = child.find("packageId")
        value = (package.text if package is not None else child.text) or ""
        value = value.strip()
        if value:
            values.append(value)
    return values


def parse_about(path: Path) -> dict[str, object] | None:
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError):
        return None
    return {
        "name": text_at(root, "name"),
        "package_id": text_at(root, "packageId"),
        "supported_versions": list_at(root, "supportedVersions"),
        "dependencies": list_at(root, "modDependencies"),
        "load_after": list_at(root, "loadAfter"),
    }


def active_packages(config: Path) -> set[str]:
    try:
        root = ET.parse(config).getroot()
    except (ET.ParseError, OSError):
        return set()
    return {
        (node.text or "").strip().lower()
        for node in root.findall("./activeMods/li")
        if (node.text or "").strip()
    }


def package_index(rimworld: Path) -> dict[str, list[str]]:
    roots = [rimworld / "Mods"]
    workshop = rimworld.parents[2] / "workshop" / "content" / "294100"
    if workshop.is_dir():
        roots.append(workshop)

    index: dict[str, list[str]] = {}
    for root in roots:
        if not root.is_dir():
            continue
        for child in root.iterdir():
            about_path = child / "About" / "About.xml"
            if not about_path.is_file():
                continue
            meta = parse_about(about_path)
            if not meta or not meta["package_id"]:
                continue
            package_id = str(meta["package_id"]).lower()
            resolved = str(child.resolve()) if child.is_symlink() else str(child)
            index.setdefault(package_id, []).append(resolved)
    return index


def patch_symbols(mod_dir: Path) -> list[str]:
    symbols: set[str] = set()
    patterns = [
        re.compile(r'AccessTools\.TypeByName\("([^"]+)"\)'),
        re.compile(r'AccessTools\.(?:Method|Field)\([^,]+,\s*"([^"]+)"'),
        re.compile(r'\[HarmonyPatch\(typeof\(([^)]+)\),\s*nameof\(([^)]+)\)\)\]'),
    ]
    for source in mod_dir.glob("Source/**/*.cs"):
        try:
            content = source.read_text(encoding="utf-8-sig")
        except OSError:
            continue
        for pattern in patterns:
            for match in pattern.finditer(content):
                symbols.add("::".join(match.groups()))
    return sorted(symbols)


def collect(args: argparse.Namespace) -> dict[str, object]:
    repo = args.repo.resolve()
    mods_dir = repo / "mods"
    active = active_packages(args.config)
    installed = package_index(args.rimworld)
    exclusions = {name.casefold() for name in args.exclude}
    game_mods = args.rimworld / "Mods"
    records = []

    for mod_dir in sorted(mods_dir.glob("RWU *"), key=lambda path: path.name.casefold()):
        if not mod_dir.is_dir() or mod_dir.name.casefold() in exclusions:
            continue
        meta = parse_about(mod_dir / "About" / "About.xml")
        if not meta:
            continue
        declared = list(dict.fromkeys(meta["dependencies"] + meta["load_after"]))
        targets = [item for item in declared if item.lower() not in IGNORED_TARGETS]
        link = game_mods / mod_dir.name
        records.append(
            {
                **meta,
                "folder": mod_dir.name,
                "kind": "extension" if " extension " in f" {mod_dir.name.lower()} " else "fix",
                "active": str(meta["package_id"]).lower() in active,
                "targets": [
                    {
                        "package_id": target,
                        "active": target.lower() in active,
                        "installed_paths": installed.get(target.lower(), []),
                    }
                    for target in targets
                ],
                "patch_symbols": patch_symbols(mod_dir),
                "game_link": {
                    "path": str(link),
                    "exists": link.exists(),
                    "is_symlink": link.is_symlink(),
                    "target": str(link.resolve()) if link.exists() else "",
                    "correct": link.is_symlink() and link.resolve() == mod_dir.resolve(),
                },
            }
        )

    return {
        "repo": str(repo),
        "rimworld": str(args.rimworld),
        "config": str(args.config),
        "mods": records,
    }


def render_markdown(data: dict[str, object]) -> str:
    lines = [
        "| RWU mod | Type | Active | Targets | Symlink |",
        "|---|---|---:|---|---:|",
    ]
    for mod in data["mods"]:
        targets = []
        for target in mod["targets"]:
            state = "active" if target["active"] else "inactive"
            found = "found" if target["installed_paths"] else "missing"
            targets.append(f"`{target['package_id']}` ({state}, {found})")
        lines.append(
            f"| {mod['folder']} | {mod['kind']} | "
            f"{'yes' if mod['active'] else 'no'} | "
            f"{'; '.join(targets) or '—'} | "
            f"{'ok' if mod['game_link']['correct'] else 'check'} |"
        )
        if mod["patch_symbols"]:
            lines.append(f"<!-- symbols: {', '.join(mod['patch_symbols'])} -->")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path.cwd())
    parser.add_argument("--rimworld", type=Path, default=DEFAULT_RIMWORLD)
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--exclude", action="append", default=[], metavar="MOD_NAME")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args()

    if not (args.repo / "mods").is_dir():
        print(f"error: no mods directory under {args.repo}", file=sys.stderr)
        return 2

    data = collect(args)
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
    else:
        print(render_markdown(data))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

---
name: audit-rwu-fixes
description: Audit local RWU RimWorld fixes and extensions against the currently installed RimWorld build and target mods. Use when checking whether RWU patches are still compatible, still necessary, duplicated by upstream fixes, safe to retire, missing from the active mod list, or broken after target-mod updates.
---

# Audit RWU Fixes

Perform a read-only audit by default. Never disable, move, delete, rebuild, or rewrite a mod unless the user explicitly asks.

## Workflow

1. Read the repository `AGENTS.md` and preserve unrelated Git changes.
2. Run the inventory script from the repository root:

   ```bash
   python3 skills/audit-rwu-fixes/scripts/audit_inventory.py
   ```

   Pass `--exclude "RWU Fix …"` once per user-excluded mod. Use `--json` when machine-readable output helps.
3. Separate mods into:
   - `Extension`: desired behavior; upstream bug fixes do not automatically make it obsolete.
   - `Fix`: candidate for retirement if upstream now implements the same correction.
   - Misnamed behavior mod: recommend reclassification, not removal.
4. For every C# mod, read its source and list the exact patched types, methods, fields, and invariants.
5. Locate the currently installed target mod from its `packageId`. Prefer shipped source. If source is absent, inspect the DLL with available .NET metadata/decompilation tools such as Mono.Cecil, `monodis`, or ILSpy.
6. Compare semantics, not only names:
   - Confirm the target still exists and the patch signature still matches.
   - Determine whether upstream contains an equivalent guard or state correction.
   - Check whether only part of a multi-part fix was adopted upstream.
   - Treat absence of recent errors as weak evidence only.
7. For XML fixes, compare every overridden key or def field with the currently loaded 1.6 source. Check `MayRequire` targets and load order.
8. Inspect the current `ModsConfig.xml` and the newest RimWorld `Player.log`. Distinguish RWU failures from unrelated stack traces.
9. Use upstream release notes or repositories only when local installed artifacts are insufficient. Prefer primary sources and record the installed artifact date separately from online release dates.
10. Assign exactly one result:
    - `KEEP`: upstream still lacks the behavior/fix, or the mod is an intentional extension.
    - `RETEST`: static comparison is inconclusive or only part of the fix remains; provide a minimal in-game A/B scenario.
    - `RETIRE`: upstream implements the full correction and no RWU-only behavior remains.

## Evidence threshold

Do not mark `RETIRE` from version numbers, changelog wording, method disappearance, or a clean log alone. Require direct source/IL evidence for the full fix plus a safe A/B test recommendation. If a patch target disappeared, first determine whether upstream replaced the code path or the RWU mod is simply broken.

## Output

Lead with a compact table:

| RWU mod | Result | Evidence | Next action |
|---|---|---|---|

Keep explanations short. Call out partial upstream fixes explicitly. End with the safest next action and state whether any files or game configuration were changed.

When the user requests retirement, disable one mod at a time on a copied profile/save, test the original failure scenario, then archive rather than delete. If any mod folder is moved or renamed, update its game symlink and the relevant README as required by the repository instructions.

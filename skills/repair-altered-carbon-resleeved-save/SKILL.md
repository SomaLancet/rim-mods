---
name: repair-altered-carbon-resleeved-save
description: "Use when repairing RimWorld save files corrupted by Altered Carbon 2: ReSleeved needlecasting state, especially after solar flares or powered-down neural matrices. Handles NeuralStack, Hediff_RemoteStack, AC_EmptySleeve, sourceStack, needleCastingInto, wasEmptySleeve, isCopied, and stackGroup originalStack/copiedStacks inconsistencies."
---

# Repair Altered Carbon 2 Saves

## Purpose

Repair logical save-state corruption from Altered Carbon 2: ReSleeved. These bugs usually do not involve missing defs; the XML is valid, but the mod-specific invariants between neural stacks, remote stack hediffs, empty-sleeve hediffs, and stack groups are inconsistent.

Common symptoms:

- A connected sleeve is downed with 0 consciousness.
- A disconnected sleeve stays in the colonist bar or has the wrong green/white name state.
- A stack in a neural matrix looks disconnected or classified as a copy.
- Problems appear after a solar flare or messages like needlecasting stopped because the neural matrix lost power.

## Safety

- Work from the latest user-designated save. If the user says to overwrite a specific save, obey that save; otherwise create a numbered copy first.
- Do not infer by pawn name alone. Stacks and sleeves can share the same name. Use pawn IDs, stack IDs, and remote stack `loadID`s.
- If multiple pawn records share the affected name, first identify the in-game pawn the user is actually seeing. Prefer the record whose `map`, `pos`, `healthState`, `disabled`, `ownedBed`, `AC_EmptySleeve`, and `Hediff_RemoteStack` match the screenshot/symptom. World-pawn, quest, tale, and dead historical records with the same name are usually not the target.
- Before writing, inspect the target pawn, its `AlteredCarbon.NeuralStack`, its `AlteredCarbon.Hediff_RemoteStack`, and its AC stack group.
- After writing, validate the XML with `xmllint --noout` when available.

## Key Objects

For each affected character, identify:

- Pawn: `<thing Class="Pawn"><id>Human...</id>`
- Stack: `<li Class="AlteredCarbon.NeuralStack"><id>AC_ActiveNeuralStack...</id>`
- Remote receiver: `<li Class="AlteredCarbon.Hediff_RemoteStack"><loadID>...</loadID>` inside the pawn health hediffs
- Empty sleeve marker: `<def>AC_EmptySleeve</def>` inside the same pawn health hediffs
- Empty sleeve registry: `<emptySleeves>` inside `AlteredCarbon.AlteredCarbonManager`, containing entries like `<li>Thing_Human...</li>`
- Stack group entry: the `stackGroup` list entry containing the stack as either `originalStack` or inside `copiedStacks`

## Invariants

### Connected needlecast sleeve

A pawn is actively controlled by a neural stack when both directions are linked:

- `NeuralStack.needleCastingInto = Hediff_<RemoteStack loadID>`
- `Hediff_RemoteStack.sourceStack = Thing_AC_ActiveNeuralStack...`
- `Hediff_RemoteStack.originalPawnData` is usually populated
- `Hediff_RemoteStack.wasEmptySleeve = True` should be present when the target was an empty sleeve
- The controlled pawn must not have `AC_EmptySleeve`

If a connected pawn has 0 consciousness or is downed for no normal health reason, first check for leftover `AC_EmptySleeve`. Remove only that hediff from that pawn when `sourceStack` and `needleCastingInto` prove the sleeve is connected.

### Disconnected empty sleeve

A pawn is an empty sleeve after disconnect when:

- `NeuralStack.needleCastingInto = null`
- `Hediff_RemoteStack.sourceStack = null`
- `Hediff_RemoteStack.originalPawnData IsNull="True"`
- `Hediff_RemoteStack.pawnOwnership IsNull="True"`
- `Hediff_RemoteStack.wasEmptySleeve = True`
- The pawn has `AC_EmptySleeve`
- The pawn is listed in `AlteredCarbonManager.emptySleeves`. Altered Carbon's green pawn name patch uses `AC_Utils.IsEmptySleeve(pawn)`, which checks `emptySleeves.Contains(pawn)`; the hediff alone is not enough for the green/white name state.
- The pawn is saved like other working empty sleeves in the same save, commonly with `disabled=True`, colony `guest.hostFaction`, `guest.joinStatus=JoinAsColonist`, and `despawnedTick=-1` when comparable healthy shells use that shape

Use an already-working sleeve in the same save, such as a known-good Antonio/Zarki-style shell, as the local template. Do not blindly copy identity, backstory, traits, relationships, or neural data.

### Neural matrix stack identity

For a real original stack stored in the matrix:

- The stack group should contain `<originalStack>Thing_AC_ActiveNeuralStack...</originalStack>`
- The same stack should not remain inside `<copiedStacks>` unless it is truly a copy
- Remove `<isCopied>True</isCopied>` from the stack neural data only when the user confirms it is not a copy and the stack group should be original

If a stack looks disconnected in the matrix even though its stack exists, check `stackGroup` first. A common repair is moving the stack from `copiedStacks` to `originalStack` in that group.

## Workflow

1. Locate the latest/user-specified save and the affected character IDs.
2. Compare each affected character against a working local example in the same save.
3. Print or summarize these fields before edits:
   - pawn `id`, `map`, `pos`, `name`, `spawnedTick`, `despawnedTick`, `disabled`, `healthState`, `ownedBed`, `guest.hostFaction`, `guest.joinStatus`, `ideo`, `kindDef`
   - stack `isCopied`, `stackDegradation`, `trackedToMatrix`, `needleCastingInto`, `assignedPawnForInstalling`
   - remote stack `originalPawnData`, `sourceHediff`, `sourceStack`, `wasEmptySleeve`, `pawnOwnership`
   - presence/count of `AC_EmptySleeve` on the pawn
   - whether the pawn's `Thing_Human...` load ID appears in `AlteredCarbonManager.emptySleeves`
   - stack group `originalStack` vs `copiedStacks`
4. Choose the smallest repair that restores the invariant.
5. Write the save only after the target save is clear. Preserve unrelated user changes.
6. Validate XML and report exactly what changed.

## Known Repairs

### Connected sleeve remains downed at 0 consciousness

Condition:

- `needleCastingInto` points to the pawn's remote stack hediff
- remote stack `sourceStack` points back to the neural stack
- pawn still has `AC_EmptySleeve`

Repair:

- Remove only that pawn's `AC_EmptySleeve` hediff block.
- Ensure `wasEmptySleeve=True` exists in the remote stack block.

### Disconnected shell remains a normal colonist or wrong color

Condition:

- `needleCastingInto=null` and `sourceStack=null`
- pawn has or should have `AC_EmptySleeve`
- pawn is missing from `AlteredCarbonManager.emptySleeves`, or the list contains a different stale pawn with the same name
- pawn's vanilla guest/spawn state differs from known-good shells in the same save

Repair:

- Restore `AC_EmptySleeve` if missing.
- Add the actual target pawn's `Thing_Human...` entry to `AlteredCarbonManager.emptySleeves`; remove only clearly mistaken stale same-name entries that were added during repair attempts.
- Set remote stack cleanup fields to null shape: `originalPawnData IsNull="True"`, `sourceStack=null`, `pawnOwnership IsNull="True"`, keep `wasEmptySleeve=True`.
- Align only minimal shell status fields with a known-good shell in the same save.

### Matrix shows stack as disconnected/copy

Condition:

- stack is present in matrix/cache but the stack group has it under `copiedStacks`
- the same stack is expected to be the original

Repair:

- Change that stack group from `copiedStacks` entry to `originalStack`.
- Remove stack `isCopied=True` if it is not a real copy.

## Do Not Do

- Do not make every broken pawn look like Antonio or any single pawn wholesale. Copy only the fields required by the invariant.
- Do not add a same-name dead/world pawn to `emptySleeves`. Confirm the target pawn by current `map`/`pos` or by matching the visible in-game state before editing the manager list.
- Do not wipe populated `originalPawnData` on an active connected sleeve; that can turn the body into a new full pawn.
- Do not remove all references to a pawn from global lists unless you have identified the exact owning system.
- Do not treat valid AC defs as removable cleanup just because the behavior is broken.

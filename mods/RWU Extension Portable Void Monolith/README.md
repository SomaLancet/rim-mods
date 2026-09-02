# RWU Extension Portable Void Monolith

Standalone RimWorld 1.6 mod for Anomaly + Odyssey.

- Makes the original `VoidMonolith` movable with RimWorld's standard uninstall, `MinifiedThing`, haul, and reinstall flow. The original object is retained; no replacement monolith or custom container is created.
- Preserves the monolith's discovery state while moving it, so reinstalling the same object does not show the first-discovery letter again.
- Carries the monolith core and its current attachment footprint as part of an Odyssey gravship.
- Rotates the core, interaction cell, attachment positions, and rectangular attachment footprints with the gravship or reinstall blueprint.
- Validates the full current monolith footprint before reinstalling so attachment generation cannot silently wipe a nearby building.
- Keeps the active monolith quest alive when the previous map is destroyed after the surviving monolith has already been packed or captured by a gravship.
- Remembers the original monolith while it is inside a `MinifiedThing` or a travelling gravship, preventing Anomaly's `MonolithMigration` / "Strange signal" fallback from creating a replacement.
- Detects Void Universe (`HaiLuan.VoidUniverse`) at runtime and prevents it from spawning a duplicate while the original monolith is packed or travelling. Void Universe is optional, not a dependency.

## Dependencies

- Harmony
- RimWorld - Anomaly
- RimWorld - Odyssey

## Load order

Load after Harmony and the DLCs. Loading after Void Universe is recommended when that mod is enabled; the metadata already expresses this soft ordering without making it mandatory.

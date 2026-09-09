# Workspace Instructions

- Local RimWorld mods are always stored at:
  `/Users/dieruki/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods`
- Whenever mod folders are changed, renamed, or moved, always update the corresponding symlinks.
- Always update the relevant README files after making changes.
- Whenever a mod contains Russian localization, verify that the active version's `LoadFolders.xml` includes the directory containing `Languages`. For a root-level `Languages` directory, include `<li>/</li>`. Keep translations under `Languages/Russian`; RimWorld also uses this legacy name when the selected language folder is named `Russian (Русский)`.

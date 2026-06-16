KNOWN: The terrain-detail concealment table in src/Utility.cs (CalculateDetailScore) is still 3.11-era. It matches grass/foliage prototypes by the last 6 hex characters of their asset names, and EFT 40087 introduced/re-hashed terrain details that aren't in the table (observed example: Detail_1_grass_cut_dry_2d5ee9, suffix 2d5ee9). Unrecognized details produce zero foliage concealment (no crash, no fallback) and, with debug enabled, a throttled "Missing terrain detail" notice. Concealment from recognized grass still works; only the new/changed prototypes are missed. Re-surveying the 4.0 terrain-detail hashes is out of scope for this compatibility fix. I tried to find all the broken assets and update them, but didn't get all of them. This shouldn't affect gameplay in any major way, you just won't get foliage impact bonuses in a few cases.

The shields.io badges broke so I'll recreate them

{ downloads | 201k } { downloads@latest | 31k }

Latest VirusTotal scan: https://www.virustotal.com/gui/file/3d9b96f675fcd1c42d33b38764a7880d7d0ce1db797536d962cfe14aeec0795a?nocache=1

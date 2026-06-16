# That's Lit — SPT 4.0.13 Compatibility Patch

Unofficial community compatibility patch of **That's Lit v1.4000.0** for **SPT 4.0.13 / EFT 0.16.9.0.40087**.

This is a restoration fork whose sole purpose is to make That's Lit **compile and run** on EFT build `40087`. It is **not** maintained beyond this fix — no new features, no ongoing support. All original design and credit belong to the original author of That's Lit (**BA**, per the in-source copyright). If an officially updated That's Lit exists for your SPT version, prefer it over this patch.

The original mod was built for SPT 3.11 / EFT build `35392`. Between that build and `40087`, BSG obfuscation numbers shifted, several EFT method signatures changed, the per-part visibility API was refactored, and — most importantly — the meaning of the core "seen coefficient" was inverted. The changes below address each of those.

---

## What changed, file by file

### `src/SeenCoefPatch.cs` (core AI-perception postfix)

This is the heart of the mod and took the most work.

1. **Target-method lookup re-anchored to `IAIData`.**
   The old lookup matched the seen-coef method by a 6-type argument signature that began with `BotDifficultySettingsClass` — a name that is now obfuscated and can no longer be referenced. The lookup now anchors on the method's `IAIData` parameter:
   ```csharp
   return ReflectionHelper.FindMethodByArgTypes(typeof(EnemyInfo), new Type[] { typeof(IAIData) });
   ```
   `IAIData` is an interface (interface names survive obfuscation) and is unique to this method within `EnemyInfo`. `FindMethodByArgTypes` is a containment match, so the extra/renamed parameters don't matter.

2. **Postfix gained the `deltaTime` parameter.**
   The EFT method picked up a 7th argument (`float deltaTime`) in 4.0. The postfix signature now includes it so Harmony binds correctly by name:
   ```csharp
   public static void PatchPostfix(EnemyInfo __instance, float personalLastSeenTime, Vector3 personalLastSeenPos, float deltaTime, ref float __result)
   ```

3. **`AllActiveParts` loop rewritten for the new per-part visibility API.**
   In 4.0, `EnemyInfo.AllActiveParts` is now a `HashSet<BodyPartType>` (was a keyed collection), and per-part visibility moved to the public `EnemyInfo.AllPartsVision` dictionary (`Dictionary<BodyPartType, …>`). The old `LastVisibilityCastSucceed` field no longer exists; the closest current equivalent is `HasLineOfSight`:
   ```csharp
   // was: if (!p.Value.LastVisibilityCastSucceed) { switch (p.Key.BodyPartType) ...
   foreach (var p in __instance.AllActiveParts)          // p is now a BodyPartType
       if (!__instance.AllPartsVision[p].HasLineOfSight)  // value type inferred; no GClass literal
           switch (p) { ... }
   ```

4. **Seen-coefficient polarity inverted for build 40087.**
   In 4.0 the value this method returns (`_visibilityChangeSpeedK`) is a visibility-fill **speed** — *higher = the bot sees you sooner*. The mod was written for the opposite convention (higher = harder to see) and multiplies the coefficient up to represent concealment. Left as-is, concealment made bots detect you **faster**. The pipeline that builds the legacy magnitude `M` is unchanged (so the debug meters keep their meaning), but the final value handed back to EFT is now polarity-corrected:
   ```csharp
   float M = __result;
   float impact = M / original;     // >1 = concealment intent, <1 = faster-detection intent, ==1 = no effect
   impact = Mathf.Max(impact, 0.5f);                       // caps "fastening" (was floor of 0.5*original)
   if (sniperHint) impact = Mathf.Min(impact, SNIPER_CAP); // re-expressed anti-sniper cap (was Min(__result,1f))
   impact = Mathf.Pow(impact, finalScale);                 // FinalImpactScale, applied in log space
   impact *= FinalImpactKnob;                              // replaces additive FinalOffset (1.0 = no-op)
   __result = original / impact;                           // concealment now LOWERS the coefficient
   ```
   The legacy `__result >= 8888` "already invisible" sentinel was replaced with `__result <= EPS` (now the low end of the coefficient, and a divide-by-zero guard). New tuning constants `EPS`, `SNIPER_CAP`, `FinalImpactKnob` were added with behavior-preserving / permissive defaults. Baseline is preserved: if no concealment branch fires, `M == original` → `impact == 1` → `__result == original` (clean no-op).

5. **Relative floor replaces the absolute one.**
   The first inversion pass used an absolute floor (`MIN_COEF = 0.005`). Because the vanilla coefficient varies by orders of magnitude across maps/lighting (observed `0.019` vs `0.001`), an absolute floor flattened the whole concealment gradient and — when `original < 0.005` — actually raised the result above vanilla (inverting the low-baseline case). It was replaced with a relative floor:
   ```csharp
   __result = Mathf.Max(original / impact, original * MIN_COEF_RATIO); // MIN_COEF_RATIO = 0.001f
   ```
   Max stealth is now ~1000× slower than the per-situation baseline, always proportional to `original`, and always below it. The absolute `MIN_COEF` constant was retired.

### `src/ExtraVisibleDistancePatch.cs` (extra vision-distance prefix)

1. **Target-method lookup de-hardcoded.**
   `AccessTools.Method(typeof(EnemyInfo), "method_0")` relied on an obfuscator-assigned name that shifts every build. Replaced with a structural fingerprint — the only `EnemyInfo` method returning `float` and taking a single `BotOwner`:
   ```csharp
   return AccessTools.FirstMethod(typeof(EnemyInfo), m =>
       m.ReturnType == typeof(float)
       && m.GetParameters().Length == 1
       && m.GetParameters()[0].ParameterType == typeof(BotOwner));
   ```

2. **Removed the now-inert `owner.IsAI` guard.**
   The early-out clause `(owner?.IsAI ?? true) == true` skipped the patch whenever the `owner` parameter was AI. In 4.0 this method is called as `this.<method>(this.Owner)`, so `owner` is the **observing bot** — always AI — which tripped the guard on **every** call and made the patch completely inert (the Extra Vision Distance feature did nothing at any setting). The clause was removed. "Human players only" is still enforced downstream by the `AllThatsLitPlayers` lookup, since `ThatsLitGameworld` registers only non-AI players (`if (player.IsAI) continue;`).

### `src/EncounteringPatch.cs`

`IBotAiming.NextShotMiss` gained a required `int missCount` parameter in 4.0. Both call sites were updated to pass `1` (matching EFT's own minimal call):
```csharp
// aim.NextShotMiss();  ->
aim.NextShotMiss(1);
```

### `src/BlindFirePatch.cs`

The postfix injected two private fields by name via Harmony — `___botOwner_0` and `___bifacialTransform_0`. The auto-generated backing-field name `botOwner_0` was renamed by the publicizer (to `BotOwner_0`), so the case-sensitive `___botOwner_0` injection threw an IL-compile error and the patch failed to apply (which, before the bootstrap was hardened, aborted the whole mod). The injection was replaced with a structural, name-independent lookup of the single `BotOwner`-typed field, cached at patch setup:
```csharp
private static FieldInfo _botOwnerField;
// in GetTargetMethod():
_botOwnerField = AccessTools.GetDeclaredFields(aimingType).Single(f => f.FieldType == typeof(BotOwner));
// in postfix (signature now: object __instance, ref Vector3 __result):
var botOwner = (BotOwner)_botOwnerField.GetValue(__instance);
```
The unused `___bifacialTransform_0` injection was confirmed dead and removed.

### `src/ThatsLitGameworld.cs`

The deobfuscated `GClass` aliases drifted between 3.11 and 4.0.13. Re-numbered to match the deobfuscated `Assembly-CSharp` for `40087` (identified by stable members — `CalculateHash`, `detailMapData`, the `spData` field type — not by trusting the numbers):
```csharp
// using BaseCellClass        = GClass1187;            ->  GClass1258;
// using CellClass            = GClass1188;            ->  GClass1259;
// using SpatialPartitionClass = GClass1202<GClass1187>; ->  GClass1273<GClass1258>;
```

### `ThatsLitPlugin.cs`

1. **Version guard updated.** `AssemblyInfo.TarkovVersion` `35392` → `40087`. This constant feeds `[assembly: VersionChecker(...)]`, which exact-matches against the running `EscapeFromTarkov.exe` build; the old value disabled the whole mod on 4.0.13.

2. **`Patches()` hardened with per-patch isolation.** Each `new XPatch().Enable()` call now runs through an `EnablePatch(ModulePatch)` helper that try/catches, logs the patch name + exception, and continues. Previously a single patch failing (e.g. BlindFirePatch's field-injection error) threw out of `Awake()`, which disabled the plugin component and left the per-frame update loop (and debug HUD) dead.

3. **`SAINNoBushOverride` gated on its target type existing.** SAIN removed `SAIN.Components.SAINNoBushESP.SetCanShoot` in SAIN 4.4.3 (the No-Bush-ESP capability is no longer an internal SAIN component), so the lookup returns null and the patch can't attach. The enable is now gated so it self-skips silently on SAIN versions without the feature, and still enables unchanged if a SAIN that has it is installed:
   ```csharp
   if (SAINLoaded && Type.GetType("SAIN.Components.SAINNoBushESP, SAIN") != null)
       EnablePatch(new SAINNoBushOverride());
   ```
   The `SAINNoBushOverride.cs` patch class itself was left untouched.

### `ThatsLit.Core.csproj`

1. **`Assembly-CSharp` reference repointed to a deobfuscated `hollowed.dll`.** The runtime `Assembly-CSharp.dll` in the install carries only raw obfuscated (Private-Use-Area) names — no `GClass*` — so it can't satisfy the mod's `GClass`-typed source. The reference now points at a local, repo-relative publicized/deobfuscated reference assembly:
   ```xml
   <ReferencesPath>$(MSBuildProjectDirectory)\..\..\References</ReferencesPath>
   <!-- ... -->
   <Reference Include="Assembly-CSharp">
     <HintPath>$(ReferencesPath)\hollowed.dll</HintPath>
     <Private>False</Private>
   </Reference>
   ```

2. **`{HintPathFromItem}` prepended to `AssemblySearchPaths`.** The project overrides `AssemblySearchPaths` to a fixed list of install directories, which omits the special token that makes RAR honor explicit `<HintPath>`s. Without it, `Assembly-CSharp` resolved by simple name from the install's `Managed` folder (the raw obfuscated DLL) and silently ignored the `hollowed.dll` HintPath. Fixed by leading the list with the token:
   ```xml
   <AssemblySearchPaths>{HintPathFromItem};$(BepInExPath);$(ManagedAssembliesPath);$(PluginsPath)\spt;</AssemblySearchPaths>
   ```

All other references (BepInEx, spt-core, spt-reflection, Unity modules, Comfort, etc.) continue to resolve from the install via `EFTPath` and were left unchanged.

---

## Known limitation

The terrain-detail concealment table in `src/Utility.cs` (`CalculateDetailScore`) is still **3.11-era**. It matches grass/foliage prototypes by the last 6 hex characters of their asset names, and EFT `40087` introduced/re-hashed terrain details that aren't in the table (observed example: `Detail_1_grass_cut_dry_2d5ee9`, suffix `2d5ee9`). Unrecognized details produce **zero foliage concealment** (no crash, no fallback) and, with debug enabled, a throttled "Missing terrain detail" notice. Concealment from *recognized* grass still works; only the new/changed prototypes are missed. Re-surveying the 4.0 terrain-detail hashes is out of scope for this compatibility fix.

---

## Building

Requirements:

- **.NET SDK** (a recent SDK is fine; the project targets `net472`).
- **.NET Framework 4.7.2 reference assemblies** — restored from NuGet (`nuget.org` reachable). If a stale local NuGet source blocks restore, disable it (`dotnet nuget disable source "<name>"`).
- **A deobfuscated / publicized `hollowed.dll`** placed at `thats-lit-src/References/hollowed.dll` and referenced as `Assembly-CSharp` (see csproj change above).
- **An SPT 4.0.13 install** for the remaining references, passed via the `EFTPath` build property.

Build command (adjust the path to your install):

```powershell
dotnet build "thats-lit-src/source/ThatsLit.Core/ThatsLit.Core.csproj" -c Release -p:EFTPath="F:\SPT DEV INSTALLATION"
```

The post-build target copies `ThatsLit.Core.dll` (and the `Packed/` data files) into `$(EFTPath)\BepInEx\plugins\ThatsLit`.

### Do NOT commit game-derived binaries

`hollowed.dll`, `Assembly-CSharp.dll`, and any `spt-*`, `Comfort`, `Sirenix`, `UnityEngine.*`, BepInEx, or other game/SPT assemblies are **derived from EFT / SPT and must not be committed** to this repository (licensing). Keep them out of source control (`.gitignore` the `References/` folder and any copied managed DLLs).

How to obtain them instead:

- **`hollowed.dll`** — the community's deobfuscated/publicized `Assembly-CSharp` reference assembly for your exact SPT build. It ships in the reference set of current SPT mod source trees (e.g. SAIN's `References/` folder) and via the usual SPT modding reference-assembly distributions. Use the one matching SPT 4.0.13; drop it in `thats-lit-src/References/`.
- **Install assemblies** — provided by your own licensed **SPT 4.0.13** installation; the build reads them in place through `EFTPath` (nothing is copied into the repo).

Never redistribute these files with the source.

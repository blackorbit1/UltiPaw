# AGENTS.md

## Language / Unity Constraints
- Unity compiles this package with `/langversion:9.0`.
- Do not introduce C# 10+ features (including global using) or compiler override changes.

## BlendShape Link System

### Goal
- Drive corrective blendshape animation (`fixingBlendshape`) from an already-animated source blendshape (`blendshapeToFix`), multiplied by a factor.
- Apply this only to VRCFury-generated temporary controllers at build/preprocess time.
- Never mutate the original user-authored controllers directly.

### Two Link Sources

1. Manual link (Advanced Mode test drawer)
- UI: `Editor/Features/BlendShapeLinkTestDrawer.cs`
- Saved as persistent config in `UltiPaw.blendShapeFactorLinks`.
- Fields:
  - target renderer path
  - source blendshape
  - destination blendshape
  - factor parameter name
  - enabled flag

2. Current-version links (version JSON corrective definitions)
- Source data: `customBlendshapes[].correctiveBlendshapes` from applied version.
- Runtime/build fallback cache: `UltiPaw.appliedVersionBlendshapeLinksCache`.
- Generated per renderer containing both source + destination blendshapes (all skinned meshes, not only Body).
- Factor selection:
  - If driver blendshape is an active slider: use slider global param.
  - If not: use a constant factor parameter with default value equal to current driver blendshape weight (0..1).

### VRCFury Interaction
- Sliders are created in `Editor/Services/VRCFuryService.cs`.
- For each slider toggle, force:
  - `content.useGlobalParam = true`
  - `content.globalParam = VRCFuryService.GetSliderGlobalParamName(sliderName)`
- This guarantees stable param names for version-based links.

### Build/Play Hook Execution
- Hook class: `Editor/Services/BlendShapeLinkPostVrcfuryHook.cs`
- Interface: `IVRCSDKPreprocessAvatarCallback`
- Order: `-9000` (after VRCFury, which runs at `-10000`).
- Runs for:
  - Upload build preprocess
  - Play mode preprocess path
- Applies:
  - version-based links
  - manual links

### Animator Mutation Strategy
- Core service: `Editor/Services/BlendShapeLinkService.cs`
- Collect only VRCFury temp controllers (`com.vrcfury.temp`).
- For each matching clip curve on target path/property:
  - clone clip as variant and write destination curve from source curve
  - create wrapper 1D blend tree with factor parameter:
    - child 0 threshold 0: original clip
    - child 1 threshold 1: variant clip
  - rewrite state/tree motion refs to wrapper
- Ensure factor parameter exists as Float in each processed controller.
- Wrapper/variant assets are attached as sub-assets to the temporary controller.

### Clone / Upload Robustness
- Preprocess may run on cloned avatar objects with editor-only components missing.
- `BlendShapeLinkService.FindUltiPaw` includes fallback lookup by root name against scene `UltiPaw` instances.
- Version links additionally fall back to serialized cache (`appliedVersionBlendshapeLinksCache`) when `appliedUltiPawVersion` is unavailable.

### Version Cache Synchronization
- `Editor/Features/VersionManagement/VersionActions.cs` synchronizes version blendshape cache:
  - set on apply/match
  - clear on reset/custom-version application
- This keeps upload preprocess deterministic even when full version objects are not present.

### Advanced Mode Debug UX
- Drawer shows:
  - manual test controls (enable/disable + save config)
  - live list of active version links
  - factor parameter name
  - exact constant factor value for non-slider links
- Link debug data comes from `BlendShapeLinkService.GetActiveVersionLinkDebugInfo`.

### Important Rules
- Do not patch default/read-only VRChat package controllers.
- Do not patch authoring controllers directly.
- Keep link operations idempotent and safe across repeated preprocess calls.
- Keep naming deterministic for parameters and generated assets.

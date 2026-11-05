# Animation System DOTS

Experimental Unity ECS animation package for Unity **6000.6.8f1**, Entities/Entities Graphics **6.6.0**, Burst **2.0.0**. One Git repository contains the runtime, authoring, Editor tools, test scenes and developer tests.

## Install with Unity Package Manager

After publishing this layout to `master`, use **Add package from git URL**:

```text
https://github.com/vertoker/animation-system-dots.git#master
```

`package.json` is at the repository root; no `?path` is needed. Package Manager resolves the declared runtime dependencies. For reproducible projects, pin a tested commit instead of `master`.

For the test scenes, use a configured **Universal 3D/URP 17.6.0** project, then use the package's **Samples > Validation Sandbox > Import**, or **Tools > Animation System DOTS > Import Validation Sandbox**. Imported samples belong in the consumer's `Assets`, where scenes, prefabs and importer settings are writable. Package-cache sources are not modified. Both sample import locations are supported.

If converting a Built-in project, installing URP alone does not activate it. After importing the sample, assign its `RenderPipeline/PC_RPAsset.asset` to **Project Settings > Graphics > Default Render Pipeline** and the active **Quality > Render Pipeline Asset**. For Player builds, retain BatchRendererGroup shader variants in Graphics settings. These are explicit host-project settings; importing the sample does not change them automatically. The clean-consumer verification uses this configured URP profile. The core CPU animation pipeline is separate from the sample's URP rendering requirements.

## Editable Git submodule in Assets

Install the dependencies below before opening Unity, then:

```sh
git submodule add https://github.com/vertoker/animation-system-dots.git Assets/AnimationSystemDOTS
git submodule update --init --recursive
```

Edit `Assets/AnimationSystemDOTS/Runtime`, `Authoring`, `Rendering` and `Editor` directly and commit inside that submodule. Do not also install a second copy with Package Manager. Unity does not install `package.json` dependencies automatically for an ordinary Assets folder:

```json
"com.unity.entities": "6.6.0",
"com.unity.entities.graphics": "6.6.0",
"com.unity.burst": "2.0.0",
"com.unity.collections": "6.6.0",
"com.unity.mathematics": "1.4.0",
"com.unity.modules.animation": "1.0.0",
"com.unity.modules.jsonserialize": "1.0.0"
```

The same **Import Validation Sandbox** menu works in this mode. To edit the original sample scenes directly in the same submodule on Windows, close Unity and run from the consumer project root:

```powershell
& '.\Assets\AnimationSystemDOTS\Tools~\Setup-Development.ps1' -ProjectPath . -SamplesOnly
```

This mounts `Samples~/Sandbox` at `Assets/AnimationSystemDOTS.Sandbox` with a directory junction. Scene, material and sample-code edits then change the submodule's own files immediately. The script refuses to overwrite an existing sample folder. Use either this live mount or a normal imported copy, not both; ignore the junction and its `.meta` in the parent project's Git repository. The source scenes and their metadata remain tracked inside this submodule.

## Develop this repository

On Windows, from the repository root:

```powershell
.\Tools~\Setup-Development.ps1
```

Open **`DevProject~`** in Unity. Its local UPM dependency points to this repository; its sample and test folders are live junctions to `Samples~` and `Tests~`. There are no duplicated source trees to synchronize. To verify Assets installation instead, close Unity, run `Setup-Development.ps1 -Mode Assets`, then reopen `DevProject~`. Switch back with `-Mode UPM`.

Open `Assets/AnimationSystemDOTS.Sandbox/Scenes/04_HumanoidCrowd1000.unity` for the 1000-character test, or `01_GenericPlayback.unity` for the smaller fixture. The development project includes URP and the test framework; these development dependencies do not become core runtime dependencies.

```text
Runtime/        ECS playback, sampling, blending and poses
Authoring/      Authoring components and bake settings
Rendering/      Public Entities Graphics integration
Editor/         Baking, validation and sample import
Samples~/       Complete test scenes and supporting assets
Tests~/         Developer regression tests, mounted by setup
DevProject~/    Unity development host; caches are ignored
Tools~/         Development and acceptance scripts
Documentation~/ Usage, verification and supported features
```

Folders ending in `~` are deliberately excluded from normal Unity asset import. Samples are included in the package download and imported on demand. The development host and test dependencies do not leak into a consumer's runtime assemblies.

## Verify installation

The developer test assemblies are available in the Test Runner after development setup. The installation probes create separate configured URP projects, verify that sample assemblies are initially excluded, import the sample, and check 1000 rigs / 3000 palettes / 3000 render entities with no runtime Animator:

```powershell
.\Tools~\Verify-Consumer.ps1 -Mode UPM
.\Tools~\Verify-Consumer.ps1 -Mode Assets
```

Generated consumer projects and their `consumer-result.json` reports live in `.verify~` and are ignored by Git. Player captures/builds from the development host live in `DevProject~/Artifacts`. The Assets probe checks the same source-folder layout as an initialized submodule; it does not publish or clone a remote branch.

## Current functionality and remaining work

Implemented and tested: Generic and target-Avatar Humanoid clip baking, explicit ECS playback and blending, affine skeleton poses, skinning, animated bounds, prefab lifecycle and 1000-character scenes. Ordinary Unity `.controller` assets now bake into a single-layer ECS/Burst state machine with typed parameters, transitions and crossfades. See [controller setup and supported subset](Documentation~/Controllers.md); the editable example is `Samples~/Sandbox/Scenes/02_Controller.unity`. The user confirmed depth/normal/tangent parity with a conventional SkinnedMeshRenderer.

The original full feature plan still includes:

- Animator layers, blend trees, nested machines and transition interruption beyond the supported single-layer subset.
- Root-motion extraction and application; current crowd playback is stationary in XZ.
- Events, scalar animation curves, animated blend shapes and general bone attachments.
- IK and procedural constraints.
- Animation culling/LOD scheduling and additional measured optimizations.
- Editor inspection/diagnostics, a small consumer-ready authoring sample, IL2CPP and additional platform verification.
- Optional GPU pose engine, Physics and Netcode integrations.

Multiple independent rendering Worlds remain unsupported in the stock deformation path; the repository includes a reproducible case. See [SupportMatrix](Documentation~/SupportMatrix.md), [runtime usage](Documentation~/Runtime.md) and [manual/Player checks](Documentation~/RenderingChecks.md). This is an experimental package, not full Mecanim compatibility.

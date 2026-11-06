# Animation System DOTS package

ECS animation for Unity **6000.6.8f1**, Entities/Entities Graphics/Collections **6.6.0**, Burst **2.0.0**, Mathematics **1.4.0**. See [supported features and limits](Documentation~/SupportMatrix.md).

Install with Package Manager **Add package from Git URL**:

```text
https://github.com/vertoker/animation-system-dots.git?path=/Assets/AnimationSystemDOTS#master
```

The package contains Runtime, Authoring, Rendering, Editor, documentation and an importable **Validation Sandbox**. Development tooling, Coplay MCP and the root Unity project are outside this boundary. Use a tested commit instead of `master` for reproducible installations. Runtime dependencies are declared in `package.json`.

Use **Samples > Validation Sandbox > Import** or **Tools > Animation System DOTS > Import Validation Sandbox** for scenes and editable assets. The sandbox requires a configured **URP 17.6.0** project. Assign its `RenderPipeline/PC_RPAsset.asset` in Graphics and active Quality settings if the host has no URP profile. For Player builds, retain BatchRendererGroup shader variants. Importing the sample does not change host settings automatically. Input System is optional; its device adapter compiles only when installed, while on-screen controls and deterministic replay remain available.

Open scenes 05 (ordinary Animator player), 06 (ECS player), 07 (100 ECS bots) or 04 (1000 Humanoid characters). See [controls, comparisons and ragdoll assessment](Documentation~/AnimatorValidation.md), [Animator setup](Documentation~/Controllers.md), [runtime API](Documentation~/Runtime.md) and [manual/Player checks](Documentation~/RenderingChecks.md).

For a writable submodule, clone the repository outside the consumer's Assets, for example `External/AnimationSystemDOTS`, then use `file:../External/AnimationSystemDOTS/Assets/AnimationSystemDOTS` as this dependency in the consumer's `Packages/manifest.json`. Commit edits inside the submodule. The complete repository is a Unity development project; install only this package subdirectory. For a plain Assets copy, declare the package dependencies in the consumer manifest yourself; Unity does not resolve package.json dependencies for ordinary Assets folders. Keep only one source copy active.

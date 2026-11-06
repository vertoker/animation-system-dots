# Animation System DOTS

Experimental Unity ECS animation: Generic and Humanoid playback, Animator Controller baking, Blend Trees, layers and Entities Graphics skinning. Unity **6000.6.8f1**, Entities/Graphics/Collections **6.6.0**, Burst **2.0.0**, Mathematics **1.4.0**.

## Develop

Open **this repository root** in Unity. It is the only permanent Unity project. Runtime edits, scenes and tests work directly without setup scripts or junctions.

```text
Assets/
  AnimationSystemDOTS/          Complete installable package
    Runtime/                   ECS playback, poses, controllers and Humanoid
    Authoring/                 Authoring components and settings
    Rendering/                 Entities Graphics integration
    Editor/                    Baking and sample import
    Documentation~/            Package usage and supported features
    Samples~/Sandbox/          Published importable sandbox snapshot
    package.json
  AnimationSystemDOTS.Sandbox/  Editable development scenes and fixtures
  Tests/                       Development regression suites
Packages/                      Root project dependencies, including Coplay MCP
ProjectSettings/               Root development configuration
Tools/                         Export, installation and Player checks
Docs/Superpowers/              Maintainer plans and historical designs
Artifacts/                     Local reports/builds, ignored by Git
.superpowers/consumers/         Disposable installation checks, ignored by Git
```

The sample's editable source is `Assets/AnimationSystemDOTS.Sandbox`. After editing it, publish the package snapshot and check its parity:

```powershell
python Tools/SyncSandbox.py
python Tools/SyncSandbox.py --check
python Tools/ValidateLayout.py
python -m unittest discover -s Tools/tests -v
```

Open `Assets/AnimationSystemDOTS.Sandbox/Scenes/05_AnimatorGO.unity`, `06_AnimatorECS.unity` or `07_AnimatorBots100.unity` for the shared 34-clip Animator comparison. Scene `04_HumanoidCrowd1000.unity` is the 1000-character test. See [controls and manual checks](Assets/AnimationSystemDOTS/Documentation~/AnimatorValidation.md).

## Install the package

After this revision is published, use Package Manager **Add package from Git URL**:

```text
https://github.com/vertoker/animation-system-dots.git?path=/Assets/AnimationSystemDOTS#master
```

Only `Assets/AnimationSystemDOTS` is the package. Pin a tested commit for reproducible consumers. Runtime dependencies come from its `package.json`; the repository's MCP, tests and project settings are not installed. Import **Validation Sandbox** from the Samples tab into a configured **URP 17.6.0** project. See [package setup](Assets/AnimationSystemDOTS/README.md).

For an editable submodule in another project, keep the repository outside that project's Assets and point UPM to its package folder:

```sh
git submodule add https://github.com/vertoker/animation-system-dots.git External/AnimationSystemDOTS
```

In the consumer's `Packages/manifest.json` dependencies:

```json
"com.vertoker.animation-system-dots": "file:../External/AnimationSystemDOTS/Assets/AnimationSystemDOTS"
```

Edit and commit runtime code in that submodule directly. A local UPM package is writable. To use an ordinary Assets installation instead, copy only the package folder to `Assets/AnimationSystemDOTS` and declare its dependencies in the consumer manifest; do not also install a second UPM copy. A full repository checkout nested inside Assets would include a second Unity project and is not the supported layout.

## Verify distribution

```powershell
.\Tools\Verify-Consumer.ps1 -Mode UPM
.\Tools\Verify-Consumer.ps1 -Mode Assets
.\Tools\Verify-Consumer.ps1 -Mode Git -Revision HEAD
```

These generate disposable projects under `.superpowers/consumers`, open Unity normally and verify sample exclusion/import plus 1000-character, ECS player and 100-bot scenes without Input System. Git mode tests the actual committed Git subpath via the local repository, without pushing. Run Editor/Runtime suites through Unity Test Runner or Coplay MCP. Unity CLI/Pipeline is disabled.

Supported behavior and remaining work: [SupportMatrix](Assets/AnimationSystemDOTS/Documentation~/SupportMatrix.md), [Animator Controllers](Assets/AnimationSystemDOTS/Documentation~/Controllers.md), [runtime usage](Assets/AnimationSystemDOTS/Documentation~/Runtime.md) and [roadmap](PLAN.md). This is an experimental package, not full Mecanim compatibility. Root motion, events, IK, ragdoll, animation LOD and additional platform verification remain separate work.

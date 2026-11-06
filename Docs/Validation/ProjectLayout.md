# Root project and Assets package validation

Date: 2026-11-07. Layout revision `8c83228`; development-path fixes `ac394da`.

The repository root is the sole permanent Unity project. The installable Git subpath is `Assets/AnimationSystemDOTS`. Development scenes and tests are ordinary files in `Assets/AnimationSystemDOTS.Sandbox` and `Assets/Tests`; neither junctions nor a package self-dependency are used. `Tools/SyncSandbox.py` exports the development sandbox into the package's `Samples~/Sandbox` and verifies exact byte parity, including metadata. Temporary consumer projects are generated only under ignored `.superpowers/consumers`.

## Observed checks

- The old layout failed `Tools/ValidateLayout.py`; the migrated layout passes. All 323 pre-existing metadata files were preserved byte-for-byte during relocation. All 597 moved production, test and sandbox files matched the preceding Git archive before the source-import path fix. Runtime and rendering implementation did not change.
- Root Editor/Sandbox suite: **263/263 passed, zero skipped**, 200.175 seconds, job `6c4010a2c0274e0688717fb1729ca957`.
- Root Runtime suite: **54/54 passed, zero skipped**, job `9866d02a94c74fceb6620dce9acfcb83`.
- Clean Assets, local UPM and pinned Git-subpath consumers of the layout revision passed sample exclusion/import, writable imported assets, 1000 rigs / 3000 palettes / 3000 render entities / zero native Animators, plus one ECS player and 100 ECS bots. Input System was absent and its adapter was excluded. Git lockfile resolved exactly `8c832288408e9f9ab495510500e8cd1a04c69969` from `?path=/Assets/AnimationSystemDOTS`.
- The sample exporter fixture passed missing, changed, extra-file refusal and exact byte-copy checks. `SyncSandbox.py --check` and `ValidateLayout.py` pass.
- Fresh whole-change review found two Important path regressions and one Minor tooling default. All three were fixed in `ac394da`: the material generator output, private Mixamo source import and reference inventory output. The actual generator and inventory command succeeded. The source-import menu failed before the fix and then successfully imported all 54 motion takes in the canonical Assets host. No imported animation settings or shader content changed.

Post-fix acceptance: **263/263 Editor/Sandbox passed, zero skipped**, 230.646 seconds, job `16ff389cd1e24beaa641e8a0e14fca04`. The final Git consumer also passed all the same 1000/1/100-character checks and its lockfile resolved exactly `ac394daae13f2912c7072dde59d6570bbc05a7a9`. The runtime implementation was unchanged after its 54/54 run. All review findings have observed passing checks.

Reports, logs and XML are local under `Artifacts/Verification/ProjectLayout`. Earlier performance and graphics reports were retained under root `Artifacts`; they are historical measurements, not new layout performance claims. Clean no-Input consumers emitted missing-script messages during scene restoration and allocation diagnostics at shutdown; the documented scene/palette checks passed. Those warnings were not investigated as part of this layout migration.

The previous incomplete root configuration was preserved under ignored `.superpowers/migration/RootBefore`. Old host leftovers and historical installation experiments were archived there. No unrelated Editor was operated. No Unity CLI, push, publishing or history rewriting was used.

All four generated consumer processes exited with code zero. Recursive deletion of their disposable directories was rejected by automatic policy (`blocked by policy`); they were instead preserved non-destructively under `.superpowers/migration/CompletedConsumers`. Reports were separately copied into root Artifacts. Those archives are local experiments, not additional tracked development hosts.

## Reproduce

From the repository root:

```powershell
python Tools/ValidateLayout.py
python Tools/SyncSandbox.py --check
python -m unittest discover -s Tools/tests -v
.\Tools\Verify-Consumer.ps1 -Mode UPM
.\Tools\Verify-Consumer.ps1 -Mode Assets
.\Tools\Verify-Consumer.ps1 -Mode Git -Revision HEAD
```

Use Coplay MCP or Unity Test Runner for `AnimationSystemDOTS.Tests.Editor`, `AnimationSystemDOTS.Sandbox.Tests.Editor` and `AnimationSystemDOTS.Tests.Runtime`. Normal development opens the repository root directly; install consumers use only the package subdirectory. See [development and install instructions](../../README.md) and [migration plan](../Superpowers/Plans/2026-11-07-project-layout.md).

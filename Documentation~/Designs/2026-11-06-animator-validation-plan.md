# Animator validation implementation plan

> **For agentic workers:** Use superpowers:executing-plans, inline. Track steps here and evidence in the plan workspace ledger. The user approved the design and explicitly requested implementation on master.

**Goal:** Deliver native GO, fully ECS player, and 100 ECS bot scenes using one editable AnimatorController and supplied Mixamo motions.

**Architecture:** Editor conversion produces immutable graph/motion/mask data; Burst systems evaluate per-entity state, motion weights, sampled poses and rendering. Native Animator is the differential oracle and the GO scene implementation. Sample locomotion shares unmanaged motor math while input/camera remain optional adapters.

**Tech Stack:** Unity 6000.6.8f1, Entities/Graphics/Collections 6.6, Burst 2, Mathematics 1.4, Input System 1.20.1 in the sample adapter only.

**Spec:** [Approved design](2026-11-06-animator-validation-design.md), approved 2026-11-06.

## Global constraints

- Master only; commit autonomously, do not push. Keep unrelated user changes.
- Root UPM package; editable samples in `Samples~/Sandbox`; canonical host `DevProject~`.
- Unity CLI disabled. Use Coplay MCP and only this project's editor.
- ECS characters have no runtime Animator, PlayableGraph or GO bone hierarchy.
- Preserve existing single-clip/controller guarantees and tests. Unsupported features require explicit conversion diagnostics.
- Ragdoll feasibility only; no Unity.Physics core dependency or placeholder implementation.
- Native Humanoid blend comparison is an early architecture gate, not a final screenshot check.

## Review focus

1. Same-name source variants must retain distinct identity; inventory uses relative path, SHA256 and imported take metadata (task 1).
2. Target Avatar retargeting then TRS blending may disagree with native muscle-space blending; record joint/rotation maxima for real motions before committing to an algorithm (task 2).
3. Graph rebake with unchanged parameter count may leave stale indices/state; test definition identity reset and dependent asset invalidation (tasks 3–5).
4. Interrupted layered transitions and trigger consumption may differ while final poses look plausible; use fixed-step state/time/weight traces (tasks 4–5).
5. Samples may run from an imported UPM sample or Assets submodule with different paths and without Input System; verify both layouts and isolate adapter references (tasks 6–8).

## Task 1 — reproducible Mixamo imports and inventory

Files: create `Samples~/Sandbox/Editor/AnimatorValidationAssets.cs`, `Samples~/Sandbox/Tests/Editor/AnimatorValidationAssetTests.cs`, `Samples~/Sandbox/Humanoid/Mixamo/` and motion inventory; preserve `Sources~/Mixamo_Bots100` as private input.

Interfaces: `AnimatorValidationAssets.ImportSources()` imports deterministically named FBX under `SandboxPaths.Root + "Humanoid/Mixamo/"`; `ImportFrom(string sourceRoot)` also records source path/hash, model/avatar status, take length/frame rate/root/loop configuration and semantic role. Keep source variants distinct. Source skeleton's Avatar is created from source; target bald male Avatar remains the playback target.

- [x] Write inventory/import tests: actual source variants map to unique assets; valid human clips, finite positive lengths, correct loop/action settings; missing input yields an actionable error without deleting existing assets.
- [x] Run red through Coplay EditMode tests; implement importer using verified ModelImporter APIs; import and record all usable motions, defer controller selection of approximately 25–40 useful clips to task 6.
- [x] Run asset tests and inspect imported Avatar diagnostics. Commit assets, provenance and tests; never force-add private sources.

## Task 2 — real Humanoid blend accuracy gate

Execution amendment (2026-11-06, after user selected exact Humanoid first): raw muscle blending plus public unbound `HumanPoseHandler` reproduces the native oracle. Add an opt-in `PreciseMuscles` bake/runtime path with an ECS-native data model and a separate `AnimationSystemDOTS.Humanoid` adapter assembly. The native retarget call is managed/main-thread, with no runtime GameObject/Animator/PlayableGraph; measured isolated 1000-target retarget cost 4.09 ms median / 4.70 ms p95, not a scene benchmark. Preserve the existing Burst target-TRS path for old samples. Do not ship a learned approximation of Unity's body-orientation correction as an exact replacement. Measure the complete ECS path before any performance claims.

Files: create `Tests~/Editor/HumanoidRuntimeParityTests.cs`, archived exploratory probes under `Tools~/Probes` and generated reports under `DevProject~/Artifacts/Verification/AnimatorValidation/`.

Interfaces: capture target-local and model-space poses at explicit clip times and graph weights using native manual PlayableGraph in a preview scene; compare with the existing baked-TRS sampler/blender, dispose graphs/scenes/blobs in finally.

- [x] Add failing native differential cases for single-clip baseline, 1D two-way, multi-way directional, upper-body masked override and additive reference pose, using imported idle/run/wave/punch motions.
- [x] Record max world joint distance and local rotation angle over significant/all target bones: investigative boundary 0.02 m / 2 degrees; preserve tighter existing baseline tolerances.
- [x] Establish whether errors come from sampling rate, quaternion mixing, additive reference or retargeting space. Fix the responsible representation/algorithm and pin with regression tests. If a substantial representation redesign is needed, ledger the evidence and revise dependent tasks before continuing; do not relax thresholds or claim parity.
- [x] Verify no leaked previews/graphs and commit the passing fixture with measured results.

## Task 3 — versioned motion trees and native weight solvers

Files: extend `Runtime/Controller/AnimatorControllerBlob.cs`, `Editor/Baking/ControllerBakePipeline.cs`; create focused `ControllerMotionSolver.cs`, `ControllerMotionBake.cs`, `Tests~/Editor/ControllerBlendTreeTests.cs`, `Tests~/Runtime/ControllerMotionTests.cs`.

Interfaces: motion node index on each state, flattened child arrays with parameter IDs/thresholds/2D positions/time scales/cycle offsets; `ControllerMotionSolver` is unmanaged and allocation-free, produces clip requests using a caller-owned scratch buffer. Maintain normalized state-time semantics and dynamic effective duration.

- [x] Add native weight/duration oracle tests for 1D, Simple Directional, Freeform Directional, Freeform Cartesian and Direct, plus nested trees, child speed/cycle offset and Direct normalization. Include outside-hull/boundary/origin/duplicate-position inputs and invalid parameters.
- [x] Observe rejection/failure with version 1; implement serialization/validation and each solver against native oracle, not assumed formulas. Avoid per-frame managed allocations.
- [x] Integrate request generation with existing single-layer advancement and verify all prior controller tests, exact tick/seek/replay and loop boundary rules.
- [x] Commit motion trees after all five modes have explicit evidence; report unsupported degenerate authoring configurations precisely.


Execution limits: Direct requires Normalize Blend Values; partial and zero weights follow native behavior. Simple Directional requires directions surrounding the origin or one nonzero direction; duplicate/open/collinear layouts are rejected with alternatives. Native grid evidence covers the supported subset, not every degenerate Mecanim configuration.

## Task 4 — layered pose evaluation and masks

Files: extend graph blob and baking, `Runtime/Controller/ControllerAdvanceSystem.cs`, `Runtime/Poses/AnimationBlendSystem.cs`, `Editor/Baking/ClipBakePipeline.cs`; create `ControllerLayerRuntime.cs`, `ControllerMaskBake.cs`, `Tests~/Editor/ControllerLayerTests.cs` and runtime layer tests.

Interfaces: dynamic per-entity layer states/weights, rig-bound mask definitions and source additive reference poses; exclusive controller request writer, immutable shared graph. Base layer remains compatible with existing queries or documented explicit layer overloads.

- [x] Add native tests for override/additive order, partial masks, Humanoid body parts and transform masks, zero/intermediate weights, authored additive reference different from rest, multiple quaternion blends and synced layer timing.
- [x] Implement from failing tests; invalidate bake caches for mask, reference and motion dependencies. Validate finite layer weights and rig compatibility.
- [x] Add rebake/same-count parameter layout and repeated tick/replay regression tests, plus real Humanoid fixtures from task 2.
- [x] Verify runtime tests and old samples, then commit.

## Task 5 — hierarchical machines and transition interruption

Files: split machine conversion and runtime transition logic into focused files alongside existing controller files; extend `Tests~/Editor/ControllerConversionTests.cs`, create `ControllerHierarchyTests.cs` and `ControllerInterruptionTests.cs`.

Interfaces: immutable machine paths/entry/exit links and layer roots; interruption source/ordered flags, bounded transition history per entity/layer; parameter APIs retain typed validation and trigger semantics.

- [x] Write native fixed-step traces for nested Entry/Exit, AnyState priority, state-machine transitions, self transitions, fixed/normalized duration, offsets, current/next/all interruption orders, speed/cycle/time parameters and synced layers/timing.
- [x] Implement each path from observed failing traces. Preserve accepted trigger consumption and prior negative speed/ExitTime behavior.
- [x] Test lifecycle/reset/rebake and old single-layer fixtures, document supported features/precise rejections. Commit.

## Task 6 — one rich shared controller and native GO player

Files: create `Samples~/Sandbox/Editor/AnimatorValidationFixtures.cs`, `Fixtures/Bots100.controller`, native/ECS prefab variants, `Runtime/AnimatorValidation/CharacterMotor.cs`, `AnimatorValidationPlayer.cs`, optional `Input/AnimationSystemDOTS.Sandbox.Input.asmdef` adapter and scene `Scenes/05_AnimatorGO.unity`.

Interfaces: unmanaged `CharacterMotor.Step` consumes fixed input/motor settings/delta and outputs transform/ground/jump state; GO wrapper uses that result plus shared parameter IDs. One editable controller defines locomotion trees, jump/action variants, nested machine and masked upper-body override/additive demonstrations. Explicit debug mode covers tree types that do not fit normal locomotion.

- [x] Write deterministic motor tests (diagonal speed, sprint, jump/landing, bounds, repeated input trace); run red and implement shared motor.
- [x] Generate the controller from real imported roles, approximately 25–40 useful motions if usable; validate conversion with all features introduced in tasks 3–5. Add asset identity and transition coverage assertions.
- [x] Create GO scene with camera, directional light, simple arena, WASD/Shift/Space/mouse and action/reset controls; UI shows state, layers, parameters and debug mode. Native Animator owns pose, motor owns root.
- [x] Play-test and capture evidence. Commit scene, controller, adapter and tests.

## Task 7 — ECS player and 100 autonomous bots

Files: create `Runtime/AnimatorValidation/CharacterMotorSystem.cs`, `CharacterBotSystem.cs`, camera/diagnostic view, authoring/bakers, `Scenes/06_AnimatorECS.unity`, `Scenes/07_AnimatorBots100.unity` and content SubScenes; create sandbox integration tests.

Interfaces: ECS entities use the same graph/model/motor math, per-instance parameters and fixed-seed bot state. Camera/input bridge only writes input data; all character state/motion/pose is ECS. Bots use goals/local separation, jumps/actions and explicit death-respawn reset.

- [x] Write failing integration tests: exactly 1/100 active entities, graph definition shared once, finite successful palettes, no runtime Animator/PlayableGraph, independent deterministic bot behavior and respawn.
- [x] Implement authoring, shared motor jobs, bot steering and input bridge; build scenes with same arena and visual comparison camera presets.
- [x] Replay identical input trace in GO/ECS: compare motor transforms/clock separately from bone pose. Exercise all action categories, layer weights and debug tree modes.
- [x] Play-test both scenes and record warm-up/frame statistics for 100 complex-graph bots separately from old 1000 simple-graph benchmark. Commit.

## Task 8 — full regression, consumption layouts and manual verification

Files: update `Documentation~/SupportMatrix.md`, manual test document, README/sample docs, approved design status and this checklist.

- [ ] Run complete Editor/Sandbox and Runtime suites, old 02/04 regression, SubScene reload/rebake/despawn and Domain Reload disabled. Save result XML/pose error reports.
- [ ] Verify clean UPM sample consumer and Assets/submodule consumer, including core/autonomous sample without Input System and informative adapter absence behavior.
- [ ] Capture visible standalone Player benchmark/screenshots for the new scenes; distinguish measurements from untested configurations.
- [ ] Document exact controls, three scene paths, imported motions, supported graph features/limits, manual comparison steps and ragdoll feasibility. Never mark stage 5 complete while mandatory features fail.
- [ ] Obtain fresh whole-change review, fix material findings with regression tests, rerun affected checks, commit on master. No push.

## Execution notes

- 2026-11-06: user approved design and requested immediate implementation. Inline execution chosen to keep dependent runtime representation and oracle work together; no extra approval gate.
- Initial workspace contains untracked root `ProjectSettings/` created by opening the package root as a Unity project. Preserve it; use the existing canonical `DevProject~` host for verification.

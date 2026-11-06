# Support matrix

Version: 0.1.0 development. The development roadmap is summarized in the repository README; the detailed local implementation plan is not shipped. No released animation runtime is claimed.

`unsupported` below means unavailable in the current build. Mandatory planned features remain open until their acceptance checks pass.

| Area | Status | Planned verification |
|---|---|---|
| Project dependencies and Coplay MCP | implemented | Live target project/version, lockfile, compilation |
| Reference inventory | implemented | HTML SHA-256; 349 visible paths / 878 declared |
| Isolated Generic Editor oracle | implemented | OracleCaptureTests/OracleFixtureTests: 10/10; native event/cleanup gate: 1/1; see Oracle.md |
| Generic rig, clip/library baking and cache | experimental | Full Editor suite 37/37; public SubScene load, live rebake/removal, cold-Editor disk reuse; mesh/mask/controller data are completed in their consuming stages |
| ECS CPU playback and blending | experimental | 31/31 Runtime + 40/40 Editor; explicit ticks, sampling/blending/masks, affine poses, prefab/two-World/no-domain-reload lifecycle; see Runtime.md |
| Entities Graphics skinning | experimental, stage 3 accepted | 35/35 Runtime + 51/51 Editor; stock buffers, animated roots, per-mesh affine palettes, bounds, live rebake/removal and SubScene reload; D3D12 Player verifies edge/shadow-only visibility and classic renderer LOD; depth/normals/tangents accepted by user comparison with native SkinnedMeshRenderer; see Rendering.md |
| Controller state machine | experimental, stage 4 subset | Single-layer AnimatorController conversion, typed parameters, Entry/Exit/AnyState, ordered conditions and timed crossfades; 102/102 Editor+Sandbox and 45/45 Runtime, native discrete/pose traces and live rebake/removal; exact constraints in Controllers.md |
| Five blend-tree modes | experimental, stage 5 subset | Native parameter-grid weights/clock/Generic poses, nested trees and child timing; real Humanoid 1D/Direct partial and zero weights; conservative authoring limits in Controllers.md |
| Controller layers and masks | experimental, stage 5 subset | Override/additive conversion, embedded rig masks, per-layer clocks/weights, partial/zero Direct history, explicit references and synced timing; Generic and real Humanoid native comparisons |
| Controller hierarchy and phase parameters | experimental, stage 5 subset | Nested Entry/Exit, state/machine transitions and ancestor Any State priority; Float Motion Time/Cycle Offset; native clocks and Generic tree-transition quaternion groups verified. Current/Next interruption, both priority orders and ordered Any State queues have native differential coverage. |
| Shared GO/ECS/bot Animator samples | experimental, verified subset | 34 clips, 21 states, 3 layers, five tree modes; complete 60-second native/ECS trace <=7.611 mm / 0 degrees; streamed 1/100 actors, finite palettes, death/respawn; final 263 Editor/Sandbox + 54 Runtime green; clean UPM/Assets without Input System; D3D12 Player100 median3.28ms/p95 5.91ms on RTX4060Ti; scope and controls in AnimatorValidation.md |
| Humanoid target-specific Editor bake | experimental, partial stage 6 | Imported muscle clips retarget through a valid target Avatar into local TRS; 1000-instance cleaned Workout scene, bind-pose/facing regression, Blender joint reference, native oracle and second-proportion Avatar; see RenderingChecks.md |
| Precise Humanoid muscle blending | experimental, opt-in | Dense native Mixamo comparisons: <=1.563 mm ordinary/masked and 14.533 mm full additive, measured 0 degrees; managed native Avatar conversion without GO/Animator/PlayableGraph, Burst sampling/blending; Editor+Sandbox 123/123 and Runtime 45/45 at the pose-backend checkpoint; SubScene reload and additive cache ownership covered; complete graph acceptance remains open; see Humanoid.md |
| Root motion extraction/application | unsupported | Crowd uses in-place planar playback, retaining authored turns and height; movement integration remains stage 6 |
| Events, scalar curves, blend shapes, exports | unsupported | Interval traversal, lifecycle; stage 7 |
| IK and procedural constraints | unsupported | Analytic and singular configurations; stage 8 |
| Animation visibility/LOD scheduling | unsupported | Reactivation, time/event continuity; stage 9. Classic renderer mesh selection is verified separately in stage 3. |
| GPU pose engine | unsupported | Optional stage 10; no CPU readback workaround |
| Physics and Netcode | unsupported | Separate optional packages; stage 11 |
| Player build, diagnostics, delivery | unsupported | Stage 12 |
| URP deformation motion vectors | unsupported | Separate GPU capture required |
| Multiple rendering Worlds | unsupported, reproduced | Stock global deformation buffer couples Worlds despite independent CPU palettes; public-API repro and commands in RenderingChecks.md |
| Linux/Vulkan and macOS/Metal | unsupported | Platform-specific runs required |

Legacy Animation, per-character Animator/PlayableGraph runtime, arbitrary MonoBehaviour/object-reference curves, StateMachineBehaviour callbacks, Animator IK Pass and automatic Timeline/Animation Rigging/VFX Graph compatibility are outside the initial release contract. Editor Playables are verification tools. The opt-in precise Humanoid adapter uses the public unbound Avatar pose solver at runtime; that conversion is managed/main-thread, not Burst.

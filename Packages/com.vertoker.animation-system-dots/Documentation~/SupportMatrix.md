# Support matrix

Version: 0.1.0 development. The implementation plan is `PLAN.md` at the project root. No released animation runtime is claimed.

`unsupported` below means unavailable in the current build. Mandatory planned features remain open until their acceptance checks pass.

| Area | Status | Planned verification |
|---|---|---|
| Project dependencies and Coplay MCP | implemented | Live target project/version, lockfile, compilation |
| Reference inventory | implemented | HTML SHA-256; 349 visible paths / 878 declared |
| Isolated Editor oracle | experimental | OracleCaptureTests; stage 0 |
| Generic rig and clip baking | unsupported | RigBakingTests, ClipBakingTests, BlobCacheTests; stage 1 |
| ECS CPU playback and blending | unsupported | ScriptedPlaybackTests, PoseSamplingTests; stage 2 |
| Entities Graphics skinning | unsupported | Offscreen rendering, palettes, roots, multi-slot mesh; stage 3 |
| Controller state machine | unsupported | Parameters, transition traces; stage 4 |
| Layers, masks, five blend-tree modes | unsupported | Differential controller tests; stage 5 |
| Humanoid and root motion | unsupported | Avatar-specific bake and movement integration; stage 6 |
| Events, scalar curves, blend shapes, exports | unsupported | Interval traversal, lifecycle; stage 7 |
| IK and procedural constraints | unsupported | Analytic and singular configurations; stage 8 |
| Visibility, LOD, scheduling | unsupported | Reactivation, time/event continuity; stage 9 |
| GPU pose engine | unsupported | Optional stage 10; no CPU readback workaround |
| Physics and Netcode | unsupported | Separate optional packages; stage 11 |
| Player build, diagnostics, delivery | unsupported | Stage 12 |
| URP deformation motion vectors | unsupported | Separate GPU capture required |
| Multiple rendering Worlds | unsupported | Separate global-buffer compatibility checks required |
| Linux/Vulkan and macOS/Metal | unsupported | Platform-specific runs required |

Legacy Animation, per-character Animator/PlayableGraph runtime, runtime Avatar retargeting, arbitrary MonoBehaviour/object-reference curves, StateMachineBehaviour callbacks, Animator IK Pass and automatic Timeline/Animation Rigging/VFX Graph compatibility are outside the initial release contract. Editor Playables are verification tools, not an ECS runtime.

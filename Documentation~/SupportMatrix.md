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
| Controller state machine | unsupported | Parameters, transition traces; stage 4 |
| Layers, masks, five blend-tree modes | unsupported | Differential controller tests; stage 5 |
| Humanoid target-specific Editor bake | experimental, partial stage 6 | Imported muscle clips retarget through a valid target Avatar into local TRS; 1000-instance cleaned Workout scene, bind-pose/facing regression, Blender joint reference, native oracle and second-proportion Avatar; see RenderingChecks.md |
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

Legacy Animation, per-character Animator/PlayableGraph runtime, runtime Avatar retargeting, arbitrary MonoBehaviour/object-reference curves, StateMachineBehaviour callbacks, Animator IK Pass and automatic Timeline/Animation Rigging/VFX Graph compatibility are outside the initial release contract. Editor Playables are verification tools, not an ECS runtime.

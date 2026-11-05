# Editor oracle

`UnityAnimationOracle.Capture(in OracleCaptureRequest)` samples a cloned hierarchy through a manual Unity PlayableGraph. A clip and a controller are mutually exclusive inputs. Times are explicit, finite, non-negative and non-decreasing. Clip-only capture currently stays within one clip duration; loop/reverse interval baselines are later work.

Pose/controller capture uses a preview scene. The source hierarchy and source clips are not animated or edited. Animation curves are inspected on private clip copies. Duplicate bone names are addressed by full relative path. Results contain local/world matrices, controller current/next state hashes, transition flags, normalized state times, parameter values, scalar binding values, Unity/converter versions and an optional serialized validation-settings snapshot.

For a Generic rig, `RootMotionPath` identifies the transform whose measured motion is captured. Translation is expressed in the previous root orientation; rotation is the relative quaternion. The first frame has zero translation and identity rotation. This is a Generic hierarchy-delta baseline; it does not yet prove Humanoid root-motion extraction.

`CaptureEvents=true` explicitly requests native AnimationEvent dispatch and requires Play mode. Unity suppresses native events in preview scenes, so this capture uses an isolated additive runtime scene instead. Event callbacks on copied clips are redirected to a transient relay; original callbacks are never invoked. Event definitions retain source callback/payload metadata and frame traces contain indices into that table. `EventsCaptured=false` means pose-only capture, not an empty native event baseline. Integer controller parameters are currently also projected into the float diagnostic array; use the later typed controller checks for exact large-integer semantics.

The graph is stopped and destroyed and temporary assets/objects are destroyed in `finally`. Preview scenes close synchronously. Native graph destruction and runtime scene unloading are deferred by Unity until a player-loop frame. EditMode tests verify stopped graphs and unchanged open scenes; the post-run check verifies zero remaining oracle graphs/runtime scenes.

The initial fixtures contain no gameplay behaviours. This oracle is a development verification tool; it is not an isolation sandbox for arbitrary user scripts or StateMachineBehaviour side effects.

Fixtures live in `Assets/AnimationSystemDOTS.Sandbox/Fixtures`. Use `Tools > Animation System DOTS > Create Missing Validation Fixtures` to create the initial assets. Existing fixture prefabs are preserved. Fixture materials use ordinary URP Unlit for the oracle; they do not yet demonstrate ECS compute deformation.

Tests: `OracleCaptureTests` and `OracleFixtureTests` in `AnimationSystemDOTS.Tests.Editor`. The native-event test enters and exits Play mode, and asserts zero owned graphs/scenes after a real player-loop frame before domain teardown. Run the full suite, then run that native-event test alone as the final cleanup gate: ordered NUnit methods otherwise execute ahead of unordered methods, so later Edit-mode captures may have legitimately deferred graphs. Verify nonzero XML counts and then query the live Editor for leftover oracle resources.

Verified 2026-11-05: full suite 10/10, final native-event/cleanup gate 1/1; post-run owned graph/scene counts are zero and SampleScene remains clean. `Artifacts/Verification` holds XML and `GenericFixtureBaseline.json` (five frames, eight paths, exact validation-settings metadata).

Read-only review found and tests reproduced silently ignored out-of-range parameter sample indices; those are now rejected before any scene allocation. Two deferred test improvements remain: failure cleanup after graph creation (the current failure fixture throws before it), and a dedicated assertion of in-progress transition fields for nonzero-duration transitions.

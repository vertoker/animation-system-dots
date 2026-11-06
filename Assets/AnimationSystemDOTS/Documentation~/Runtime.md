# CPU playback and poses — stage 2

`AnimationRigAuthoring` plus explicit bake settings produces immutable rig/library references and per-entity playback, command, request, scratch and pose buffers. Core jobs use Entities, Collections, Mathematics and Burst; they create no bone GameObjects, Animator or PlayableGraph and do not read static Unity time.

The host supplies exactly one `AnimationStepContext` singleton per World before `AnimationSimulationSystemGroup` updates. Set `Tick`, finite nonnegative `DeltaTime`, `Mode` and `Revision`. Normal ticks must increase; repeated or older normal ticks preserve state and leave queued commands pending. A new Seek revision reevaluates the same tick without advancing time. Replay is an explicit evaluation mode; the caller restores its authoritative state before replaying. Snapshot/prediction integration is stage 11.

For graphs authored in Unity's Animator window, add `AnimationControllerAuthoring` instead of submitting scripted commands. Its selected driver is a parallel Burst state machine sharing immutable definitions across instances. See [controller setup, APIs, supported subset and 1000-instance scene](Controllers.md). Both drivers consume this same explicit clock; `AnimationPlaybackMode` gives requests to exactly one of them.

Queue commands on each rig's `ScriptedPlaybackCommand` buffer before the animation group:

```csharp
var commands = entityManager.GetBuffer<ScriptedPlaybackCommand>(rigEntity);
commands.Add(ScriptedPlaybackCommand.Play(new ClipId(0), sequence: 1));
commands.Add(ScriptedPlaybackCommand.SetSpeed(-1, sequence: 2));
```

Clip IDs are local indices of that rig's library. Sequences increase monotonically per rig, including rejected commands. The player stably sorts each submitted buffer, consumes it once and rejects duplicate/stale sequences. Inspect `ClipPlaybackState.LastCommandResult` for the last result; invalid IDs/values preserve playback. Initialize manually created rigs with `ClipPlaybackState.Initial`.

Play resumes playback and preserves player speed. CrossFade transitions from the current clip over a finite nonnegative duration; zero duration switches immediately. Its duration follows simulation seconds, independently of speed magnitude/sign. Pause stops clip and fade progress while the simulation clock continues. Seek sets previous/current time together and cancels an active fade; it does not advance by that update's delta. Nonlooping state clamps to endpoints and can reverse immediately from the end. Looping time stays unwrapped in double precision; the sampler uses signed remainder, including negative and extremely large finite times. Positive exact loop boundaries preserve the authored endpoint.

This scripted player keeps two clip instances during a fade. A further CrossFade takes the current incoming clip as its new source; it does not snapshot an arbitrary partially blended pose. Controller transition interruption remains planned for stage 5. Events/root motion remain planned for their stages.

The default Simulation order is playback → sample → blend → `AnimationPoseModificationSystemGroup` → model pose, before `TransformSystemGroup`. Each job owns one rig; bones propagate in topological order inside that job. `BoneModelPose.Matrix` is a full rig-space affine `float3x4`, preserving shear from rotated nonuniform scales. Rendering and bone export consumers are introduced in stage 3.

Sampling scratch is request-major, then bone-major, and belongs to the rig entity. Layers blend in ascending integer order. Within an override layer, positive effective weights form a normalized pose; weight below one leaves a remainder of the preceding pose, starting from rig rest pose. Zero contributions retain rest/previous layers. Quaternion interpolation uses normalized shortest-path slerp in stable request order. Additive contributions follow override contributions within each layer, in producer order: position difference, reference-relative quaternion delta, and multiplicative scale ratio. Signed scales are preserved. Rig-bound masks multiply weights; wrong rig/schema/length or nonfinite mask entries contribute zero. `BoneMaskBlob` consumption exists; Unity AvatarMask conversion remains stage 5.

Baked blobs belong to Unity's Baker/blob store. Runtime systems never dispose them. For runtime-created allocations, explicitly create `RuntimeAnimationBlobOwnerSystem` and call `Adopt(ref rig, ref library)` on the main thread; successful transfer clears caller handles. It deduplicates allocations within the World and releases them after ECS jobs complete during teardown. Register non-ECS job readers through `RegisterReader`. Borrowed references must not be disposed by consumers. No static cross-World Entity maps or shared mutable pose pools exist.

Verification includes command edge cases, sampling seams/double precision, analytical override/additive TRS, fractional masks, full affine shear, independent rigs, despawn/reuse, actual ECS prefab instantiation, two Worlds, and teardown with an unfinished external blob reader. Scheduling comparison restores initial state and commands, varies workers (1/4) and chunk capacity (many rigs/one rig), then compares playback state and poses. The native-oracle comparison uses the default Simulation group with fully bound Generic TRS at endpoints, samples and interior times; maximum matrix element error measured 2.38418579e-7. Missing-channel rest fallback is this package's explicit contract and has separate analytical coverage.

Verified 2026-11-05: **31/31 PlayMode Runtime tests, 40/40 EditMode tests**. Two Play sessions with Domain Reload disabled retained a domain marker and automatically disposed owned Worlds/jobs without native leak errors while stack-trace leak detection was enabled. Editor preferences were restored. Evidence: `Artifacts/Verification/stage2-runtime-final-green.xml`, `stage2-editor-final-green.xml`.

# AnimatorController source, ECS runtime

The Unity Animator window is the graph editor. An ordinary `.controller` asset is converted during baking into an immutable `AnimatorControllerBlob`. Runtime state, parameters and time belong to each rig entity. `ControllerAdvanceSystem` evaluates states and transitions in a parallel Burst job and writes requests for the existing sampling, blending and skinning pipeline. It creates no runtime Animator, PlayableGraph or bone GameObjects.

## Set up a rig

1. Add `AnimationRigAuthoring` to the rig root and assign `AnimationBakeSettings`. Generic clips must match its transform paths. Humanoid clips require a valid target Avatar on the authoring Animator, as with ordinary Humanoid clip baking.
2. Add `AnimationControllerAuthoring` and assign the `.controller` asset. `AnimationLibraryAuthoring` is optional: controller clips are collected automatically. Explicit library clips, when provided, retain their prefix IDs; missing controller clips are appended.
3. Open the asset in Unity's Animator window. Use AnimationClip or Blend Tree states and optional override/additive layers with Avatar Masks. Disable **Write Defaults** on every state and **IK Pass** on the layers. Direct trees require **Normalize Blend Values**; additive clips require an explicit **Additive Reference Pose**.
4. Put the rig/prefab into a SubScene and instantiate its baked entity. Baking selects `PlaybackDriver.Controller`, initializes parameters from the asset defaults and registers the immutable controller with Unity's blob store.
5. Supply one `AnimationStepContext` singleton per World before `AnimationSimulationSystemGroup`, using your simulation's increasing tick and finite nonnegative delta. The package does not read Unity's static time. The sample supplies this clock itself.

The authoring Animator supplies the Avatar when needed; assigning its native `runtimeAnimatorController` is unnecessary. The `.controller` reference belongs to `AnimationControllerAuthoring`.

Different rigs/target Avatars produce rig-specific baked libraries and controller references. Instances of the same baked prefab share one immutable graph/library while keeping independent mutable buffers. Never dispose baked blobs from gameplay code.

## Parameters and state queries

Resolve names at setup and retain local parameter IDs in gameplay components. Names use exact UTF-8 comparison, rather than unchecked hash equality. IDs belong to that controller's parameter table and must be resolved again if the controller definition changes.

```csharp
var controller = entityManager.GetComponentData<AnimationController>(rigEntity);
var values = entityManager.GetBuffer<AnimationParameterValue>(rigEntity);
var speed = AnimationApi.FindParameter(controller, new FixedString128Bytes("Speed"));
var workout = AnimationApi.FindParameter(controller, new FixedString128Bytes("Workout"));

var result = AnimationApi.SetFloat(controller, values, speed, 1.2f);
AnimationApi.SetBool(controller, values, workout, true);
// SetInt, SetTrigger and ResetTrigger use the same controller/buffer/ID arguments.

var layer = entityManager.GetComponentData<ControllerLayerState>(rigEntity);
if (ControllerStateQuery.TryGet(layer, out var state))
{
    // state.CurrentState / NextState are local indices; NextState is -1 when settled.
    var path = controller.Definition.Value.States[state.CurrentState].Path;
    // state.NormalizedTime, NextNormalizedTime, IsInTransition, TransitionProgress
}
```

Parameter setters are usable inside Burst jobs. They return `AnimationParameterResult` and leave values unchanged for invalid IDs, types, layouts or nonfinite Float input. `ControllerLayerState.Result` reports evaluation failures, including a missing/mismatched library or directly corrupted parameter data. Failed evaluation clears requests and the pose pipeline falls back to rest.

The controller and scripted driver check `AnimationPlaybackMode`; only the selected driver owns requests and the clock. Scripted commands queued on a controller rig remain pending. Mode switching is explicit low-level control; clear unwanted queued commands and initialize/restore the selected driver's state yourself.

Layer weights and snapshots belong to the rig entity:

```csharp
var layers = entityManager.GetBuffer<ControllerLayerRuntime>(rigEntity);
var upper = AnimationApi.FindLayer(controller, new FixedString128Bytes("Upper"));
var result = AnimationApi.SetLayerWeight(controller, layers, upper, 0.7f);
ControllerStateQuery.TryGet(layers, upper, out var upperState);
```

The baker initializes layer buffers; programmatically created controller entities receive missing buffers on their first update. Set weights after that initialization, or create/populate the buffers yourself. The base layer always has weight one. Invalid IDs, nonfinite/out-of-range weights and mismatched buffer layouts return `AnimationLayerResult` without changing data. `ControllerLayerState` remains the authoritative base-layer component and mirrors buffer element zero.

## Supported graph subset

| Feature | Contract |
|---|---|
| Parameters | Float, Int, Bool, Trigger; defaults come from the asset. Float conditions use Greater/Less; Int additionally supports Equals/NotEqual; Bool/Trigger use If/IfNot. |
| State motions | AnimationClip or nested Blend Trees, Generic or target-baked Humanoid; finite constant speed, Float speed multiplier, constant or Float cycle offset, Float Motion Time. |
| Blend Trees | 1D, Simple Directional 2D, Freeform Directional 2D, Freeform Cartesian 2D and normalized Direct. Dynamic effective duration, child clip speed/reversal and cycle offsets. |
| Layers | Ordered override/additive layers, per-instance weights, rig-bound transform masks and Humanoid body-part masks. Additive references come from source clip settings. |
| Synced layers | Motion overrides and optional timing influence from an earlier source layer; timing responds to changing layer weights. Unity canonicalizes chained sync targets to their source. |
| Entry | Default state plus serialized conditional Entry transitions; accepted Entry triggers are consumed. |
| Transitions | Serialized order, all conditions must pass, Any State before current-state edges, self transitions and root Exit/re-entry. |
| Timing | Exit Time, repeated loop gates, values above one, fixed-seconds or normalized duration, destination normalized offset; source speed changes affect normalized fade progress. |
| Interruption | None, Current State, Next State and both priority orders; ordered interruption stops the complete candidate queue at the active edge, including Any State. None keeps the fade atomic. |
| Trigger lifetime | Consume only conditions of an accepted transition; failed/deferred transitions retain triggers. Accepted triggers remain visible to all layers during the step and clear after layer evaluation. |
| Clock | Repeated/older normal ticks do not advance or consume inputs. A new Seek revision evaluates with zero delta. Replay requires caller-restored authoritative state. |
| Loop boundaries | Controller requests use native forward-loop start / reversed-loop end selection. Scripted requests retain their existing authored-endpoint policy. Times remain unwrapped. |

**Every state clip must have the same authored curve-property layout.** The standalone baker completes absent transform groups with rest values. Native Write Defaults=false can preserve previously animated properties when another state does not bind them; that history is not represented by these complete baked poses. The converter rejects differing layouts with the exact asset/state path. This is deliberately conservative: author clips with a common set of animated properties. The included Walk and Workout assets both provide the same 130 Humanoid bindings.

Unsupported settings produce diagnostics rather than being ignored: empty motions or state machines, override controllers, StateMachineBehaviour callbacks, IK, mirror, Write Defaults=true. Root motion, animation events and animated scalar/object-reference properties remain outside this controller milestone. Moving Humanoid additive sources with Root enabled are rejected when Unity reports nonzero average root velocity/rotation: use an in-place source or disable Root in the layer mask. Names and full state paths are limited to 125 UTF-8 bytes.

Nested state machines preserve full layer/machine/state paths, conditional Entry, state-to-machine transitions and machine exits. Ancestor Any State transitions take priority over inner machines and state transitions. If a machine has outgoing conditional links and none match, Unity selects the parent default; an explicit machine Exit propagates through parent exits instead. Eleven native differential cases cover these paths, including deep Entry and nested defaults. This remains a tested subset of Mecanim; the three new validation scenes are under implementation.

Motion Time selects a normalized sample phase without replacing the autonomous state clock or Exit Time gate. A Cycle Offset parameter replaces the authored constant and is clamped to [0,1]. Cycle offsets are ignored by nonlooping clip leaves, preserving their reversal and Motion Time phase, matching the native Animator. Parameters must be existing Floats; nonfinite values fail through the usual parameter validation.

## Blend Tree constraints and timing

Motion graph version 5 stores post-order nodes, child edges, layer and machine definitions, state phase parameters and owned embedded mask arrays. Repeated clip occurrences remain separate so their timing is independent. Each entity owns a reusable `ControllerMotionValue` scratch buffer; it is baked with the rig or added once for programmatically created controller entities. Weight/duration evaluation and leaf request generation run inside the controller Burst job without per-frame managed allocations.

All tree parameters must be Float. Duplicate 1D thresholds and duplicate 2D positions are rejected. Simple Directional permits one nonzero direction or a set surrounding the origin with every angular gap below 180 degrees; multiple speeds along one ray require Freeform Directional. Open/collinear Simple Directional layouts are rejected with 1D/Cartesian guidance because their native boundary behavior is not covered by this solver contract. Zero/nonfinite child time scales, mirrors, empty trees and cyclic references are rejected when present in source data. Unity's authoring setter may already sanitize some invalid values before conversion.

Direct requires **Normalize Blend Values**. Unity normalizes sums above one, retains partial influence below one, and preserves the previous pose when all weights are zero with Write Defaults disabled. The ECS pipeline follows that behavior; the scripted driver's empty-request fallback still restores rest. Unnormalized Direct is rejected: extrapolating authored properties while retaining unanimated channels needs a binding-aware representation beyond complete baked poses.

Tree duration follows current parameters, including during normalized transitions. A normalized fade integrates against the source's current effective duration; fixed fades remain measured in seconds. Child speed/cycle-offset settings apply to leaf clips. In the tested Unity version those settings on a nested Blend Tree child do not alter its output, which the converter preserves.

Native differential fixtures cover 289 parameter points per configuration, all five tree modes, multiple speeds on one direction, missing Idle, unequal radii, irregular Cartesian positions, nesting, reversal and cycle offsets. They compare leaf weights, normalized clocks and actual Generic ECS sampled poses. Real Humanoid controller checks cover 1D and Direct partial/zero inputs: maximum measured joint errors were 0.936 mm and 1.008 mm respectively, with 0 degrees measured local rotation error. These are accuracy checks, not performance or complete layer/hierarchy claims.

## Layer pose and state ownership

Controller Generic blending uses hemisphere-correct quaternion means and native layer interpolation. State streams normalize their rotations before transition blending; flattening tree leaves across a fade would produce different rotations. Additive deltas are blended within a layer before applying its weight; scale deltas add across layers. The public scripted `PoseBlendMath` contract retains its existing interpolation behavior. Precise Humanoid layers blend authored muscles and body inputs, with a separate root-owning stream preserving masked hips; see [Humanoid.md](Humanoid.md).

Layer weight is distinct from the sum of leaf weights. Partial Direct output supplies default pose influence inside an override layer; a zero-weight motion retains the last evaluated layer stream. `ControllerCachedRequest` records the last evaluated unweighted requests and intervals. Raw pose histories retain complete layer streams, so other layers and layer weights can keep changing. Layers with weight zero keep advancing clocks without updating their pose history. An empty layer that has never been evaluated contributes nothing. Final requests can therefore include retained contributions even when native current clip-info weights are zero.

Replacing the controller definition resets states, layer weights, cached motion and parameter defaults, including when the parameter count is unchanged. Re-resolve parameter and layer IDs for the new definition. For rollback, restore `AnimationClock`, `ControllerLayerState`, parameters, `ControllerLayerRuntime`, `ControllerCachedRequest` and `ControllerBoneHistory` for Generic rigs, or `HumanoidControllerMuscleHistory` plus `HumanoidControllerBodyHistory` for precise Humanoid rigs; also restore retained pose buffers/components when the final pose is held (the single-base-layer empty-output case). Scratch weights are derived and rebuilt. Replay remains caller-controlled.

Mask data lives inside the controller blob and is deep-copied during conversion; gameplay must keep its owning graph alive while requests reference it. Standalone scripted masks retain their existing ownership. SubScene tests cover mask edits, external additive-reference edits and removal of derived controller buffers. Native fixtures cover Generic layer order, nested/multiway rotations, partial/zero trees, multiple additive layers, nonunit reference scale, shared triggers and synced timing; real Humanoid layered checks measured up to 1.114 mm for masked cases and 6.112 mm in the tested full additive trace, with 0 degrees measured rotation error. Dense full additive acceptance and its wider 14.533 mm maximum remain documented separately.

The layer checkpoint passed **179/179 Editor/Sandbox and 51/51 Runtime tests**, including cache rollback and same-tick definition replacement. Existing scenes 02 and 04 each ran 1000 rigs with 3000 successful finite skin palettes and zero native Animators after this change. This is regression evidence for those scenes, not a benchmark or completion of the three new scenes.

## Included 1000-controller scene

Import the Validation Sandbox and open `Scenes/02_Controller.unity`. Its `Fixtures/Crowd.controller` has Walk and Workout states, a Bool `Workout`, Float `Speed`, and 0.3-second crossfades. `Fixtures/ControllerHumanoid.prefab` references that graph. Both runtime and these assets are editable inside the same repository/submodule; UPM imports place a writable sample copy under Assets.

Automatic mode changes the Bool independently for each of 1000 entities. **All Walk**, **All Workout** and the Speed slider demonstrate direct parameter control; **Close** and **Crowd** change the camera. The scene contains 1000 rigs / 3000 successful finite skin palettes, shares one controller definition, and creates zero native runtime Animators. The sample parameter driver uses a Burst job; the overlay reads diagnostic state periodically.

Verified 2026-11-06 with Unity 6000.6.8f1 and Entities/Graphics 6.6.0: **102/102 Editor+Sandbox tests**, **45/45 Runtime tests**. Native manual-step comparisons cover current/next states, normalized times, fade progress and weighted pose, including reverse/cycle offsets, speed changes, root Exit, Entry, priority, triggers and loop boundaries. Public SubScene tests cover controller clip collection, defaults, asset-change rebaking and removal of derived controller data.

Separate configured URP consumers passed both UPM sample import and Assets/submodule-layout import. Each also opened the new controller scene and verified 1000 active controllers, one shared graph, 3000 palettes, zero native Animators, then all 1000 entering Workout and returning to Walk. These additional controller runs used the development host's disabled Domain Reload setting; they are Editor checks, not a Player benchmark.

The isolated 1000-controller test measured **0.0346 ms median / 0.0382 ms p95** over 180 warmed frames in this Editor, including job scheduling/completion and excluding pose sampling, skinning and rendering. This is a small two-state graph benchmark, not a whole-scene FPS claim or a guarantee for arbitrary controllers. Local evidence is under `Artifacts/Verification/Controller/`.

The hierarchy/phase checkpoint passed **201/201 Editor/Sandbox and 51/51 Runtime tests**, including ten nested-machine cases, Motion Time/Cycle Offset and quaternion tree-to-tree fades. That checkpoint preceded interruption support.

Interruption snapshots capture the previous evaluated raw layer pose before masks and layer weights. Generic history stores TRS; precise Humanoid history stores authored muscles and body inputs before retargeting. The reported current-state clock advances once on interruption, then remains frozen until promotion; normalized interrupted fades follow the new destination duration. Bounded pre-step history preserves repeated-tick and replay behavior without growing a list of past transitions. During a fade, an empty Direct source supplies rest and an empty Direct destination reads the prior layer stream; partial nonzero weights retain their usual default-pose behavior.

The interruption checkpoint passed **245/245 Editor/Sandbox and 53/53 Runtime tests**, including ordered Any State queues, repeated interruptions, synced/additive layers, zero Direct during fades, hidden layers, duplicate ticks, replay with pose history and same-tick definition replacement. New-scene gameplay and performance remain separate acceptance steps.

The native comparison sample is now available at `Samples~/Sandbox/Scenes/05_AnimatorGO.unity`: one ordinary Animator and SkinnedMeshRenderers, using the editable `Fixtures/Bots100.controller`. The graph includes 34 distinct clips, 21 states, six machines, all five tree types, nested variation trees, jump/death variants, masked upper-body gestures and an additive reaction. ECS scenes 06/07 use this same controller; see AnimatorValidation.md for the completed native/ECS acceptance trace.

Controls: WASD move, Shift sprint, Alt strafe, Space jump, RMB orbit, 1 wave, 2 punch, 3 reaction, 4 death, R reset, T deterministic trace, Tab tree mode, V variant, C cycle offset and F Motion Time scrub. On-screen buttons also exercise the graph without keyboard input. `AnimatorValidationSettings.asset` exposes gesture strength; defaults are upper-body 0.7, additive 0.2 and airborne influence 0.35. Jump root height is extracted from the clips because the shared motor owns vertical movement.

# AnimatorController source, ECS runtime

The Unity Animator window is the graph editor. An ordinary `.controller` asset is converted during baking into an immutable `AnimatorControllerBlob`. Runtime state, parameters and time belong to each rig entity. `ControllerAdvanceSystem` evaluates states and transitions in a parallel Burst job and writes requests for the existing sampling, blending and skinning pipeline. It creates no runtime Animator, PlayableGraph or bone GameObjects.

## Set up a rig

1. Add `AnimationRigAuthoring` to the rig root and assign `AnimationBakeSettings`. Generic clips must match its transform paths. Humanoid clips require a valid target Avatar on the authoring Animator, as with ordinary Humanoid clip baking.
2. Add `AnimationControllerAuthoring` and assign the `.controller` asset. `AnimationLibraryAuthoring` is optional: controller clips are collected automatically. Explicit library clips, when provided, retain their prefix IDs; missing controller clips are appended.
3. Open the asset in Unity's Animator window. Use one base layer with AnimationClip or Blend Tree states. Disable **Write Defaults** on every state and **IK Pass** on the layer. Use **Interruption Source: None** on transitions. Direct trees require **Normalize Blend Values**.
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

## Supported graph subset

| Feature | Contract |
|---|---|
| Parameters | Float, Int, Bool, Trigger; defaults come from the asset. Float conditions use Greater/Less; Int additionally supports Equals/NotEqual; Bool/Trigger use If/IfNot. |
| State motions | AnimationClip or nested Blend Trees, Generic or target-baked Humanoid; finite constant speed, Float speed multiplier, constant cycle offset. |
| Blend Trees | 1D, Simple Directional 2D, Freeform Directional 2D, Freeform Cartesian 2D and normalized Direct. Dynamic effective duration, child clip speed/reversal and cycle offsets. |
| Entry | Default state plus serialized conditional Entry transitions; accepted Entry triggers are consumed. |
| Transitions | Serialized order, all conditions must pass, Any State before current-state edges, self transitions and root Exit/re-entry. |
| Timing | Exit Time, repeated loop gates, values above one, fixed-seconds or normalized duration, destination normalized offset; source speed changes affect normalized fade progress. |
| Atomic fades | Interruption Source=None only. Unity 6000.6 native regression confirms Any State also waits for the active fade in this configuration. |
| Trigger lifetime | Consume only conditions of an accepted transition; failed conditions and deferred transitions retain triggers. |
| Clock | Repeated/older normal ticks do not advance or consume inputs. A new Seek revision evaluates with zero delta. Replay requires caller-restored authoritative state. |
| Loop boundaries | Controller requests use native forward-loop start / reversed-loop end selection. Scripted requests retain their existing authored-endpoint policy. Times remain unwrapped. |

**Every state clip must have the same authored curve-property layout.** The standalone baker completes absent transform groups with rest values. Native Write Defaults=false can preserve previously animated properties when another state does not bind them; that history is not represented by these complete baked poses. The converter rejects differing layouts with the exact asset/state path. This is deliberately conservative: author clips with a common set of animated properties. The included Walk and Workout assets both provide the same 130 Humanoid bindings.

Unsupported settings produce diagnostics rather than being ignored: additional/synced/additive layers and masks, nested state machines, empty motions, override controllers, StateMachineBehaviour callbacks, IK, mirror, Write Defaults=true, motion-time/cycle-offset parameters and transition interruption. Root motion, animation events and animated scalar/object-reference properties remain outside this controller milestone. Names and full state paths are limited to 125 UTF-8 bytes.

This is a tested subset, not full Mecanim API compatibility. Layers, hierarchy and interruption remain under implementation for the three new validation scenes.

## Blend Tree constraints and timing

Motion graph version 2 stores post-order nodes and child edges. Repeated clip occurrences remain separate so their timing is independent. Each entity owns a reusable `ControllerMotionValue` scratch buffer; it is baked with the rig or added once for programmatically created controller entities. Weight/duration evaluation and leaf request generation run inside the controller Burst job without per-frame managed allocations.

All tree parameters must be Float. Duplicate 1D thresholds and duplicate 2D positions are rejected. Simple Directional permits one nonzero direction or a set surrounding the origin with every angular gap below 180 degrees; multiple speeds along one ray require Freeform Directional. Open/collinear Simple Directional layouts are rejected with 1D/Cartesian guidance because their native boundary behavior is not covered by this solver contract. Zero/nonfinite child time scales, mirrors, empty trees and cyclic references are rejected when present in source data. Unity's authoring setter may already sanitize some invalid values before conversion.

Direct requires **Normalize Blend Values**. Unity normalizes sums above one, retains partial influence below one, and preserves the previous pose when all weights are zero with Write Defaults disabled. The ECS pipeline follows that behavior; the scripted driver's empty-request fallback still restores rest. Unnormalized Direct is rejected: extrapolating authored properties while retaining unanimated channels needs a binding-aware representation beyond complete baked poses.

Tree duration follows current parameters, including during normalized transitions. A normalized fade integrates against the source's current effective duration; fixed fades remain measured in seconds. Child speed/cycle-offset settings apply to leaf clips. In the tested Unity version those settings on a nested Blend Tree child do not alter its output, which the converter preserves.

Native differential fixtures cover 289 parameter points per configuration, all five tree modes, multiple speeds on one direction, missing Idle, unequal radii, irregular Cartesian positions, nesting, reversal and cycle offsets. They compare leaf weights, normalized clocks and actual Generic ECS sampled poses. Real Humanoid controller checks cover 1D and Direct partial/zero inputs: maximum measured joint errors were 0.936 mm and 1.008 mm respectively, with 0 degrees measured local rotation error. These are accuracy checks, not performance or complete layer/hierarchy claims.

## Included 1000-controller scene

Import the Validation Sandbox and open `Scenes/02_Controller.unity`. Its `Fixtures/Crowd.controller` has Walk and Workout states, a Bool `Workout`, Float `Speed`, and 0.3-second crossfades. `Fixtures/ControllerHumanoid.prefab` references that graph. Both runtime and these assets are editable inside the same repository/submodule; UPM imports place a writable sample copy under Assets.

Automatic mode changes the Bool independently for each of 1000 entities. **All Walk**, **All Workout** and the Speed slider demonstrate direct parameter control; **Close** and **Crowd** change the camera. The scene contains 1000 rigs / 3000 successful finite skin palettes, shares one controller definition, and creates zero native runtime Animators. The sample parameter driver uses a Burst job; the overlay reads diagnostic state periodically.

Verified 2026-11-06 with Unity 6000.6.8f1 and Entities/Graphics 6.6.0: **102/102 Editor+Sandbox tests**, **45/45 Runtime tests**. Native manual-step comparisons cover current/next states, normalized times, fade progress and weighted pose, including reverse/cycle offsets, speed changes, root Exit, Entry, priority, triggers and loop boundaries. Public SubScene tests cover controller clip collection, defaults, asset-change rebaking and removal of derived controller data.

Separate configured URP consumers passed both UPM sample import and Assets/submodule-layout import. Each also opened the new controller scene and verified 1000 active controllers, one shared graph, 3000 palettes, zero native Animators, then all 1000 entering Workout and returning to Walk. These additional controller runs used the development host's disabled Domain Reload setting; they are Editor checks, not a Player benchmark.

The isolated 1000-controller test measured **0.0346 ms median / 0.0382 ms p95** over 180 warmed frames in this Editor, including job scheduling/completion and excluding pose sampling, skinning and rendering. This is a small two-state graph benchmark, not a whole-scene FPS claim or a guarantee for arbitrary controllers. Local evidence is under `DevProject~/Artifacts/Verification/Controller/`.

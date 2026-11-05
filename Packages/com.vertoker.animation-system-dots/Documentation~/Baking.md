# Generic baking — stage 1 in progress

Implemented so far: immutable rig definitions, topological parents, rest local TRS, complete UTF-8 paths, FNV-1a path acceleration with byte-for-byte collision checks, schema version and SHA-256 content identity. `RigBakingTests` includes the real `costarring`/`liquid` FNV collision. Zero/non-finite scales, invalid quaternions, ambiguous full paths, over-capacity paths and optimized-away skeletons produce diagnostics.

Bone zero is the rig-space origin, with identity rest pose. The source root's placement/scale belongs to the ECS rig entity and must not be applied twice. Non-root bones retain their authored local TRS, including negative scale.

`RigBakePipeline.Bake(GameObject)` returns a standalone Persistent blob. The caller must dispose it after readers finish. `RigBaker` registers the rig and library through `AddBlobAsset`; Unity's blob store then owns those references and runtime systems must not dispose them.

Standalone Generic clip baking is implemented through `ClipBakePipeline.Bake(in ClipBakeRequest)`. A private native clip sampler resets the hierarchy to rest and completes absent TRS binding groups with constant rest curves. Unity materializes zero y/z curves when authoring only position.x; such bound zeros remain clip data. The PlayableGraph differential fixture explicitly binds all TRS channels. Originals are not sampled in place.

For positive duration, N = ceil(duration * SampleRate) + 1, including both endpoints. Zero duration stores one sample. Identical tracks store one value. Sampling linearly interpolates position/scale and uses normalized shortest-path quaternion slerp. Positive exact loop boundaries preserve the endpoint; times beyond a boundary and negative times wrap. Loop seam, zero-duration and Euler fixtures are covered.

Baking measures position, angular and scale errors at sample/interior/key-adjacent times and explicit settings times. A weighted or stepped curve that cannot meet the configured tolerance fails with clip/path/time information and publishes no blob. No lossy compression or unmeasured exact-curve promise is made. Generic root animation is normalized to rig space; root-relative shear that cannot be represented as TRS is diagnosed. Humanoid, scalar/shape and event import remain explicit unsupported diagnostics until their planned stages.

Pass `CacheDirectory` to enable the disk cache; null disables it. Content identity includes Editor/converter/schema versions, source clip asset identity/dependencies, rig identity, settings, normalized curve data, animated-root normalization TRS and `AdditionalDependencyKey` for external import inputs. Mask/controller converters must supply their own dependencies when those stages are implemented. The cache writes immutable SHA-256-named blob payloads before atomically publishing a per-key JSON index. Payload bytes are checked before native deserialization; schema, rig and layout are validated. Corrupt/incompatible entries are rebuilt. Failure or cancellation cannot replace the previous published entry. Cache hits return fresh caller-owned Persistent allocations.

`LibraryBakePipeline.Build` embeds each clip and all nested arrays into one allocation, preserving input order as local `ClipId` indices. Input clips can be disposed after construction. Empty libraries are valid; incompatible rig identities are rejected.

Add `AnimationRigAuthoring` and assign explicit `AnimationBakeSettings`; add `AnimationLibraryAuthoring` on the same root and assign Generic clips. `RigBaker` publishes one `AnimationRig` component only after all conversion succeeds. It tracks child transforms (including inactive children), Animator/Avatar, settings, library component and clip dependencies. Intermediate standalone clip allocations are disposed after the library copy. Do not call the standalone pipelines every runtime frame.

The integration tests import a temporary saved scene through public `SceneSystem`, then independently test live incremental baking in one ECS World. Removing clips replaces the library without stale bindings; removing rig authoring retracts the component. No internal BakingUtility or private deformation API is used.

Publication uses a per-destination named mutex shared by Editor import processes, followed by atomic move/replacement. Simultaneous Windows replacements require serialization; the regression runs eight publishers over five rounds and checks that the destination is always a complete payload. Mutex waits and transient reader-sharing retries are bounded; unrelated I/O failures propagate. Cancellation is checked again after acquiring the publication gate.

Actual Editor close/reopen verification recovered the same rig/clip keys and pose from disk with `FromCache=true`; CLI remained stopped. Evidence: `Artifacts/Verification/ColdBakeBefore.json`, `ColdBakeAfter.json`. The sandbox Generic fixture at 30 Hz measured maximum position error 1.1920929e-7, angular and scale errors 0, within configured tolerances; see `stage1-error-report.json`.

Verified: full Editor suite **37/37**, including 7 rig, 8 clip, 6 cache, 1 concurrent publication, 3 library, 2 SubScene integration and 10 oracle/fixture tests. XML: `Artifacts/Verification/stage1-final-green.xml`.

The package does not yet provide an ECS animation player. Mesh bindings and mask/controller import dependencies are completed with their consuming stages, without placeholder implementations.

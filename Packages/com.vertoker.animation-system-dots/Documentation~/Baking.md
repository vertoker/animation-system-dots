# Generic baking — stage 1 in progress

Implemented so far: immutable rig definitions, topological parents, rest local TRS, complete UTF-8 paths, FNV-1a path acceleration with byte-for-byte collision checks, schema version and SHA-256 content identity. `RigBakingTests` includes the real `costarring`/`liquid` FNV collision. Zero/non-finite scales, invalid quaternions, ambiguous full paths, over-capacity paths and optimized-away skeletons produce diagnostics.

Bone zero is the rig-space origin, with identity rest pose. The source root's placement/scale belongs to the ECS rig entity and must not be applied twice. Non-root bones retain their authored local TRS, including negative scale.

`RigBakePipeline.Bake(GameObject)` currently returns a standalone Persistent blob. The caller must dispose it after readers finish. Once the Baker integration exists, ownership will transfer to Unity's blob store; runtime systems must not dispose baked references.

Standalone Generic clip baking is implemented through `ClipBakePipeline.Bake(in ClipBakeRequest)`. A private native clip sampler resets the hierarchy to rest and completes absent TRS binding groups with constant rest curves. Unity materializes zero y/z curves when authoring only position.x; such bound zeros remain clip data. The PlayableGraph differential fixture explicitly binds all TRS channels. Originals are not sampled in place.

For positive duration, N = ceil(duration * SampleRate) + 1, including both endpoints. Zero duration stores one sample. Identical tracks store one value. Sampling linearly interpolates position/scale and uses normalized shortest-path quaternion slerp. Positive exact loop boundaries preserve the endpoint; times beyond a boundary and negative times wrap. Loop seam, zero-duration and Euler fixtures are covered.

Baking measures position, angular and scale errors at sample/interior/key-adjacent times and explicit settings times. A weighted or stepped curve that cannot meet the configured tolerance fails with clip/path/time information and publishes no blob. No lossy compression or unmeasured exact-curve promise is made. Generic root animation is normalized to rig space; root-relative shear that cannot be represented as TRS is diagnosed. Humanoid, scalar/shape and event import remain explicit unsupported diagnostics until their planned stages.

Pass `CacheDirectory` to enable the disk cache; null disables it. Content identity includes Editor/converter/schema versions, source clip asset identity/dependencies, rig identity, settings, normalized curve data and `AdditionalDependencyKey` for external Avatar/mask/controller import inputs. The cache writes immutable SHA-256-named blob payloads before atomically publishing a per-key JSON index. Payload bytes are checked before native deserialization; schema, rig and layout are validated. Corrupt/incompatible entries are rebuilt. Failure or cancellation cannot replace the previous published entry. Cache hits return fresh caller-owned Persistent allocations.

Verified: full Editor suite **29/29**, including 6 rig, 8 clip and 5 cache tests plus the 10 oracle/fixture tests. XML: `Artifacts/Verification/stage1-clips-cache-green.xml`.

Library construction, Baker integration and an actual close/reopen cache check are still pending. Stage 1 is not complete and the package does not yet provide an ECS animation player.

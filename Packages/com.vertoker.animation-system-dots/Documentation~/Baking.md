# Generic baking — stage 1 in progress

Implemented so far: immutable rig definitions, topological parents, rest local TRS, complete UTF-8 paths, FNV-1a path acceleration with byte-for-byte collision checks, schema version and SHA-256 content identity. `RigBakingTests` includes the real `costarring`/`liquid` FNV collision. Zero/non-finite scales, invalid quaternions, ambiguous full paths, over-capacity paths and optimized-away skeletons produce diagnostics.

Bone zero is the rig-space origin, with identity rest pose. The source root's placement/scale belongs to the ECS rig entity and must not be applied twice. Non-root bones retain their authored local TRS, including negative scale.

`RigBakePipeline.Bake(GameObject)` currently returns a standalone Persistent blob. The caller must dispose it after readers finish. Once the Baker integration exists, ownership will transfer to Unity's blob store; runtime systems must not dispose baked references.

Clip sampling/baking, disk cache and Baker integration are still pending. Stage 1 is not complete and the package does not yet provide an ECS animation player.

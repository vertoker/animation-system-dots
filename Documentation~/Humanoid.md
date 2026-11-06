# Humanoid pose modes

The existing target-TRS bake reproduces individual clips, but blending already retargeted bone rotations differs from Unity's Humanoid blending. Real Mixamo idle/run/wave comparisons measured up to 0.148 m joint error and 12.95 degrees local rotation error with that representation. This is a pose-space difference, not a rendering defect or a problem solved by increasing the sample rate.

`AnimationBakeSettings.HumanoidPoseMode` now offers an opt-in `PreciseMuscles` path. Existing settings remain `BakedTargetTRS`; they are not silently migrated.

The precise bake retains the authored 95 muscle channels and target-normalized body position/orientation. It does not reconstruct muscle inputs from a retargeted skeleton. Runtime sampling and muscle blending run as Burst ECS jobs. The separate `AnimationSystemDOTS.Humanoid` assembly converts the blended human pose into rig-local bone poses through the public, unbound `HumanPoseHandler(Avatar, string[])` API. It uses an Avatar asset reference and shares one solver per rig inside each World. It creates no GameObject, Animator, PlayableGraph or scene bone hierarchy. Unity documents the [unbound pose conversion API](https://docs.unity3d.com/cn/6000.0/ScriptReference/HumanPoseHandler.GetInternalAvatarPose.html).

The final Avatar conversion is a managed ECS system on the main thread. This is **not an all-Burst Humanoid retargeter**. An isolated Editor measurement of 1000 native retarget calls for the 70-joint target measured 4.09 ms median / 4.70 ms p95 over 100 warm batches. That excludes sampling, ECS buffer copies, skinning and rendering, and is not a scene or Player benchmark.

`HumanoidRuntimeParityTests` compares actual ECS buffers and model poses against a native manual Mixamo graph. The initial one-, two- and three-input fixtures measured at most 0.177 mm joint error and 0 degrees measured local rotation error at the tested times and weights. These fixtures are not evidence of complete Animator graph, mask, additive, transition or IK compatibility. Further acceptance remains open in the [implementation plan](Designs/2026-11-06-animator-validation-plan.md).

Blob schema 2 contains the extra channel arrays. Cache identity includes the pose mode and converter version, and animation libraries deep-copy the arrays. Mixing Generic clips into a precise Humanoid library and layered/masked/additive requests require their own explicit support checks; callers must inspect `HumanoidBodyPose.Result` instead of assuming that every request is supported.

The three new validation scenes are still under implementation. The existing 02 and 04 samples retain their prior target-TRS configuration. Ragdoll is not implemented in this milestone; its feasibility and separate physics adapter are described in the [approved scene design](Designs/2026-11-06-animator-validation-design.md).

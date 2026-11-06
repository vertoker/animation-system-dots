using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AnimationSystemDOTS.Humanoid
{
    // Only this adapter calls Unity's public native Avatar solver. Sampling and
    // muscle blending run in Burst; this final conversion is a managed ECS
    // system, explicitly not a claim of a Burst Humanoid retargeter.
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateAfter(typeof(HumanoidBlendSystem))]
    [UpdateBefore(typeof(AnimationPoseModificationSystemGroup))]
    public partial class HumanoidRetargetSystem : SystemBase
    {
        readonly Dictionary<FixedString128Bytes, Solver> solvers = new Dictionary<FixedString128Bytes, Solver>();
        readonly List<FixedString128Bytes> stale = new List<FixedString128Bytes>();
        uint generation;
        protected override void OnCreate() => RequireForUpdate<AnimationStepContext>();

        protected override void OnUpdate()
        {
            Dependency.Complete(); ++generation;
            foreach (var (binding, rig, body, muscles, rootMuscles, localBuffer) in SystemAPI.Query<RefRO<HumanoidRigBinding>, RefRO<AnimationRig>,
                RefRW<HumanoidBodyPose>, DynamicBuffer<HumanoidMuscleValue>, DynamicBuffer<HumanoidRootMuscleValue>, DynamicBuffer<BoneLocalPose>>())
            {
                var local = localBuffer;
                if (body.ValueRO.Result == HumanoidEvaluationResult.RetainedPose) continue;
                if (body.ValueRO.Result == HumanoidEvaluationResult.NoRequests)
                {
                    if (rig.ValueRO.Rig.IsCreated)
                    {
                        local.ResizeUninitialized(rig.ValueRO.Rig.Value.RestLocalPoses.Length);
                        for (var b = 0; b < local.Length; b++) local[b] = rig.ValueRO.Rig.Value.RestLocalPoses[b];
                    }
                    else local.Clear();
                    continue;
                }
                if (body.ValueRO.Result != HumanoidEvaluationResult.Success) { local.Clear(); continue; }
                var avatar = binding.ValueRO.Avatar.Value;
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                { body.ValueRW.Result = HumanoidEvaluationResult.MissingAvatar; local.Clear(); continue; }
                var definition = rig.ValueRO.Rig;
                if (!definition.IsCreated || muscles.Length != HumanTrait.MuscleCount)
                { body.ValueRW.Result = HumanoidEvaluationResult.InvalidDefinition; local.Clear(); continue; }
                var key = definition.Value.ContentKey;
                if (solvers.TryGetValue(key, out var solver) && solver.Avatar != avatar)
                { solver.Dispose(); solvers.Remove(key); solver = null; }
                if (solver == null)
                {
                    try { solver = new Solver(avatar, definition); solvers.Add(key, solver); }
                    catch (ArgumentException) { body.ValueRW.Result = HumanoidEvaluationResult.InvalidDefinition; local.Clear(); continue; }
                }
                solver.Generation = generation;
                solver.Human.bodyPosition = body.ValueRO.Position; solver.Human.bodyRotation = body.ValueRO.Rotation;
                var preserveRoot = rootMuscles.Length == 0;
                if (rootMuscles.Length != 0 && rootMuscles.Length != muscles.Length)
                { body.ValueRW.Result = HumanoidEvaluationResult.InvalidChannels; local.Clear(); continue; }
                for (var m = 0; m < rootMuscles.Length; m++) if (rootMuscles[m].Value != muscles[m].Value) { preserveRoot = true; break; }
                var rootPose = definition.Value.RestLocalPoses[solver.Hips];
                if (preserveRoot && rootMuscles.Length > 0)
                {
                    for (var m = 0; m < rootMuscles.Length; m++) solver.Human.muscles[m] = rootMuscles[m].Value;
                    solver.Handler.SetInternalHumanPose(ref solver.Human); solver.Handler.GetInternalAvatarPose(solver.Pose);
                    var j = solver.Hips * 7;
                    rootPose.Position = new float3(solver.Pose[j], solver.Pose[j + 1], solver.Pose[j + 2]);
                    rootPose.Rotation = new quaternion(solver.Pose[j + 3], solver.Pose[j + 4], solver.Pose[j + 5], solver.Pose[j + 6]);
                }
                for (var m = 0; m < muscles.Length; m++) solver.Human.muscles[m] = muscles[m].Value;
                solver.Handler.SetInternalHumanPose(ref solver.Human);
                solver.Handler.GetInternalAvatarPose(solver.Pose);
                local.ResizeUninitialized(definition.Value.RestLocalPoses.Length);
                for (var b = 0; b < local.Length; b++)
                {
                    var j = b * 7;
                    var pose = new BoneLocalPose
                    {
                        Position = new float3(solver.Pose[j], solver.Pose[j + 1], solver.Pose[j + 2]),
                        Rotation = new quaternion(solver.Pose[j + 3], solver.Pose[j + 4], solver.Pose[j + 5], solver.Pose[j + 6]),
                        Scale = definition.Value.RestLocalPoses[b].Scale
                    };
                    // Root trajectory belongs to the entity's motor.
                    local[b] = b == 0 ? BoneLocalPose.Identity : b == solver.Hips && preserveRoot ? rootPose : pose;
                    if (!PoseValidation.IsValid(local[b])) { body.ValueRW.Result = HumanoidEvaluationResult.InvalidPose; local.Clear(); break; }
                }
            }
            stale.Clear();
            foreach (var pair in solvers) if (pair.Value.Generation != generation) stale.Add(pair.Key);
            foreach (var key in stale) { solvers[key].Dispose(); solvers.Remove(key); }
        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
            foreach (var solver in solvers.Values) solver.Dispose();
            solvers.Clear();
        }

        sealed class Solver : IDisposable
        {
            public readonly Avatar Avatar;
            public readonly HumanPoseHandler Handler;
            public readonly int Hips;
            public NativeArray<float> Pose;
            public HumanPose Human;
            public uint Generation;
            public Solver(Avatar avatar, BlobAssetReference<RigDefinitionBlob> rig)
            {
                Avatar = avatar;
                var count = rig.Value.Paths.Length; var paths = new string[count];
                for (var b = 0; b < count; b++) paths[b] = rig.Value.Paths[b].ToString();
                var hipsName = "";
                foreach (var bone in avatar.humanDescription.human) if (bone.humanName == HumanTrait.BoneName[(int)HumanBodyBones.Hips]) { hipsName = bone.boneName; break; }
                Hips = Array.FindIndex(paths, path => path == hipsName || path.EndsWith("/" + hipsName, StringComparison.Ordinal));
                if (string.IsNullOrEmpty(hipsName) || Hips < 0) throw new ArgumentException("Humanoid target hips mapping is absent from the rig.");
                Handler = new HumanPoseHandler(avatar, paths);
                try
                {
                    Pose = new NativeArray<float>(count * 7, Allocator.Persistent);
                    for (var b = 0; b < count; b++)
                    {
                        var rest = rig.Value.RestLocalPoses[b]; var j = b * 7;
                        Pose[j] = rest.Position.x; Pose[j + 1] = rest.Position.y; Pose[j + 2] = rest.Position.z;
                        Pose[j + 3] = rest.Rotation.value.x; Pose[j + 4] = rest.Rotation.value.y; Pose[j + 5] = rest.Rotation.value.z; Pose[j + 6] = rest.Rotation.value.w;
                    }
                    Handler.SetInternalAvatarPose(Pose);
                    Human = new HumanPose { muscles = new float[HumanTrait.MuscleCount], bodyRotation = Quaternion.identity };
                }
                catch { Dispose(); throw; }
            }
            public void Dispose() { if (Pose.IsCreated) Pose.Dispose(); Handler.Dispose(); }
        }
    }
}

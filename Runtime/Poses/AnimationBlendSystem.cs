using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
namespace AnimationSystemDOTS
{
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateAfter(typeof(AnimationSampleSystem))]
    public partial class AnimationBlendSystem : SystemBase
    {
        EntityQuery missingHistory;
        protected override void OnCreate()
        {
            RequireForUpdate<AnimationStepContext>();
            missingHistory = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<AnimationRig>(), ComponentType.ReadOnly<SampledBonePose>() },
                None = new[] { ComponentType.ReadOnly<ControllerBoneHistory>(), ComponentType.ReadOnly<PreciseHumanoidPose>() } });
        }
        protected override void OnUpdate()
        {
            if (!missingHistory.IsEmptyIgnoreFilter) EntityManager.AddComponent<ControllerBoneHistory>(missingHistory);
            Dependency = new BlendJob { Step = SystemAPI.GetSingleton<AnimationStepContext>(), Controllers = GetComponentLookup<ControllerLayerState>(true), Playback = GetComponentLookup<AnimationPlaybackMode>(true) }.ScheduleParallel(Dependency);
        }

        [BurstCompile]
        [WithNone(typeof(PreciseHumanoidPose))]
        partial struct BlendJob : IJobEntity
        {
            public AnimationStepContext Step;
            [ReadOnly] public ComponentLookup<ControllerLayerState> Controllers;
            [ReadOnly] public ComponentLookup<AnimationPlaybackMode> Playback;
            void Execute(Entity entity, in AnimationRig rig, in DynamicBuffer<AnimationClipRequest> requests,
                in DynamicBuffer<SampledBonePose> samples, DynamicBuffer<BoneLocalPose> local, DynamicBuffer<ControllerBoneHistory> history)
            {
                var nativeController = Controllers.HasComponent(entity) && Playback.HasComponent(entity) && Playback[entity].Value == PlaybackDriver.Controller;
                if (nativeController
                    && Controllers[entity].Result == ControllerEvaluationResult.Success && Controllers[entity].RetainPose != 0 && local.Length > 0) return;
                if (!rig.Rig.IsCreated) { local.Clear(); return; }
                var count = rig.Rig.Value.RestLocalPoses.Length;
                if (nativeController)
                {
                    var layerCount = 0;
                    for (var r = 0; r < requests.Length; r++) layerCount = math.max(layerCount, requests[r].Layer + 1);
                    var required = count * layerCount;
                    if (history.Length < required) history.Resize(required, NativeArrayOptions.ClearMemory);
                }
                local.ResizeUninitialized(count);
                for (var bone = 0; bone < count; bone++)
                {
                    var result = rig.Rig.Value.RestLocalPoses[bone];
                    var first = true; var previousLayer = 0;
                    // Read requests without reordering the producer's buffer. Layers are
                    // ascending; additive contributions within one layer retain input order.
                    for (var pass = 0; pass < requests.Length && samples.Length == requests.Length * count; pass++)
                    {
                        var found = false; var layer = int.MaxValue;
                        for (var i = 0; i < requests.Length; i++)
                            if ((first || requests[i].Layer > previousLayer) && (!found || requests[i].Layer < layer))
                            { layer = requests[i].Layer; found = true; }
                        if (!found) break;
                        first = false; previousLayer = layer;
                        if (nativeController)
                        {
                            var index = layer * count + bone; var item = history[index];
                            var definition = Controllers[entity].Definition;
                            if (item.Definition != definition) item = new ControllerBoneHistory { Definition = definition };
                            if (!item.Step.Repeats(Step))
                            { item.BeforeStep = item.Previous; item.BeforeInitialized = item.Initialized; item.Step.Set(Step); }
                            BlendControllerLayer(ref result, rig, requests, samples, layer, bone, count, ref item);
                            history[index] = item; continue;
                        }
                        var average = BoneLocalPose.Identity; double total = 0;
                        for (var i = 0; i < requests.Length; i++)
                        {
                            var request = requests[i]; var sample = samples[i * count + bone];
                            if (request.Layer != layer || request.BlendMode != PoseBlendMode.Override || sample.IsValid == 0) continue;
                            var weight = Weight(request, rig, bone, count);
                            if (weight <= 0) continue;
                            average = PoseBlendMath.Override(average, sample.Pose, (float)(weight / (total + weight)));
                            total += weight;
                        }
                        if (total > 0) result = PoseBlendMath.Override(result, average, (float)math.min(total, 1));
                        for (var i = 0; i < requests.Length; i++)
                        {
                            var request = requests[i]; var sample = samples[i * count + bone];
                            if (request.Layer != layer || request.BlendMode != PoseBlendMode.Additive || sample.IsValid == 0) continue;
                            var weight = Weight(request, rig, bone, count);
                            if (weight <= 0) continue;
                            result = PoseBlendMath.Additive(result, sample.Pose, rig.Library.Value.Clips[request.Clip.Value].ReferencePose[bone], weight);
                        }
                    }
                    local[bone] = result;
                }
            }
            static void BlendControllerLayer(ref BoneLocalPose result, in AnimationRig rig, in DynamicBuffer<AnimationClipRequest> requests,
                in DynamicBuffer<SampledBonePose> samples, int layer, int bone, int count, ref ControllerBoneHistory history)
            {
                var grouped = false;
                for (var r = 0; r < requests.Length; r++) if (requests[r].Layer == layer && requests[r].MotionWeight > 0) { grouped = true; break; }
                if (grouped)
                {
                    BlendControllerStates(ref result, rig, requests, samples, layer, bone, count, ref history); return;
                }
                var position = float3.zero; var scale = float3.zero; var rotation = float4.zero;
                var reference = quaternion.identity; var total = 0f; var influence = 0f; var mode = PoseBlendMode.Override;
                for (var i = 0; i < requests.Length; i++)
                {
                    var request = requests[i]; var sample = samples[i * count + bone];
                    if (request.Layer != layer || sample.IsValid == 0) continue;
                    var weight = Weight(request, rig, bone, count); if (weight <= 0) continue;
                    var layerRequest = request; layerRequest.Weight = request.LayerWeight;
                    influence = math.max(influence, Weight(layerRequest, rig, bone, count));
                    var p = sample.Pose.Position; var q = sample.Pose.Rotation; var s = sample.Pose.Scale;
                    mode = request.BlendMode;
                    if (mode == PoseBlendMode.Additive)
                    {
                        ref var additive = ref rig.Library.Value.Clips[request.Clip.Value].ReferencePose[bone];
                        p -= additive.Position; s -= additive.Scale; q = math.mul(math.inverse(additive.Rotation), q);
                    }
                    if (total == 0) reference = q;
                    position += p * weight; scale += s * weight;
                    rotation += q.value * (math.dot(reference.value, q.value) < 0 ? -weight : weight); total += weight;
                }
                if (total <= 0 || influence <= 0) return;
                influence = math.saturate(influence); var missing = math.max(0, influence - total);
                if (mode == PoseBlendMode.Additive)
                {
                    var delta = math.normalizesafe(new quaternion(rotation), quaternion.identity);
                    // Unity blends a layer's deltas first, then scales the imaginary
                    // quaternion part while retaining its scalar part. Scale deltas
                    // add across layers rather than multiplying the current scale.
                    var weighted = math.normalizesafe(new quaternion(new float4(delta.value.xyz * influence, delta.value.w)), quaternion.identity);
                    result.Position += position; result.Scale += scale;
                    result.Rotation = math.normalize(math.mul(result.Rotation, weighted));
                }
                else
                {
                    var rest = rig.Rig.Value.RestLocalPoses[bone];
                    rotation += rest.Rotation.value * (math.dot(reference.value, rest.Rotation.value) < 0 ? -missing : missing);
                    var pose = new BoneLocalPose { Position = (position + rest.Position * missing) / influence,
                        Scale = (scale + rest.Scale * missing) / influence,
                        Rotation = math.normalizesafe(new quaternion(rotation), rest.Rotation) };
                    result.Position = math.lerp(result.Position, pose.Position, influence);
                    result.Scale = math.lerp(result.Scale, pose.Scale, influence);
                    result.Rotation = math.nlerp(result.Rotation, pose.Rotation, influence);
                }
            }
            static void BlendControllerStates(ref BoneLocalPose result, in AnimationRig rig, in DynamicBuffer<AnimationClipRequest> requests,
                in DynamicBuffer<SampledBonePose> samples, int layer, int bone, int count, ref ControllerBoneHistory history)
            {
                var position = float3.zero; var scale = float3.zero; var rotation = float4.zero;
                var reference = quaternion.identity; var total = 0f; var influence = 0f; var mode = PoseBlendMode.Override;
                var evaluated = false;
                for (var slot = 0; slot < 2; slot++)
                {
                    var p = float3.zero; var s = float3.zero; var q = float4.zero;
                    var qReference = quaternion.identity; var motionTotal = 0f; var stateWeight = 0f;
                    for (var r = 0; r < requests.Length; r++)
                    {
                        var request = requests[r]; var sample = samples[r * count + bone];
                        if (request.Layer != layer || request.StateSlot != slot || request.PoseSource == ControllerPoseSource.Motion && sample.IsValid == 0 || request.MotionWeight <= 0) continue;
                        var layerRequest = request; layerRequest.Weight = request.LayerWeight;
                        evaluated |= request.LayerWeight > 0;
                        influence = math.max(influence, Weight(layerRequest, rig, bone, count));
                        mode = request.BlendMode; stateWeight = request.StateWeight;
                        var pose = sample.Pose;
                        if (request.PoseSource != ControllerPoseSource.Motion)
                        {
                            var neutral = mode == PoseBlendMode.Additive ? new BoneLocalPose { Rotation = quaternion.identity } : rig.Rig.Value.RestLocalPoses[bone];
                            if (request.PoseSource == ControllerPoseSource.Captured && history.FrozenSequence != request.InstanceSequence)
                            {
                                history.Frozen = history.BeforeInitialized != 0 ? history.BeforeStep : neutral;
                                history.FrozenSequence = request.InstanceSequence;
                            }
                            pose = request.PoseSource == ControllerPoseSource.Captured ? history.Frozen : request.PoseSource == ControllerPoseSource.PreviousLayer && history.BeforeInitialized != 0 ? history.BeforeStep : neutral;
                        }
                        else if (mode == PoseBlendMode.Additive)
                        {
                            ref var additive = ref rig.Library.Value.Clips[request.Clip.Value].ReferencePose[bone];
                            pose.Position -= additive.Position; pose.Scale -= additive.Scale; pose.Rotation = math.mul(math.inverse(additive.Rotation), pose.Rotation);
                        }
                        if (motionTotal == 0) qReference = pose.Rotation;
                        p += pose.Position * request.MotionWeight; s += pose.Scale * request.MotionWeight;
                        q += pose.Rotation.value * (math.dot(qReference.value, pose.Rotation.value) < 0 ? -request.MotionWeight : request.MotionWeight);
                        motionTotal += request.MotionWeight;
                    }
                    if (motionTotal <= 0 || stateWeight <= 0) continue;
                    if (mode == PoseBlendMode.Override)
                    {
                        var rest = rig.Rig.Value.RestLocalPoses[bone]; var missing = math.max(0, 1 - motionTotal);
                        p += rest.Position * missing; s += rest.Scale * missing;
                        q += rest.Rotation.value * (math.dot(qReference.value, rest.Rotation.value) < 0 ? -missing : missing);
                    }
                    var stateRotation = math.normalizesafe(new quaternion(q), quaternion.identity);
                    if (total == 0) reference = stateRotation;
                    position += p * stateWeight; scale += s * stateWeight;
                    rotation += stateRotation.value * (math.dot(reference.value, stateRotation.value) < 0 ? -stateWeight : stateWeight); total += stateWeight;
                }
                if (total <= 0) return;
                var layerRotation = math.normalizesafe(new quaternion(rotation), quaternion.identity);
                if (evaluated) { history.Previous = new BoneLocalPose { Position = position, Rotation = layerRotation, Scale = scale }; history.Initialized = 1; }
                if (influence <= 0) return;
                if (mode == PoseBlendMode.Additive)
                {
                    var weighted = math.normalizesafe(new quaternion(new float4(layerRotation.value.xyz * influence, layerRotation.value.w)), quaternion.identity);
                    result.Position += position * influence; result.Scale += scale * influence;
                    result.Rotation = math.normalize(math.mul(result.Rotation, weighted));
                }
                else
                {
                    result.Position = math.lerp(result.Position, position, influence); result.Scale = math.lerp(result.Scale, scale, influence);
                    result.Rotation = math.nlerp(result.Rotation, layerRotation, influence);
                }
            }
            static float Weight(in AnimationClipRequest request, in AnimationRig rig, int bone, int count)
            {
                if (!math.isfinite(request.Weight) || request.Weight <= 0) return 0;
                if (!AnimationRequestMask.HasMask(request)) return request.Weight;
                if (!AnimationRequestMask.CanRead(request)) return 0;
                ref var mask = ref AnimationRequestMask.Get(request);
                if (mask.Version != AnimationBakeVersion.Schema || mask.RigKey != rig.Rig.Value.ContentKey || mask.Weights.Length != count) return 0;
                var value = mask.Weights[bone];
                return math.isfinite(value) ? request.Weight * math.saturate(value) : 0;
            }
        }
    }
}

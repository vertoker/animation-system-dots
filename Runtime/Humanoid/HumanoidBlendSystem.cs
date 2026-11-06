using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystemDOTS.Humanoid
{
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateAfter(typeof(AnimationBlendSystem))]
    [UpdateBefore(typeof(AnimationPoseModificationSystemGroup))]
    public partial class HumanoidBlendSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<AnimationStepContext>();
        protected override void OnUpdate() => Dependency = new BlendJob
        { Controllers = GetComponentLookup<ControllerLayerState>(true), Playback = GetComponentLookup<AnimationPlaybackMode>(true) }.ScheduleParallel(Dependency);

        [BurstCompile]
        partial struct BlendJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<ControllerLayerState> Controllers;
            [ReadOnly] public ComponentLookup<AnimationPlaybackMode> Playback;
            void Execute(Entity entity, in HumanoidRigBinding binding, in AnimationRig rig, in DynamicBuffer<AnimationClipRequest> requests,
                ref HumanoidBodyPose body, DynamicBuffer<HumanoidMuscleValue> muscles, DynamicBuffer<HumanoidRootMuscleValue> rootMuscles)
            {
                var nativeController = Controllers.HasComponent(entity) && Playback.HasComponent(entity) && Playback[entity].Value == PlaybackDriver.Controller;
                if (nativeController
                    && Controllers[entity].Result == ControllerEvaluationResult.Success && Controllers[entity].RetainPose != 0
                    && (body.Result == HumanoidEvaluationResult.Success || body.Result == HumanoidEvaluationResult.RetainedPose))
                { body.Result = HumanoidEvaluationResult.RetainedPose; return; }
                body = new HumanoidBodyPose { Rotation = quaternion.identity, Result = HumanoidEvaluationResult.NoRequests };
                muscles.ResizeUninitialized(95);
                rootMuscles.Clear();
                for (var m = 0; m < muscles.Length; m++) muscles[m] = default;
                if (requests.Length == 0) return;
                if (!rig.Rig.IsCreated || rig.Rig.Value.Version != AnimationBakeVersion.Schema || !rig.Library.IsCreated || rig.Library.Value.Version != AnimationBakeVersion.Schema
                    || rig.Library.Value.RigKey != rig.Rig.Value.ContentKey)
                { body.Result = HumanoidEvaluationResult.InvalidDefinition; return; }
                rootMuscles.ResizeUninitialized(95);
                for (var m = 0; m < 95; m++) rootMuscles[m] = default;
                var firstLayer = true; var previousLayer = 0; var contributed = false; var rootContributed = false;
                for (var pass = 0; pass < requests.Length; pass++)
                {
                    var found = false; var layer = int.MaxValue;
                    for (var r = 0; r < requests.Length; r++)
                        if ((firstLayer || requests[r].Layer > previousLayer) && (!found || requests[r].Layer < layer))
                        { layer = requests[r].Layer; found = true; }
                    if (!found) break;
                    firstLayer = false; previousLayer = layer;
                    var values = new FixedList512Bytes<float>(); values.Length = 95;
                    var totals = new FixedList512Bytes<float>(); totals.Length = 95;
                    var rootValues = new FixedList512Bytes<float>(); rootValues.Length = 95;
                    var rootTotals = new FixedList512Bytes<float>(); rootTotals.Length = 95;
                    var influences = new FixedList512Bytes<float>(); influences.Length = 95;
                    var rootInfluences = new FixedList512Bytes<float>(); rootInfluences.Length = 95;
                    for (var m = 0; m < 95; m++) { values[m] = 0; totals[m] = 0; rootValues[m] = 0; rootTotals[m] = 0; influences[m] = rootInfluences[m] = 0; }
                    var position = float3.zero; var rotation = float4.zero; double bodyTotal = 0;
                    var bodyInfluence = 0f;
                    var reference = quaternion.identity; var hasMode = false; var mode = PoseBlendMode.Override;
                    for (var r = 0; r < requests.Length; r++)
                    {
                        var request = requests[r]; if (request.Layer != layer) continue;
                        if (!math.isfinite(request.Weight)) { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                        if (request.Weight <= 0) continue;
                        if (nativeController && (!math.isfinite(request.LayerWeight) || request.LayerWeight < 0 || request.LayerWeight > 1))
                        { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                        if ((uint)request.Clip.Value >= (uint)rig.Library.Value.Clips.Length || !math.isfinite(request.Time))
                        { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                        if (hasMode && mode != request.BlendMode || request.BlendMode != PoseBlendMode.Override && request.BlendMode != PoseBlendMode.Additive)
                        { body.Result = HumanoidEvaluationResult.UnsupportedBlendMode; return; }
                        hasMode = true; mode = request.BlendMode;
                        var hasMask = AnimationRequestMask.HasMask(request);
                        if (hasMask)
                        {
                            if (!AnimationRequestMask.CanRead(request)) { body.Result = HumanoidEvaluationResult.UnsupportedMask; return; }
                            ref var mask = ref AnimationRequestMask.Get(request);
                            if (mask.Version != AnimationBakeVersion.Schema || mask.RigKey != rig.Rig.Value.ContentKey || mask.HumanMuscleWeights.Length != 95)
                            { body.Result = HumanoidEvaluationResult.UnsupportedMask; return; }
                        }
                        ref var clip = ref rig.Library.Value.Clips[request.Clip.Value];
                        if (clip.Version != AnimationBakeVersion.Schema || clip.RigKey != rig.Rig.Value.ContentKey
                            || clip.HumanMuscles.Length != 95 || clip.HumanBody.Length != clip.SampleCount || clip.SampleCount < 1
                            || clip.Duration > 0 && (clip.SampleCount < 2 || !math.isfinite(clip.SampleStep) || clip.SampleStep <= 0))
                        { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                        if (mode == PoseBlendMode.Additive && (clip.HasHumanAdditiveReference == 0 || clip.HumanReferenceMuscles.Length != 95))
                        { body.Result = HumanoidEvaluationResult.UnsupportedBlendMode; return; }
                        var rootWeight = request.Weight; var rootMaskWeight = 1f;
                        if (hasMask)
                        {
                            var maskWeight = AnimationRequestMask.Get(request).HumanRootWeight;
                            if (!math.isfinite(maskWeight)) { body.Result = HumanoidEvaluationResult.UnsupportedMask; return; }
                            rootMaskWeight = math.saturate(maskWeight); rootWeight *= rootMaskWeight;
                        }
                        if (rootWeight > 0) rootContributed = true;
                        if (nativeController) bodyInfluence = math.max(bodyInfluence, request.LayerWeight * rootMaskWeight);
                        Interval(ref clip, request.Time, request.LoopBoundary, out var first, out var alpha);
                        for (var m = 0; m < 95; m++)
                        {
                            ref var samples = ref clip.HumanMuscles[m].Samples;
                            if (samples.Length != 1 && samples.Length != clip.SampleCount)
                            { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                            var value = samples.Length == 1 ? samples[0] : math.lerp(samples[first], samples[first + 1], alpha);
                            if (mode == PoseBlendMode.Additive) value -= clip.HumanReferenceMuscles[m];
                            var weight = request.Weight;
                            var muscleMask = 1f;
                            if (hasMask)
                            {
                                var maskWeight = AnimationRequestMask.Get(request).HumanMuscleWeights[m];
                                if (!math.isfinite(maskWeight)) { body.Result = HumanoidEvaluationResult.UnsupportedMask; return; }
                                muscleMask = math.saturate(maskWeight); weight *= muscleMask;
                            }
                            if (!math.isfinite(value)) { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                            values[m] += value * weight; totals[m] += weight;
                            if (nativeController) influences[m] = math.max(influences[m], request.LayerWeight * muscleMask);
                            // Root-enabled partial layers update the same masked
                            // muscle channels as the visible pose. Root-off layers
                            // leave this snapshot untouched, preserving prior hips.
                            var rootMuscleWeight = weight * rootMaskWeight;
                            rootValues[m] += value * rootMuscleWeight; rootTotals[m] += rootMuscleWeight;
                            if (nativeController) rootInfluences[m] = math.max(rootInfluences[m], request.LayerWeight * muscleMask * rootMaskWeight);
                        }
                        var a = clip.HumanBody[first]; var b = clip.HumanBody[math.min(first + 1, clip.SampleCount - 1)];
                        var p = math.lerp(a.Position, b.Position, alpha); var q = math.slerp(a.Rotation, b.Rotation, alpha);
                        if (mode == PoseBlendMode.Additive)
                        {
                            p -= clip.HumanReferenceBody.Position;
                            q = math.mul(math.inverse(clip.HumanReferenceBody.Rotation), q);
                        }
                        if (!math.all(math.isfinite(p)) || !math.all(math.isfinite(q.value)) || math.lengthsq(q.value) < .5f)
                        { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                        var bodyWeight = rootWeight;
                        if (bodyTotal == 0) reference = q;
                        position += p * bodyWeight;
                        rotation += q.value * (math.dot(reference.value, q.value) < 0 ? -bodyWeight : bodyWeight);
                        bodyTotal += bodyWeight; contributed = true;
                    }
                    if (!hasMode) continue;
                    for (var m = 0; m < 95; m++)
                    {
                        var value = muscles[m].Value;
                        if (mode == PoseBlendMode.Additive) value += values[m];
                        else if (totals[m] > 0) value = nativeController ? value * (1 - influences[m]) + values[m]
                            : math.lerp(value, values[m] / totals[m], math.saturate(totals[m]));
                        muscles[m] = new HumanoidMuscleValue { Value = value };
                        var rootValue = rootMuscles[m].Value;
                        if (mode == PoseBlendMode.Additive) rootValue += rootValues[m];
                        else if (rootTotals[m] > 0) rootValue = nativeController ? rootValue * (1 - rootInfluences[m]) + rootValues[m]
                            : math.lerp(rootValue, rootValues[m] / rootTotals[m], math.saturate(rootTotals[m]));
                        rootMuscles[m] = new HumanoidRootMuscleValue { Value = rootValue };
                    }
                    if (bodyTotal > 0)
                    {
                        var q = math.normalizesafe(new quaternion(rotation), quaternion.identity);
                        if (mode == PoseBlendMode.Additive)
                        {
                            body.Position += position;
                            body.Rotation = math.normalize(math.mul(body.Rotation, math.nlerp(quaternion.identity, q, (float)math.min(bodyTotal, 1))));
                        }
                        else
                        {
                            if (nativeController)
                            {
                                var missing = math.max(0, bodyInfluence - (float)bodyTotal);
                                rotation += quaternion.identity.value * (reference.value.w < 0 ? -missing : missing);
                                q = math.normalizesafe(new quaternion(rotation), quaternion.identity);
                                body.Position = body.Position * (1 - bodyInfluence) + position;
                                body.Rotation = math.nlerp(body.Rotation, q, bodyInfluence);
                            }
                            else
                            {
                                body.Position = math.lerp(body.Position, position / (float)bodyTotal, (float)math.min(bodyTotal, 1));
                                body.Rotation = math.nlerp(body.Rotation, q, (float)math.min(bodyTotal, 1));
                            }
                        }
                    }
                }
                if (!rootContributed) rootMuscles.Clear();
                for (var m = 0; m < muscles.Length; m++)
                    if (!math.isfinite(muscles[m].Value) || rootMuscles.Length != 0 && !math.isfinite(rootMuscles[m].Value))
                    { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                if (!math.all(math.isfinite(body.Position)) || !math.all(math.isfinite(body.Rotation.value)) || math.abs(math.lengthsq(body.Rotation.value) - 1) > .001f)
                { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                body.Result = contributed ? HumanoidEvaluationResult.Success : HumanoidEvaluationResult.NoRequests;
            }

            static void Interval(ref AnimationClipBlob clip, double time, ClipLoopBoundary boundary, out int first, out float alpha)
            {
                if (clip.SampleCount == 1 || clip.Duration <= 0) { first = 0; alpha = 0; return; }
                if (clip.IsLooping != 0)
                {
                    var original = time; time %= clip.Duration; if (time < 0) time += clip.Duration;
                    if (time == 0 && (boundary == ClipLoopBoundary.End || boundary == ClipLoopBoundary.AuthoredEndpoint && original > 0)) time = clip.Duration;
                }
                else time = math.clamp(time, 0, clip.Duration);
                var sample = time / clip.SampleStep;
                first = math.min((int)math.floor(sample), clip.SampleCount - 2); alpha = (float)(sample - first);
            }
        }
    }
}

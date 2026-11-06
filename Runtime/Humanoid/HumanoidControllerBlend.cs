using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystemDOTS.Humanoid
{
    // Controller state streams and interruption history remain in authored muscle
    // space. Retargeting their frozen TRS instead would lose Humanoid semantics.
    internal static class HumanoidControllerBlend
    {
        struct StatePose
        {
            internal FixedList512Bytes<float> Muscles;
            internal float3 Position;
            internal quaternion Rotation;
            internal float Weight;
        }

        internal static void Evaluate(in AnimationRig rig, BlobAssetReference<AnimatorControllerBlob> definition, in AnimationStepContext step,
            in DynamicBuffer<AnimationClipRequest> requests, ref HumanoidBodyPose body,
            DynamicBuffer<HumanoidMuscleValue> muscles, DynamicBuffer<HumanoidRootMuscleValue> roots,
            DynamicBuffer<HumanoidControllerMuscleHistory> history, DynamicBuffer<HumanoidControllerBodyHistory> bodies)
        {
            body = new HumanoidBodyPose { Rotation = quaternion.identity, Result = HumanoidEvaluationResult.InvalidDefinition };
            if (!definition.IsCreated || !rig.Rig.IsCreated || !rig.Library.IsCreated
                || rig.Library.Value.Version != AnimationBakeVersion.Schema || rig.Rig.Value.Version != AnimationBakeVersion.Schema
                || rig.Library.Value.RigKey != rig.Rig.Value.ContentKey) return;
            var layerCount = definition.Value.Layers.Length;
            if (layerCount == 0) layerCount = 1;
            if (bodies.Length != layerCount) { bodies.Resize(layerCount, NativeArrayOptions.ClearMemory); history.Clear(); }
            if (history.Length != layerCount * 95) history.Resize(layerCount * 95, NativeArrayOptions.ClearMemory);
            muscles.ResizeUninitialized(95); roots.ResizeUninitialized(95);
            for (var m = 0; m < 95; m++) { muscles[m] = default; roots[m] = default; }
            var rootContributed = false; var contributed = false;
            for (var layer = 0; layer < layerCount; layer++)
            {
                var first = -1;
                for (var r = 0; r < requests.Length; r++) if (requests[r].Layer == layer) { first = r; break; }
                if (first < 0) continue;
                var request = requests[first]; var layerWeight = request.LayerWeight; var mode = request.BlendMode;
                if (!math.isfinite(layerWeight) || layerWeight < 0 || layerWeight > 1)
                { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                var masked = AnimationRequestMask.HasMask(request); var rootMask = 1f;
                if (masked)
                {
                    if (!AnimationRequestMask.CanRead(request)) { body.Result = HumanoidEvaluationResult.UnsupportedMask; return; }
                    ref var mask = ref AnimationRequestMask.Get(request);
                    if (mask.Version != AnimationBakeVersion.Schema || mask.RigKey != rig.Rig.Value.ContentKey || mask.HumanMuscleWeights.Length != 95 || !math.isfinite(mask.HumanRootWeight))
                    { body.Result = HumanoidEvaluationResult.UnsupportedMask; return; }
                    rootMask = math.saturate(mask.HumanRootWeight);
                }
                var header = bodies[layer];
                if (header.Definition != definition)
                {
                    header = new HumanoidControllerBodyHistory { Definition = definition, PreviousRotation = quaternion.identity, FrozenRotation = quaternion.identity };
                    for (var m = 0; m < 95; m++) history[layer * 95 + m] = default;
                }
                if (!header.Step.Repeats(step))
                {
                    header.BeforePosition = header.PreviousPosition; header.BeforeRotation = header.PreviousRotation; header.Step.Set(step);
                    for (var m = 0; m < 95; m++) { var value = history[layer * 95 + m]; value.BeforeStep = value.Previous; history[layer * 95 + m] = value; }
                }
                for (var r = 0; r < requests.Length; r++)
                {
                    var frozen = requests[r];
                    if (frozen.Layer != layer || frozen.PoseSource != ControllerPoseSource.Captured || header.FrozenSequence == frozen.InstanceSequence) continue;
                    header.FrozenPosition = header.BeforePosition; header.FrozenRotation = header.BeforeRotation; header.FrozenSequence = frozen.InstanceSequence;
                    for (var m = 0; m < 95; m++) { var value = history[layer * 95 + m]; value.Frozen = value.BeforeStep; history[layer * 95 + m] = value; }
                }
                if (!SampleState(rig, requests, layer, 0, mode, history, header, out var a, out var error)
                    || !SampleState(rig, requests, layer, 1, mode, history, header, out var b, out error))
                { body.Result = error; return; }
                var total = a.Weight + b.Weight; if (total <= 0) continue;
                var position = a.Position * a.Weight + b.Position * b.Weight;
                var rotation = math.normalizesafe(new quaternion(a.Rotation.value * a.Weight
                    + b.Rotation.value * (math.dot(a.Rotation.value, b.Rotation.value) < 0 ? -b.Weight : b.Weight)), quaternion.identity);
                if (layerWeight > 0) { header.PreviousPosition = position; header.PreviousRotation = rotation; header.Initialized = 1; }
                bodies[layer] = header;
                var rootWeight = layerWeight * rootMask;
                if (rootWeight > 0) rootContributed = true;
                for (var m = 0; m < 95; m++)
                {
                    var value = a.Muscles[m] * a.Weight + b.Muscles[m] * b.Weight;
                    if (layerWeight > 0) { var past = history[layer * 95 + m]; past.Previous = value; history[layer * 95 + m] = past; }
                    var maskWeight = masked ? AnimationRequestMask.Get(request).HumanMuscleWeights[m] : 1;
                    if (!math.isfinite(maskWeight) || !math.isfinite(value)) { body.Result = HumanoidEvaluationResult.InvalidChannels; return; }
                    var weight = layerWeight * math.saturate(maskWeight); var rootInfluence = weight * rootMask;
                    muscles[m] = new HumanoidMuscleValue { Value = mode == PoseBlendMode.Additive ? muscles[m].Value + value * weight : math.lerp(muscles[m].Value, value, weight) };
                    roots[m] = new HumanoidRootMuscleValue { Value = mode == PoseBlendMode.Additive ? roots[m].Value + value * rootInfluence : math.lerp(roots[m].Value, value, rootInfluence) };
                }
                if (mode == PoseBlendMode.Additive)
                {
                    body.Position += position * rootWeight;
                    body.Rotation = math.normalize(math.mul(body.Rotation, math.nlerp(quaternion.identity, rotation, rootWeight)));
                }
                else
                {
                    body.Position = math.lerp(body.Position, position, rootWeight);
                    body.Rotation = math.nlerp(body.Rotation, rotation, rootWeight);
                }
                contributed = true;
            }
            if (!rootContributed) roots.Clear();
            body.Result = contributed ? HumanoidEvaluationResult.Success : HumanoidEvaluationResult.NoRequests;
        }

        static bool SampleState(in AnimationRig rig, in DynamicBuffer<AnimationClipRequest> requests, int layer, int slot, PoseBlendMode mode,
            in DynamicBuffer<HumanoidControllerMuscleHistory> history, in HumanoidControllerBodyHistory header, out StatePose pose, out HumanoidEvaluationResult error)
        {
            pose = new StatePose { Rotation = quaternion.identity }; pose.Muscles.Length = 95;
            for (var m = 0; m < 95; m++) pose.Muscles[m] = 0;
            error = HumanoidEvaluationResult.InvalidChannels;
            var rotation = float4.zero; var reference = quaternion.identity; var total = 0f;
            for (var r = 0; r < requests.Length; r++)
            {
                var request = requests[r]; if (request.Layer != layer || request.StateSlot != slot) continue;
                if (!math.isfinite(request.StateWeight) || !math.isfinite(request.MotionWeight) || request.StateWeight < 0 || request.MotionWeight < 0) return false;
                pose.Weight = request.StateWeight;
                if (request.PoseSource != ControllerPoseSource.Motion)
                {
                    if (request.PoseSource == ControllerPoseSource.Captured)
                    {
                        pose.Position = header.FrozenPosition; pose.Rotation = header.FrozenRotation;
                        for (var m = 0; m < 95; m++) pose.Muscles[m] = history[layer * 95 + m].Frozen;
                    }
                    else if (request.PoseSource == ControllerPoseSource.PreviousLayer)
                    {
                        pose.Position = header.BeforePosition; pose.Rotation = header.BeforeRotation;
                        for (var m = 0; m < 95; m++) pose.Muscles[m] = history[layer * 95 + m].BeforeStep;
                    }
                    return true;
                }
                if ((uint)request.Clip.Value >= (uint)rig.Library.Value.Clips.Length || !math.isfinite(request.Time)) return false;
                ref var clip = ref rig.Library.Value.Clips[request.Clip.Value];
                if (clip.Version != AnimationBakeVersion.Schema || clip.RigKey != rig.Rig.Value.ContentKey || clip.HumanMuscles.Length != 95
                    || clip.HumanBody.Length != clip.SampleCount || clip.SampleCount < 1 || clip.Duration > 0 && (clip.SampleCount < 2 || clip.SampleStep <= 0 || !math.isfinite(clip.SampleStep))) return false;
                if (mode == PoseBlendMode.Additive && (clip.HasHumanAdditiveReference == 0 || clip.HumanReferenceMuscles.Length != 95))
                { error = HumanoidEvaluationResult.UnsupportedBlendMode; return false; }
                Interval(ref clip, request.Time, request.LoopBoundary, out var first, out var alpha);
                for (var m = 0; m < 95; m++)
                {
                    ref var samples = ref clip.HumanMuscles[m].Samples;
                    if (samples.Length != 1 && samples.Length != clip.SampleCount) return false;
                    var value = samples.Length == 1 ? samples[0] : math.lerp(samples[first], samples[first + 1], alpha);
                    if (mode == PoseBlendMode.Additive) value -= clip.HumanReferenceMuscles[m];
                    if (!math.isfinite(value)) return false;
                    pose.Muscles[m] += value * request.MotionWeight;
                }
                var a = clip.HumanBody[first]; var b = clip.HumanBody[math.min(first + 1, clip.SampleCount - 1)];
                var position = math.lerp(a.Position, b.Position, alpha); var q = math.slerp(a.Rotation, b.Rotation, alpha);
                if (mode == PoseBlendMode.Additive) { position -= clip.HumanReferenceBody.Position; q = math.mul(math.inverse(clip.HumanReferenceBody.Rotation), q); }
                if (!math.all(math.isfinite(position)) || !math.all(math.isfinite(q.value)) || math.lengthsq(q.value) < .5f) return false;
                if (total == 0) reference = q;
                pose.Position += position * request.MotionWeight;
                rotation += q.value * (math.dot(reference.value, q.value) < 0 ? -request.MotionWeight : request.MotionWeight); total += request.MotionWeight;
            }
            if (mode == PoseBlendMode.Override) rotation += quaternion.identity.value * (reference.value.w < 0 ? -math.max(0, 1 - total) : math.max(0, 1 - total));
            pose.Rotation = math.normalizesafe(new quaternion(rotation), quaternion.identity);
            return true;
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
            var sample = time / clip.SampleStep; first = math.min((int)math.floor(sample), clip.SampleCount - 2); alpha = (float)(sample - first);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using AnimationSystemDOTS.Authoring;

namespace AnimationSystemDOTS.Editor.Baking
{
    public struct ClipBakeRequest
    {
        public GameObject Source;
        public AnimationClip Clip;
        public BlobAssetReference<RigDefinitionBlob> Rig;
        public AnimationBakeSettings Settings;
        public string AdditionalDependencyKey;
        public string CacheDirectory;
        public CancellationToken Cancellation;
    }

    public struct ClipBakeResult
    {
        public bool Success;
        public BlobAssetReference<AnimationClipBlob> Clip;
        public string ContentKey;
        public string[] Diagnostics;
        public float MaxPositionError;
        public float MaxAngleErrorDegrees;
        public float MaxScaleError;
        public bool FromCache;
    }

    public static class ClipBakePipeline
    {
        public static ClipBakeResult Bake(in ClipBakeRequest request)
        {
            var candidate = default(BlobAssetReference<AnimationClipBlob>);
            var builder = default(BlobBuilder);
            var ownsBuilder = false;
            var result = new ClipBakeResult { Diagnostics = Array.Empty<string>() };
            try
            {
                Validate(request);
                request.Cancellation.ThrowIfCancellationRequested();
                var comparison = RigBakePipeline.Bake(request.Source);
                if (!comparison.Success) throw new ArgumentException(string.Join("\n", comparison.Diagnostics));
                try
                {
                    if (!comparison.Rig.Value.ContentKey.Equals(request.Rig.Value.ContentKey))
                        throw new ArgumentException(request.Clip.name + ": target hierarchy no longer matches the rig definition.");
                }
                finally { comparison.Rig.Dispose(); }
                using (IEditorClipSampler sampler = request.Clip.isHumanMotion
                    ? new HumanoidClipSampler(request.Source, request.Clip, request.Rig)
                    : new EditorClipSampler(request.Source, request.Clip, request.Rig))
                {
                    if (!request.Clip.isHumanMotion) ValidateBindings(sampler, request.Rig);
                    var settings = request.Settings;
                    var duration = (double)sampler.Clip.length;
                    var count = duration == 0 ? 1 : checked((int)Math.Ceiling(duration * settings.SampleRate) + 1);
                    if (count > settings.MaximumSamplesPerClip) throw new ArgumentException(request.Clip.name + ": sample count exceeds MaximumSamplesPerClip.");
                    var step = count > 1 ? duration / (count - 1) : 0;
                    var identity = Application.unityVersion + "|" + AnimationBakeVersion.Converter + "|" + AnimationBakeVersion.Schema + "|"
                        + request.Rig.Value.ContentKey + "|" + BakeContentKey.Asset(request.Clip) + "|" + request.AdditionalDependencyKey + "|"
                        + JsonUtility.ToJson(settings) + "|" + EditorJsonUtility.ToJson(sampler.Clip) + sampler.RootNormalizationIdentity;
                    result.ContentKey = BakeContentKey.Hash(identity);
                    if (!string.IsNullOrWhiteSpace(request.CacheDirectory)
                        && AnimationBlobCache.TryLoad(request.CacheDirectory, result.ContentKey, request.Rig.Value.ContentKey, out var cached))
                    {
                        candidate = cached.Clip;
                        request.Cancellation.ThrowIfCancellationRequested();
                        candidate = default;
                        return cached;
                    }
                    var frames = new BoneLocalPose[count][];
                    var preciseHuman = request.Clip.isHumanMotion && settings.HumanoidPoseMode == HumanoidPoseMode.PreciseMuscles;
                    var humanFrames = preciseHuman ? new float[count][] : null;
                    var bodyFrames = preciseHuman ? new BoneLocalPose[count] : null;
                    for (var frame = 0; frame < count; frame++)
                    {
                        request.Cancellation.ThrowIfCancellationRequested();
                        var time = count == 1 ? 0 : duration * frame / (count - 1);
                        frames[frame] = sampler.Sample(time);
                        if (preciseHuman)
                        {
                            humanFrames[frame] = new float[HumanTrait.MuscleCount];
                            bodyFrames[frame] = ((HumanoidClipSampler)sampler).ReadHumanPose(time, humanFrames[frame]);
                        }
                    }
                    builder = new BlobBuilder(Allocator.Temp); ownsBuilder = true;
                    ref var data = ref builder.ConstructRoot<AnimationClipBlob>();
                    data.Version = AnimationBakeVersion.Schema;
                    data.ContentKey = new FixedString128Bytes(result.ContentKey);
                    data.RigKey = request.Rig.Value.ContentKey;
                    data.Duration = duration; data.SampleStep = step; data.SampleCount = count;
                    data.IsLooping = sampler.IsLooping ? (byte)1 : (byte)0;
                    if (preciseHuman)
                    {
                        var muscles = builder.Allocate(ref data.HumanMuscles, HumanTrait.MuscleCount);
                        for (var m = 0; m < muscles.Length; m++)
                        {
                            var constant = true;
                            for (var frame = 1; frame < count; frame++) if (humanFrames[frame][m] != humanFrames[0][m]) { constant = false; break; }
                            var values = builder.Allocate(ref muscles[m].Samples, constant ? 1 : count);
                            for (var frame = 0; frame < values.Length; frame++) values[frame] = humanFrames[frame][m];
                        }
                        var bodies = builder.Allocate(ref data.HumanBody, count);
                        for (var frame = 0; frame < count; frame++) bodies[frame] = bodyFrames[frame];
                    }
                    var tracks = builder.Allocate(ref data.Tracks, frames[0].Length);
                    var reference = builder.Allocate(ref data.ReferencePose, frames[0].Length);
                    for (var bone = 0; bone < tracks.Length; bone++)
                    {
                        reference[bone] = request.Rig.Value.RestLocalPoses[bone];
                        var constant = true;
                        for (var frame = 1; frame < count; frame++)
                            if (!Equal(frames[0][bone], frames[frame][bone])) { constant = false; break; }
                        var values = builder.Allocate(ref tracks[bone].Samples, constant ? 1 : count);
                        for (var frame = 0; frame < values.Length; frame++) values[frame] = frames[frame][bone];
                    }
                    candidate = builder.CreateBlobAssetReference<AnimationClipBlob>(Allocator.Persistent);
                    foreach (var time in VerificationTimes(sampler, settings, duration, count))
                    {
                        request.Cancellation.ThrowIfCancellationRequested();
                        var expected = sampler.Sample(time);
                        for (var bone = 0; bone < expected.Length; bone++)
                        {
                            var actual = ClipSampler.SampleBone(ref candidate.Value, bone, time);
                            var positionError = math.distance(actual.Position, expected[bone].Position);
                            var scaleError = math.cmax(math.abs(actual.Scale - expected[bone].Scale));
                            var a = math.normalize(actual.Rotation).value;
                            var b = math.normalize(expected[bone].Rotation).value;
                            if (math.dot(a, b) < 0) b = -b;
                            var angularError = math.degrees(4 * math.asin(math.clamp(math.length(a - b) * 0.5f, 0, 1)));
                            result.MaxPositionError = math.max(result.MaxPositionError, positionError);
                            result.MaxAngleErrorDegrees = math.max(result.MaxAngleErrorDegrees, angularError);
                            result.MaxScaleError = math.max(result.MaxScaleError, scaleError);
                            if (positionError > settings.PositionTolerance || angularError > settings.AngleToleranceDegrees || scaleError > settings.ScaleTolerance)
                                throw new ArgumentException(request.Clip.name + ": " + request.Rig.Value.Paths[bone].ToString()
                                    + ": sampling tolerance exceeded at time " + time.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                                    + " (position=" + positionError + ", angle=" + angularError + ", scale=" + scaleError + "). Increase sample rate or revise the clip/settings.");
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(request.CacheDirectory))
                        AnimationBlobCache.Store(request.CacheDirectory, result.ContentKey, builder, result, request.Cancellation);
                    result.Success = true; result.Clip = candidate;
                    candidate = default;
                    return result;
                }
            }
            catch (OperationCanceledException) { result.Diagnostics = new[] { (request.Clip != null ? request.Clip.name : "<clip>") + ": bake cancelled; no output published." }; return result; }
            catch (ArgumentException exception) { result.Diagnostics = new[] { exception.Message }; return result; }
            catch (OverflowException) { result.Diagnostics = new[] { "Clip sample layout exceeds supported allocation limits." }; return result; }
            catch (IOException exception) { result.Diagnostics = new[] { request.Clip.name + ": cache I/O failed: " + exception.Message }; return result; }
            catch (UnauthorizedAccessException exception) { result.Diagnostics = new[] { request.Clip.name + ": cache write denied: " + exception.Message }; return result; }
            finally
            {
                if (candidate.IsCreated) candidate.Dispose();
                if (ownsBuilder) builder.Dispose();
            }
        }

        static void Validate(in ClipBakeRequest request)
        {
            if (request.Source == null || request.Clip == null || !request.Rig.IsCreated || request.Settings == null)
                throw new ArgumentException("Clip bake requires source hierarchy, clip, rig definition and explicit settings.");
            if (request.Clip.legacy)
                throw new ArgumentException(request.Clip.name + ": Legacy clips are unsupported.");
            if (request.Clip.isHumanMotion) HumanoidRigValidator.Validate(request.Source);
            var s = request.Settings;
            if (s.SampleRate < 1 || s.MaximumSamplesPerClip < 1 || !FiniteTolerance(s.PositionTolerance)
                || !FiniteTolerance(s.AngleToleranceDegrees) || !FiniteTolerance(s.ScaleTolerance) || !FiniteTolerance(s.ScalarTolerance))
                throw new ArgumentException(request.Clip.name + ": bake settings contain an invalid sample rate, limit or tolerance.");
            if (request.Rig.Value.Version != AnimationBakeVersion.Schema) throw new ArgumentException(request.Clip.name + ": incompatible rig schema.");
        }

        static bool FiniteTolerance(float value) => math.isfinite(value) && value >= 0;

        static void ValidateBindings(IEditorClipSampler sampler, BlobAssetReference<RigDefinitionBlob> rig)
        {
            foreach (var binding in sampler.Bindings)
            {
                var property = binding.propertyName;
                var axis = property.Length > 1 ? property[property.Length - 1] : '\0';
                var transformCurve = property.StartsWith("m_LocalPosition.", StringComparison.Ordinal)
                    || property.StartsWith("m_LocalScale.", StringComparison.Ordinal)
                    || property.StartsWith("m_LocalRotation.", StringComparison.Ordinal)
                    || property.StartsWith("localEulerAnglesRaw.", StringComparison.Ordinal)
                    || property.StartsWith("localEulerAnglesBaked.", StringComparison.Ordinal)
                    || property.StartsWith("localEulerAngles.", StringComparison.Ordinal);
                if (binding.type != typeof(Transform) || !transformCurve || (axis != 'x' && axis != 'y' && axis != 'z' && axis != 'w'))
                    throw new ArgumentException(sampler.Clip.name + ": " + binding.path + ": unsupported binding " + property + "; scalar/shape import is pending stage 7.");
                if (RigPathLookup.Find(ref rig.Value, new FixedString512Bytes(binding.path)) < 0)
                    throw new ArgumentException(sampler.Clip.name + ": " + binding.path + ": animation binding does not exist in the target rig.");
                foreach (var key in AnimationUtility.GetEditorCurve(sampler.Clip, binding).keys)
                    if (!float.IsFinite(key.time) || !float.IsFinite(key.value) || float.IsNaN(key.inTangent) || float.IsNaN(key.outTangent)
                        || !float.IsFinite(key.inWeight) || !float.IsFinite(key.outWeight))
                        throw new ArgumentException(sampler.Clip.name + ": " + binding.path + ": non-finite source curve data.");
            }
        }

        static bool Equal(in BoneLocalPose a, in BoneLocalPose b)
            => math.all(a.Position == b.Position) && math.all(a.Scale == b.Scale)
                && (math.all(a.Rotation.value == b.Rotation.value) || math.all(a.Rotation.value == -b.Rotation.value));

        static IEnumerable<double> VerificationTimes(IEditorClipSampler sampler, AnimationBakeSettings settings, double duration, int count)
        {
            var times = new SortedSet<double> { 0, duration };
            var step = count > 1 ? duration / (count - 1) : 0;
            for (var i = 0; i < count - 1; i++)
            {
                times.Add(i * step); times.Add((i + 0.25) * step); times.Add((i + 0.5) * step); times.Add((i + 0.75) * step);
            }
            var epsilon = Math.Min(step * 0.001, 0.0001);
            foreach (var binding in sampler.Bindings)
                foreach (var key in AnimationUtility.GetEditorCurve(sampler.Clip, binding).keys)
                    if (key.time >= 0 && key.time <= duration)
                    {
                        times.Add(key.time);
                        times.Add(Math.Max(0, key.time - epsilon)); times.Add(Math.Min(duration, key.time + epsilon));
                    }
            if (settings.CaptureTimes != null)
                foreach (var time in settings.CaptureTimes)
                    if (double.IsFinite(time) && time >= 0 && time <= duration) times.Add(time);
            return times;
        }
    }
}

using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEditor;
using AnimationSystemDOTS.Authoring;
using AnimationSystemDOTS.Editor.Baking;
using AnimationSystemDOTS.Editor.Verification;

namespace AnimationSystemDOTS.Tests
{
    public abstract class ClipBakeFixture
    {
        protected GameObject source;
        protected AnimationClip clip;
        protected AnimationBakeSettings settings;
        protected BlobAssetReference<RigDefinitionBlob> rig;

        [SetUp]
        public void SetUp()
        {
            source = new GameObject("ClipRig");
            var bone = new GameObject("Bone").transform;
            bone.SetParent(source.transform, false);
            bone.localPosition = new Vector3(3, 4, 5); bone.localScale = new Vector3(-1, 2, 1);
            var rest = new GameObject("Rest").transform;
            rest.SetParent(source.transform, false); rest.localPosition = new Vector3(0, 7, 0);
            clip = new AnimationClip { name = "GenericClip", frameRate = 30 };
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 3, 1, 5));
            settings = ScriptableObject.CreateInstance<AnimationBakeSettings>();
            settings.SampleRate = 2;
            var result = RigBakePipeline.Bake(source);
            Assert.That(result.Success, Is.True); rig = result.Rig;
        }

        [TearDown]
        public void TearDown()
        {
            if (rig.IsCreated) rig.Dispose();
            UnityEngine.Object.DestroyImmediate(settings);
            UnityEngine.Object.DestroyImmediate(clip);
            UnityEngine.Object.DestroyImmediate(source);
        }

        protected ClipBakeRequest Request() => new ClipBakeRequest { Source = source, Clip = clip, Rig = rig, Settings = settings };
    }

    public class ClipBakingTests : ClipBakeFixture
    {
        [Test]
        public void Bake_UsesUniformEndpointSamplesConstantTracksAndRestFallback()
        {
            var result = ClipBakePipeline.Bake(Request());
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            try
            {
                ref var data = ref result.Clip.Value;
                Assert.That(data.SampleCount, Is.EqualTo(3));
                Assert.That(data.SampleStep, Is.EqualTo(0.5).Within(1e-8));
                Assert.That(data.Tracks[2].Samples.Length, Is.EqualTo(1));
                var bone = ClipSampler.SampleBone(ref data, 1, 0.3);
                Assert.That(bone.Position.x, Is.EqualTo(3.6).Within(settings.PositionTolerance));
                // Unity materializes explicit zero y/z curves when SetCurve writes position.x.
                // Bound zero channels must not be mistaken for absent tracks.
                Assert.That(bone.Position.y, Is.Zero); Assert.That(bone.Scale.x, Is.EqualTo(-1));
                Assert.That(ClipSampler.SampleBone(ref data, 2, 0.7).Position.y, Is.EqualTo(7));
                Assert.That(ClipSampler.SampleBone(ref data, 1, 1).Position.x, Is.EqualTo(5).Within(1e-6));
                Assert.That(ClipSampler.SampleBone(ref data, 1, -4).Position.x, Is.EqualTo(3).Within(1e-6));
            }
            finally { result.Clip.Dispose(); }
        }

        [Test]
        public void Bake_AgreesWithIndependentPlayableOracleBetweenSamples()
        {
            // This comparison uses fully bound TRS; missing-channel rest fallback is our explicit
            // runtime contract and is independently asserted by the analytical fixture above.
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0, 1, 4));
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.z", AnimationCurve.Constant(0, 1, 5));
            clip.SetCurve("Bone", typeof(Transform), "m_LocalScale.x", AnimationCurve.Constant(0, 1, -1));
            clip.SetCurve("Bone", typeof(Transform), "m_LocalScale.y", AnimationCurve.Constant(0, 1, 2));
            clip.SetCurve("Bone", typeof(Transform), "m_LocalScale.z", AnimationCurve.Constant(0, 1, 1));
            for (var axis = 0; axis < 4; axis++)
                clip.SetCurve("Bone", typeof(Transform), "m_LocalRotation." + "xyzw"[axis], AnimationCurve.Constant(0, 1, axis == 3 ? 1 : 0));
            var times = new[] { 0, 0.017, 0.353, 0.987, 1 };
            var oracle = UnityAnimationOracle.Capture(new OracleCaptureRequest { Prefab = source, Clip = clip, Times = times });
            var result = ClipBakePipeline.Bake(Request());
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            try
            {
                ref var data = ref result.Clip.Value;
                for (var frame = 0; frame < times.Length; frame++)
                {
                    var pose = ClipSampler.SampleBone(ref data, 1, times[frame]);
                    var matrix = Matrix4x4.TRS(pose.Position, pose.Rotation, pose.Scale);
                    var expected = oracle.Frames[frame].LocalMatrices[Array.IndexOf(oracle.BonePaths, "Bone")];
                    for (var row = 0; row < 4; row++)
                        for (var column = 0; column < 4; column++)
                            Assert.That(matrix[row, column], Is.EqualTo(expected[row, column]).Within(1e-4));
                }
            }
            finally { result.Clip.Dispose(); }
        }

        [Test]
        public void Bake_ZeroLengthClipContainsOneFiniteSample()
        {
            clip.ClearCurves();
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", new AnimationCurve(new Keyframe(0, 9)));
            var result = ClipBakePipeline.Bake(Request());
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            try
            {
                Assert.That(result.Clip.Value.SampleCount, Is.EqualTo(1));
                Assert.That(result.Clip.Value.SampleStep, Is.Zero);
                Assert.That(ClipSampler.SampleBone(ref result.Clip.Value, 1, 50).Position.x, Is.EqualTo(9));
            }
            finally { result.Clip.Dispose(); }
        }

        [Test]
        public void Bake_RejectsUnknownBoneWithClipAndBindingDiagnostic()
        {
            clip.SetCurve("Missing/Joint", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 1, 1));
            var result = ClipBakePipeline.Bake(Request());
            try
            {
                Assert.That(result.Success, Is.False);
                Assert.That(string.Join("\n", result.Diagnostics), Does.Contain("GenericClip").And.Contain("Missing/Joint"));
                Assert.That(result.Clip.IsCreated, Is.False);
            }
            finally { if (result.Clip.IsCreated) result.Clip.Dispose(); }
        }

        [Test]
        public void Bake_RejectsNonlinearCurveWhenSamplingMissesTolerance()
        {
            settings.SampleRate = 1;
            var a = new Keyframe(0, 3, 0, 6) { weightedMode = WeightedMode.Both, inWeight = 0.2f, outWeight = 0.2f };
            var b = new Keyframe(1, 5, -2, 0) { weightedMode = WeightedMode.Both, inWeight = 0.4f, outWeight = 0.4f };
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", new AnimationCurve(a, b));
            var result = ClipBakePipeline.Bake(Request());
            try
            {
                Assert.That(result.Success, Is.False);
                Assert.That(string.Join("\n", result.Diagnostics), Does.Contain("GenericClip").And.Contain("Bone").And.Contain("tolerance"));
                Assert.That(result.Clip.IsCreated, Is.False);
            }
            finally { if (result.Clip.IsCreated) result.Clip.Dispose(); }
        }

        [Test]
        public void Bake_DetectsSteppedCurveErrorImmediatelyBeforeTheKey()
        {
            settings.SampleRate = 30;
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", new AnimationCurve(
                new Keyframe(0, 0, 0, float.PositiveInfinity), new Keyframe(0.5f, 1, 0, 0), new Keyframe(1, 1)));
            var result = ClipBakePipeline.Bake(Request());
            try
            {
                Assert.That(result.Success, Is.False);
                Assert.That(string.Join("\n", result.Diagnostics), Does.Contain("tolerance"));
            }
            finally { if (result.Clip.IsCreated) result.Clip.Dispose(); }
        }

        [Test]
        public void Bake_SamplesEulerCurvesAndMeasuresAngularError()
        {
            settings.SampleRate = 30;
            clip.SetCurve("Bone", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Linear(0, 0, 1, 90));
            var result = ClipBakePipeline.Bake(Request());
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            try
            {
                var pose = ClipSampler.SampleBone(ref result.Clip.Value, 1, 0.5);
                Assert.That(math.distance(math.rotate(pose.Rotation, new float3(1, 0, 0)), new float3(math.sqrt(0.5f), math.sqrt(0.5f), 0)), Is.LessThan(1e-4));
                Assert.That(result.MaxAngleErrorDegrees, Is.LessThanOrEqualTo(settings.AngleToleranceDegrees));
            }
            finally { result.Clip.Dispose(); }
        }

        [Test]
        public void Bake_PreservesLoopEndpointAndSamplesAcrossPositiveAndNegativeSeams()
        {
            var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
            var result = ClipBakePipeline.Bake(Request());
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            try
            {
                ref var data = ref result.Clip.Value;
                Assert.That(data.IsLooping, Is.EqualTo(1));
                Assert.That(ClipSampler.SampleBone(ref data, 1, 1).Position.x, Is.EqualTo(5).Within(1e-5));
                Assert.That(ClipSampler.SampleBone(ref data, 1, 1.25).Position.x, Is.EqualTo(3.5).Within(1e-5));
                Assert.That(ClipSampler.SampleBone(ref data, 1, -0.25).Position.x, Is.EqualTo(4.5).Within(1e-5));
                Assert.That(ClipSampler.SampleBone(ref data, 1, 1.0001).Position.x, Is.EqualTo(3.0002).Within(1e-5));
            }
            finally { result.Clip.Dispose(); }
        }
    }
}

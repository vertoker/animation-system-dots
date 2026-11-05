using System;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using AnimationSystemDOTS.Editor.Baking;

namespace AnimationSystemDOTS.Tests
{
    public class RigBakingTests
    {
        GameObject source;
        [SetUp] public void SetUp() => source = new GameObject("RigFixture");
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(source);

        Transform Child(string name, Transform parent)
        {
            var result = new GameObject(name).transform;
            result.SetParent(parent, false); return result;
        }

        [Test]
        public void Bake_StoresRestPoseFullPathsAndTopologicalParents()
        {
            var left = Child("Left", source.transform);
            var a = Child("Joint", left); a.localPosition = new Vector3(1, 2, 3);
            var right = Child("Right", source.transform);
            Child("Joint", right).localScale = new Vector3(-1, 2, 3);
            var result = RigBakePipeline.Bake(source);
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            try
            {
                ref var rig = ref result.Rig.Value;
                Assert.That(rig.Version, Is.EqualTo(AnimationBakeVersion.Schema));
                Assert.That(rig.Paths.Length, Is.EqualTo(5));
                for (var i = 0; i < rig.Parents.Length; i++) Assert.That(rig.Parents[i], Is.LessThan(i));
                var l = RigPathLookup.Find(ref rig, new FixedString512Bytes("Left/Joint"));
                var r = RigPathLookup.Find(ref rig, new FixedString512Bytes("Right/Joint"));
                Assert.That(l, Is.GreaterThanOrEqualTo(0)); Assert.That(r, Is.GreaterThanOrEqualTo(0));
                Assert.That(l, Is.Not.EqualTo(r));
                Assert.That(math.distance(rig.RestLocalPoses[l].Position, new float3(1, 2, 3)), Is.LessThan(1e-6));
                Assert.That(rig.RestLocalPoses[r].Scale.x, Is.EqualTo(-1));
                Assert.That(RigPathLookup.Find(ref rig, new FixedString512Bytes("Missing/Joint")), Is.EqualTo(-1));
            }
            finally { result.Rig.Dispose(); }
        }

        [Test]
        public void Lookup_ResolvesActualFNVHashCollisionByCompletePath()
        {
            Child("costarring", source.transform); Child("liquid", source.transform);
            var a = new FixedString512Bytes("costarring"); var b = new FixedString512Bytes("liquid");
            Assert.That(RigPathLookup.Hash(a), Is.EqualTo(RigPathLookup.Hash(b)), "Known FNV-1a collision fixture");
            var result = RigBakePipeline.Bake(source);
            Assert.That(result.Success, Is.True);
            try
            {
                ref var rig = ref result.Rig.Value;
                var indexA = RigPathLookup.Find(ref rig, a); var indexB = RigPathLookup.Find(ref rig, b);
                Assert.That(indexA, Is.GreaterThan(0)); Assert.That(indexB, Is.GreaterThan(0));
                Assert.That(indexA, Is.Not.EqualTo(indexB));
            }
            finally { result.Rig.Dispose(); }
        }

        [Test]
        public void Bake_RejectsZeroScaleWithAssetAndBindingDiagnostic()
        {
            Child("Broken", source.transform).localScale = Vector3.zero;
            var result = RigBakePipeline.Bake(source);
            try
            {
                Assert.That(result.Success, Is.False);
                Assert.That(string.Join("\n", result.Diagnostics), Does.Contain("RigFixture").And.Contain("Broken"));
                Assert.That(result.Rig.IsCreated, Is.False);
            }
            finally { if (result.Rig.IsCreated) result.Rig.Dispose(); }
        }

        [Test]
        public void Bake_RejectsTwoSiblingsWithTheSameFullPath()
        {
            Child("Ambiguous", source.transform); Child("Ambiguous", source.transform);
            var result = RigBakePipeline.Bake(source);
            try
            {
                Assert.That(result.Success, Is.False);
                Assert.That(string.Join("\n", result.Diagnostics), Does.Contain("Ambiguous"));
            }
            finally { if (result.Rig.IsCreated) result.Rig.Dispose(); }
        }

        [Test]
        public void Bake_RootIsRigSpaceOriginAndContentIdentityIsRepeatable()
        {
            source.transform.localPosition = new Vector3(7, 3, -2);
            source.transform.localScale = new Vector3(2, 2, 2);
            Child("Bone", source.transform).localPosition = Vector3.up;
            var a = RigBakePipeline.Bake(source); var b = RigBakePipeline.Bake(source);
            Assert.That(a.Success && b.Success, Is.True);
            try
            {
                Assert.That(a.Rig.Value.RestLocalPoses[0].Position, Is.EqualTo(float3.zero));
                Assert.That(a.Rig.Value.RestLocalPoses[0].Scale, Is.EqualTo(new float3(1)));
                Assert.That(a.Rig.Value.ContentKey, Is.EqualTo(b.Rig.Value.ContentKey));
            }
            finally { a.Rig.Dispose(); b.Rig.Dispose(); }
        }

        [Test]
        public void Bake_RejectsOptimizedAwaySkeletonWithActionableDiagnostic()
        {
            Child("Bone", source.transform);
            var animator = source.AddComponent<Animator>();
            var avatar = AvatarBuilder.BuildGenericAvatar(source, "");
            try
            {
                Assert.That(avatar.isValid, Is.True);
                animator.avatar = avatar;
                AnimatorUtility.OptimizeTransformHierarchy(source, Array.Empty<string>());
                Assert.That(animator.hasTransformHierarchy, Is.False, "Fixture must exercise an actual optimized hierarchy.");
                var result = RigBakePipeline.Bake(source);
                try
                {
                    Assert.That(result.Success, Is.False);
                    Assert.That(string.Join("\n", result.Diagnostics), Does.Contain("Optimize Game Objects").And.Contain("unoptimized"));
                }
                finally { if (result.Rig.IsCreated) result.Rig.Dispose(); }
            }
            finally { UnityEngine.Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void PoseValidation_RejectsNonFiniteAndDegenerateData()
        {
            var pose = BoneLocalPose.Identity;
            Assert.That(PoseValidation.IsValid(pose), Is.True);
            pose.Position.x = float.NaN; Assert.That(PoseValidation.IsValid(pose), Is.False);
            pose = BoneLocalPose.Identity; pose.Rotation = new quaternion(0, 0, 0, 0);
            Assert.That(PoseValidation.IsValid(pose), Is.False);
            pose = BoneLocalPose.Identity; pose.Scale.z = 0;
            Assert.That(PoseValidation.IsValid(pose), Is.False);
        }
    }
}

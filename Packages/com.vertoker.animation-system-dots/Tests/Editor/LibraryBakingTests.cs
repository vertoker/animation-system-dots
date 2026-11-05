using System;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using AnimationSystemDOTS.Editor.Baking;

namespace AnimationSystemDOTS.Tests
{
    public class LibraryBakingTests : ClipBakeFixture
    {
        [Test]
        public void Library_DeepCopiesTracksAndPreservesClipOrderAfterInputsAreDisposed()
        {
            var a = ClipBakePipeline.Bake(Request());
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 3, 1, 7));
            var b = ClipBakePipeline.Bake(Request());
            Assert.That(a.Success && b.Success, Is.True);
            var library = default(BlobAssetReference<AnimationLibraryBlob>);
            try
            {
                var result = LibraryBakePipeline.Build(rig, new[] { a.Clip, b.Clip });
                Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics)); library = result.Library;
                a.Clip.Dispose(); b.Clip.Dispose(); rig.Dispose();
                Assert.That(library.Value.Clips.Length, Is.EqualTo(2));
                Assert.That(ClipSampler.SampleBone(ref library.Value.Clips[0], 1, 0.5).Position.x, Is.EqualTo(4).Within(1e-5));
                Assert.That(ClipSampler.SampleBone(ref library.Value.Clips[1], 1, 0.5).Position.x, Is.EqualTo(5).Within(1e-5));
            }
            finally
            {
                if (a.Clip.IsCreated) a.Clip.Dispose(); if (b.Clip.IsCreated) b.Clip.Dispose();
                if (library.IsCreated) library.Dispose();
            }
        }

        [Test]
        public void Library_RejectsClipsFromAnotherRig()
        {
            var baked = ClipBakePipeline.Bake(Request());
            var other = new GameObject("OtherRig");
            var otherRig = default(BlobAssetReference<RigDefinitionBlob>);
            try
            {
                var child = new GameObject("OtherBone"); child.transform.SetParent(other.transform, false);
                var rest = new GameObject("Rest"); rest.transform.SetParent(other.transform, false);
                otherRig = RigBakePipeline.Bake(other).Rig;
                var result = LibraryBakePipeline.Build(otherRig, new[] { baked.Clip });
                Assert.That(result.Success, Is.False);
                Assert.That(result.Library.IsCreated, Is.False);
                Assert.That(string.Join("\n", result.Diagnostics), Does.Contain("rig"));
            }
            finally
            {
                if (baked.Clip.IsCreated) baked.Clip.Dispose(); if (otherRig.IsCreated) otherRig.Dispose();
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void Library_EmptyLibraryStillHasRigIdentityAndNoExternalAllocations()
        {
            var result = LibraryBakePipeline.Build(rig, Array.Empty<BlobAssetReference<AnimationClipBlob>>());
            Assert.That(result.Success, Is.True);
            try
            {
                Assert.That(result.Library.Value.RigKey, Is.EqualTo(rig.Value.ContentKey));
                Assert.That(result.Library.Value.Clips.Length, Is.Zero);
            }
            finally { result.Library.Dispose(); }
        }
    }
}

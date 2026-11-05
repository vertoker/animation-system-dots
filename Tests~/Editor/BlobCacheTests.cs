using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using AnimationSystemDOTS.Editor.Baking;

namespace AnimationSystemDOTS.Tests
{
    public class BlobCacheTests : ClipBakeFixture
    {
        string directory;

        [SetUp]
        public void CreateCacheDirectoryName()
        {
            directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts/CacheTests/" + Guid.NewGuid().ToString("N")));
        }

        [TearDown]
        public void DeleteOwnedCache()
        {
            var allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts/CacheTests")) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Cache cleanup escaped the test workspace.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }

        ClipBakeRequest CachedRequest()
        {
            var request = Request(); request.CacheDirectory = directory; return request;
        }

        string FirstBake()
        {
            var result = ClipBakePipeline.Bake(CachedRequest());
            try
            {
                Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
                Assert.That(result.FromCache, Is.False);
                Assert.That(File.Exists(Path.Combine(directory, result.ContentKey + ".json")), Is.True, "Successful cache publication must create a durable index.");
                return result.ContentKey;
            }
            finally { if (result.Clip.IsCreated) result.Clip.Dispose(); }
        }

        [Test]
        public void Cache_ChangingFullyBoundRootBasisInvalidatesNormalizedRootTrack()
        {
            foreach (var property in new[] { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
                "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
                "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" })
            {
                var value = property == "m_LocalRotation.w" || property.StartsWith("m_LocalScale") ? 1 : 0;
                clip.SetCurve("", typeof(Transform), property, AnimationCurve.Constant(0, 1, value));
            }
            clip.SetCurve("", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 2, 1, 4));
            var oldKey = FirstBake();
            source.transform.localPosition = new Vector3(10, 0, 0);
            // Root placement belongs to the ECS entity, so the rig-space skeleton remains identical.
            var nextRig = RigBakePipeline.Bake(source);
            try { Assert.That(nextRig.Rig.Value.ContentKey, Is.EqualTo(rig.Value.ContentKey)); }
            finally { nextRig.Rig.Dispose(); }
            var result = ClipBakePipeline.Bake(CachedRequest());
            try
            {
                Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
                Assert.That(result.ContentKey, Is.Not.EqualTo(oldKey), "Root normalization basis is an input to sampled clip data.");
                Assert.That(result.FromCache, Is.False);
                Assert.That(ClipSampler.SampleBone(ref result.Clip.Value, 0, 0.5).Position.x, Is.EqualTo(-7).Within(1e-5));
            }
            finally { if (result.Clip.IsCreated) result.Clip.Dispose(); }
            var uncached = ClipBakePipeline.Bake(Request());
            try
            {
                Assert.That(uncached.Success, Is.True, string.Join("\n", uncached.Diagnostics));
                Assert.That(uncached.ContentKey, Is.EqualTo(result.ContentKey));
                Assert.That(ClipSampler.SampleBone(ref uncached.Clip.Value, 0, 0.5).Position.x, Is.EqualTo(-7).Within(1e-5));
            }
            finally { if (uncached.Clip.IsCreated) uncached.Clip.Dispose(); }
        }

        [Test]
        public void Cache_ReusesVerifiedDiskBlobAfterTheFirstResultIsDisposed()
        {
            var key = FirstBake();
            var result = ClipBakePipeline.Bake(CachedRequest());
            try
            {
                Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
                Assert.That(result.ContentKey, Is.EqualTo(key)); Assert.That(result.FromCache, Is.True);
                Assert.That(ClipSampler.SampleBone(ref result.Clip.Value, 1, 0.25).Position.x, Is.EqualTo(3.5).Within(1e-5));
            }
            finally { if (result.Clip.IsCreated) result.Clip.Dispose(); }
        }

        [Test]
        public void Cache_ChangingRateClipRigOrExternalDependencyInvalidatesContentIdentity()
        {
            var previous = FirstBake();
            settings.SampleRate = 10;
            var request = CachedRequest();
            var result = ClipBakePipeline.Bake(request);
            try { Assert.That(result.Success, Is.True); Assert.That(result.ContentKey, Is.Not.EqualTo(previous)); Assert.That(result.FromCache, Is.False); previous = result.ContentKey; }
            finally { if (result.Clip.IsCreated) result.Clip.Dispose(); }
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 3, 1, 8));
            result = ClipBakePipeline.Bake(request);
            try { Assert.That(result.Success, Is.True); Assert.That(result.ContentKey, Is.Not.EqualTo(previous)); previous = result.ContentKey; }
            finally { if (result.Clip.IsCreated) result.Clip.Dispose(); }
            request.AdditionalDependencyKey = "avatar-mask-controller-import-revision-2";
            result = ClipBakePipeline.Bake(request);
            try { Assert.That(result.Success, Is.True); Assert.That(result.ContentKey, Is.Not.EqualTo(previous)); previous = result.ContentKey; }
            finally { if (result.Clip.IsCreated) result.Clip.Dispose(); }
            source.transform.Find("Rest").localPosition = new Vector3(0, 9, 0);
            rig.Dispose(); rig = RigBakePipeline.Bake(source).Rig; request.Rig = rig;
            result = ClipBakePipeline.Bake(request);
            try { Assert.That(result.Success, Is.True); Assert.That(result.ContentKey, Is.Not.EqualTo(previous)); }
            finally { if (result.Clip.IsCreated) result.Clip.Dispose(); }
        }

        [Test]
        public void Cache_CorruptPayloadIsRejectedAndRebuiltBeforeNativeDeserialization()
        {
            FirstBake();
            var payload = Directory.GetFiles(directory, "*.blob").Single();
            var bytes = File.ReadAllBytes(payload); bytes[bytes.Length - 1] ^= 1; File.WriteAllBytes(payload, bytes);
            var result = ClipBakePipeline.Bake(CachedRequest());
            try
            {
                Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics)); Assert.That(result.FromCache, Is.False);
                Assert.That(ClipSampler.SampleBone(ref result.Clip.Value, 1, 0.25).Position.x, Is.EqualTo(3.5).Within(1e-5));
            }
            finally { if (result.Clip.IsCreated) result.Clip.Dispose(); }
        }

        [Test]
        public void Cache_FailedOrCancelledBakePreservesThePreviousPublishedEntry()
        {
            var key = FirstBake(); var entry = Path.Combine(directory, key + ".json"); var before = File.ReadAllBytes(entry);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); var request = CachedRequest(); request.Cancellation = cancellation.Token;
                var result = ClipBakePipeline.Bake(request);
                Assert.That(result.Success, Is.False); Assert.That(result.Clip.IsCreated, Is.False);
                Assert.That(File.ReadAllBytes(entry), Is.EqualTo(before));
            }
            clip.SetCurve("Missing/Bone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 1, 1));
            var failed = ClipBakePipeline.Bake(CachedRequest());
            Assert.That(failed.Success, Is.False); Assert.That(failed.Clip.IsCreated, Is.False);
            Assert.That(File.ReadAllBytes(entry), Is.EqualTo(before));
        }

        [Test]
        public void Cache_IncompatibleSchemaIsRebuilt()
        {
            var key = FirstBake(); var entry = Path.Combine(directory, key + ".json");
            var text = File.ReadAllText(entry);
            var changed = Regex.Replace(text, "\"Schema\"\\s*:\\s*\\d+", "\"Schema\":999");
            Assert.That(changed, Is.Not.EqualTo(text)); File.WriteAllText(entry, changed);
            var result = ClipBakePipeline.Bake(CachedRequest());
            try { Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics)); Assert.That(result.FromCache, Is.False); }
            finally { if (result.Clip.IsCreated) result.Clip.Dispose(); }
        }
    }
}

using System;
using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine.TestTools;

namespace AnimationSystemDOTS.Tests
{
    public class DomainReloadLifecycleTests
    {
        static readonly Guid DomainMarker = Guid.NewGuid();
        bool previousOptionsEnabled;
        EnterPlayModeOptions previousOptions;
        NativeLeakDetectionMode previousLeakMode;
        World ownedWorld;
        BlobAssetReference<RigDefinitionBlob> pendingRig;
        BlobAssetReference<AnimationLibraryBlob> pendingLibrary;

        [SetUp]
        public void SetUp()
        {
            previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            previousOptions = EditorSettings.enterPlayModeOptions;
            previousLeakMode = NativeLeakDetection.Mode;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            if (ownedWorld != null && ownedWorld.IsCreated) ownedWorld.Dispose();
            if (pendingLibrary.IsCreated) pendingLibrary.Dispose();
            if (pendingRig.IsCreated) pendingRig.Dispose();
            EditorSettings.enterPlayModeOptions = previousOptions;
            EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
            NativeLeakDetection.Mode = previousLeakMode;
        }
        [UnityTest]
        public IEnumerator DisabledDomainReload_RepeatedPlaySessionsDisposeWorldOwnedBlobsAndJobs()
        {
            var marker = DomainMarker;
            for (var cycle = 0; cycle < 2; cycle++)
            {
                yield return new EnterPlayMode(expectDomainReload: false);
                Assert.That(DomainMarker, Is.EqualTo(marker), "Domain must really remain loaded across Play sessions.");
                CreateBlobs();
                ownedWorld = new World("AnimationSystemDOTS Lifecycle " + cycle);
                var entity = ownedWorld.EntityManager.CreateEntity(typeof(AnimationRig));
                ownedWorld.EntityManager.SetComponentData(entity, new AnimationRig { Rig = pendingRig, Library = pendingLibrary });
                ownedWorld.GetOrCreateSystemManaged<RuntimeAnimationBlobOwnerSystem>().Adopt(ref pendingRig, ref pendingLibrary);
                ownedWorld.EntityManager.CreateEntity(typeof(AnimationStepContext));
                ownedWorld.EntityManager.AddBuffer<AnimationClipRequest>(entity);
                ownedWorld.EntityManager.AddBuffer<SampledBonePose>(entity);
                ownedWorld.EntityManager.AddBuffer<BoneLocalPose>(entity);
                ownedWorld.EntityManager.AddBuffer<BoneModelPose>(entity);
                ownedWorld.GetOrCreateSystemManaged<AnimationSampleSystem>().Update();
                ownedWorld.GetOrCreateSystemManaged<AnimationBlendSystem>().Update();
                ownedWorld.GetOrCreateSystemManaged<ModelPoseSystem>().Update();
                // No Complete here: Unity's Play-mode shutdown must wait for outstanding
                // ECS readers before the owner's blobs and the World are destroyed.
                yield return new ExitPlayMode();
                Assert.That(ownedWorld.IsCreated, Is.False);
                foreach (var current in World.All)
                    Assert.That(current.Name.StartsWith("AnimationSystemDOTS Lifecycle ", StringComparison.Ordinal), Is.False);
                ownedWorld = null;
                for (var frame = 0; frame < 4; frame++) yield return null;
            }
        }
        void CreateBlobs()
        {
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<RigDefinitionBlob>();
                root.Version = AnimationBakeVersion.Schema; root.ContentKey = new FixedString128Bytes("lifecycleRig");
                var parents = builder.Allocate(ref root.Parents, 1); parents[0] = -1;
                var rest = builder.Allocate(ref root.RestLocalPoses, 1); rest[0] = BoneLocalPose.Identity;
                pendingRig = builder.CreateBlobAssetReference<RigDefinitionBlob>(Allocator.Persistent);
            }
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<AnimationLibraryBlob>();
                root.Version = AnimationBakeVersion.Schema; root.RigKey = pendingRig.Value.ContentKey;
                builder.Allocate(ref root.Clips, 0);
                pendingLibrary = builder.CreateBlobAssetReference<AnimationLibraryBlob>(Allocator.Persistent);
            }
        }
    }
}

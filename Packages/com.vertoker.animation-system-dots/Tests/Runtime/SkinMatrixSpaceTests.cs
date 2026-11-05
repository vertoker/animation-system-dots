using AnimationSystemDOTS.Rendering;
using NUnit.Framework;
using Unity.Collections;
using Unity.Deformations;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace AnimationSystemDOTS.Tests
{
    public class SkinMatrixSpaceTests
    {
        World world;
        Entity rig, root, render;
        BlobAssetReference<SkinnedMeshBindingBlob> binding;

        [SetUp]
        public void SetUp()
        {
            world = new World("AnimationSystemDOTS Skin Space Test");
            var em = world.EntityManager;
            rig = em.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld));
            em.SetComponentData(rig, LocalTransform.FromPosition(10, 0, 0));
            // Deliberately stale: export must use this step's transform inputs.
            em.SetComponentData(rig, new LocalToWorld { Value = float4x4.Translate(new float3(-100, 0, 0)) });
            var models = em.AddBuffer<BoneModelPose>(rig);
            models.Add(new BoneModelPose { Matrix = Pack(float4x4.identity) });
            // Moving parent + animated root: root translation is (2, 3, 0).
            models.Add(new BoneModelPose { Matrix = Pack(float4x4.Translate(new float3(2, 3, 0))) });
            models.Add(new BoneModelPose { Matrix = Pack(float4x4.TRS(new float3(4, 5, 0), quaternion.identity, new float3(-2, 3, 1))) });
            root = em.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(Parent), typeof(BoneExportBinding));
            em.SetComponentData(root, LocalTransform.Identity);
            em.SetComponentData(root, new Parent { Value = rig });
            em.SetComponentData(root, new LocalToWorld { Value = float4x4.identity });
            em.SetComponentData(root, new BoneExportBinding { Rig = rig, BoneIndex = 1 });
            render = em.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(Parent));
            em.SetComponentData(render, LocalTransform.Identity);
            em.SetComponentData(render, new Parent { Value = root });
            em.SetComponentData(render, new LocalToWorld { Value = float4x4.identity });
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var blob = ref builder.ConstructRoot<SkinnedMeshBindingBlob>();
                blob.Version = AnimationBakeVersion.Schema; blob.RigKey = new FixedString128Bytes("skinRig"); blob.DeformationRootBone = 1;
                var remap = builder.Allocate(ref blob.BoneRemap, 2); remap[0] = 2; remap[1] = 1;
                var poses = builder.Allocate(ref blob.BindPoses, 2);
                poses[0] = float4x4.Translate(new float3(-1, 0, 0)); poses[1] = float4x4.Translate(new float3(0, -1, 0));
                var bounds = builder.Allocate(ref blob.BoneBounds, 2);
                bounds[0] = new AnimationBounds { Center = new float3(1, 1, 0), Extents = new float3(1, 1, .1f) };
                bounds[1] = new AnimationBounds { Center = new float3(2, 0, 0), Extents = new float3(1, 1, .1f) };
                blob.RigBounds = new AnimationBounds { Center = float3.zero, Extents = new float3(8) };
                binding = builder.CreateBlobAssetReference<SkinnedMeshBindingBlob>(Allocator.Persistent);
            }
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var blob = ref builder.ConstructRoot<RigDefinitionBlob>();
                blob.Version = AnimationBakeVersion.Schema; blob.ContentKey = new FixedString128Bytes("skinRig");
                var parents = builder.Allocate(ref blob.Parents, 3); parents[0] = -1; parents[1] = 0; parents[2] = 1;
                ownedRig = builder.CreateBlobAssetReference<RigDefinitionBlob>(Allocator.Persistent);
                em.AddComponentData(rig, new AnimationRig { Rig = ownedRig });
            }
        }
        BlobAssetReference<RigDefinitionBlob> ownedRig;

        static float3x4 Pack(float4x4 m) => new float3x4(m.c0.xyz, m.c1.xyz, m.c2.xyz, m.c3.xyz);
        static float4x4 Unpack(float3x4 m) => new float4x4(new float4(m.c0, 0), new float4(m.c1, 0), new float4(m.c2, 0), new float4(m.c3, 1));
        Entity Mesh(Entity deformationRoot)
        {
            var em = world.EntityManager;
            var mesh = em.CreateEntity(typeof(SkinnedMeshBinding), typeof(SkinMatrixPublication));
            em.SetComponentData(mesh, new SkinnedMeshBinding { Rig = rig, DeformationRoot = deformationRoot, Binding = binding });
            var palette = em.AddBuffer<SkinMatrix>(mesh); palette.Add(new SkinMatrix { Value = Pack(float4x4.identity) }); palette.Add(new SkinMatrix { Value = Pack(float4x4.identity) });
            return mesh;
        }
        void Evaluate()
        {
            world.GetOrCreateSystemManaged<BoneExportSystem>().Update();
            var transforms = world.GetOrCreateSystemManaged<TransformSystemGroup>();
            transforms.AddSystemToUpdateList(world.GetOrCreateSystem<ParentSystem>());
            transforms.AddSystemToUpdateList(world.GetOrCreateSystem<LocalToWorldSystem>());
            transforms.SortSystems(); transforms.Update();
            world.GetOrCreateSystemManaged<SkinMatrixPublishSystem>().Update();
            world.EntityManager.CompleteAllTrackedJobs();
        }
        [Test]
        public void Bounds_ContainOfflineEnvelopeAndProceduralPoseInEachRenderEntitySpace()
        {
            var em = world.EntityManager;
            var models = em.GetBuffer<BoneModelPose>(rig); var pose = models[2]; pose.Matrix.c3.x = 100; models[2] = pose;
            em.AddComponentData(render, new AnimatedRenderBinding { Rig = rig, Binding = binding });
            em.AddComponentData(render, new Unity.Rendering.RenderBounds());
            Evaluate(); world.GetOrCreateSystemManaged<AnimatedBoundsPublishSystem>().Update(); em.CompleteAllTrackedJobs();
            var bounds = em.GetComponentData<Unity.Rendering.RenderBounds>(render).Value;
            var minimum = bounds.Center - bounds.Extents; var maximum = bounds.Center + bounds.Extents;
            // Rig-space envelope [-8,8] transformed through rig(10) and root(12,3).
            Assert.That(math.all(minimum <= new float3(-10, -11, -8)), Is.True);
            Assert.That(math.all(maximum >= new float3(6, 5, 8)), Is.True);
            // Procedural bone x=100 with negative scale -2 maps its local box x=[0,2]
            // to world x=[106,110], hence render x=[94,98].
            Assert.That(maximum.x, Is.GreaterThanOrEqualTo(98));
        }

        [Test]
        public void Palette_SingularRootRetainsLastFinitePaletteAndReportsFailure()
        {
            var mesh = Mesh(root);
            world.EntityManager.SetComponentData(root, new LocalToWorld { Value = float4x4.Scale(new float3(0, 1, 1)) });
            world.GetOrCreateSystemManaged<SkinMatrixPublishSystem>().Update(); world.EntityManager.CompleteAllTrackedJobs();
            Assert.That(world.EntityManager.GetComponentData<SkinMatrixPublication>(mesh).Result, Is.EqualTo(SkinMatrixPublicationResult.SingularRoot));
            Assert.That(world.EntityManager.GetBuffer<SkinMatrix>(mesh)[0].Value.c0, Is.EqualTo(new float3(1, 0, 0)));
        }

        [Test]
        public void PrefabClones_RemapRigAndRootAndKeepPalettesIndependentAfterDespawn()
        {
            var mesh = Mesh(root);
            var em = world.EntityManager;
            var prefab = em.CreateEntity(typeof(Prefab));
            var group = em.AddBuffer<LinkedEntityGroup>(prefab); group.Add(prefab); group.Add(rig); group.Add(root); group.Add(render); group.Add(mesh);
            foreach (var e in new[] { rig, root, render, mesh }) em.AddComponent<Prefab>(e);
            var first = em.Instantiate(prefab); var second = em.Instantiate(prefab);
            var firstRig = em.GetBuffer<LinkedEntityGroup>(first)[1].Value; var secondRig = em.GetBuffer<LinkedEntityGroup>(second)[1].Value;
            var firstMesh = em.GetBuffer<LinkedEntityGroup>(first)[4].Value; var secondMesh = em.GetBuffer<LinkedEntityGroup>(second)[4].Value;
            Assert.That(em.GetComponentData<SkinnedMeshBinding>(firstMesh).Rig, Is.EqualTo(firstRig));
            var poses = em.GetBuffer<BoneModelPose>(secondRig); var pose = poses[2]; pose.Matrix.c3.y += 4; poses[2] = pose;
            Evaluate();
            Assert.That(em.GetBuffer<SkinMatrix>(firstMesh)[0].Value.c3.y, Is.EqualTo(2).Within(1e-5));
            Assert.That(em.GetBuffer<SkinMatrix>(secondMesh)[0].Value.c3.y, Is.EqualTo(6).Within(1e-5));
            em.DestroyEntity(first); Evaluate();
            Assert.That(em.GetBuffer<SkinMatrix>(secondMesh)[0].Value.c3.y, Is.EqualTo(6).Within(1e-5));
        }

        [Test]
        public void Palette_UsesActualAnimatedRootAndPerMeshRemapForWeightedVertices()
        {
            var first = Mesh(root); var second = Mesh(rig); Evaluate();
            var em = world.EntityManager;
            Assert.That(em.GetComponentData<LocalToWorld>(root).Position, Is.EqualTo(new float3(12, 3, 0)));
            Assert.That(em.GetComponentData<LocalToWorld>(render).Position, Is.EqualTo(new float3(12, 3, 0)), "Stock transform writer must retain the exported root and propagate it to render children.");
            var vertex = new float4(2, 1, 0, 1);
            foreach (var mesh in new[] { first, second })
            {
                var skin = em.GetBuffer<SkinMatrix>(mesh);
                var local = 0.25f * math.mul(Unpack(skin[0].Value), vertex) + 0.75f * math.mul(Unpack(skin[1].Value), vertex);
                var transform = mesh == first ? root : rig;
                var actual = math.mul(em.GetComponentData<LocalToWorld>(transform).Value, local).xyz;
                // Bone 2: (-2 * (2-1) + 14, 3*1+5)=(12,8).
                // Bone 1: (2+12, (1-1)+3)=(14,3). Weighted world=(13.5,4.25).
                Assert.That(math.distance(actual, new float3(13.5f, 4.25f, 0)), Is.LessThan(1e-5));
                Assert.That(em.GetComponentData<SkinMatrixPublication>(mesh).Result, Is.EqualTo(SkinMatrixPublicationResult.Success));
            }
        }
        [TearDown]
        public void TearDown() { world?.Dispose(); if (binding.IsCreated) binding.Dispose(); if (ownedRig.IsCreated) ownedRig.Dispose(); }
    }
}

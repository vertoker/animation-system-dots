using AnimationSystemDOTS.Rendering;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using UnityEditor;
using UnityEngine;

namespace AnimationSystemDOTS.Tests
{
    public class AnimationRenderRegistrationTests
    {
        void WithFixture(bool graphicsEnabled, System.Action<World, EntitiesGraphicsSystem, AnimationRenderRegistrationSystem, Entity, Mesh> verify)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/AnimationSystemDOTS.Sandbox/Fixtures/Materials/ComputeDeformation.shadergraph");
            var material = new Material(shader); var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }; mesh.subMeshCount = 2;
                mesh.SetTriangles(new[] { 0, 1, 2 }, 0); mesh.SetTriangles(new[] { 0, 1, 2 }, 1);
                using (var world = new World("AnimationSystemDOTS Registration Change Test"))
                {
                    var graphics = world.GetOrCreateSystemManaged<EntitiesGraphicsSystem>(); graphics.Enabled = graphicsEnabled;
                    var system = world.GetOrCreateSystemManaged<AnimationRenderRegistrationSystem>();
                    var em = world.EntityManager; var entity = em.CreateEntity(typeof(AnimatedRenderBinding), typeof(MaterialMeshInfo));
                    em.AddSharedComponent(entity, new RenderMeshArray(new[] { material }, new[] { mesh }));
                    em.SetComponentData(entity, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                    verify(world, graphics, system, entity, mesh);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(material); }
        }

        [Test]
        public void Registration_DisabledGraphicsPreservesStaticAssetReferences()
            => WithFixture(false, (world, graphics, system, entity, mesh) =>
            {
                system.Update();
                Assert.That(world.EntityManager.GetComponentData<MaterialMeshInfo>(entity).Mesh, Is.EqualTo(-1));
                Assert.That(world.EntityManager.HasComponent<AnimationRenderRegistration>(entity), Is.False);
            });

        [Test]
        public void Registration_SubMeshChangeKeepsOwnedMaterialAndMeshAlive()
            => WithFixture(true, (world, graphics, system, entity, mesh) =>
            {
                system.Update(); var em = world.EntityManager; var info = em.GetComponentData<MaterialMeshInfo>(entity);
                Assert.That(graphics.GetMesh(info.MeshID), Is.SameAs(mesh), "Initial registration is missing.");
                info.SubMesh = 1; em.SetComponentData(entity, info); system.Update();
                Assert.That(graphics.GetMesh(info.MeshID), Is.SameAs(mesh));
                Assert.That(em.GetComponentData<MaterialMeshInfo>(entity).SubMesh, Is.EqualTo(1));
            });

        [Test]
        public void Registration_PartialExternalMaterialSwapRetainsOnlyOwnedMesh()
            => WithFixture(true, (world, graphics, system, entity, mesh) =>
            {
                system.Update(); var em = world.EntityManager; var info = em.GetComponentData<MaterialMeshInfo>(entity);
                Assert.That(graphics.GetMesh(info.MeshID), Is.SameAs(mesh), "Initial registration is missing.");
                var replacement = new Material(graphics.GetMaterial(info.MaterialID));
                var external = graphics.RegisterMaterial(replacement);
                try
                {
                    em.SetComponentData(entity, new MaterialMeshInfo(external, info.MeshID)); system.Update();
                    Assert.That(graphics.GetMesh(info.MeshID), Is.SameAs(mesh));
                    em.RemoveComponent<AnimatedRenderBinding>(entity); system.Update();
                    Assert.That(graphics.GetMaterial(external), Is.SameAs(replacement), "The external material registration belongs to its caller.");
                    Assert.That(graphics.GetMesh(info.MeshID), Is.Null);
                }
                finally { graphics.UnregisterMaterial(external); UnityEngine.Object.DestroyImmediate(replacement); }
            });

        [Test]
        public void UnmanagedRenderMeshArray_RegistersPublicRuntimeIdsAndReleasesOnDespawn()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/AnimationSystemDOTS.Sandbox/Fixtures/Materials/ComputeDeformation.shadergraph");
            var material = new Material(shader); var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }; mesh.triangles = new[] { 0, 1, 2 };
            using (var world = new World("AnimationSystemDOTS Registration Test"))
            {
                var graphics = world.GetOrCreateSystemManaged<EntitiesGraphicsSystem>();
                var system = world.GetOrCreateSystemManaged<AnimationRenderRegistrationSystem>();
                var em = world.EntityManager;
                var entity = em.CreateEntity(typeof(AnimatedRenderBinding), typeof(MaterialMeshInfo));
                var assets = new RenderMeshArray(new[] { material }, new[] { mesh });
                em.AddSharedComponent(entity, assets);
                em.SetComponentData(entity, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                system.Update();
                var info = em.GetComponentData<MaterialMeshInfo>(entity);
                Assert.That(info.Material, Is.GreaterThan(0), "Stock 6.6 registration fails for a signed unmanaged shared index; use public runtime material IDs.");
                Assert.That(info.Mesh, Is.GreaterThan(0));
                Assert.That(graphics.GetMesh(info.MeshID), Is.SameAs(mesh));
                var clone = em.Instantiate(entity); system.Update();
                em.DestroyEntity(entity); system.Update();
                Assert.That(em.Exists(entity), Is.False, "Registration cleanup must release handles and let despawn finish.");
                var cloneInfo = em.GetComponentData<MaterialMeshInfo>(clone);
                Assert.That(graphics.GetMesh(cloneInfo.MeshID), Is.SameAs(mesh), "Instantiating an already registered entity must acquire an independent registration before its source despawns.");
                em.DestroyEntity(clone); system.Update();
                Assert.That(graphics.GetMesh(info.MeshID), Is.Null, "Despawn must release the native mesh registration, not only its cleanup entity.");
            }
            UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(material);
        }
    }
}

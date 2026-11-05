using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;

namespace AnimationSystemDOTS.Rendering
{
    public struct AnimationRenderRegistration : ICleanupComponentData
    {
        public MaterialMeshInfo Original;
        public MaterialMeshInfo Published;
        public byte OwnsMaterial;
        public byte OwnsMesh;
    }

    // Cleanup components are deliberately omitted by EntityManager.Instantiate.
    // This cloneable provenance lets a live clone acquire its own references.
    public struct AnimationRenderSource : IComponentData
    {
        public AnimationRenderRegistration Registration;
    }

    // Entities 6.6 unmanaged shared component indices have the high bit set.
    // Stock deformation currently rejects those indices as negative. Runtime
    // MaterialMeshInfo IDs are a supported public route around that lookup.
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(DeformationsInPresentation))]
    [CreateAfter(typeof(EntitiesGraphicsSystem))]
    public partial class AnimationRenderRegistrationSystem : SystemBase
    {
        EntitiesGraphicsSystem graphics;
        EntityQuery pending, changed, removedBinding, removedInfo, removedAssets, owned;
        protected override void OnCreate()
        {
            graphics = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            if (graphics == null || !EntitiesGraphicsSystem.EntitiesGraphicsEnabled || !graphics.Enabled)
            { graphics = null; Enabled = false; return; }
            var options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState;
            pending = new EntityQueryBuilder(Allocator.Temp).WithAll<AnimatedRenderBinding, MaterialMeshInfo, RenderMeshArray>()
                .WithNone<AnimationRenderRegistration>().WithOptions(options).Build(this);
            changed = new EntityQueryBuilder(Allocator.Temp).WithAll<AnimationRenderRegistration, MaterialMeshInfo>()
                .WithOptions(options).Build(this);
            changed.SetChangedVersionFilter(ComponentType.ReadOnly<MaterialMeshInfo>());
            removedBinding = new EntityQueryBuilder(Allocator.Temp).WithAll<AnimationRenderRegistration>().WithNone<AnimatedRenderBinding>().WithOptions(options).Build(this);
            // WithNone on an enableable component ignores its presence under
            // IgnoreComponentEnabledState. Cleanup needs actual structural absence.
            removedInfo = new EntityQueryBuilder(Allocator.Temp).WithAll<AnimationRenderRegistration>().WithAbsent<MaterialMeshInfo>().WithOptions(options).Build(this);
            removedAssets = new EntityQueryBuilder(Allocator.Temp).WithAll<AnimationRenderRegistration>().WithNone<RenderMeshArray>().WithOptions(options).Build(this);
            owned = new EntityQueryBuilder(Allocator.Temp).WithAll<AnimationRenderRegistration>().WithOptions(options).Build(this);
        }
        protected override void OnUpdate()
        {
            if (graphics == null) return;
            ReleaseMissing(removedBinding); ReleaseMissing(removedInfo); ReleaseMissing(removedAssets);
            // Live baking or another explicit owner may replace the published
            // info. Release our previous references before resolving new assets.
            using (var entities = changed.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    var registration = EntityManager.GetComponentData<AnimationRenderRegistration>(entity);
                    if (!Same(EntityManager.GetComponentData<MaterialMeshInfo>(entity), registration.Published))
                        UpdateRegistration(entity, registration, true);
                }
            using (var entities = pending.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    var provenance = EntityManager.HasComponent<AnimationRenderSource>(entity)
                        ? EntityManager.GetComponentData<AnimationRenderSource>(entity).Registration : default;
                    UpdateRegistration(entity, provenance, false);
                }
        }

        void UpdateRegistration(Entity entity, AnimationRenderRegistration previous, bool alreadyOwned)
        {
            var current = EntityManager.GetComponentData<MaterialMeshInfo>(entity);
            if (current.HasMaterialMeshIndexRange || !EntityManager.HasComponent<RenderMeshArray>(entity)) return;
            var materialWasOurs = previous.OwnsMaterial != 0 && current.Material == previous.Published.Material;
            var meshWasOurs = previous.OwnsMesh != 0 && current.Mesh == previous.Published.Mesh;
            var reuseMaterial = alreadyOwned && materialWasOurs;
            var reuseMesh = alreadyOwned && meshWasOurs;
            var acquireMaterial = current.Material < 0 || (!alreadyOwned && materialWasOurs);
            var acquireMesh = current.Mesh < 0 || (!alreadyOwned && meshWasOurs);
            if (!alreadyOwned && !acquireMaterial && !acquireMesh)
            { EntityManager.RemoveComponent<AnimationRenderSource>(entity); return; }
            var original = current;
            if (materialWasOurs) original.Material = previous.Original.Material;
            if (meshWasOurs) original.Mesh = previous.Original.Mesh;
            var published = current;
            var assets = EntityManager.GetSharedComponent<RenderMeshArray>(entity);
            var material = acquireMaterial ? assets.GetMaterial(original) : null;
            var mesh = acquireMesh ? assets.GetMesh(original) : null;
            if ((acquireMaterial && material == null) || (acquireMesh && mesh == null)) return;
            if (acquireMaterial)
            {
                var id = graphics.RegisterMaterial(material);
                if (id == UnityEngine.Rendering.BatchMaterialID.Null) return;
                published.Material = (int)id.value;
            }
            if (acquireMesh)
            {
                var id = graphics.RegisterMesh(mesh);
                if (id == UnityEngine.Rendering.BatchMeshID.Null)
                {
                    if (acquireMaterial) graphics.UnregisterMaterial(published.MaterialID);
                    return;
                }
                published.Mesh = (int)id.value;
            }
            // Acquire first, then release replaced handles. Unchanged owned IDs
            // remain ours, even when a caller changes only a submesh or one asset.
            if (alreadyOwned)
            {
                if (previous.OwnsMaterial != 0 && !reuseMaterial) graphics.UnregisterMaterial(previous.Published.MaterialID);
                if (previous.OwnsMesh != 0 && !reuseMesh) graphics.UnregisterMesh(previous.Published.MeshID);
            }
            var registration = new AnimationRenderRegistration
            {
                Original = original, Published = published,
                OwnsMaterial = (byte)(reuseMaterial || acquireMaterial ? 1 : 0),
                OwnsMesh = (byte)(reuseMesh || acquireMesh ? 1 : 0)
            };
            EntityManager.SetComponentData(entity, published);
            if (registration.OwnsMaterial == 0 && registration.OwnsMesh == 0)
            {
                if (alreadyOwned) EntityManager.RemoveComponent<AnimationRenderRegistration>(entity);
                EntityManager.RemoveComponent<AnimationRenderSource>(entity);
                return;
            }
            if (alreadyOwned) EntityManager.SetComponentData(entity, registration);
            else EntityManager.AddComponentData(entity, registration);
            var source = new AnimationRenderSource { Registration = registration };
            if (EntityManager.HasComponent<AnimationRenderSource>(entity)) EntityManager.SetComponentData(entity, source);
            else EntityManager.AddComponentData(entity, source);
        }
        void ReleaseMissing(EntityQuery query)
        {
            using (var entities = query.ToEntityArray(Allocator.Temp)) foreach (var entity in entities) Release(entity, true);
        }
        void Release(Entity entity, bool restore)
        {
            var registration = EntityManager.GetComponentData<AnimationRenderRegistration>(entity);
            if (restore && EntityManager.HasComponent<MaterialMeshInfo>(entity))
            {
                var current = EntityManager.GetComponentData<MaterialMeshInfo>(entity);
                if (registration.OwnsMaterial != 0 && current.Material == registration.Published.Material) current.Material = registration.Original.Material;
                if (registration.OwnsMesh != 0 && current.Mesh == registration.Published.Mesh) current.Mesh = registration.Original.Mesh;
                EntityManager.SetComponentData(entity, current);
            }
            if (registration.OwnsMaterial != 0) graphics.UnregisterMaterial(registration.Published.MaterialID);
            if (registration.OwnsMesh != 0) graphics.UnregisterMesh(registration.Published.MeshID);
            EntityManager.RemoveComponent<AnimationRenderSource>(entity);
            EntityManager.RemoveComponent<AnimationRenderRegistration>(entity);
        }
        static bool Same(MaterialMeshInfo a, MaterialMeshInfo b) => a.Material == b.Material && a.Mesh == b.Mesh && a.SubMesh == b.SubMesh;
        protected override void OnDestroy()
        {
            if (graphics == null) return;
            using (var entities = owned.ToEntityArray(Allocator.Temp)) foreach (var entity in entities) Release(entity, false);
        }
    }
}

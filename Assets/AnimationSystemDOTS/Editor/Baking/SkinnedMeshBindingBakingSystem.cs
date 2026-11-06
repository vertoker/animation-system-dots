using AnimationSystemDOTS.Rendering;
using Unity.Collections;
using Unity.Deformations;
using Unity.Entities;
using Unity.Entities.Hybrid.Baking;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace AnimationSystemDOTS.Editor.Baking
{
    [BakingType]
    public struct SkinnedMeshBindingBakingData : IBufferElementData
    {
        public Entity Mesh;
        public Entity DeformationRoot;
        public EntityId RendererId;
        public BlobAssetReference<SkinnedMeshBindingBlob> Binding;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(PostBakingSystemGroup))]
    public partial class SkinnedMeshBindingBakingSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            // Rebuild only our derived bindings, including prefab and disabled entities.
            // This also retracts links when a mesh or rig authoring is removed live.
            var options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities;
            using (var query = new EntityQueryBuilder(Allocator.Temp).WithAll<SkinnedMeshBinding>().WithOptions(options).Build(EntityManager))
            {
                EntityManager.RemoveComponent(query, new ComponentTypeSet(typeof(SkinnedMeshBinding), typeof(SkinMatrixPublication)));
            }
            using (var query = new EntityQueryBuilder(Allocator.Temp).WithAll<AnimatedRenderBinding>().WithOptions(options).Build(EntityManager))
                EntityManager.RemoveComponent<AnimatedRenderBinding>(query);
            using (var query = new EntityQueryBuilder(Allocator.Temp).WithAll<BoneExportBinding>().WithOptions(options).Build(EntityManager))
                EntityManager.RemoveComponent<BoneExportBinding>(query);
            using (var query = new EntityQueryBuilder(Allocator.Temp).WithAll<SkinnedMeshBindingBakingData, AnimationRig>().WithOptions(options).Build(EntityManager))
            using (var entities = query.ToEntityArray(Allocator.Temp))
            foreach (var rig in entities)
            {
                // Copy the input: adding components may invalidate buffer handles.
                using (var inputs = EntityManager.GetBuffer<SkinnedMeshBindingBakingData>(rig).ToNativeArray(Allocator.Temp))
                foreach (var input in inputs)
                {
                    if (!EntityManager.HasBuffer<SkinMatrix>(input.Mesh))
                    { Debug.LogError("Animation binding requires the stock SkinnedMeshRenderer SkinMatrix buffer."); continue; }
                    EntityManager.AddComponentData(input.Mesh, new SkinnedMeshBinding { Rig = rig, DeformationRoot = input.DeformationRoot, Binding = input.Binding });
                    EntityManager.AddComponentData(input.Mesh, new SkinMatrixPublication());
                    if (input.DeformationRoot != rig && !EntityManager.HasComponent<BoneExportBinding>(input.DeformationRoot))
                        EntityManager.AddComponentData(input.DeformationRoot, new BoneExportBinding { Rig = rig, BoneIndex = input.Binding.Value.DeformationRootBone });
                    if (!EntityManager.HasBuffer<AdditionalEntitiesBakingData>(input.Mesh)) continue;
                    using (var additional = EntityManager.GetBuffer<AdditionalEntitiesBakingData>(input.Mesh).ToNativeArray(Allocator.Temp))
                    foreach (var child in additional)
                        if (child.AuthoringComponentID == input.RendererId && EntityManager.HasComponent<RenderBounds>(child.Value))
                            EntityManager.AddComponentData(child.Value, new AnimatedRenderBinding { Rig = rig, Binding = input.Binding });
                }
            }
        }
    }
}

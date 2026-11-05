using Unity.Entities;

namespace AnimationSystemDOTS.Rendering
{
    public struct AnimatedRenderBinding : IComponentData
    {
        public Entity Rig;
        public BlobAssetReference<SkinnedMeshBindingBlob> Binding;
    }
}

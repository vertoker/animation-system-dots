using Unity.Entities;

namespace AnimationSystemDOTS.Rendering
{
    public struct SkinnedMeshBinding : IComponentData
    {
        public Entity Rig;
        public Entity DeformationRoot;
        public BlobAssetReference<SkinnedMeshBindingBlob> Binding;
    }

    public enum SkinMatrixPublicationResult : byte { Success, MissingPose, InvalidBinding, SingularRoot }

    public struct SkinMatrixPublication : IComponentData
    {
        public SkinMatrixPublicationResult Result;
    }
}

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystemDOTS
{
    public struct AnimationBounds
    {
        public float3 Center;
        public float3 Extents;
    }

    public struct SkinnedMeshBindingBlob
    {
        public int Version;
        public FixedString128Bytes ContentKey;
        public FixedString128Bytes RigKey;
        public int DeformationRootBone;
        public BlobArray<int> BoneRemap;
        public BlobArray<float4x4> BindPoses;
        public BlobArray<AnimationBounds> BoneBounds;
        public AnimationBounds RigBounds;
    }
}

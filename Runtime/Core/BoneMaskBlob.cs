using Unity.Collections;
using Unity.Entities;

namespace AnimationSystemDOTS
{
    public struct BoneMaskBlob
    {
        public int Version;
        public FixedString128Bytes ContentKey;
        public FixedString128Bytes RigKey;
        public BlobArray<float> Weights;
    }
}

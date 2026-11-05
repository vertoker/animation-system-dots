using Unity.Collections;
using Unity.Entities;

namespace AnimationSystemDOTS
{
    public readonly struct ClipId
    {
        public readonly int Value;
        public ClipId(int value) => Value = value;
        public static ClipId Invalid => new ClipId(-1);
    }

    public struct AnimationLibraryBlob
    {
        public int Version;
        public FixedString128Bytes ContentKey;
        public FixedString128Bytes RigKey;
        // Clips are embedded, including their nested arrays. There are no external blob pointers.
        public BlobArray<AnimationClipBlob> Clips;
    }

    public struct AnimationRig : IComponentData
    {
        public BlobAssetReference<RigDefinitionBlob> Rig;
        public BlobAssetReference<AnimationLibraryBlob> Library;
    }
}

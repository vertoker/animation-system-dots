using Unity.Entities;

namespace AnimationSystemDOTS
{
    public enum PoseBlendMode : byte { Override, Additive }
    public enum ClipLoopBoundary : byte { AuthoredEndpoint, Start, End }
    public struct AnimationClipRequest : IBufferElementData
    {
        public ClipId Clip;
        public double PreviousTime;
        public double Time;
        public float Weight;
        public int Layer;
        public PoseBlendMode BlendMode;
        public ClipLoopBoundary LoopBoundary;
        public BlobAssetReference<BoneMaskBlob> Mask;
        public ulong InstanceSequence;
    }
}

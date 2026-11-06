using Unity.Entities;

namespace AnimationSystemDOTS
{
    public enum ControllerPoseSource : byte { Motion, Captured, PreviousLayer, Rest }
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
        // A controller owns embedded mask data for the whole request lifetime.
        // Standalone scripted requests continue using Mask directly.
        public BlobAssetReference<AnimatorControllerBlob> MaskController;
        public int ControllerMask;
        public float LayerWeight;
        // Native controller transitions blend complete state streams. Preserve
        // each state's influence separately from its leaf motion weights.
        public float StateWeight, MotionWeight;
        public byte StateSlot;
        public ControllerPoseSource PoseSource;
    }
}

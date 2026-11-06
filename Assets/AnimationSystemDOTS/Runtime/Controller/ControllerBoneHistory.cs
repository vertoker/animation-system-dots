using Unity.Entities;

namespace AnimationSystemDOTS
{
    // Raw layer poses, before masks and layer weights. Interruptions capture
    // the previous evaluated pose once; subsequent fades keep that source fixed.
    public struct ControllerBoneHistory : IBufferElementData
    {
        public BlobAssetReference<AnimatorControllerBlob> Definition;
        public BoneLocalPose Previous, BeforeStep, Frozen;
        public ulong FrozenSequence;
        public byte Initialized, BeforeInitialized;
        public ControllerHistoryStep Step;
    }
    public struct ControllerHistoryStep
    {
        public ulong Tick;
        public uint Revision;
        public AnimationStepMode Mode;
        public byte Initialized;
        public bool Repeats(in AnimationStepContext step) => Initialized != 0 &&
            (step.Mode == AnimationStepMode.Normal && step.Tick <= Tick || step.Tick == Tick && step.Revision == Revision && step.Mode == Mode);
        public void Set(in AnimationStepContext step)
        { Tick = step.Tick; Revision = step.Revision; Mode = step.Mode; Initialized = 1; }
    }
}

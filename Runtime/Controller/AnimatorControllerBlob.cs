using Unity.Collections;
using Unity.Entities;

namespace AnimationSystemDOTS
{
    public struct AnimatorControllerBlob
    {
        public const int CurrentVersion = 1;
        public int Version;
        public FixedString128Bytes LibraryKey;
        public int DefaultState;
        public int AnyStart, AnyCount, EntryStart, EntryCount;
        public BlobArray<AnimationParameterDefinition> Parameters;
        public BlobArray<ControllerStateDefinition> States;
        public BlobArray<ControllerTransitionDefinition> Transitions;
        public BlobArray<ControllerCondition> Conditions;
    }

    public struct AnimationController : IComponentData
    {
        public BlobAssetReference<AnimatorControllerBlob> Definition;
    }

    public enum AnimationParameterType : byte { Float, Int, Bool, Trigger }
    public struct AnimationParameterDefinition
    {
        public FixedString128Bytes Name;
        public AnimationParameterType Type;
        public float DefaultFloat;
        public int DefaultInt;
    }
    public struct ControllerStateDefinition
    {
        public FixedString128Bytes Path;
        public ClipId Clip;
        public double Duration;
        public float Speed, CycleOffset;
        public int SpeedParameter;
        public int TransitionStart, TransitionCount;
    }
    public enum ControllerConditionMode : byte { If, IfNot, Greater, Less, Equals, NotEqual }
    public struct ControllerCondition
    {
        public int Parameter;
        public ControllerConditionMode Mode;
        public float Threshold;
    }
    public struct ControllerTransitionDefinition
    {
        // -1 is Exit; re-enter the root machine through its Entry/default selection.
        public int Destination;
        public int ConditionStart, ConditionCount;
        public float Duration, Offset, ExitTime;
        public byte HasExitTime, FixedDuration, CanTransitionToSelf;
    }
}

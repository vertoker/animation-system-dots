using Unity.Collections;
using Unity.Entities;

namespace AnimationSystemDOTS
{
    public struct AnimatorControllerBlob
    {
        public const int CurrentVersion = 5;
        public int Version;
        public FixedString128Bytes LibraryKey;
        public int DefaultState;
        public int AnyStart, AnyCount, EntryStart, EntryCount;
        public BlobArray<AnimationParameterDefinition> Parameters;
        public BlobArray<ControllerStateDefinition> States;
        public BlobArray<ControllerTransitionDefinition> Transitions;
        public BlobArray<ControllerCondition> Conditions;
        // Post-order nodes: every child precedes its parent. Each occurrence owns
        // a node so repeated clips retain independent child timing.
        public BlobArray<ControllerMotionDefinition> Motions;
        public BlobArray<ControllerMotionChild> MotionChildren;
        public BlobArray<ControllerLayerDefinition> Layers;
        public BlobArray<BoneMaskBlob> Masks;
        public BlobArray<ControllerMachineDefinition> Machines;
    }
    public struct ControllerLayerDefinition
    {
        public FixedString128Bytes Name;
        public int StateStart, StateCount, DefaultState;
        public int AnyStart, AnyCount, EntryStart, EntryCount;
        public int SyncedLayer, Mask;
        public float DefaultWeight;
        public PoseBlendMode BlendMode;
        public byte SyncedTiming;
        public int RootMachine;
    }
    public struct ControllerMachineDefinition
    {
        public FixedString128Bytes Path;
        public int Parent, DefaultState;
        public int AnyStart, AnyCount, EntryStart, EntryCount, ExitStart, ExitCount;
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
        public int Motion;
        public double Duration;
        public float Speed, CycleOffset;
        public int SpeedParameter;
        public int TransitionStart, TransitionCount;
        public int Machine;
        public int TimeParameter, CycleParameter;
        public byte HasTimeParameter, HasCycleParameter;
    }
    public enum ControllerMotionType : byte { Clip, Simple1D, SimpleDirectional2D, FreeformDirectional2D, FreeformCartesian2D, Direct }
    public struct ControllerMotionDefinition
    {
        public ControllerMotionType Type;
        public ClipId Clip;
        public int ParameterX, ParameterY, ChildStart, ChildCount;
        public byte NormalizeDirect;
    }
    public struct ControllerMotionChild
    {
        public int Motion, DirectParameter;
        public float Threshold, TimeScale, CycleOffset;
        public Unity.Mathematics.float2 Position;
    }
    public struct ControllerMotionValue : IBufferElementData
    {
        public double Duration, PreviousTime, Time, PhaseOffset;
        public float Weight, ChildWeight;
        public byte Reversed;
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
        public ControllerDestinationKind DestinationKind;
        public int SourceMachine;
        public ControllerInterruptionSource Interruption;
        public byte OrderedInterruption;
    }
    public enum ControllerDestinationKind : byte { State, Machine, Exit }
    public enum ControllerInterruptionSource : byte { None, Source, Destination, SourceThenDestination, DestinationThenSource }
}

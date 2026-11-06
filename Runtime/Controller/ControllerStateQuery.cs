using Unity.Entities;

namespace AnimationSystemDOTS
{
    public enum ControllerEvaluationResult : byte { Success, InvalidDefinition, InvalidParameters }
    public struct ControllerLayerState : IComponentData
    {
        public int CurrentState, NextState;
        public double NormalizedTime, NextNormalizedTime;
        // Seconds for fixed fades, source normalized units otherwise. Dynamic
        // Blend Tree duration changes the normalized progress rate every step.
        public double TransitionTime, TransitionDuration;
        public ulong CurrentSequence, NextSequence, Sequence;
        public byte Initialized;
        public byte NormalizedTransition;
        public ControllerEvaluationResult Result;
        public byte RetainPose;
    }
    public struct ControllerStateSnapshot
    {
        public int CurrentState, NextState;
        public double NormalizedTime, NextNormalizedTime, TransitionProgress;
        public bool IsInTransition;
    }
    public static class ControllerStateQuery
    {
        public static bool TryGet(in ControllerLayerState layer, out ControllerStateSnapshot snapshot)
        {
            snapshot = default;
            if (layer.Initialized == 0 || layer.Result != ControllerEvaluationResult.Success) return false;
            snapshot = new ControllerStateSnapshot { CurrentState = layer.CurrentState, NextState = layer.NextState,
                NormalizedTime = layer.NormalizedTime, NextNormalizedTime = layer.NextNormalizedTime,
                IsInTransition = layer.NextState >= 0,
                TransitionProgress = layer.NextState >= 0 && layer.TransitionDuration > 0 ? layer.TransitionTime / layer.TransitionDuration : 0 };
            return true;
        }
    }
}

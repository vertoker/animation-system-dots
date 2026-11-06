using Unity.Entities;

namespace AnimationSystemDOTS
{
    public enum ScriptedCommandType : byte { Play, CrossFade, Seek, Pause, SetSpeed }
    public enum PlaybackCommandResult : byte { Success, InvalidClip, InvalidValue, StaleSequence }
    public struct ScriptedPlaybackCommand : IBufferElementData
    {
        public ScriptedCommandType Type;
        public ClipId Clip;
        public double Time;
        public double Duration;
        public double Speed;
        public ulong Sequence;
        public byte IsPaused;
        public static ScriptedPlaybackCommand Play(ClipId clip, ulong sequence, double time = 0) => new ScriptedPlaybackCommand { Type = ScriptedCommandType.Play, Clip = clip, Sequence = sequence, Time = time };
        public static ScriptedPlaybackCommand CrossFade(ClipId clip, double duration, ulong sequence, double time = 0) => new ScriptedPlaybackCommand { Type = ScriptedCommandType.CrossFade, Clip = clip, Sequence = sequence, Duration = duration, Time = time };
        public static ScriptedPlaybackCommand Seek(double time, ulong sequence) => new ScriptedPlaybackCommand { Type = ScriptedCommandType.Seek, Time = time, Sequence = sequence };
        public static ScriptedPlaybackCommand Pause(bool paused, ulong sequence) => new ScriptedPlaybackCommand { Type = ScriptedCommandType.Pause, IsPaused = paused ? (byte)1 : (byte)0, Sequence = sequence };
        public static ScriptedPlaybackCommand SetSpeed(double speed, ulong sequence) => new ScriptedPlaybackCommand { Type = ScriptedCommandType.SetSpeed, Speed = speed, Sequence = sequence };
    }
    public struct ClipPlaybackState : IComponentData
    {
        public ClipId Clip;
        public double PreviousTime;
        public double Time;
        public double Speed;
        public ClipId FadeSource;
        public double FadeSourcePreviousTime;
        public double FadeSourceTime;
        public double FadeDuration;
        public double FadeProgress;
        public ulong InstanceSequence;
        public ulong FadeSourceSequence;
        public ulong LastCommandSequence;
        public byte HasCommandSequence;
        public byte IsPaused;
        public PlaybackCommandResult LastCommandResult;
        public static ClipPlaybackState Initial => new ClipPlaybackState { Clip = ClipId.Invalid, FadeSource = ClipId.Invalid, Speed = 1 };
    }
}

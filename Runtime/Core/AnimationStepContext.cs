using Unity.Entities;

namespace AnimationSystemDOTS
{
    public enum AnimationStepMode : byte { Normal, Replay, Seek }
    public struct AnimationStepContext : IComponentData
    {
        public ulong Tick;
        public double DeltaTime;
        public AnimationStepMode Mode;
        public uint Revision;
    }
    public struct AnimationClock : IComponentData
    {
        public ulong Tick;
        public double Time;
        public uint Revision;
        public AnimationStepMode Mode;
        public byte HasTick;
    }
    public enum PlaybackDriver : byte { Scripted, Controller }
    public struct AnimationPlaybackMode : IComponentData { public PlaybackDriver Value; }
}

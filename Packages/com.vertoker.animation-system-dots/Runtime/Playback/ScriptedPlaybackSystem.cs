using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystemDOTS
{
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    public partial class ScriptedPlaybackSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<AnimationStepContext>();
        protected override void OnUpdate()
        {
            var step = SystemAPI.GetSingleton<AnimationStepContext>();
            if (!math.isfinite(step.DeltaTime) || step.DeltaTime < 0)
                throw new ArgumentException("Animation delta time must be finite and nonnegative; reverse playback uses negative speed.");
            Dependency = new PlaybackJob { Step = step }.ScheduleParallel(Dependency);
        }

        [BurstCompile]
        partial struct PlaybackJob : IJobEntity
        {
            public AnimationStepContext Step;
            void Execute(in AnimationRig rig, in AnimationPlaybackMode mode, ref AnimationClock clock, ref ClipPlaybackState state,
                DynamicBuffer<ScriptedPlaybackCommand> commands, DynamicBuffer<AnimationClipRequest> requests)
            {
                if (mode.Value != PlaybackDriver.Scripted) return;
                if (clock.HasTick != 0)
                {
                    if (Step.Mode == AnimationStepMode.Normal && Step.Tick <= clock.Tick) return;
                    if (Step.Tick == clock.Tick && Step.Revision == clock.Revision && Step.Mode == clock.Mode) return;
                }
                if (!rig.Library.IsCreated)
                {
                    state.LastCommandResult = PlaybackCommandResult.InvalidClip;
                    commands.Clear(); requests.Clear(); return;
                }
                ref var library = ref rig.Library.Value;
                // Stable insertion sort: command buffers are normally tiny and equal sequences
                // preserve producer order; subsequent duplicate/stale sequences are rejected.
                for (var i = 1; i < commands.Length; i++)
                {
                    var next = commands[i]; var j = i - 1;
                    while (j >= 0 && commands[j].Sequence > next.Sequence) { commands[j + 1] = commands[j]; j--; }
                    commands[j + 1] = next;
                }
                var seek = Step.Mode == AnimationStepMode.Seek;
                for (var i = 0; i < commands.Length; i++)
                {
                    var command = commands[i];
                    if (state.HasCommandSequence != 0 && command.Sequence <= state.LastCommandSequence)
                    {
                        state.LastCommandResult = PlaybackCommandResult.StaleSequence; continue;
                    }
                    state.LastCommandSequence = command.Sequence; state.HasCommandSequence = 1;
                    state.LastCommandResult = Validate(command, ref library);
                    if (state.LastCommandResult != PlaybackCommandResult.Success) continue;
                    switch (command.Type)
                    {
                        case ScriptedCommandType.Play:
                        case ScriptedCommandType.CrossFade:
                            if (command.Type == ScriptedCommandType.CrossFade && command.Duration > 0 && ValidClip(state.Clip, ref library))
                            {
                                state.FadeSource = state.Clip; state.FadeSourceTime = state.Time;
                                state.FadeSourceSequence = state.InstanceSequence; state.FadeDuration = command.Duration; state.FadeProgress = 0;
                            }
                            else { state.FadeSource = ClipId.Invalid; state.FadeDuration = state.FadeProgress = 0; }
                            state.Clip = command.Clip; state.Time = Clamp(command.Time, ref library.Clips[state.Clip.Value]);
                            state.InstanceSequence = command.Sequence; state.IsPaused = 0;
                            break;
                        case ScriptedCommandType.Seek:
                            if (ValidClip(state.Clip, ref library)) state.Time = Clamp(command.Time, ref library.Clips[state.Clip.Value]);
                            state.FadeSource = ClipId.Invalid; state.FadeDuration = state.FadeProgress = 0; seek = true;
                            break;
                        case ScriptedCommandType.Pause: state.IsPaused = command.IsPaused; break;
                        case ScriptedCommandType.SetSpeed: state.Speed = command.Speed; break;
                    }
                }
                commands.Clear();
                var dt = seek || state.IsPaused != 0 ? 0 : Step.DeltaTime;
                state.PreviousTime = state.Time;
                if (ValidClip(state.Clip, ref library)) state.Time = Advance(state.Time, state.Speed, dt, ref library.Clips[state.Clip.Value]);
                if (ValidClip(state.FadeSource, ref library))
                {
                    state.FadeSourcePreviousTime = state.FadeSourceTime;
                    state.FadeSourceTime = Advance(state.FadeSourceTime, state.Speed, dt, ref library.Clips[state.FadeSource.Value]);
                    state.FadeProgress = math.min(state.FadeDuration, state.FadeProgress + dt);
                    if (state.FadeProgress >= state.FadeDuration) state.FadeSource = ClipId.Invalid;
                }
                requests.Clear();
                if (ValidClip(state.Clip, ref library))
                {
                    var weight = 1f;
                    if (ValidClip(state.FadeSource, ref library))
                    {
                        weight = (float)(state.FadeProgress / state.FadeDuration);
                        requests.Add(new AnimationClipRequest { Clip = state.FadeSource, PreviousTime = state.FadeSourcePreviousTime,
                            Time = state.FadeSourceTime, Weight = 1 - weight, InstanceSequence = state.FadeSourceSequence });
                    }
                    requests.Add(new AnimationClipRequest { Clip = state.Clip, PreviousTime = state.PreviousTime,
                        Time = state.Time, Weight = weight, InstanceSequence = state.InstanceSequence });
                }
                clock.Tick = Step.Tick; clock.Revision = Step.Revision; clock.Mode = Step.Mode; clock.HasTick = 1;
                if (Step.Mode != AnimationStepMode.Seek && math.isfinite(clock.Time + Step.DeltaTime)) clock.Time += Step.DeltaTime;
            }

            static bool ValidClip(ClipId id, ref AnimationLibraryBlob library) => (uint)id.Value < (uint)library.Clips.Length;
            static PlaybackCommandResult Validate(in ScriptedPlaybackCommand command, ref AnimationLibraryBlob library)
            {
                switch (command.Type)
                {
                    case ScriptedCommandType.Play:
                    case ScriptedCommandType.CrossFade:
                        if (!ValidClip(command.Clip, ref library)) return PlaybackCommandResult.InvalidClip;
                        if (!math.isfinite(command.Time) || !math.isfinite(command.Duration) || command.Duration < 0) return PlaybackCommandResult.InvalidValue;
                        return PlaybackCommandResult.Success;
                    case ScriptedCommandType.Seek: return math.isfinite(command.Time) ? PlaybackCommandResult.Success : PlaybackCommandResult.InvalidValue;
                    case ScriptedCommandType.Pause: return command.IsPaused <= 1 ? PlaybackCommandResult.Success : PlaybackCommandResult.InvalidValue;
                    case ScriptedCommandType.SetSpeed: return math.isfinite(command.Speed) ? PlaybackCommandResult.Success : PlaybackCommandResult.InvalidValue;
                    default: return PlaybackCommandResult.InvalidValue;
                }
            }
            static double Clamp(double time, ref AnimationClipBlob clip) => clip.Duration <= 0 ? 0 : clip.IsLooping != 0 ? time : math.clamp(time, 0, clip.Duration);
            static double Advance(double time, double speed, double dt, ref AnimationClipBlob clip)
            {
                var next = time + speed * dt;
                return Clamp(math.isfinite(next) ? next : time, ref clip);
            }
        }
    }
}

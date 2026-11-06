using System;
using Unity.Mathematics;

namespace AnimationSystemDOTS.Sandbox
{
    [Flags] public enum CharacterActions : byte { None = 0, Jump = 1, Wave = 2, Punch = 4, React = 8, Die = 16 }
    public struct CharacterCommand
    {
        public CharacterMotorInput Motor;
        public CharacterActions Actions;
        public int Variant, TreeMode;
        public float Variation, Cycle, PoseTime;
        public byte Scrub, Reset;
    }
    public struct CharacterActionDurations
    {
        public float3 Waves;
        public float2 Punches;
        public float Reaction;
    }
    public struct CharacterAnimationState
    {
        public float UpperRemaining, AdditiveRemaining, UpperWeight, AdditiveWeight;
        public byte Dead;
    }
    public struct CharacterLayerWeights
    {
        public float Upper, Additive, AirborneInfluence;
        public static CharacterLayerWeights Default => new CharacterLayerWeights { Upper = .7f, Additive = .2f, AirborneInfluence = .35f };
    }
    public struct CharacterAnimationFrame
    {
        public float Speed;
        public float2 Move;
        public float3 Direct;
        public float UpperWeight, AdditiveWeight;
        public CharacterActions Actions;
    }
    public static class CharacterAnimation
    {
        public static CharacterAnimationFrame Step(ref CharacterAnimationState state, in CharacterMotorState motor,
            in CharacterCommand command, in CharacterActionDurations durations, float deltaTime)
            => Step(ref state, motor, command, durations, CharacterLayerWeights.Default, deltaTime);
        public static CharacterAnimationFrame Step(ref CharacterAnimationState state, in CharacterMotorState motor,
            in CharacterCommand command, in CharacterActionDurations durations, in CharacterLayerWeights weights, float deltaTime)
        {
            var actions = state.Dead == 0 ? command.Actions : CharacterActions.None;
            var variant = math.max(0, command.Variant);
            state.UpperRemaining = math.max(0, state.UpperRemaining - deltaTime);
            state.AdditiveRemaining = math.max(0, state.AdditiveRemaining - deltaTime);
            if ((actions & CharacterActions.Wave) != 0) state.UpperRemaining = durations.Waves[variant % 3] * .95f + .18f;
            if ((actions & CharacterActions.Punch) != 0) state.UpperRemaining = durations.Punches[variant % 2] * .95f + .18f;
            if ((actions & CharacterActions.React) != 0) state.AdditiveRemaining = durations.Reaction * .95f + .18f;
            if ((actions & CharacterActions.Die) != 0) { state.Dead = 1; state.UpperRemaining = state.AdditiveRemaining = 0; actions = CharacterActions.Die; }
            var airborne = motor.Grounded != 0 ? 1 : math.saturate(weights.AirborneInfluence);
            state.UpperWeight = Fade(state.UpperWeight, state.UpperRemaining > 0 ? math.saturate(weights.Upper) * airborne : 0, deltaTime);
            state.AdditiveWeight = Fade(state.AdditiveWeight, state.AdditiveRemaining > 0 ? math.saturate(weights.Additive) * airborne : 0, deltaTime);
            math.sincos(motor.Yaw, out var sine, out var cosine);
            // Stable gameplay input resolution across managed and Burst sqrt:
            // an ULP around 3 m/s must not change a conditional Jump Entry.
            var speed = math.round(math.length(motor.Velocity) * 10000) * .0001f;
            var phase = math.saturate(speed / 3);
            var direct = speed <= 3 ? new float3(1 - phase, phase, 0) : new float3(0, 1 - math.saturate((speed - 3) / 3), math.saturate((speed - 3) / 3));
            return new CharacterAnimationFrame { Speed = speed, Move = new float2(cosine * motor.Velocity.x - sine * motor.Velocity.y,
                sine * motor.Velocity.x + cosine * motor.Velocity.y), Direct = direct, Actions = actions,
                UpperWeight = state.UpperWeight, AdditiveWeight = state.AdditiveWeight };
        }
        static float Fade(float current, float target, float dt) => current + math.clamp(target - current, -6 * dt, 6 * dt);
    }
}

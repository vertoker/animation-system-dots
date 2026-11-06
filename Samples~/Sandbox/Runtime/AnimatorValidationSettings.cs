using Unity.Mathematics;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    public sealed class AnimatorValidationSettings : ScriptableObject
    {
        public const float Step = 1f / 60;
        public RuntimeAnimatorController Controller;
        public Vector3 WaveDurations;
        public Vector2 PunchDurations;
        public float ReactionDuration;
        [Range(0, 1)] public float UpperActionWeight = .7f;
        [Range(0, 1)] public float AdditiveReactionWeight = .2f;
        [Range(0, 1)] public float AirborneActionInfluence = .35f;
        public CharacterLayerWeights LayerWeights => new CharacterLayerWeights { Upper = UpperActionWeight, Additive = AdditiveReactionWeight, AirborneInfluence = AirborneActionInfluence };
        public string[] StateNames;
        public int[] StateHashes;
        public string StateName(int hash)
        {
            var index = StateHashes == null ? -1 : System.Array.IndexOf(StateHashes, hash);
            return index >= 0 && StateNames != null && index < StateNames.Length ? StateNames[index] : hash.ToString();
        }
        public CharacterActionDurations Durations => new CharacterActionDurations { Waves = (float3)WaveDurations, Punches = (float2)PunchDurations, Reaction = ReactionDuration };
    }
    public static class CharacterTestTrace
    {
        // Forty-eight seconds, fixed tick identity. Both native and ECS consume
        // this command stream without consulting devices or frame delta time.
        public static CharacterCommand Sample(ulong tick)
        {
            var frame = (int)(tick % 2880); var time = frame * AnimatorValidationSettings.Step;
            var command = new CharacterCommand { TreeMode = frame / 360 % 5, Variant = frame / 120 % 3,
                Variation = frame / 180 % 5, PoseTime = .5f + .4f * math.sin(time), Cycle = frame / 600 % 4 * .25f,
                Scrub = frame >= 2100 && frame < 2220 ? (byte)1 : (byte)0, Reset = frame == 2640 ? (byte)1 : (byte)0 };
            command.Motor = new CharacterMotorInput { Move = frame < 60 || frame >= 2400 && frame < 2640 ? float2.zero : new float2(math.sin(time * .7f), math.cos(time * .7f)),
                Sprint = frame / 240 % 2 == 1 ? (byte)1 : (byte)0, Strafe = 1, CameraYaw = 0, Jump = frame % 180 == 120 ? (byte)1 : (byte)0 };
            if (frame % 480 == 90) command.Actions |= CharacterActions.Wave;
            if (frame % 480 == 270) command.Actions |= CharacterActions.Punch;
            if (frame % 360 == 210) command.Actions |= CharacterActions.React;
            if (frame == 2400) command.Actions = CharacterActions.Die;
            return command;
        }
    }
}

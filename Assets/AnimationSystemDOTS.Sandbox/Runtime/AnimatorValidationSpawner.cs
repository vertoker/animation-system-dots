using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    public sealed class AnimatorValidationSpawner : MonoBehaviour
    {
        public GameObject Prefab;
        public AnimatorValidationSettings Settings;
        public int Count = 1;
        public bool Bots;
        public uint Seed = 7301;
    }
    public struct CharacterSceneConfig : IComponentData
    {
        public Entity Prefab;
        public int Count;
        public byte Bots;
        public uint Seed;
        public CharacterActionDurations Durations;
        public CharacterLayerWeights Weights;
    }
    // Runtime state is separate from baked data: live edits cannot overwrite
    // the spawn/tick ownership bookkeeping.
    public struct CharacterSceneControl : IComponentData
    {
        public Entity Prefab, Primary;
        public BlobAssetReference<AnimatorControllerBlob> Definition;
        public int Count;
        public byte Bots, AutomatedTrace;
        public uint Seed;
        public ulong Tick;
        public CharacterCommand ManualCommand, FrameCommand;
    }
    public struct CharacterMember : IComponentData
    {
        public Entity Owner;
        public int Index, Respawns;
        public byte Bot, ResetRequested;
        public float3 ResetPosition, DeathDurations;
        public CharacterMotorState Motor;
        public CharacterAnimationState Animation;
        public CharacterCommand Command;
        public CharacterActions PendingActions;
        public CharacterActionDurations Durations;
        public CharacterLayerWeights Weights;
    }
    public struct CharacterBotState : IComponentData
    {
        public Unity.Mathematics.Random Random;
        public float2 Goal;
        public float Think, Jump, Action, Death, Facing, Speed;
    }
    public struct CharacterParameters : IComponentData
    {
        public AnimationParameterId Speed, MoveX, MoveY, DirectIdle, DirectWalk, DirectRun,
            Variation, Cycle, PoseTime, PlaybackSpeed, TreeMode, Variant, Grounded, Scrub, Dead, Jump, Wave, Punch, React, Die;
        public static CharacterParameters Resolve(in AnimationController c)
        {
            return new CharacterParameters {
                Speed = AnimationApi.FindParameter(c, "Speed"), MoveX = AnimationApi.FindParameter(c, "MoveX"), MoveY = AnimationApi.FindParameter(c, "MoveY"),
                DirectIdle = AnimationApi.FindParameter(c, "DirectIdle"), DirectWalk = AnimationApi.FindParameter(c, "DirectWalk"), DirectRun = AnimationApi.FindParameter(c, "DirectRun"),
                Variation = AnimationApi.FindParameter(c, "Variation"), Cycle = AnimationApi.FindParameter(c, "Cycle"), PoseTime = AnimationApi.FindParameter(c, "PoseTime"), PlaybackSpeed = AnimationApi.FindParameter(c, "PlaybackSpeed"),
                TreeMode = AnimationApi.FindParameter(c, "TreeMode"), Variant = AnimationApi.FindParameter(c, "Variant"), Grounded = AnimationApi.FindParameter(c, "Grounded"), Scrub = AnimationApi.FindParameter(c, "Scrub"), Dead = AnimationApi.FindParameter(c, "Dead"),
                Jump = AnimationApi.FindParameter(c, "Jump"), Wave = AnimationApi.FindParameter(c, "Wave"), Punch = AnimationApi.FindParameter(c, "Punch"), React = AnimationApi.FindParameter(c, "React"), Die = AnimationApi.FindParameter(c, "Die") };
        }
        public void Write(in AnimationController c, DynamicBuffer<AnimationParameterValue> p, DynamicBuffer<ControllerLayerRuntime> layers,
            in CharacterMember member, in CharacterAnimationFrame frame)
        {
            var command = member.Command;
            AnimationApi.SetFloat(c, p, Speed, frame.Speed); AnimationApi.SetFloat(c, p, MoveX, frame.Move.x); AnimationApi.SetFloat(c, p, MoveY, frame.Move.y);
            AnimationApi.SetFloat(c, p, DirectIdle, frame.Direct.x); AnimationApi.SetFloat(c, p, DirectWalk, frame.Direct.y); AnimationApi.SetFloat(c, p, DirectRun, frame.Direct.z);
            AnimationApi.SetFloat(c, p, Variation, command.Variation); AnimationApi.SetFloat(c, p, Cycle, command.Cycle); AnimationApi.SetFloat(c, p, PoseTime, command.PoseTime);
            AnimationApi.SetInt(c, p, TreeMode, math.clamp(command.TreeMode, 0, 4)); AnimationApi.SetInt(c, p, Variant, math.max(command.Variant, 0) % 3);
            AnimationApi.SetBool(c, p, Grounded, member.Motor.Grounded != 0); AnimationApi.SetBool(c, p, Scrub, command.Scrub != 0); AnimationApi.SetBool(c, p, Dead, member.Animation.Dead != 0);
            AnimationApi.SetLayerWeight(c, layers, 1, frame.UpperWeight); AnimationApi.SetLayerWeight(c, layers, 2, frame.AdditiveWeight);
            if ((frame.Actions & CharacterActions.Jump) != 0) AnimationApi.SetTrigger(c, p, Jump);
            if ((frame.Actions & CharacterActions.Wave) != 0) AnimationApi.SetTrigger(c, p, Wave);
            if ((frame.Actions & CharacterActions.Punch) != 0) AnimationApi.SetTrigger(c, p, Punch);
            if ((frame.Actions & CharacterActions.React) != 0) AnimationApi.SetTrigger(c, p, React);
            if ((frame.Actions & CharacterActions.Die) != 0) AnimationApi.SetTrigger(c, p, Die);
        }
    }
}

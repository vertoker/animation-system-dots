using AnimationSystemDOTS.Humanoid;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace AnimationSystemDOTS.Sandbox
{
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateBefore(typeof(CharacterPoseResetSystem))]
    public partial class CharacterClockSystem : SystemBase
    {
        ulong tick;
        protected override void OnCreate() { RequireForUpdate<CharacterSceneControl>(); RequireForUpdate<AnimationStepContext>(); }
        protected override void OnUpdate()
        {
            var step = SystemAPI.GetSingleton<AnimationStepContext>(); tick = (tick > step.Tick ? tick : step.Tick) + 1;
            SystemAPI.SetSingleton(new AnimationStepContext { Tick = tick, DeltaTime = AnimatorValidationSettings.Step });
            foreach (var control in SystemAPI.Query<RefRW<CharacterSceneControl>>())
            {
                if (control.ValueRO.ManualCommand.Reset != 0) control.ValueRW.Tick = 0;
                control.ValueRW.FrameCommand = control.ValueRO.AutomatedTrace != 0 ? CharacterTestTrace.Sample(control.ValueRO.Tick) : control.ValueRO.ManualCommand;
                if (control.ValueRO.ManualCommand.Reset != 0) { var frame = control.ValueRO.FrameCommand; frame.Reset = 1; control.ValueRW.FrameCommand = frame; }
                var manual = control.ValueRO.ManualCommand; manual.Actions = CharacterActions.None; manual.Motor.Jump = 0; manual.Reset = 0; control.ValueRW.ManualCommand = manual;
                control.ValueRW.Tick++;
            }
            foreach (var member in SystemAPI.Query<RefRW<CharacterMember>>())
            {
                if (member.ValueRO.Bot != 0 || !EntityManager.HasComponent<CharacterSceneControl>(member.ValueRO.Owner)) continue;
                var command = EntityManager.GetComponentData<CharacterSceneControl>(member.ValueRO.Owner).FrameCommand;
                member.ValueRW.Command = command;
                if (command.Reset != 0) member.ValueRW.ResetRequested = 1;
            }
        }
    }
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateAfter(typeof(CharacterClockSystem))]
    [UpdateBefore(typeof(CharacterMotorSystem))]
    public partial class CharacterPoseResetSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<CharacterSceneControl>();
        protected override void OnUpdate()
        {
            foreach (var (member, entity) in SystemAPI.Query<RefRW<CharacterMember>>().WithEntityAccess())
            {
                if (member.ValueRO.ResetRequested == 0) continue;
                var controller = EntityManager.GetComponentData<AnimationController>(entity); var state = default(ControllerLayerState);
                var result = AnimationApi.ResetControllerState(controller, ref state, EntityManager.GetBuffer<AnimationParameterValue>(entity), EntityManager.GetBuffer<ControllerLayerRuntime>(entity), EntityManager.GetBuffer<ControllerCachedRequest>(entity));
                if (result != ControllerEvaluationResult.Success) throw new System.InvalidOperationException("Cannot reset validation controller: " + result);
                EntityManager.SetComponentData(entity, state); EntityManager.SetComponentData(entity, default(AnimationClock));
                Clear<ControllerBoneHistory>(entity); Clear<HumanoidControllerMuscleHistory>(entity); Clear<HumanoidControllerBodyHistory>(entity);
                Clear<HumanoidMuscleValue>(entity); Clear<HumanoidRootMuscleValue>(entity); Clear<BoneLocalPose>(entity); Clear<BoneModelPose>(entity); Clear<AnimationClipRequest>(entity);
                if (EntityManager.HasComponent<HumanoidBodyPose>(entity)) EntityManager.SetComponentData(entity, default(HumanoidBodyPose));
                member.ValueRW.Motor = new CharacterMotorState { Position = member.ValueRO.ResetPosition, Grounded = 1 };
                member.ValueRW.Animation = default; member.ValueRW.ResetRequested = 0; member.ValueRW.Respawns++;
                var bot = EntityManager.GetComponentData<CharacterBotState>(entity); bot.Death = 0; bot.Think = 0; EntityManager.SetComponentData(entity, bot);
            }
        }
        void Clear<T>(Entity entity) where T : unmanaged, IBufferElementData { if (EntityManager.HasBuffer<T>(entity)) EntityManager.GetBuffer<T>(entity).Clear(); }
    }
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateAfter(typeof(CharacterPoseResetSystem))]
    [UpdateBefore(typeof(ControllerAdvanceSystem))]
    public partial class CharacterMotorSystem : SystemBase
    {
        EntityQuery members;
        protected override void OnCreate() { members = GetEntityQuery(typeof(CharacterMember)); RequireForUpdate<CharacterSceneControl>(); }
        protected override void OnUpdate()
        {
            var snapshot = members.ToComponentDataArray<CharacterMember>(Allocator.TempJob);
            var job = new MotorJob { DeltaTime = AnimatorValidationSettings.Step, Neighbors = snapshot };
            Dependency = job.ScheduleParallel(Dependency); Dependency = snapshot.Dispose(Dependency);
        }
        [BurstCompile]
        partial struct MotorJob : IJobEntity
        {
            public float DeltaTime;
            [ReadOnly] public NativeArray<CharacterMember> Neighbors;
            void Execute(ref CharacterMember member, ref CharacterBotState bot, ref LocalTransform transform,
                in AnimationController controller, in CharacterParameters ids,
                DynamicBuffer<AnimationParameterValue> parameters, DynamicBuffer<ControllerLayerRuntime> layers)
            {
                if (member.Bot != 0) Think(ref member, ref bot);
                var command = member.Command; command.Actions |= member.PendingActions; member.PendingActions = CharacterActions.None;
                if ((command.Actions & CharacterActions.Jump) != 0) { command.Motor.Jump = 1; command.Actions &= ~CharacterActions.Jump; }
                command.Motor.Disabled = member.Animation.Dead != 0 || command.Scrub != 0 ? (byte)1 : (byte)0;
                if ((command.Actions & CharacterActions.Die) != 0) { member.Motor.Velocity = float2.zero; command.Motor.Disabled = 1; }
                var grounded = member.Motor.Grounded;
                CharacterMotor.Step(ref member.Motor, command.Motor, DeltaTime);
                if (grounded != 0 && member.Motor.Grounded == 0) command.Actions |= CharacterActions.Jump;
                var frame = CharacterAnimation.Step(ref member.Animation, member.Motor, command, member.Durations, member.Weights, DeltaTime);
                member.Command = command;
                ids.Write(controller, parameters, layers, member, frame);
                transform.Position = member.Motor.Position; transform.Rotation = quaternion.RotateY(member.Motor.Yaw);
                if (member.Bot != 0 && (frame.Actions & CharacterActions.Die) != 0) bot.Death = member.DeathDurations[math.max(command.Variant, 0) % 3] + .35f;
            }
            void Think(ref CharacterMember member, ref CharacterBotState bot)
            {
                var command = new CharacterCommand { TreeMode = member.Index % 5, Variant = member.Index % 3, Variation = member.Index % 5, Cycle = member.Index % 4 * .25f };
                if (member.Animation.Dead != 0)
                {
                    bot.Death -= DeltaTime; if (bot.Death <= 0) member.ResetRequested = 1;
                    member.Command = command; return;
                }
                bot.Think -= DeltaTime; bot.Jump -= DeltaTime; bot.Action -= DeltaTime;
                if (bot.Think <= 0 || math.distancesq(bot.Goal, member.Motor.Position.xz) < 1)
                { bot.Goal = bot.Random.NextFloat2(new float2(-21), new float2(21)); bot.Think = bot.Random.NextFloat(3, 8); bot.Speed = bot.Random.NextFloat() < .35f ? 6 : bot.Random.NextFloat(1.2f, 3); }
                var desired = math.normalizesafe(bot.Goal - member.Motor.Position.xz);
                var separation = float2.zero;
                for (var i = 0; i < Neighbors.Length; i++)
                {
                    var other = Neighbors[i]; if (other.Owner != member.Owner || other.Index == member.Index) continue;
                    var offset = member.Motor.Position.xz - other.Motor.Position.xz; var distance = math.lengthsq(offset);
                    if (distance < 1.44f && distance > .0001f) separation += offset * (1.44f - distance) / math.max(distance, .09f);
                }
                desired = math.normalizesafe(desired + separation * .65f, desired);
                var yaw = math.atan2(desired.x, desired.y); bot.Facing += math.atan2(math.sin(yaw - bot.Facing), math.cos(yaw - bot.Facing)) * math.min(1, DeltaTime * 1.5f);
                var strafe = command.TreeMode > 0 && command.TreeMode < 4;
                var speed = command.TreeMode == 1 || command.TreeMode == 4 ? math.min(bot.Speed, 3) : bot.Speed;
                command.Motor.Sprint = speed > 3 ? (byte)1 : (byte)0;
                var divisor = command.Motor.Sprint != 0 ? 6 : 3;
                math.sincos(bot.Facing, out var sine, out var cosine);
                command.Motor.Move = strafe ? new float2(cosine * desired.x - sine * desired.y, sine * desired.x + cosine * desired.y) * speed / divisor : desired * speed / divisor;
                command.Motor.Strafe = strafe ? (byte)1 : (byte)0; command.Motor.CameraYaw = strafe ? bot.Facing : 0;
                if (bot.Jump <= 0 && member.Motor.Grounded != 0) { command.Motor.Jump = 1; bot.Jump = bot.Random.NextFloat(2, 6); }
                if (bot.Action <= 0)
                {
                    var choice = bot.Random.NextInt(0, 12); command.Actions = choice == 0 ? CharacterActions.Die : choice < 5 ? CharacterActions.Wave : choice < 9 ? CharacterActions.Punch : CharacterActions.React;
                    bot.Action = bot.Random.NextFloat(3, 8);
                }
                member.Command = command;
            }
        }
    }
}

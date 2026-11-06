using Unity.Mathematics;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    public sealed class NativeAnimatorCharacter : MonoBehaviour
    {
        public Animator Animator;
        public AnimatorValidationSettings Settings;
        public AnimatorValidationOrbit Orbit;
        public bool AutomatedTrace;
        public CharacterCommand Command;
        public CharacterMotorState Motor = new CharacterMotorState { Grounded = 1 };
        public CharacterAnimationState Animation;
        public ulong Tick;
        public CharacterCommand LastCommand;
        CharacterActions queued;
        static readonly CharacterActions[] Triggers = { CharacterActions.Jump, CharacterActions.Wave, CharacterActions.Punch, CharacterActions.React, CharacterActions.Die };
        bool jump;
        float previousFixedDelta;
        bool ownsFixedDelta;

        void OnEnable()
        {
            previousFixedDelta = Time.fixedDeltaTime; Time.fixedDeltaTime = AnimatorValidationSettings.Step; ownsFixedDelta = true;
            if (Animator == null) Animator = GetComponent<Animator>();
            if (Animator != null) { Animator.updateMode = AnimatorUpdateMode.Fixed; Animator.applyRootMotion = false; Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
        }
        void OnDisable()
        { if (ownsFixedDelta) { Time.fixedDeltaTime = previousFixedDelta; ownsFixedDelta = false; } }
        public void Queue(CharacterActions action) => queued |= action;
        public void QueueJump() => jump = true;
        public void ResetCharacter(bool restartTrace = true)
        {
            Motor = new CharacterMotorState { Grounded = 1 }; Animation = default; queued = CharacterActions.None; jump = false;
            if (restartTrace) Tick = 0;
            if (Animator != null) { Animator.Rebind(); Animator.Update(0); }
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
        void FixedUpdate()
            => SimulateStep();
        public void SimulateStep()
        {
            if (Animator == null || Settings == null) return;
            var command = AutomatedTrace ? CharacterTestTrace.Sample(Tick) : Command;
            if (command.Reset != 0) ResetCharacter(false);
            command.Actions |= queued; queued = CharacterActions.None;
            if (jump) command.Motor.Jump = 1; jump = false;
            command.Motor.Disabled = Animation.Dead != 0 || command.Scrub != 0 ? (byte)1 : (byte)0;
            if ((command.Actions & CharacterActions.Die) != 0) { Motor.Velocity = float2.zero; command.Motor.Disabled = 1; }
            var grounded = Motor.Grounded;
            CharacterMotor.Step(ref Motor, command.Motor, AnimatorValidationSettings.Step);
            if (grounded != 0 && Motor.Grounded == 0) command.Actions |= CharacterActions.Jump;
            var frame = CharacterAnimation.Step(ref Animation, Motor, command, Settings.Durations, Settings.LayerWeights, AnimatorValidationSettings.Step);
            LastCommand = command;
            transform.SetPositionAndRotation(Motor.Position, Quaternion.Euler(0, math.degrees(Motor.Yaw), 0));
            WriteNative(Animator, Motor, command, frame); ++Tick;
        }
        public static void WriteNative(Animator animator, in CharacterMotorState motor, in CharacterCommand command, in CharacterAnimationFrame frame)
        {
            animator.SetFloat("Speed", frame.Speed); animator.SetFloat("MoveX", frame.Move.x); animator.SetFloat("MoveY", frame.Move.y);
            animator.SetFloat("DirectIdle", frame.Direct.x); animator.SetFloat("DirectWalk", frame.Direct.y); animator.SetFloat("DirectRun", frame.Direct.z);
            animator.SetFloat("Variation", command.Variation); animator.SetFloat("Cycle", command.Cycle); animator.SetFloat("PoseTime", command.PoseTime);
            animator.SetInteger("TreeMode", math.clamp(command.TreeMode, 0, 4)); animator.SetInteger("Variant", math.max(0, command.Variant) % 3);
            animator.SetBool("Grounded", motor.Grounded != 0); animator.SetBool("Scrub", command.Scrub != 0);
            animator.SetBool("Dead", (frame.Actions & CharacterActions.Die) != 0 || animator.GetBool("Dead"));
            animator.SetLayerWeight(1, frame.UpperWeight); animator.SetLayerWeight(2, frame.AdditiveWeight);
            foreach (var action in Triggers)
                if ((frame.Actions & action) != 0) animator.SetTrigger(action.ToString());
        }
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, 610, 360), GUI.skin.box);
            GUILayout.Label("05 | GameObject + native Animator | shared Bots100.controller");
            GUILayout.Label("WASD move | Shift sprint | Alt strafe | Space jump | RMB orbit");
            GUILayout.Label("1 wave | 2 punch | 3 reaction | 4 death | R reset | T trace | Tab tree | V variant | C cycle | F scrub");
            if (GUILayout.Button(AutomatedTrace ? "Stop deterministic trace" : "Run deterministic trace (60 s loop)")) { AutomatedTrace = !AutomatedTrace; ResetCharacter(); }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset")) ResetCharacter();
            if (GUILayout.Button("Tree: " + Command.TreeMode)) Command.TreeMode = (Command.TreeMode + 1) % 5;
            if (GUILayout.Button("Variant: " + Command.Variant)) { Command.Variant = (Command.Variant + 1) % 3; Command.Variation = (Command.Variation + 1) % 5; }
            foreach (var action in new[] { CharacterActions.Wave, CharacterActions.Punch, CharacterActions.React, CharacterActions.Die }) if (GUILayout.Button(action.ToString())) Queue(action);
            GUILayout.EndHorizontal();
            Command.Scrub = GUILayout.Toggle(Command.Scrub != 0, "Motion Time / pose scrub") ? (byte)1 : (byte)0;
            if (Command.Scrub != 0) Command.PoseTime = GUILayout.HorizontalSlider(Command.PoseTime, 0, 1);
            GUILayout.Label("Tick " + Tick + " | tree " + LastCommand.TreeMode + " | speed " + math.length(Motor.Velocity).ToString("F2") + " | ground " + Motor.Grounded + " | cycle " + LastCommand.Cycle.ToString("F2"));
            if (Animator != null && Animator.isInitialized)
                for (var layer = 0; layer < Animator.layerCount; layer++)
                {
                    var info = Animator.GetCurrentAnimatorStateInfo(layer);
                    GUILayout.Label(Animator.GetLayerName(layer) + " | " + (Settings != null ? Settings.StateName(info.shortNameHash) : info.shortNameHash.ToString()) + " | t " + info.normalizedTime.ToString("F2") + " | weight " + Animator.GetLayerWeight(layer).ToString("F2") + (Animator.IsInTransition(layer) ? " → " + Settings.StateName(Animator.GetNextAnimatorStateInfo(layer).shortNameHash) : ""));
                }
            GUILayout.EndArea();
        }
    }
}

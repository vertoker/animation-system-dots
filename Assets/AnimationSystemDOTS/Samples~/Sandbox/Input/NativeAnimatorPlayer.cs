using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimationSystemDOTS.Sandbox.Input
{
    // Device adapter only. Simulation, trace and on-screen controls remain in
    // the autonomous sample assembly and are shared with the ECS character.
    [DefaultExecutionOrder(-200)]
    public sealed class NativeAnimatorPlayer : MonoBehaviour
    {
        public NativeAnimatorCharacter Character;
        public AnimatorValidationOrbit Orbit;
        void Update()
        {
            if (Character == null) return;
            var keyboard = Keyboard.current; var mouse = Mouse.current; var command = Character.Command;
            if (keyboard != null)
            {
                command.Motor.Move = new float2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0), (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
                command.Motor.Sprint = keyboard.leftShiftKey.isPressed ? (byte)1 : (byte)0;
                command.Motor.Strafe = keyboard.leftAltKey.isPressed ? (byte)1 : (byte)0;
                if (keyboard.spaceKey.wasPressedThisFrame) Character.QueueJump();
                if (keyboard.digit1Key.wasPressedThisFrame) Character.Queue(CharacterActions.Wave);
                if (keyboard.digit2Key.wasPressedThisFrame) Character.Queue(CharacterActions.Punch);
                if (keyboard.digit3Key.wasPressedThisFrame) Character.Queue(CharacterActions.React);
                if (keyboard.digit4Key.wasPressedThisFrame) Character.Queue(CharacterActions.Die);
                if (keyboard.rKey.wasPressedThisFrame) Character.ResetCharacter();
                if (keyboard.tKey.wasPressedThisFrame) { Character.AutomatedTrace = !Character.AutomatedTrace; Character.ResetCharacter(); }
                if (keyboard.tabKey.wasPressedThisFrame) command.TreeMode = (command.TreeMode + 1) % 5;
                if (keyboard.vKey.wasPressedThisFrame) { command.Variant = (command.Variant + 1) % 3; command.Variation = (command.Variation + 1) % 5; }
                if (keyboard.cKey.wasPressedThisFrame) command.Cycle = (command.Cycle + .25f) % 1;
                if (keyboard.fKey.wasPressedThisFrame) command.Scrub = command.Scrub == 0 ? (byte)1 : (byte)0;
            }
            if (Orbit != null)
            {
                if (mouse != null && mouse.rightButton.isPressed)
                { var delta = mouse.delta.ReadValue(); Orbit.Yaw += delta.x * .15f; Orbit.Pitch = Mathf.Clamp(Orbit.Pitch - delta.y * .15f, -10, 65); }
                command.Motor.CameraYaw = math.radians(Orbit.Yaw);
            }
            Character.Command = command;
        }
    }
}

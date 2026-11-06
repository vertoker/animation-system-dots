using NUnit.Framework;
using Unity.Mathematics;

namespace AnimationSystemDOTS.Sandbox.Tests
{
    public class CharacterMotorTests
    {
        [Test]
        public void StrafeMovement_KeepsCameraFacingWhileMovingSideways()
        {
            var state = new CharacterMotorState { Grounded = 1 };
            var input = new CharacterMotorInput { Move = new float2(1, 0), CameraYaw = math.PI / 2, Strafe = 1 };
            CharacterMotor.Step(ref state, input, .1f);
            Assert.That(state.Yaw, Is.EqualTo(input.CameraYaw).Within(.0001f));
            Assert.That(state.Velocity.y, Is.LessThan(0));
        }
        [Test]
        public void CameraRelativeDiagonalMovement_IsNormalizedAndSprintReachesSixMetersPerSecond()
        {
            var state = new CharacterMotorState { Grounded = 1 };
            var input = new CharacterMotorInput { Move = new float2(1, 1), CameraYaw = math.PI / 2, Sprint = 1 };
            for (var i = 0; i < 60; i++) CharacterMotor.Step(ref state, input, 1f / 60);
            Assert.That(math.length(state.Velocity), Is.EqualTo(6).Within(.0001f));
            Assert.That(state.Velocity.x, Is.GreaterThan(0));
            Assert.That(state.Velocity.y, Is.LessThan(0));
        }

        [Test]
        public void Jump_CannotDoubleJumpAndLandsOnAnalyticFloor()
        {
            var state = new CharacterMotorState { Grounded = 1 };
            var input = new CharacterMotorInput { Jump = 1 };
            CharacterMotor.Step(ref state, input, 1f / 60);
            Assert.That(state.Grounded, Is.Zero); Assert.That(state.Position.y, Is.GreaterThan(0));
            var previousSpeed = state.VerticalSpeed;
            CharacterMotor.Step(ref state, input, 1f / 60);
            Assert.That(state.VerticalSpeed, Is.LessThan(previousSpeed));
            input.Jump = 0;
            for (var i = 0; i < 120; i++) CharacterMotor.Step(ref state, input, 1f / 60);
            Assert.That(state.Grounded, Is.EqualTo(1)); Assert.That(state.Position.y, Is.Zero); Assert.That(state.VerticalSpeed, Is.Zero);
        }

        [Test]
        public void ReplayedInputTrace_IsDeterministicAndCannotEscapeArena()
        {
            var a = new CharacterMotorState { Grounded = 1 }; var b = a;
            for (var i = 0; i < 900; i++)
            {
                var input = new CharacterMotorInput { Move = new float2(1, .3f), Sprint = 1, Jump = (byte)(i % 100 == 0 ? 1 : 0) };
                CharacterMotor.Step(ref a, input, 1f / 60); CharacterMotor.Step(ref b, input, 1f / 60);
                Assert.That(math.all(a.Position == b.Position), Is.True);
                Assert.That(math.all(math.abs(a.Position.xz) <= 24), Is.True);
            }
        }
    }
}

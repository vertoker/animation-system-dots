using NUnit.Framework;
using Unity.Mathematics;

namespace AnimationSystemDOTS.Sandbox.Tests
{
    public class CharacterAnimationTests
    {
        [Test]
        public void DeterministicTrace_ExercisesEveryJumpAndDeathVariant()
        {
            var jumps = new System.Collections.Generic.HashSet<int>(); var deaths = new System.Collections.Generic.HashSet<int>();
            for (ulong tick = 0; tick < CharacterTestTrace.TickCount; tick++)
            {
                var command = CharacterTestTrace.Sample(tick);
                if (command.Motor.Jump != 0 && command.Motor.Sprint == 0) jumps.Add(command.Variant);
                if ((command.Actions & CharacterActions.Die) != 0) deaths.Add(command.Variant);
            }
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, jumps); CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, deaths);
        }
        [Test]
        public void AnimationSpeed_StabilizesFloatRoundingAtGraphThresholds()
        {
            var state = new CharacterAnimationState();
            var motor = new CharacterMotorState { Grounded = 1, Velocity = new float2(math.asfloat(0x40400001u), 0) };
            var frame = CharacterAnimation.Step(ref state, motor, default, default, 1f / 60);
            Assert.That(frame.Speed, Is.EqualTo(3f), "Managed and Burst motors can differ by one ULP; that must not select a different Jump Entry at Speed > 3.");
        }
        [Test]
        public void AnimationFrame_UsesCharacterLocalVelocityAndNormalizedDirectWeights()
        {
            var state = new CharacterAnimationState();
            var motor = new CharacterMotorState { Grounded = 1, Velocity = new float2(3, 0), Yaw = math.PI / 2 };
            var frame = CharacterAnimation.Step(ref state, motor, new CharacterCommand(), new CharacterActionDurations(), 1f / 60);
            Assert.That(frame.Move.x, Is.EqualTo(0).Within(.00001f)); Assert.That(frame.Move.y, Is.EqualTo(3).Within(.00001f));
            Assert.That(math.csum(frame.Direct), Is.EqualTo(1).Within(.00001f));
        }
        [Test]
        public void UpperActions_UseVariantDurationAndFadeBackOut()
        {
            var state = new CharacterAnimationState(); var motor = new CharacterMotorState { Grounded = 1 };
            var durations = new CharacterActionDurations { Waves = new float3(1, 2, 3), Punches = new float2(.5f, .7f), Reaction = 1 };
            var command = new CharacterCommand { Actions = CharacterActions.Wave, Variant = 2 };
            var first = CharacterAnimation.Step(ref state, motor, command, durations, .1f);
            Assert.That(first.UpperWeight, Is.GreaterThan(0)); Assert.That(state.UpperRemaining, Is.GreaterThan(2.5f));
            command.Actions = CharacterActions.None;
            for (var i = 0; i < 50; i++) CharacterAnimation.Step(ref state, motor, command, durations, .1f);
            Assert.That(state.UpperWeight, Is.Zero); Assert.That(state.UpperRemaining, Is.Zero);
        }
        [Test]
        public void Death_BlocksFurtherActionsUntilTheStateIsReset()
        {
            var state = new CharacterAnimationState(); var motor = new CharacterMotorState { Grounded = 1 };
            CharacterAnimation.Step(ref state, motor, new CharacterCommand { Actions = CharacterActions.Die }, default, .1f);
            var frame = CharacterAnimation.Step(ref state, motor, new CharacterCommand { Actions = CharacterActions.Wave | CharacterActions.Punch }, default, .1f);
            Assert.That(state.Dead, Is.EqualTo(1)); Assert.That(frame.Actions, Is.EqualTo(CharacterActions.None));
        }
    }
}

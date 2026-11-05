using NUnit.Framework;

namespace AnimationSystemDOTS.Tests
{
    public class PlaybackTests : RuntimeAnimationFixture
    {
        [Test]
        public void Play_AdvancesOncePerNormalTickAndSeekRevisionReevaluatesWithoutAdvancing()
        {
            Command(ScriptedPlaybackCommand.Play(new ClipId(0), 1)); Step(1, 0.25);
            Assert.That(State.Time, Is.EqualTo(0.25)); Step(1, 0.25);
            Assert.That(State.Time, Is.EqualTo(0.25));
            Command(ScriptedPlaybackCommand.Seek(0.75, 2)); Step(1, 10, 1, AnimationStepMode.Seek);
            Assert.That(State.Time, Is.EqualTo(0.75)); Assert.That(State.PreviousTime, Is.EqualTo(0.75));
            var requests = world.EntityManager.GetBuffer<AnimationClipRequest>(rigEntity);
            Assert.That(requests.Length, Is.EqualTo(1)); Assert.That(requests[0].Time, Is.EqualTo(0.75));
            Assert.That(world.EntityManager.GetComponentData<AnimationClock>(rigEntity).Time, Is.EqualTo(0.25));
        }
        [Test]
        public void Commands_ApplyInSequenceAndKeepReverseTimeUnwrappedAcrossManyLoops()
        {
            Command(ScriptedPlaybackCommand.SetSpeed(-2, 2)); Command(ScriptedPlaybackCommand.Play(new ClipId(0), 1));
            Step(1, 3.25); Assert.That(State.Speed, Is.EqualTo(-2)); Assert.That(State.Time, Is.EqualTo(-6.5));
            Assert.That(ClipSampler.SampleBone(ref library.Value.Clips[0], 1, State.Time).Position.x, Is.EqualTo(5).Within(1e-5));
        }
        [Test]
        public void Pause_StopsBothClipAndFadeProgressAndResumeContinues()
        {
            Command(ScriptedPlaybackCommand.Play(new ClipId(0), 1)); Step(1, 0);
            Command(ScriptedPlaybackCommand.CrossFade(new ClipId(1), 1, 2)); Step(2, 0.25);
            Command(ScriptedPlaybackCommand.Pause(true, 3)); Step(3, 10);
            Assert.That(State.Time, Is.EqualTo(0.25)); Assert.That(State.FadeProgress, Is.EqualTo(0.25));
            Command(ScriptedPlaybackCommand.Pause(false, 4)); Step(4, 0.25);
            Assert.That(State.Time, Is.EqualTo(0.5)); Assert.That(State.FadeProgress, Is.EqualTo(0.5));
        }
        [Test]
        public void CrossFade_PublishesTwoWeightedRequestsThenRetiresSource()
        {
            Command(ScriptedPlaybackCommand.Play(new ClipId(0), 1)); Step(1, 0.1);
            Command(ScriptedPlaybackCommand.CrossFade(new ClipId(1), 1, 2)); Step(2, 0.25);
            var requests = world.EntityManager.GetBuffer<AnimationClipRequest>(rigEntity);
            Assert.That(requests.Length, Is.EqualTo(2)); Assert.That(requests[0].Weight, Is.EqualTo(0.75f));
            Assert.That(requests[1].Weight, Is.EqualTo(0.25f)); Assert.That(requests[0].Time, Is.EqualTo(0.35).Within(1e-12));
            Step(3, 1); requests = world.EntityManager.GetBuffer<AnimationClipRequest>(rigEntity);
            Assert.That(requests.Length, Is.EqualTo(1)); Assert.That(requests[0].Clip.Value, Is.EqualTo(1));
        }
        [Test]
        public void ZeroDurationCrossFade_IsImmediateEvenAtZeroDelta()
        {
            Command(ScriptedPlaybackCommand.Play(new ClipId(0), 1)); Step(1, 0.5);
            Command(ScriptedPlaybackCommand.CrossFade(new ClipId(1), 0, 2, 0.75)); Step(2, 0);
            Assert.That(State.Clip.Value, Is.EqualTo(1)); Assert.That(State.Time, Is.EqualTo(0.75));
            Assert.That(world.EntityManager.GetBuffer<AnimationClipRequest>(rigEntity).Length, Is.EqualTo(1));
        }
        [Test]
        public void InvalidCommands_DoNotReplacePlaybackAndReportFailure()
        {
            Command(ScriptedPlaybackCommand.Play(new ClipId(0), 1)); Step(1, 0.5);
            Command(ScriptedPlaybackCommand.Play(ClipId.Invalid, 2)); Step(2, 0);
            Assert.That(State.Clip.Value, Is.EqualTo(0)); Assert.That(State.Time, Is.EqualTo(0.5)); Assert.That(State.LastCommandResult, Is.EqualTo(PlaybackCommandResult.InvalidClip));
            Command(ScriptedPlaybackCommand.CrossFade(new ClipId(1), -1, 3)); Step(3, 0);
            Assert.That(State.Clip.Value, Is.EqualTo(0)); Assert.That(State.LastCommandResult, Is.EqualTo(PlaybackCommandResult.InvalidValue));
            Command(ScriptedPlaybackCommand.SetSpeed(double.NaN, 4)); Step(4, 0);
            Assert.That(State.Speed, Is.EqualTo(1)); Assert.That(State.LastCommandResult, Is.EqualTo(PlaybackCommandResult.InvalidValue));
        }
        [Test]
        public void NonLoopingClip_ClampsThenCanReverseFromItsEndpoint()
        {
            Command(ScriptedPlaybackCommand.Play(new ClipId(1), 1)); Step(1, 5);
            Assert.That(State.Time, Is.EqualTo(2));
            Command(ScriptedPlaybackCommand.SetSpeed(-1, 2)); Step(2, 0.5);
            Assert.That(State.Time, Is.EqualTo(1.5));
        }
        [Test]
        public void ZeroLengthClip_RemainsFiniteAtLargeDeltaAndNegativeSpeed()
        {
            Command(ScriptedPlaybackCommand.Play(new ClipId(2), 1)); Command(ScriptedPlaybackCommand.SetSpeed(-100, 2)); Step(1, 100000);
            Assert.That(State.Time, Is.Zero); Assert.That(ClipSampler.SampleBone(ref library.Value.Clips[2], 1, State.Time).Position.x, Is.EqualTo(7));
        }
    }
}

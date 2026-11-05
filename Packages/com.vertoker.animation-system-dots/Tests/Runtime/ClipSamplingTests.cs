using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystemDOTS.Tests
{
    public class ClipSamplingTests : RuntimeAnimationFixture
    {
        [Test]
        public void Sampler_FiniteHugeTimeAndTinyLoopRemainWithinTheTrackRange()
        {
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<AnimationClipBlob>();
                root.Duration = root.SampleStep = 1e-20; root.SampleCount = 2; root.IsLooping = 1;
                var tracks = builder.Allocate(ref root.Tracks, 1);
                var samples = builder.Allocate(ref tracks[0].Samples, 2); samples[0] = Pose(0); samples[1] = Pose(1);
                var blob = builder.CreateBlobAssetReference<AnimationClipBlob>(Allocator.Persistent);
                try
                {
                    foreach (var time in new[] { double.MaxValue, -double.MaxValue })
                    {
                        var sampled = ClipSampler.SampleBone(ref blob.Value, 0, time);
                        Assert.That(math.isfinite(sampled.Position.x), Is.True);
                        Assert.That(sampled.Position.x, Is.InRange(0, 1));
                    }
                }
                finally { blob.Dispose(); }
            }
        }
        [TestCase(-2.0, 0.0f)]
        [TestCase(-0.25, 7.5f)]
        [TestCase(0.0, 0.0f)]
        [TestCase(1.0, 10.0f)]
        [TestCase(2.0, 10.0f)]
        [TestCase(2.25, 2.5f)]
        [TestCase(16777216.25, 2.5f)]
        public void Sampler_PreservesLoopEndpointsAndDoublePrecision(double time, float expected)
            => Assert.That(ClipSampler.SampleBone(ref library.Value.Clips[0], 1, time).Position.x, Is.EqualTo(expected).Within(1e-5));
        [Test]
        public void Sampler_NonLoopingTimesClampAndZeroLengthStaysConstant()
        {
            Assert.That(ClipSampler.SampleBone(ref library.Value.Clips[1], 1, -100).Position.x, Is.EqualTo(20));
            Assert.That(ClipSampler.SampleBone(ref library.Value.Clips[1], 1, 100).Position.x, Is.EqualTo(40));
            Assert.That(ClipSampler.SampleBone(ref library.Value.Clips[2], 1, -100).Position.x, Is.EqualTo(7));
        }
    }
}

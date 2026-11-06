using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
namespace AnimationSystemDOTS
{
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateAfter(typeof(ScriptedPlaybackSystem))]
    public partial class AnimationSampleSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<AnimationStepContext>();
        protected override void OnUpdate() => Dependency = new SampleJob().ScheduleParallel(Dependency);

        [BurstCompile]
        [WithNone(typeof(PreciseHumanoidPose))]
        partial struct SampleJob : IJobEntity
        {
            void Execute(in AnimationRig rig, in DynamicBuffer<AnimationClipRequest> requests, DynamicBuffer<SampledBonePose> samples)
            {
                if (!rig.Rig.IsCreated) { samples.Clear(); return; }
                var count = rig.Rig.Value.RestLocalPoses.Length;
                samples.ResizeUninitialized(count * requests.Length);
                for (var requestIndex = 0; requestIndex < requests.Length; requestIndex++)
                {
                    var request = requests[requestIndex];
                    var valid = rig.Library.IsCreated && rig.Library.Value.RigKey == rig.Rig.Value.ContentKey
                        && (uint)request.Clip.Value < (uint)rig.Library.Value.Clips.Length && math.isfinite(request.Time);
                    for (var bone = 0; bone < count; bone++)
                    {
                        var sample = new SampledBonePose { Pose = rig.Rig.Value.RestLocalPoses[bone] };
                        if (valid)
                        {
                            ref var clip = ref rig.Library.Value.Clips[request.Clip.Value];
                            if (clip.Version == AnimationBakeVersion.Schema && clip.RigKey == rig.Rig.Value.ContentKey
                                && clip.Tracks.Length == count && clip.ReferencePose.Length == count && clip.SampleCount > 0
                                && (clip.Duration <= 0 || clip.SampleCount > 1 && clip.SampleStep > 0)
                                && (clip.Tracks[bone].Samples.Length == 1 || clip.Tracks[bone].Samples.Length == clip.SampleCount))
                            {
                                sample.Pose = ClipSampler.SampleBone(ref clip, bone, request.Time, request.LoopBoundary); sample.IsValid = 1;
                            }
                        }
                        samples[requestIndex * count + bone] = sample;
                    }
                }
            }
        }
    }
}

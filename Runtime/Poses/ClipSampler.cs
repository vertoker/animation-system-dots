using System;
using Unity.Mathematics;

namespace AnimationSystemDOTS
{
    public static class ClipSampler
    {
        public static BoneLocalPose SampleBone(ref AnimationClipBlob clip, int boneIndex, double unwrappedTime)
        {
            if ((uint)boneIndex >= (uint)clip.Tracks.Length) throw new ArgumentOutOfRangeException(nameof(boneIndex));
            if (!math.isfinite(unwrappedTime)) throw new ArgumentException("Sample time must be finite.");
            ref var track = ref clip.Tracks[boneIndex];
            if (track.Samples.Length == 1 || clip.Duration <= 0) return track.Samples[0];
            var time = unwrappedTime;
            if (clip.IsLooping != 0)
            {
                // Remainder avoids overflowing time / duration for finite huge times
                // and tiny clips; floor-based reduction can produce infinity/NaN.
                time %= clip.Duration;
                if (time < 0) time += clip.Duration;
                // Preserve the authored endpoint at an exact positive loop boundary.
                if (time == 0 && unwrappedTime > 0) time = clip.Duration;
            }
            else time = math.clamp(time, 0, clip.Duration);
            var position = time / clip.SampleStep;
            var first = math.min((int)math.floor(position), clip.SampleCount - 2);
            var alpha = (float)(position - first);
            var a = track.Samples[first]; var b = track.Samples[first + 1];
            return new BoneLocalPose
            {
                Position = math.lerp(a.Position, b.Position, alpha),
                Rotation = math.normalize(math.slerp(a.Rotation, b.Rotation, alpha)),
                Scale = math.lerp(a.Scale, b.Scale, alpha)
            };
        }
    }
}

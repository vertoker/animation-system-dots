using Unity.Collections;
using Unity.Entities;

namespace AnimationSystemDOTS
{
    public struct BoneTrack
    {
        // One value for a constant track, SampleCount values otherwise.
        public BlobArray<BoneLocalPose> Samples;
    }

    public struct AnimationClipBlob
    {
        public int Version;
        public FixedString128Bytes ContentKey;
        public FixedString128Bytes RigKey;
        public double Duration;
        public double SampleStep;
        public int SampleCount;
        public byte IsLooping;
        public BlobArray<BoneTrack> Tracks;
        public BlobArray<BoneLocalPose> ReferencePose;
    }
}

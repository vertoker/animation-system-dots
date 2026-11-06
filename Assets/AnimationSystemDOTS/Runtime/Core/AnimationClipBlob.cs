using Unity.Collections;
using Unity.Entities;

namespace AnimationSystemDOTS
{
    public struct BoneTrack
    {
        // One value for a constant track, SampleCount values otherwise.
        public BlobArray<BoneLocalPose> Samples;
    }

    public struct ScalarTrack
    {
        public BlobArray<float> Samples;
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
        // Empty for Generic and the legacy target-TRS Humanoid mode.
        // Body is normalized center-of-mass TR; muscle values remain in source
        // muscle space until after blending. They must not be reconstructed
        // from an already retargeted skeleton.
        public BlobArray<ScalarTrack> HumanMuscles;
        public BlobArray<BoneLocalPose> HumanBody;
        public byte HasHumanAdditiveReference;
        public BlobArray<float> HumanReferenceMuscles;
        public BoneLocalPose HumanReferenceBody;
    }
}

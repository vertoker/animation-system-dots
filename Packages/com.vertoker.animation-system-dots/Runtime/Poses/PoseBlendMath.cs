using Unity.Mathematics;

namespace AnimationSystemDOTS
{
    public static class PoseBlendMath
    {
        public static BoneLocalPose Override(in BoneLocalPose a, in BoneLocalPose b, float weight)
        {
            weight = math.isfinite(weight) ? math.saturate(weight) : 0;
            if (weight == 0) return a;
            if (weight == 1) return b;
            return new BoneLocalPose
            {
                Position = math.lerp(a.Position, b.Position, weight),
                Rotation = math.normalize(math.slerp(a.Rotation, b.Rotation, weight)),
                Scale = math.lerp(a.Scale, b.Scale, weight)
            };
        }
        public static BoneLocalPose Additive(in BoneLocalPose basis, in BoneLocalPose sample, in BoneLocalPose reference, float weight)
        {
            if (!math.isfinite(weight) || weight == 0) return basis;
            var delta = math.mul(math.inverse(reference.Rotation), sample.Rotation);
            return new BoneLocalPose
            {
                Position = basis.Position + (sample.Position - reference.Position) * weight,
                Rotation = math.normalize(math.mul(basis.Rotation, math.slerp(quaternion.identity, delta, weight))),
                Scale = basis.Scale * math.lerp(new float3(1), sample.Scale / reference.Scale, weight)
            };
        }
    }
}

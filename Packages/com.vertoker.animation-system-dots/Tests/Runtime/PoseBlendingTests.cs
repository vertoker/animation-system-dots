using NUnit.Framework;
using Unity.Mathematics;

namespace AnimationSystemDOTS.Tests
{
    public class PoseBlendingTests
    {
        [Test]
        public void Override_InterpolatesTRSAndAllowsNegativeScale()
        {
            var a = BoneLocalPose.Identity; a.Position = new float3(1, 2, 3); a.Scale = new float3(-2, 1, 1);
            var b = BoneLocalPose.Identity; b.Position = new float3(5, 6, 7); b.Scale = new float3(-4, 3, 1); b.Rotation = quaternion.RotateZ(math.PI / 2);
            var result = PoseBlendMath.Override(a, b, 0.25f);
            Assert.That(math.distance(result.Position, new float3(2, 3, 4)), Is.LessThan(1e-6));
            Assert.That(math.distance(result.Scale, new float3(-2.5f, 1.5f, 1)), Is.LessThan(1e-6));
            Assert.That(math.distance(math.rotate(result.Rotation, new float3(1, 0, 0)), new float3(math.cos(math.PI / 8), math.sin(math.PI / 8), 0)), Is.LessThan(1e-5));
        }
        [Test]
        public void Override_EquivalentQuaternionSignsDoNotCauseAnExtraRotation()
        {
            var a = BoneLocalPose.Identity; a.Rotation = quaternion.RotateY(1.1f);
            var b = a; b.Rotation.value = -b.Rotation.value;
            var result = PoseBlendMath.Override(a, b, 0.37f);
            Assert.That(math.abs(math.dot(a.Rotation.value, result.Rotation.value)), Is.EqualTo(1).Within(1e-6));
        }
        [Test]
        public void Additive_UsesExplicitReferenceAndMultiplicativeScaleDelta()
        {
            var basis = BoneLocalPose.Identity; basis.Position = new float3(10, 0, 0); basis.Scale = new float3(-2, 3, 1); basis.Rotation = quaternion.RotateY(math.PI / 2);
            var reference = BoneLocalPose.Identity; reference.Position.x = 2; reference.Scale = new float3(2, -2, 1); reference.Rotation = quaternion.RotateX(0.3f);
            var sample = reference; sample.Position.x = 6; sample.Scale = new float3(4, -4, 1);
            sample.Rotation = math.mul(reference.Rotation, quaternion.RotateZ(math.PI / 2));
            var result = PoseBlendMath.Additive(basis, sample, reference, 0.5f);
            Assert.That(result.Position.x, Is.EqualTo(12).Within(1e-6));
            Assert.That(math.distance(result.Scale, new float3(-3, 4.5f, 1)), Is.LessThan(1e-6));
            var expected = math.mul(basis.Rotation, quaternion.RotateZ(math.PI / 4));
            Assert.That(math.abs(math.dot(expected.value, result.Rotation.value)), Is.EqualTo(1).Within(1e-6));
        }
        [Test]
        public void Additive_ZeroWeightAndReferenceSamplePreserveBasis()
        {
            var basis = BoneLocalPose.Identity; basis.Position = new float3(3, 4, 5); basis.Scale = new float3(-2, 1, 3); basis.Rotation = quaternion.RotateX(0.7f);
            var sample = BoneLocalPose.Identity;
            Assert.That(PoseBlendMath.Additive(basis, sample, sample, 1).Position, Is.EqualTo(basis.Position));
            Assert.That(PoseBlendMath.Additive(basis, sample, sample, 1).Scale, Is.EqualTo(basis.Scale));
            Assert.That(PoseBlendMath.Additive(basis, sample, sample, 0).Rotation, Is.EqualTo(basis.Rotation));
        }
    }
}

using Unity.Entities;

namespace AnimationSystemDOTS
{
    public static class AnimationRequestMask
    {
        public static bool HasMask(in AnimationClipRequest request) => request.Mask.IsCreated || request.MaskController.IsCreated;
        public static bool CanRead(in AnimationClipRequest request)
            => request.MaskController.IsCreated
                ? request.MaskController.Value.Version == AnimatorControllerBlob.CurrentVersion && (uint)request.ControllerMask < (uint)request.MaskController.Value.Masks.Length
                : request.Mask.IsCreated;
        public static ref BoneMaskBlob Get(in AnimationClipRequest request)
        {
            if (request.MaskController.IsCreated) return ref request.MaskController.Value.Masks[request.ControllerMask];
            return ref request.Mask.Value;
        }
    }
}

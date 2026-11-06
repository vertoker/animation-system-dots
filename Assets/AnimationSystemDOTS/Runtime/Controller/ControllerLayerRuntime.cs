using Unity.Entities;

namespace AnimationSystemDOTS
{
    public enum AnimationLayerResult : byte { Success, InvalidLayer, InvalidWeight, InvalidLayout }
    public struct ControllerLayerRuntime : IBufferElementData
    {
        public ControllerLayerState State;
        public float Weight;
    }
    // Last evaluated layer motion, before applying the layer weight. Zero-weight
    // Direct motions retain this stream with frozen clip times (Write Defaults off).
    public struct ControllerCachedRequest : IBufferElementData { public AnimationClipRequest Request; }
}

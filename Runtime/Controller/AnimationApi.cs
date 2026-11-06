using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystemDOTS
{
    public static class AnimationApi
    {
        public static int FindLayer(in AnimationController controller, in FixedString128Bytes name)
        {
            if (controller.Definition.IsCreated)
                for (var i = 0; i < controller.Definition.Value.Layers.Length; i++)
                    if (controller.Definition.Value.Layers[i].Name == name) return i;
            return -1;
        }
        public static AnimationLayerResult SetLayerWeight(in AnimationController controller, DynamicBuffer<ControllerLayerRuntime> layers, int layer, float weight)
        {
            if (!controller.Definition.IsCreated || controller.Definition.Value.Version != AnimatorControllerBlob.CurrentVersion
                || layers.Length != math.max(controller.Definition.Value.Layers.Length, 1)) return AnimationLayerResult.InvalidLayout;
            if ((uint)layer >= (uint)layers.Length) return AnimationLayerResult.InvalidLayer;
            if (!math.isfinite(weight) || weight < 0 || weight > 1) return AnimationLayerResult.InvalidWeight;
            var value = layers[layer]; value.Weight = layer == 0 ? 1 : weight; layers[layer] = value; return AnimationLayerResult.Success;
        }
        // Resolve names once at setup, retain local IDs in gameplay components/jobs.
        public static AnimationParameterId FindParameter(in AnimationController controller, in FixedString128Bytes name)
        {
            if (controller.Definition.IsCreated)
                for (var i = 0; i < controller.Definition.Value.Parameters.Length; i++)
                    if (controller.Definition.Value.Parameters[i].Name == name) return new AnimationParameterId(i);
            return AnimationParameterId.Invalid;
        }
        public static AnimationParameterResult SetFloat(in AnimationController controller, DynamicBuffer<AnimationParameterValue> values, AnimationParameterId id, float value)
        {
            var result = Validate(controller, values, id, AnimationParameterType.Float);
            if (result != AnimationParameterResult.Success) return result;
            if (!math.isfinite(value)) return AnimationParameterResult.InvalidValue;
            var item = values[id.Value]; item.Float = value; values[id.Value] = item;
            return AnimationParameterResult.Success;
        }
        public static AnimationParameterResult SetInt(in AnimationController controller, DynamicBuffer<AnimationParameterValue> values, AnimationParameterId id, int value)
            => SetInteger(controller, values, id, AnimationParameterType.Int, value);
        public static AnimationParameterResult SetBool(in AnimationController controller, DynamicBuffer<AnimationParameterValue> values, AnimationParameterId id, bool value)
            => SetInteger(controller, values, id, AnimationParameterType.Bool, value ? 1 : 0);
        public static AnimationParameterResult SetTrigger(in AnimationController controller, DynamicBuffer<AnimationParameterValue> values, AnimationParameterId id)
            => SetInteger(controller, values, id, AnimationParameterType.Trigger, 1);
        public static AnimationParameterResult ResetTrigger(in AnimationController controller, DynamicBuffer<AnimationParameterValue> values, AnimationParameterId id)
            => SetInteger(controller, values, id, AnimationParameterType.Trigger, 0);
        static AnimationParameterResult SetInteger(in AnimationController controller, DynamicBuffer<AnimationParameterValue> values, AnimationParameterId id, AnimationParameterType type, int value)
        {
            var result = Validate(controller, values, id, type);
            if (result != AnimationParameterResult.Success) return result;
            var item = values[id.Value]; item.Int = value; values[id.Value] = item;
            return AnimationParameterResult.Success;
        }
        static AnimationParameterResult Validate(in AnimationController controller, DynamicBuffer<AnimationParameterValue> values, AnimationParameterId id, AnimationParameterType type)
        {
            if (!controller.Definition.IsCreated || controller.Definition.Value.Version != AnimatorControllerBlob.CurrentVersion)
                return AnimationParameterResult.InvalidLayout;
            ref var definition = ref controller.Definition.Value;
            if ((uint)id.Value >= (uint)definition.Parameters.Length) return AnimationParameterResult.InvalidId;
            if (values.Length != definition.Parameters.Length) return AnimationParameterResult.InvalidLayout;
            return definition.Parameters[id.Value].Type == type ? AnimationParameterResult.Success : AnimationParameterResult.InvalidType;
        }
    }
}

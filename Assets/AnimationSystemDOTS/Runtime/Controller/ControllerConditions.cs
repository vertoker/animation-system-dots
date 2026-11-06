using Unity.Entities;

namespace AnimationSystemDOTS
{
    internal static class ControllerConditions
    {
        internal static bool Matches(ref AnimatorControllerBlob graph, ref ControllerTransitionDefinition edge, DynamicBuffer<AnimationParameterValue> parameters)
        {
            for (var i = 0; i < edge.ConditionCount; i++)
            {
                ref var condition = ref graph.Conditions[edge.ConditionStart + i]; var value = parameters[condition.Parameter];
                var number = graph.Parameters[condition.Parameter].Type == AnimationParameterType.Float ? (double)value.Float : value.Int;
                switch (condition.Mode)
                {
                    case ControllerConditionMode.If: if (value.Int == 0) return false; break;
                    case ControllerConditionMode.IfNot: if (value.Int != 0) return false; break;
                    case ControllerConditionMode.Greater: if (!(number > condition.Threshold)) return false; break;
                    case ControllerConditionMode.Less: if (!(number < condition.Threshold)) return false; break;
                    case ControllerConditionMode.Equals: if (number != condition.Threshold) return false; break;
                    case ControllerConditionMode.NotEqual: if (number == condition.Threshold) return false; break;
                }
            }
            return true;
        }
        internal static void Consume(ref AnimatorControllerBlob graph, ref ControllerTransitionDefinition edge, DynamicBuffer<AnimationParameterValue> parameters)
        {
            for (var i = 0; i < edge.ConditionCount; i++)
            {
                var id = graph.Conditions[edge.ConditionStart + i].Parameter;
                if (graph.Parameters[id].Type != AnimationParameterType.Trigger) continue;
                var value = parameters[id]; value.Consumed = 1; parameters[id] = value;
            }
        }
    }
}

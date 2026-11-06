using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystemDOTS
{
    public static class ControllerMotionSolver
    {
        public static void Evaluate(ref AnimatorControllerBlob graph, ref AnimationLibraryBlob library,
            DynamicBuffer<AnimationParameterValue> parameters, DynamicBuffer<ControllerMotionValue> scratch)
        {
            scratch.ResizeUninitialized(graph.Motions.Length);
            for (var i = 0; i < graph.Motions.Length; i++)
            {
                ref var node = ref graph.Motions[i];
                if (node.Type == ControllerMotionType.Clip)
                { scratch[i] = new ControllerMotionValue { Duration = library.Clips[node.Clip.Value].Duration }; continue; }
                var total = 0f;
                double durationTotal = 0;
                for (var c = 0; c < node.ChildCount; c++)
                {
                    ref var edge = ref graph.MotionChildren[node.ChildStart + c];
                    var value = scratch[edge.Motion];
                    value.ChildWeight = RawWeight(ref graph, ref node, c, parameters);
                    scratch[edge.Motion] = value; total += value.ChildWeight; durationTotal += value.ChildWeight;
                }
                double duration = 0;
                for (var c = 0; c < node.ChildCount; c++)
                {
                    ref var edge = ref graph.MotionChildren[node.ChildStart + c]; var value = scratch[edge.Motion];
                    var durationWeight = durationTotal > 0 ? value.ChildWeight / durationTotal : 0;
                    var divisor = node.Type == ControllerMotionType.Direct ? math.max(total, 1) : total;
                    value.ChildWeight = divisor > 0 ? value.ChildWeight / divisor : 0;
                    var childScale = graph.Motions[edge.Motion].Type == ControllerMotionType.Clip ? math.abs(edge.TimeScale) : 1;
                    duration += value.Duration / childScale * durationWeight;
                    scratch[edge.Motion] = value;
                }
                scratch[i] = new ControllerMotionValue { Duration = duration > 0 ? duration : 1 };
            }
        }

        static float RawWeight(ref AnimatorControllerBlob graph, ref ControllerMotionDefinition node, int index,
            DynamicBuffer<AnimationParameterValue> parameters)
        {
            ref var child = ref graph.MotionChildren[node.ChildStart + index];
            if (node.Type == ControllerMotionType.Direct) return math.max(0, parameters[child.DirectParameter].Float);
            var x = parameters[node.ParameterX].Float;
            if (node.Type == ControllerMotionType.Simple1D)
            {
                if (x == child.Threshold) return 1;
                if (x < child.Threshold)
                {
                    if (index == 0) return 1;
                    var previous = graph.MotionChildren[node.ChildStart + index - 1].Threshold;
                    return math.saturate((x - previous) / (child.Threshold - previous));
                }
                if (index == node.ChildCount - 1) return 1;
                var next = graph.MotionChildren[node.ChildStart + index + 1].Threshold;
                return math.saturate((next - x) / (next - child.Threshold));
            }
            var input = new float2(x, parameters[node.ParameterY].Float);
            if (node.Type == ControllerMotionType.SimpleDirectional2D) return SimpleDirectional(ref graph, ref node, index, input);
            var weight = 1f;
            var from = child.Position;
            for (var j = 0; j < node.ChildCount; j++)
            {
                if (j == index) continue;
                var to = graph.MotionChildren[node.ChildStart + j].Position;
                var edge = to - from; var offset = input - from;
                if (node.Type == ControllerMotionType.FreeformDirectional2D)
                {
                    var a = math.length(from); var b = math.length(to); var p = math.length(input);
                    var average = (a + b) * .5f;
                    // At the origin the missing direction follows the input ray.
                    // Keeping the pair's angular term gives Unity's rounded idle
                    // region, including a contribution beyond the radial threshold.
                    var pairAngle = a > 0 && b > 0 ? Angle(from, to)
                        : p > 0 ? (a == 0 ? Angle(input, to) : Angle(from, input)) : 0;
                    var inputAngle = a > 0 && p > 0 ? Angle(from, input) : 0;
                    if (math.abs(math.abs(pairAngle) - math.PI) < 1e-6f) pairAngle = inputAngle < 0 ? -math.PI : math.PI;
                    edge = new float2((b - a) / average, pairAngle);
                    offset = new float2((p - a) / average, inputAngle);
                }
                weight = math.min(weight, math.saturate(1 - math.dot(offset, edge) / math.lengthsq(edge)));
            }
            return weight;
        }

        static float Angle(float2 from, float2 to) => math.atan2(Cross(from, to), math.dot(from, to));
        static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

        static float SimpleDirectional(ref AnimatorControllerBlob graph, ref ControllerMotionDefinition node, int index, float2 input)
        {
            var origin = -1; var left = -1; var right = -1; var leftAngle = float.MaxValue; var rightAngle = float.MaxValue;
            for (var i = 0; i < node.ChildCount; i++)
            {
                var position = graph.MotionChildren[node.ChildStart + i].Position;
                if (math.lengthsq(position) == 0) { origin = i; continue; }
                var angle = Angle(input, position);
                var positive = angle >= 0 ? angle : angle + 2 * math.PI;
                var negative = angle <= 0 ? -angle : 2 * math.PI - angle;
                if (positive < leftAngle) { leftAngle = positive; left = i; }
                if (negative < rightAngle) { rightAngle = negative; right = i; }
            }
            if (math.lengthsq(input) == 0) return origin >= 0 ? (index == origin ? 1 : 0) : 1f / node.ChildCount;
            if (left < 0) return index == origin ? 1 : 0;
            if (leftAngle + rightAngle > math.PI + 1e-6f)
                return origin >= 0 ? (index == origin ? 1 : 0) : 1f / node.ChildCount;
            if (left == right)
            {
                var value = math.saturate(math.length(input) / math.length(graph.MotionChildren[node.ChildStart + left].Position));
                return origin >= 0 ? (index == left ? value : index == origin ? 1 - value : 0)
                    : (index == left ? value : 0) + (1 - value) / node.ChildCount;
            }
            var a = graph.MotionChildren[node.ChildStart + left].Position;
            var b = graph.MotionChildren[node.ChildStart + right].Position;
            var determinant = Cross(a, b);
            var wa = math.abs(determinant) > 1e-8f ? math.max(0, Cross(input, b) / determinant) : 0;
            var wb = math.abs(determinant) > 1e-8f ? math.max(0, Cross(a, input) / determinant) : 0;
            var sum = wa + wb;
            var divisor = math.max(sum, 1);
            var directional = index == left ? wa / divisor : index == right ? wb / divisor : 0;
            return directional + (origin >= 0 ? (index == origin ? math.max(0, 1 - sum) : 0) : math.max(0, 1 - sum) / node.ChildCount);
        }

        public static double Duration(ref AnimatorControllerBlob graph, ref ControllerStateDefinition state, DynamicBuffer<ControllerMotionValue> scratch)
            => graph.Motions.Length > 0 ? scratch[state.Motion].Duration : state.Duration;

        public static void Emit(ref AnimatorControllerBlob graph, ref AnimationLibraryBlob library, ref ControllerStateDefinition state,
            DynamicBuffer<ControllerMotionValue> scratch, double previous, double time, float weight, ulong sequence, DynamicBuffer<AnimationClipRequest> requests)
        {
            for (var i = 0; i < scratch.Length; i++) { var value = scratch[i]; value.Weight = 0; scratch[i] = value; }
            var root = scratch[state.Motion]; root.Weight = weight;
            root.PreviousTime = state.Speed < 0 ? 1 - previous - state.CycleOffset : previous + state.CycleOffset;
            root.Time = state.Speed < 0 ? 1 - time - state.CycleOffset : time + state.CycleOffset;
            root.Reversed = state.Speed < 0 ? (byte)1 : (byte)0; scratch[state.Motion] = root;
            root.PhaseOffset = state.Speed < 0 ? -state.CycleOffset : state.CycleOffset; scratch[state.Motion] = root;
            for (var i = state.Motion; i >= 0; i--)
            {
                var value = scratch[i]; if (value.Weight <= 0) continue;
                ref var node = ref graph.Motions[i];
                if (node.Type == ControllerMotionType.Clip)
                {
                    var duration = library.Clips[node.Clip.Value].Duration;
                    var offset = library.Clips[node.Clip.Value].IsLooping != 0 ? 0 : value.PhaseOffset;
                    requests.Add(new AnimationClipRequest { Clip = node.Clip, PreviousTime = (value.PreviousTime - offset) * duration, Time = (value.Time - offset) * duration,
                        Weight = value.Weight, InstanceSequence = sequence,
                        LoopBoundary = value.Reversed != 0 ? ClipLoopBoundary.End : ClipLoopBoundary.Start });
                    continue;
                }
                for (var c = 0; c < node.ChildCount; c++)
                {
                    ref var edge = ref graph.MotionChildren[node.ChildStart + c]; var child = scratch[edge.Motion];
                    child.Weight = value.Weight * child.ChildWeight;
                    var isClip = graph.Motions[edge.Motion].Type == ControllerMotionType.Clip;
                    var reverse = isClip && edge.TimeScale < 0;
                    var offset = isClip ? edge.CycleOffset : 0;
                    child.PreviousTime = reverse ? 1 - value.PreviousTime - offset : value.PreviousTime + offset;
                    child.Time = reverse ? 1 - value.Time - offset : value.Time + offset;
                    child.Reversed = (byte)(value.Reversed ^ (reverse ? 1 : 0)); scratch[edge.Motion] = child;
                    child.PhaseOffset = reverse ? -value.PhaseOffset - offset : value.PhaseOffset + offset; scratch[edge.Motion] = child;
                }
            }
        }
    }
}

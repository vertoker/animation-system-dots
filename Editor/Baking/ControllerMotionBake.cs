using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AnimationSystemDOTS.Editor.Baking
{
    // One traversal validates every leaf against the same authored binding layout.
    internal sealed class ControllerMotionBake
    {
        internal readonly List<ControllerMotionDefinition> Nodes = new List<ControllerMotionDefinition>();
        internal readonly List<ControllerMotionChild> Children = new List<ControllerMotionChild>();
        readonly IReadOnlyList<AnimationClip> clips;
        readonly Dictionary<string, int> parameters;
        readonly AnimationParameterDefinition[] definitions;
        readonly HashSet<Motion> active = new HashSet<Motion>();
        HashSet<string> layout;

        internal ControllerMotionBake(IReadOnlyList<AnimationClip> clips, Dictionary<string, int> parameters, AnimationParameterDefinition[] definitions)
        { this.clips = clips; this.parameters = parameters; this.definitions = definitions; }

        internal int Add(Motion motion, string path)
        {
            Require(motion != null, path, "Empty motions are unsupported.");
            Require(active.Add(motion), path, "Cyclic Blend Tree reference.");
            try
            {
                if (motion is AnimationClip clip)
                {
                    var bindings = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        bindings.Add(binding.type.FullName + "\n" + binding.propertyName + "\n" + binding.path);
                    if (layout == null) layout = bindings;
                    else Require(layout.SetEquals(bindings), path, "All controller clips must have the same authored property layout. Sparse Write Defaults=false channel preservation is unsupported.");
                    var id = -1; for (var i = 0; i < clips.Count; i++) if (clips[i] == clip) { id = i; break; }
                    Require(id >= 0, path, "State clip is absent from the rig's baked library.");
                    Nodes.Add(new ControllerMotionDefinition { Type = ControllerMotionType.Clip, Clip = new ClipId(id) });
                    return Nodes.Count - 1;
                }
                var tree = motion as BlendTree;
                Require(tree != null, path, "Unsupported motion type.");
                var source = tree.children;
                Require(source.Length > 0, path, "Blend Tree must have at least one child.");
                var node = new ControllerMotionDefinition { ParameterX = -1, ParameterY = -1 };
                switch (tree.blendType)
                {
                    case BlendTreeType.Simple1D: node.Type = ControllerMotionType.Simple1D; break;
                    case BlendTreeType.SimpleDirectional2D: node.Type = ControllerMotionType.SimpleDirectional2D; break;
                    case BlendTreeType.FreeformDirectional2D: node.Type = ControllerMotionType.FreeformDirectional2D; break;
                    case BlendTreeType.FreeformCartesian2D: node.Type = ControllerMotionType.FreeformCartesian2D; break;
                    case BlendTreeType.Direct: node.Type = ControllerMotionType.Direct; break;
                    default: throw new ArgumentException(path + ": Unknown Blend Tree type.");
                }
                if (node.Type != ControllerMotionType.Direct)
                {
                    node.ParameterX = FloatParameter(tree.blendParameter, path);
                    if (node.Type != ControllerMotionType.Simple1D) node.ParameterY = FloatParameter(tree.blendParameterY, path);
                }
                else
                {
                    // Unity's Blend Tree inspector serializes this option but exposes no
                    // public BlendTree property for it in Unity 6000.6.
                    var property = new SerializedObject(tree).FindProperty("m_NormalizedBlendValues");
                    Require(property != null && property.propertyType == SerializedPropertyType.Boolean, path, "Cannot read Direct normalization setting.");
                    node.NormalizeDirect = property.boolValue ? (byte)1 : (byte)0;
                    Require(property.boolValue, path, "Direct Blend Tree requires Normalize Blend Values. Unnormalized Direct channels and sparse default preservation are unsupported.");
                }
                var children = new ControllerMotionChild[source.Length];
                for (var i = 0; i < source.Length; i++)
                {
                    var child = source[i]; var childPath = path + "/" + tree.name + "[" + i + "]";
                    Require(!child.mirror, childPath, "Mirrored Blend Tree children are unsupported.");
                    Require(math.isfinite(child.threshold) && math.all(math.isfinite((float2)child.position)) && math.isfinite(child.timeScale)
                        && child.timeScale != 0 && math.isfinite(child.cycleOffset), childPath, "Child threshold/position/time scale/cycle offset must be finite; zero child time scale is unsupported.");
                    children[i] = new ControllerMotionChild { Motion = Add(child.motion, childPath), DirectParameter = node.Type == ControllerMotionType.Direct ? FloatParameter(child.directBlendParameter, childPath) : -1,
                        Position = child.position, Threshold = child.threshold, TimeScale = child.timeScale, CycleOffset = child.cycleOffset };
                    for (var j = 0; j < i; j++)
                    {
                        if (node.Type == ControllerMotionType.Simple1D) Require(child.threshold != source[j].threshold, childPath, "Duplicate 1D threshold.");
                        else if (node.Type != ControllerMotionType.Direct) Require(child.position != source[j].position, childPath, "Duplicate 2D motion position.");
                        if (node.Type == ControllerMotionType.SimpleDirectional2D && child.position.sqrMagnitude > 0 && source[j].position.sqrMagnitude > 0)
                            Require(math.abs(math.cross(new float3(child.position.x, child.position.y, 0), new float3(source[j].position.x, source[j].position.y, 0)).z) > 1e-6f
                                || Vector2.Dot(child.position, source[j].position) < 0, childPath, "Simple Directional requires at most one motion per direction; use Freeform Directional for multiple speeds.");
                    }
                }
                if (node.Type == ControllerMotionType.Simple1D) Array.Sort(children, (a, b) => a.Threshold.CompareTo(b.Threshold));
                if (node.Type == ControllerMotionType.SimpleDirectional2D)
                {
                    var angles = new List<float>();
                    foreach (var child in children) if (math.lengthsq(child.Position) > 0) angles.Add(math.atan2(child.Position.y, child.Position.x));
                    angles.Sort();
                    if (angles.Count > 1)
                        for (var i = 0; i < angles.Count; i++)
                        {
                            var next = i + 1 < angles.Count ? angles[i + 1] : angles[0] + 2 * math.PI;
                            Require(next - angles[i] < math.PI - 1e-6f, path,
                                "Simple Directional requires directions surrounding the origin (angular gaps below 180 degrees). Open or collinear layouts are unsupported; use 1D or Freeform Cartesian.");
                        }
                }
                node.ChildStart = Children.Count; node.ChildCount = children.Length; Children.AddRange(children);
                Nodes.Add(node); return Nodes.Count - 1;
            }
            finally { active.Remove(motion); }
        }

        int FloatParameter(string name, string path)
        {
            Require(name != null && parameters.TryGetValue(name, out _), path, "Unknown Blend Tree parameter: " + name);
            var id = parameters[name]; Require(definitions[id].Type == AnimationParameterType.Float, path, "Blend Tree requires a Float parameter: " + name); return id;
        }
        static void Require(bool value, string path, string message) { if (!value) throw new ArgumentException(path + ": " + message); }
    }
}

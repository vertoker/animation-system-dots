using NUnit.Framework;
using UnityEditor.Animations;

namespace AnimationSystemDOTS.Tests
{
    public class ControllerStateBlendTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void TreeFade_QuaternionGroupsMatchNative(bool destinationTree)
        {
            using (var oracle = new ControllerGraphOracle("tree-quaternion-fade-" + destinationTree))
            {
                var a = oracle.State(oracle.Root, "A", 0); var b = oracle.State(oracle.Root, "B", 2); oracle.Root.defaultState = a;
                var tree = new BlendTree { name = "SourceTree", blendType = BlendTreeType.Simple1D, blendParameter = "Cycle", useAutomaticThresholds = false };
                tree.AddChild(oracle.Clips[0], 0); tree.AddChild(oracle.Clips[1], 1);
                UnityEditor.AssetDatabase.AddObjectToAsset(tree, oracle.Source); a.motion = tree;
                if (destinationTree)
                {
                    var next = new BlendTree { name = "DestinationTree", blendType = BlendTreeType.Simple1D, blendParameter = "Time", useAutomaticThresholds = false };
                    next.AddChild(oracle.Clips[1], 0); next.AddChild(oracle.Clips[2], 1);
                    UnityEditor.AssetDatabase.AddObjectToAsset(next, oracle.Source); b.motion = next;
                }
                ControllerGraphOracle.Edge(a, b, "Go", .8f);
                oracle.StartNative(); oracle.StartEcs();
                for (var frame = 0; frame < 45; frame++)
                {
                    oracle.Float("Cycle", .31f); oracle.Float("Time", .62f);
                    if (frame == 3) oracle.Trigger("Go"); oracle.Tick(frame == 0 ? 0 : .071f);
                }
            }
        }
    }
}

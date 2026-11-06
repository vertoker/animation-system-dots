using NUnit.Framework;

namespace AnimationSystemDOTS.Tests
{
    public class ControllerTimeParameterTests
    {
        [TestCase("cycle", false)]
        [TestCase("reverseCycle", false)]
        [TestCase("time", false)]
        [TestCase("timeCycle", false)]
        [TestCase("timeExit", false)]
        [TestCase("cycle", true)]
        [TestCase("reverseCycle", true)]
        [TestCase("time", true)]
        [TestCase("timeCycle", true)]
        [TestCase("timeExit", true)]
        public void StateParameters_ClocksAndPoseMatchNative(string scenario, bool nativeOnly)
        {
            using (var oracle = new ControllerGraphOracle("parameters-" + scenario + (nativeOnly ? "-native" : "")))
            {
                var a = oracle.State(oracle.Root, "A", 0); oracle.Root.defaultState = a;
                if (scenario.Contains("Cycle") || scenario == "cycle")
                { a.cycleOffset = .13f; a.cycleOffsetParameterActive = true; a.cycleOffsetParameter = "Cycle"; }
                if (scenario.StartsWith("time"))
                { a.timeParameterActive = true; a.timeParameter = "Time"; }
                if (scenario == "reverseCycle") a.speed = -1;
                if (scenario == "timeExit")
                {
                    var b = oracle.State(oracle.Root, "B", 1);
                    var edge = a.AddTransition(b); edge.hasExitTime = true; edge.exitTime = .45f; edge.hasFixedDuration = true; edge.duration = .3f;
                }
                oracle.StartNative(); if (!nativeOnly) oracle.StartEcs();
                for (var frame = 0; frame < 45; frame++)
                {
                    oracle.Float("Cycle", frame < 8 ? .27f : frame < 17 ? -.19f : 1.31f);
                    oracle.Float("Time", frame < 4 ? .23f : frame < 9 ? .77f : frame < 15 ? -.13f : 1.19f);
                    if (nativeOnly) oracle.CaptureNative(frame == 0 ? 0 : .071f);
                    else oracle.Tick(frame == 0 ? 0 : .071f);
                }
            }
        }
    }
}

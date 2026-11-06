using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    // Camera/UI/input bridge only: no runtime character GameObject or Animator.
    [DefaultExecutionOrder(-50)]
    public sealed class EcsCharacterView : MonoBehaviour
    {
        public AnimatorValidationOrbit Orbit;
        public bool Bots;
        public CharacterCommand Command;
        public bool AutomatedTrace;
        public ulong Tick;
        public int ActorCount;
        public CharacterMember Primary;
        public string[] LayerStates = System.Array.Empty<string>();
        World world;
        Entity owner;
        EntityQuery owners;
        CharacterActions queued;
        bool reset, jump;
        public void Queue(CharacterActions action) => queued |= action;
        public void QueueJump() => jump = true;
        public void ResetCharacter() => reset = true;
        void LateUpdate()
        {
            var active = World.DefaultGameObjectInjectionWorld;
            if (active == null || !active.IsCreated) { ActorCount = 0; return; }
            if (world != active)
            { Release(); world = active; owners = world.EntityManager.CreateEntityQuery(typeof(CharacterSceneControl)); }
            var em = world.EntityManager;
            if (!em.Exists(owner) || !em.HasComponent<CharacterSceneControl>(owner))
            {
                owner = Entity.Null;
                using (var entities = owners.ToEntityArray(Allocator.Temp)) foreach (var entity in entities)
                    if ((em.GetComponentData<CharacterSceneControl>(entity).Bots != 0) == Bots) { owner = entity; break; }
                if (owner == Entity.Null) { ActorCount = 0; return; }
            }
            var control = em.GetComponentData<CharacterSceneControl>(owner); ActorCount = control.Count; Tick = control.Tick;
            var command = Command; command.Actions |= queued; queued = CharacterActions.None;
            if (jump) command.Motor.Jump = 1; jump = false;
            if (reset) command.Reset = 1; reset = false;
            // Preserve edges if no simulation step occurred since the last frame.
            command.Actions |= control.ManualCommand.Actions; command.Motor.Jump |= control.ManualCommand.Motor.Jump; command.Reset |= control.ManualCommand.Reset;
            control.ManualCommand = command; control.AutomatedTrace = AutomatedTrace ? (byte)1 : (byte)0; em.SetComponentData(owner, control);
            if (!em.Exists(control.Primary)) return;
            Primary = em.GetComponentData<CharacterMember>(control.Primary);
            if (Orbit != null && !Bots) Orbit.Focus = Primary.Motor.Position;
            var controller = em.GetComponentData<AnimationController>(control.Primary);
            var layers = em.GetBuffer<ControllerLayerRuntime>(control.Primary);
            if (LayerStates.Length != layers.Length) LayerStates = new string[layers.Length];
            for (var l = 0; l < layers.Length; l++)
            {
                var state = layers[l].State; ref var graph = ref controller.Definition.Value;
                LayerStates[l] = graph.Layers[l].Name + " | " + ((uint)state.CurrentState < (uint)graph.States.Length ? graph.States[state.CurrentState].Path.ToString() : "invalid")
                    + " | t " + state.NormalizedTime.ToString("F2") + " | weight " + layers[l].Weight.ToString("F2")
                    + (state.NextState >= 0 ? " -> " + graph.States[state.NextState].Path.ToString() : "");
            }
        }
        void Release() { if (world != null && world.IsCreated) owners.Dispose(); world = null; owner = Entity.Null; }
        void OnDisable() => Release();
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, 680, 340), GUI.skin.box);
            GUILayout.Label(Bots ? "07 | 100 autonomous ECS characters | shared Bots100.controller" : "06 | ECS player | shared Bots100.controller");
            GUILayout.Label("Actors " + ActorCount + " | tick " + Tick + " | speed " + math.length(Primary.Motor.Velocity).ToString("F2") + " | ground " + Primary.Motor.Grounded + " | respawns " + Primary.Respawns);
            if (!Bots)
            {
                GUILayout.Label("WASD / Shift / Alt / Space / RMB | 1 wave | 2 punch | 3 react | 4 death | R reset | T trace | Tab tree | V variant | C cycle | F scrub");
                if (GUILayout.Button(AutomatedTrace ? "Stop deterministic trace" : "Run deterministic trace")) { AutomatedTrace = !AutomatedTrace; ResetCharacter(); }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Reset")) ResetCharacter();
                if (GUILayout.Button("Tree: " + Command.TreeMode)) Command.TreeMode = (Command.TreeMode + 1) % 5;
                if (GUILayout.Button("Variant: " + Command.Variant)) { Command.Variant = (Command.Variant + 1) % 3; Command.Variation = (Command.Variation + 1) % 5; }
                if (GUILayout.Button("Wave")) Queue(CharacterActions.Wave); if (GUILayout.Button("Punch")) Queue(CharacterActions.Punch);
                if (GUILayout.Button("React")) Queue(CharacterActions.React); if (GUILayout.Button("Die")) Queue(CharacterActions.Die);
                GUILayout.EndHorizontal();
                Command.Scrub = GUILayout.Toggle(Command.Scrub != 0, "Motion Time / pose scrub") ? (byte)1 : (byte)0;
                if (Command.Scrub != 0) Command.PoseTime = GUILayout.HorizontalSlider(Command.PoseTime, 0, 1);
            }
            foreach (var state in LayerStates) GUILayout.Label(state);
            GUILayout.EndArea();
        }
    }
}

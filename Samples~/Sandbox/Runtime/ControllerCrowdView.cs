using Unity.Entities;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    public sealed class ControllerCrowdView : MonoBehaviour
    {
        public bool ShowOverlay = true;
        int count, active, transitions;
        string firstState = "Loading";
        void Update()
        {
            if (Time.frameCount % 30 != 0) return;
            var world = World.DefaultGameObjectInjectionWorld; if (world == null || !world.IsCreated) return;
            count = active = transitions = 0;
            using (var query = world.EntityManager.CreateEntityQuery(typeof(ControllerCrowdMember), typeof(AnimationController), typeof(ControllerLayerState)))
            using (var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                count = entities.Length;
                foreach (var entity in entities)
                {
                    var state = world.EntityManager.GetComponentData<ControllerLayerState>(entity);
                    if (!ControllerStateQuery.TryGet(state, out var snapshot)) continue;
                    active++; if (snapshot.IsInTransition) transitions++;
                    if (world.EntityManager.GetComponentData<ControllerCrowdMember>(entity).Index == 0)
                    {
                        var graph = world.EntityManager.GetComponentData<AnimationController>(entity).Definition;
                        firstState = graph.Value.States[snapshot.CurrentState].Path.ToString() + (snapshot.IsInTransition ? " -> " + graph.Value.States[snapshot.NextState].Path.ToString() : "");
                    }
                }
            }
        }
        void OnGUI()
        {
            if (!ShowOverlay) return;
            GUI.Box(new Rect(12, 12, 390, 208), "AnimatorController -> ECS: " + active + " / " + count);
            GUI.Label(new Rect(24, 38, 370, 24), firstState + "  |  fades: " + transitions);
            var world = World.DefaultGameObjectInjectionWorld; if (world == null || !world.IsCreated) return;
            using (var query = world.EntityManager.CreateEntityQuery(typeof(ControllerCrowdControl)))
            {
                if (query.CalculateEntityCount() != 1) return;
                var control = query.GetSingleton<ControllerCrowdControl>(); var old = control;
                control.Automatic = GUI.Toggle(new Rect(24, 70, 330, 24), control.Automatic != 0, "Automatic: independent Walk / Workout") ? (byte)1 : (byte)0;
                if (GUI.Button(new Rect(24, 102, 170, 28), "All Walk")) { control.Automatic = 0; control.Workout = 0; }
                if (GUI.Button(new Rect(208, 102, 170, 28), "All Workout")) { control.Automatic = 0; control.Workout = 1; }
                GUI.Label(new Rect(24, 138, 330, 20), "Speed: " + control.Speed.ToString("0.00"));
                control.Speed = GUI.HorizontalSlider(new Rect(24, 166, 354, 20), control.Speed, .25f, 2);
                if (GUI.Button(new Rect(24, 186, 170, 24), "Crowd")) View(false);
                if (GUI.Button(new Rect(208, 186, 170, 24), "Close")) View(true);
                if (old.Automatic != control.Automatic || old.Workout != control.Workout || old.Speed != control.Speed)
                    world.EntityManager.SetComponentData(query.GetSingletonEntity(), control);
            }
        }
        public void View(bool close)
        {
            var camera = GetComponent<Camera>(); camera.fieldOfView = close ? 45 : 48;
            transform.position = close ? new Vector3(-44.6f, 2, 34) : new Vector3(58, 52, 72);
            transform.LookAt(close ? new Vector3(-46.8f, 1, 28.8f) : new Vector3(0, .5f, 0));
        }
    }
}

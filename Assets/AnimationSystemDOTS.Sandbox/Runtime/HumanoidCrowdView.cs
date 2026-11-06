using Unity.Entities;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    public class HumanoidCrowdView : MonoBehaviour
    {
        public bool ShowOverlay = true;
        public bool CloseView;
        int count;
        float fps;
        void Start() => SetView(false);
        void Update()
        {
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, .0001f), .05f);
            if (Time.frameCount % 30 != 0) return;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            using (var q = world.EntityManager.CreateEntityQuery(typeof(HumanoidCrowdMember))) count = q.CalculateEntityCount();
        }
        public void SetView(bool close)
        {
            CloseView = close; var camera = GetComponent<Camera>(); camera.orthographic = false;
            camera.fieldOfView = close ? 55 : 48;
            transform.position = close ? new Vector3(-43.2f, 2.4f, 36.5f) : new Vector3(58, 52, 72);
            transform.LookAt(close ? new Vector3(-43.2f, 1, 28.8f) : new Vector3(0, .5f, 0));
        }
        public void SetReferenceView()
        {
            CloseView = true;
            GetComponent<Camera>().fieldOfView = 38;
            transform.position = new Vector3(-44.6f, 1.8f, 33.5f);
            transform.LookAt(new Vector3(-46.8f, 1, 28.8f));
        }
        void OnGUI()
        {
            if (!ShowOverlay) return;
            GUI.Box(new Rect(12, 12, 370, 188), "Humanoid crowd — " + count + " / 1000");
            GUI.Label(new Rect(24, 38, 350, 22), "Workout: walk / punch / squat / jump  |  " + fps.ToString("0") + " FPS");
            if (GUI.Button(new Rect(24, 65, 164, 28), "Crowd view")) SetView(false);
            if (GUI.Button(new Rect(198, 65, 164, 28), "Close: first four")) SetView(true);
            var world = World.DefaultGameObjectInjectionWorld; if (world == null || !world.IsCreated) return;
            using (var query = world.EntityManager.CreateEntityQuery(typeof(HumanoidCrowdControl)))
            {
                if (query.CalculateEntityCount() != 1) return;
                var control = query.GetSingleton<HumanoidCrowdControl>(); var changed = false;
                if (GUI.Button(new Rect(24, 102, 108, 26), control.Manual != 0 ? "Resume" : "Freeze t=0"))
                { control.Manual = control.Manual == 0 ? (byte)1 : (byte)0; control.Time = 0; changed = true; }
                if (GUI.Button(new Rect(139, 102, 108, 26), "Pose t=0")) { control.Manual = 1; control.Time = 0; changed = true; }
                if (GUI.Button(new Rect(254, 102, 108, 26), "Pose t=1")) { control.Manual = 1; control.Time = 1; changed = true; }
                if (GUI.Button(new Rect(24, 136, 164, 26), "Video: first agent")) SetReferenceView();
                GUI.Label(new Rect(198, 138, 160, 22), "Pose: " + control.Time.ToString("0.00") + " s");
                var time = GUI.HorizontalSlider(new Rect(24, 176, 334, 20), (float)control.Time, 0, 12.29167f);
                if (Mathf.Abs(time - (float)control.Time) > .0001f) { control.Manual = 1; control.Time = time; changed = true; }
                if (changed) world.EntityManager.SetComponentData(query.GetSingletonEntity(), control);
            }
        }
    }
}

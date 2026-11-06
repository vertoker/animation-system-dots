using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    public sealed class AnimatorValidationOrbit : MonoBehaviour
    {
        public Transform Target;
        public Vector3 Focus;
        public float Yaw, Pitch = 18, Distance = 4.5f;
        void LateUpdate()
        {
            var focus = (Target != null ? Target.position : Focus) + Vector3.up * 1.05f;
            var rotation = Quaternion.Euler(Pitch, Yaw, 0);
            transform.SetPositionAndRotation(focus + rotation * new Vector3(0, 0, -Distance), rotation);
        }
    }
}

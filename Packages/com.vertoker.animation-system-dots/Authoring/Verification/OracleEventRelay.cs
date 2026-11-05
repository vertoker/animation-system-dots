#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace AnimationSystemDOTS.Authoring.Verification
{
    // Only attached to the transient preview instance. Original clip callbacks are never invoked.
    [AddComponentMenu("")]
    public sealed class OracleEventRelay : MonoBehaviour
    {
        public readonly List<int> Events = new List<int>();
        public void RecordOracleEvent(int eventIndex) => Events.Add(eventIndex);
    }
}
#endif

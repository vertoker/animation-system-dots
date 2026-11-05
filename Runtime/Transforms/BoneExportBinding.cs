using Unity.Entities;
using Unity.Transforms;

namespace AnimationSystemDOTS
{
    // The animation system owns this world matrix. Stock transforms still update
    // children from it, but must never replace it with an authoring rest transform.
    [WriteGroup(typeof(LocalToWorld))]
    public struct BoneExportBinding : IComponentData
    {
        public Entity Rig;
        public int BoneIndex;
    }
}

using Unity.Entities;

namespace AnimationSystemDOTS.Sandbox.Editor
{
    public class HumanoidCrowdBaker : Baker<HumanoidCrowd>
    {
        public override void Bake(HumanoidCrowd authoring)
        {
            if (authoring.Prefab == null) return;
            AddComponent(GetEntity(TransformUsageFlags.None), new HumanoidCrowdSpawn
            { Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic), Count = authoring.Count, Columns = authoring.Columns, Spacing = authoring.Spacing });
        }
    }
}

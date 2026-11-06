using Unity.Entities;

namespace AnimationSystemDOTS.Sandbox.Editor
{
    public sealed class AnimatorValidationBaker : Baker<AnimatorValidationSpawner>
    {
        public override void Bake(AnimatorValidationSpawner authoring)
        {
            if (authoring.Prefab == null || authoring.Settings == null) return;
            DependsOn(authoring.Settings);
            AddComponent(GetEntity(TransformUsageFlags.None), new CharacterSceneConfig {
                Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic), Count = authoring.Count,
                Bots = authoring.Bots ? (byte)1 : (byte)0, Seed = authoring.Seed,
                Durations = authoring.Settings.Durations, Weights = authoring.Settings.LayerWeights });
        }
    }
}

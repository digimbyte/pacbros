using UnityEngine;

public static class EnemyPhysicsBootstrapper
{
    static readonly string[] EnemyLayerNames = { "Enemy", "Ghost", "Enemies" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ConfigureEnemyLayerCollisions()
    {
        var layers = new System.Collections.Generic.List<int>();

        for (int i = 0; i < EnemyLayerNames.Length; i++)
        {
            string name = EnemyLayerNames[i];
            if (string.IsNullOrWhiteSpace(name))
                continue;

            int layer = LayerMask.NameToLayer(name);
            if (layer >= 0 && !layers.Contains(layer))
                layers.Add(layer);
        }

        for (int i = 0; i < layers.Count; i++)
        {
            for (int j = i; j < layers.Count; j++)
            {
                Physics.IgnoreLayerCollision(layers[i], layers[j], true);
            }
        }
    }
}

using UnityEngine;

/// <summary>
/// Base class for all enemy brain behaviors.
/// Each brain type inherits from this and implements its specific AI logic.
/// </summary>
public abstract class BrainBehavior
{
    protected EnemyBrainController controller;

    public BrainBehavior(EnemyBrainController controller)
    {
        this.controller = controller;
    }

    /// <summary>
    /// Update method called every frame for this brain type.
    /// </summary>
    /// <param name="dt">Delta time since last frame</param>
    public abstract void Update(float dt);
}
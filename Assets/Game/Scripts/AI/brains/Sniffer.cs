using UnityEngine;

/// <summary>
/// Sniffer brain: Tracks player breadcrumbs when out of range, chases directly when close.
/// </summary>
public class SnifferBrain : BrainBehavior
{
    public SnifferBrain(EnemyBrainController controller) : base(controller) { }

    public override void Update(float dt)
    {
        Vector3 targetPos = controller.CurrentTarget.transform.position;
        float dist = Vector3.Distance(controller.transform.position, targetPos);

        Vector3 goal = targetPos;
        var crumbs = controller.PlayerTracker.GetBreadcrumbs(controller.CurrentTarget);

        if (dist > controller.snifferChaseDistance)
        {
            if (crumbs != null && crumbs.Count > 0)
                goal = crumbs[0];
        }

        controller.ScheduleDestination(goal, forceImmediate: false);
    }
}
using UnityEngine;

/// <summary>
/// Afraid brain: Flees from the player while maintaining safe distance.
/// </summary>
public class AfraidBrain : BrainBehavior
{
    private Vector3 _afraidFleeDirection;
    private Vector3 _afraidLastPlayerPos;
    private float _lastAfraidDecision;

    public AfraidBrain(EnemyBrainController controller) : base(controller) { }

    public override void Update(float dt)
    {
        Vector3 playerPos = controller.CurrentTarget.transform.position;
        float dist = Vector3.Distance(controller.transform.position, playerPos);

        if (dist <= controller.afraidPanicChaseDistance)
        {
            controller.ScheduleDestination(playerPos, forceImmediate: true);
            return;
        }

        if (
            _afraidFleeDirection == Vector3.zero
            || Time.time - _lastAfraidDecision > controller.decisionCooldown
            || Vector3.Distance(_afraidLastPlayerPos, playerPos) > 1f
        )
        {
            Vector3 away = (controller.transform.position - playerPos);
            away.y = 0f;

            if (away.sqrMagnitude < 0.001f)
                away = Random.insideUnitSphere;

            away.y = 0f;
            if (away.sqrMagnitude < 0.001f)
                away = Vector3.forward;

            away.Normalize();
            _afraidFleeDirection = away;
            _afraidLastPlayerPos = playerPos;
            _lastAfraidDecision = Time.time;
        }

        float desired = dist < controller.afraidInnerRadius ? controller.afraidFleeDistance : controller.afraidDesiredRadius;
        Vector3 goal = playerPos + _afraidFleeDirection * desired;
        controller.ScheduleDestination(goal, forceImmediate: false);
    }
}
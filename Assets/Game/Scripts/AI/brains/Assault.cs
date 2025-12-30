using UnityEngine;

/// <summary>
/// Assault brain: Sets up ambushes and strikes at the player.
/// </summary>
public class AssaultBrain : BrainBehavior
{
    enum AssaultState
    {
        Moving,
        Holding,
        Striking
    }

    private AssaultState _assaultState = AssaultState.Moving;
    private float _assaultHoldTimer;
    private Vector3 _assaultAmbushPoint;
    private bool _isCamping;

    public AssaultBrain(EnemyBrainController controller) : base(controller) { }

    public override void Update(float dt)
    {
        switch (_assaultState)
        {
            case AssaultState.Moving:
            {
                _isCamping = false;

                if (
                    _assaultAmbushPoint == Vector3.zero
                    || Vector3.Distance(_assaultAmbushPoint, controller.CurrentTarget.transform.position) > 2f
                )
                {
                    if (Time.time - controller.LastDecisionAt >= controller.decisionCooldown)
                    {
                        PickAmbushPoint();
                        controller.LastDecisionAt = Time.time;
                    }
                }

                if (_assaultAmbushPoint != Vector3.zero)
                    controller.ScheduleDestination(_assaultAmbushPoint, forceImmediate: false);

                if (
                    _assaultAmbushPoint != Vector3.zero
                    && Vector3.Distance(controller.transform.position, _assaultAmbushPoint)
                        <= Mathf.Max(0.5f, controller.destinationUpdateThreshold)
                )
                {
                    if (Time.time - controller.LastDecisionAt >= controller.decisionCooldown)
                    {
                        _assaultState = AssaultState.Holding;
                        _assaultHoldTimer = controller.assaultHoldSeconds;
                        controller.LastDecisionAt = Time.time;
                    }
                }
                break;
            }

            case AssaultState.Holding:
            {
                _isCamping = true;
                _assaultHoldTimer -= dt;

                float dist = Vector3.Distance(
                    controller.transform.position,
                    controller.CurrentTarget.transform.position
                );
                if (
                    (dist <= controller.assaultTriggerDistance || _assaultHoldTimer <= 0f)
                    && Time.time - controller.LastDecisionAt >= controller.decisionCooldown
                )
                {
                    _assaultState = AssaultState.Striking;
                    controller.LastDecisionAt = Time.time;
                }
                break;
            }

            case AssaultState.Striking:
            {
                _isCamping = false;
                Vector3 strikeGoal = controller.CurrentTarget.transform.position;
                controller.ScheduleDestination(strikeGoal, forceImmediate: true);

                if (
                    Vector3.Distance(controller.transform.position, strikeGoal) <= controller.assaultResetDistance
                    && Time.time - controller.LastDecisionAt >= controller.decisionCooldown
                )
                {
                    _assaultState = AssaultState.Moving;
                    _assaultAmbushPoint = Vector3.zero;
                    controller.LastDecisionAt = Time.time;
                }
                break;
            }
        }
    }

    private void PickAmbushPoint()
    {
        Vector3 predicted = controller.PredictPlayerPosition(controller.CurrentTarget, controller.assaultLeadTime);
        Vector3 lateral = Vector3.Cross(Vector3.up, predicted - controller.transform.position).normalized;
        lateral *= Random.Range(-1f, 1f) > 0 ? 1f : -1f;

        _assaultAmbushPoint = controller.SampleReachablePoint(predicted + lateral, 2.5f);
    }
}
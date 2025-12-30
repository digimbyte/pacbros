using UnityEngine;

/// <summary>
/// Curious brain: Alternates between wandering and predicting player movement.
/// </summary>
public class CuriousBrain : BrainBehavior
{
    enum CuriousMode
    {
        Wander,
        Predict
    }

    private CuriousMode _curiousMode = CuriousMode.Wander;
    private float _curiousModeUntil;
    private Vector3 _curiousGoal;

    public CuriousBrain(EnemyBrainController controller) : base(controller) { }

    public override void Update(float dt)
    {
        if (Time.time >= _curiousModeUntil && Time.time - controller.LastDecisionAt >= controller.decisionCooldown)
            SwitchCuriousMode();

        if (
            Vector3.Distance(controller.transform.position, _curiousGoal)
            <= Mathf.Max(0.6f, controller.destinationUpdateThreshold)
        )
        {
            if (Time.time - controller.LastDecisionAt >= controller.decisionCooldown)
                SwitchCuriousMode();
        }

        controller.ScheduleDestination(_curiousGoal, forceImmediate: false);
    }

    private void SwitchCuriousMode()
    {
        _curiousMode =
            (_curiousMode == CuriousMode.Wander) ? CuriousMode.Predict : CuriousMode.Wander;
        float duration =
            (_curiousMode == CuriousMode.Wander) ? controller.curiousWanderDuration : controller.curiousInterceptDuration;
        _curiousModeUntil = Time.time + duration;
        controller.LastDecisionAt = Time.time;

        if (_curiousMode == CuriousMode.Wander)
            _curiousGoal = controller.SampleReachablePoint(
                controller.CurrentTarget.transform.position,
                controller.curiousWanderRadius
            );
        else
            _curiousGoal = controller.PredictPlayerPosition(controller.CurrentTarget, controller.curiousLeadTime);
    }
}
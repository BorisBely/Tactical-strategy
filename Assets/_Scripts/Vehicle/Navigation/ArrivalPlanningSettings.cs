using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Settings for the precision arrival planner. All distances derived from turning radius.
	/// </summary>
	public sealed class ArrivalPlanningSettings
	{
		public float TurnRadius { get; }

		public float PlanningDistance => Mathf.Max(4f * TurnRadius, 6f);
		public float PrecisionRadius => Mathf.Max(0.3f, TurnRadius * 0.08f);
		public float ReachableSectorAngle => Mathf.Atan2(TurnRadius, Mathf.Max(1f, PlanningDistance)) * Mathf.Rad2Deg * 1.5f;
		public float SideOffsetThreshold => Mathf.Max(0.8f, TurnRadius * 0.15f);
		public float PreGoalDistance => Mathf.Max(TurnRadius * 0.4f, 2f);
		public float RepositionStep => Mathf.Max(TurnRadius * 0.55f, 1.5f);
		public float PrecisionMaxSpeedKmh = 3f;
		public float PrecisionLookAhead = 1.2f;
		public float PrecisionActivationDistance = 2f;

		public ArrivalPlanningSettings(float _turnRadius)
		{
			TurnRadius = Mathf.Max(2f, _turnRadius);
		}
	}
}

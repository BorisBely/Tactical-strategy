using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Tuning data for the virtual driver. Kept separate from VehicleTuning (physics).
	/// </summary>
	[CreateAssetMenu(fileName = "VehicleNavigationSettings", menuName = "Vehicles/Navigation Settings")]
	public class VehicleNavigationSettings : ScriptableObject
	{
		[Tooltip("Look-ahead distance at low speed.")]
		public float LookAheadBase = 6f;

		[Tooltip("Maximum straight-line segment that may be driven in reverse.")]
		public float ReverseMaxSegment = 12f;

		[Tooltip("Angle from forward beyond which reverse is preferred over forward.")]
		public float ReverseAngleDegrees = 150f;

		[Tooltip("Multiplier applied to kinematic min turning radius for planning margin.")]
		public float TurnRadiusMultiplier = 1f;

		[Tooltip("Extra clearance margin around vehicle width for probes and NavMesh.")]
		public float SafetyMargin = 0.3f;

		[Tooltip("Vehicle width used for side-clearance probes.")]
		public float VehicleWidth = 2.4f;

		[Tooltip("Layers checked by local geometry probes.")]
		public LayerMask GeometryLayers;

		[Tooltip("Speed below which the vehicle is considered stuck.")]
		public float StuckSpeedKmh = 1.2f;

		[Tooltip("Time below stuck speed before recovery triggers.")]
		public float StuckTimeSeconds = 3f;

		[Tooltip("Legacy equivalent radius Max(long, lat). Prefer longitudinal/lateral fields.")]
		public float ArrivalPositionTolerance = 0.45f;

		[Tooltip("Arrival oval: forward/back relative to chassis (tight — no intentional short-stop).")]
		public float ArrivalLongitudinalTolerance = 0.1f;

		[Tooltip("Arrival oval: left/right relative to chassis (wider — stop finish micro-corrections).")]
		public float ArrivalLateralTolerance = 0.45f;

		[Tooltip("Default final heading tolerance in degrees.")]
		public float ArrivalHeadingTolerance = 5f;

		[Tooltip("Max speed considered as arrived.")]
		public float ArrivalMaxSpeedKmh = 1f;

		[Tooltip("Time pose must stay within tolerance before success.")]
		public float ArrivalStableWindowSeconds = 0.4f;

		[Tooltip("Legacy alias kept for serialized scenes; derived from profile when zero.")]
		public float TurnRadius = 7f;

		[Header("Local Pose Planner")]
		[Tooltip("Use Hybrid A* local pose planner for short / precise arrivals.")]
		public bool UseLocalPosePlanner = true;

		[Tooltip("Distance within which the local pose planner owns the route.")]
		public float LocalPlanningDistance = 15f;

		[Tooltip("Dense fan ray count used only during replan.")]
		public int DenseFanRayCount = 24;

		[Tooltip("Max probe distance for planning-only dense fan.")]
		public float DenseFanMaxDistance = 12f;

		[Tooltip("Motion primitive step length for lattice expansion.")]
		public float LocalPrimitiveStep = 0.6f;

		[Tooltip("Max CPU milliseconds spent per planning slice (one slice per rendered frame).")]
		public float LocalPlanSliceBudgetMs = 1.5f;

		[Tooltip("Max total CPU milliseconds for one local planning session before bounded failure.")]
		public float LocalPlanTotalBudgetMs = 350f;

		[Tooltip("Max realtime seconds in Planning state before timeout.")]
		public float LocalPlanWallTimeoutSec = 6f;

		[Tooltip("If true, continuous FeedbackSystem uses lightweight emergency probes instead of full 8-ray geometry.")]
		public bool LightweightRuntimeProbes = true;
	}
}

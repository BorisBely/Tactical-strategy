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

		[Tooltip("Approximate minimum turning radius used for arc generation.")]
		public float TurnRadius = 7f;

		[Tooltip("Vehicle width used for side-clearance probes.")]
		public float VehicleWidth = 2.4f;

		[Tooltip("Layers checked by local geometry probes.")]
		public LayerMask GeometryLayers;

		[Tooltip("Speed below which the vehicle is considered stuck.")]
		public float StuckSpeedKmh = 1.2f;

		[Tooltip("Time below stuck speed before recovery triggers.")]
		public float StuckTimeSeconds = 3f;

		[Tooltip("Default final position tolerance.")]
		public float ArrivalPositionTolerance = 0.6f;

		[Tooltip("Default final heading tolerance in degrees.")]
		public float ArrivalHeadingTolerance = 8f;
	}
}

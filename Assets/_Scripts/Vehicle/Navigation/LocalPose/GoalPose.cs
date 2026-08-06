using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Unified navigation goal: position is always required, yaw policy is explicit.
	/// Arrival accept uses an oval band in the vehicle frame (longitudinal / lateral).
	/// <see cref="PositionTolerance"/> is Max(long, lat) for planner/search compatibility.
	/// </summary>
	public readonly struct GoalPose
	{
		public readonly Vector3 Position;
		public readonly float YawDegrees;
		public readonly GoalHeadingSource HeadingSource;
		public readonly float PositionTolerance;
		public readonly float LongitudinalTolerance;
		public readonly float LateralTolerance;
		public readonly float HeadingToleranceDeg;

		public bool RequiresPosePlanning => HeadingSource == GoalHeadingSource.RequiredExplicit;
		public bool HasAdvisoryHeading => HeadingSource == GoalHeadingSource.SoftPathTangent;
		public bool HasHeading => RequiresPosePlanning;

		public GoalPose(
			Vector3 _position,
			float? _yawDegrees,
			float _positionTolerance = 0.5f,
			float _headingToleranceDeg = 5f)
			: this(
				_position,
				_yawDegrees,
				_yawDegrees.HasValue ? GoalHeadingSource.RequiredExplicit : GoalHeadingSource.None,
				_positionTolerance,
				_headingToleranceDeg)
		{
		}

		public GoalPose(
			Vector3 _position,
			float? _yawDegrees,
			GoalHeadingSource _headingSource,
			float _positionTolerance = 0.5f,
			float _headingToleranceDeg = 5f)
			: this(
				_position,
				_yawDegrees,
				_headingSource,
				_positionTolerance,
				_positionTolerance,
				_positionTolerance,
				_headingToleranceDeg)
		{
		}

		public GoalPose(
			Vector3 _position,
			float? _yawDegrees,
			GoalHeadingSource _headingSource,
			float _longitudinalTolerance,
			float _lateralTolerance,
			float _headingToleranceDeg)
			: this(
				_position,
				_yawDegrees,
				_headingSource,
				ArrivalPositionBand.EquivalentRadius(_longitudinalTolerance, _lateralTolerance),
				_longitudinalTolerance,
				_lateralTolerance,
				_headingToleranceDeg)
		{
		}

		private GoalPose(
			Vector3 _position,
			float? _yawDegrees,
			GoalHeadingSource _headingSource,
			float _positionTolerance,
			float _longitudinalTolerance,
			float _lateralTolerance,
			float _headingToleranceDeg)
		{
			Position = _position;
			HeadingSource = _yawDegrees.HasValue ? _headingSource : GoalHeadingSource.None;
			YawDegrees = _yawDegrees ?? 0f;
			LongitudinalTolerance = Mathf.Max(0.05f, _longitudinalTolerance);
			LateralTolerance = Mathf.Max(0.05f, _lateralTolerance);
			PositionTolerance = Mathf.Max(
				0.05f,
				Mathf.Max(_positionTolerance, ArrivalPositionBand.EquivalentRadius(LongitudinalTolerance, LateralTolerance)));
			HeadingToleranceDeg = Mathf.Max(0.5f, _headingToleranceDeg);
		}

		public static GoalPose FromRequest(NavigationRequest _request, GoalPoseCriteria _criteria)
		{
			float lon = _criteria.LongitudinalTolerance > 0f
				? _criteria.LongitudinalTolerance
				: ArrivalPositionBand.DefaultLongitudinal;
			float lat = _criteria.LateralTolerance > 0f
				? _criteria.LateralTolerance
				: (_request.MinArrivalDistance > 0f
					? _request.MinArrivalDistance
					: ArrivalPositionBand.DefaultLateral);
			float heading = _criteria.HeadingToleranceDeg > 0f
				? _criteria.HeadingToleranceDeg
				: _request.MinArrivalHeading;
			return new GoalPose(
				_request.Destination,
				_request.HeadingYaw,
				_request.HeadingSource,
				lon,
				lat,
				heading);
		}

		public bool IsReached(Vector3 _position, float _yaw) =>
			ArrivalPositionBand.IsInside(this, _position, _yaw) &&
			(!RequiresPosePlanning ||
			 Mathf.Abs(Mathf.DeltaAngle(_yaw, YawDegrees)) <= HeadingToleranceDeg);

		public float AdvisoryYawPenalty(float _yaw)
		{
			if (!HasAdvisoryHeading)
				return 0f;
			return Mathf.Abs(Mathf.DeltaAngle(_yaw, YawDegrees));
		}
	}
}

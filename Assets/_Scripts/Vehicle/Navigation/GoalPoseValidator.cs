using UnityEngine;

namespace VehicleNavigation
{
	public readonly struct GoalPoseCriteria
	{
		/// <summary>Equivalent radius Max(long, lat) for legacy callers / planner search.</summary>
		public readonly float PositionTolerance;
		public readonly float LongitudinalTolerance;
		public readonly float LateralTolerance;
		public readonly float HeadingToleranceDeg;
		public readonly float MaxSpeedKmh;
		public readonly float StableWindowSeconds;

		public GoalPoseCriteria(
			float _positionTolerance = 0.45f,
			float _headingToleranceDeg = 5f,
			float _maxSpeedKmh = 1f,
			float _stableWindowSeconds = 0.4f)
			: this(
				_positionTolerance,
				_positionTolerance,
				_headingToleranceDeg,
				_maxSpeedKmh,
				_stableWindowSeconds)
		{
		}

		public GoalPoseCriteria(
			float _longitudinalTolerance,
			float _lateralTolerance,
			float _headingToleranceDeg,
			float _maxSpeedKmh,
			float _stableWindowSeconds)
		{
			LongitudinalTolerance = Mathf.Max(0.05f, _longitudinalTolerance);
			LateralTolerance = Mathf.Max(0.05f, _lateralTolerance);
			PositionTolerance = ArrivalPositionBand.EquivalentRadius(LongitudinalTolerance, LateralTolerance);
			HeadingToleranceDeg = _headingToleranceDeg;
			MaxSpeedKmh = _maxSpeedKmh;
			StableWindowSeconds = _stableWindowSeconds;
		}

		public static GoalPoseCriteria Default => new GoalPoseCriteria(
			ArrivalPositionBand.DefaultLongitudinal,
			ArrivalPositionBand.DefaultLateral,
			5f, 1f, 0.4f);
	}

	public sealed class GoalPoseValidator
	{
		private float m_StableTimer;

		public bool Evaluate(
			Vector3 _position,
			float _yaw,
			float _speedKmh,
			Vector3 _goalPosition,
			float _goalYaw,
			GoalPoseCriteria _criteria,
			float _dt,
			out float _positionError,
			out float _headingError)
		{
			return Evaluate(
				_position, _yaw, _speedKmh, _goalPosition, _goalYaw, true,
				_criteria, _dt, out _positionError, out _headingError);
		}

		public bool Evaluate(
			Vector3 _position,
			float _yaw,
			float _speedKmh,
			Vector3 _goalPosition,
			float _goalYaw,
			bool _requireHeading,
			GoalPoseCriteria _criteria,
			float _dt,
			out float _positionError,
			out float _headingError)
		{
			_positionError = FlatDistance(_position, _goalPosition);
			_headingError = _requireHeading
				? Mathf.Abs(Mathf.DeltaAngle(_yaw, _goalYaw))
				: 0f;

			bool positionOk = ArrivalPositionBand.IsInside(
				_position,
				_yaw,
				_goalPosition,
				_criteria.LongitudinalTolerance,
				_criteria.LateralTolerance);
			bool headingOk = !_requireHeading || _headingError <= _criteria.HeadingToleranceDeg;
			bool speedOk = Mathf.Abs(_speedKmh) <= _criteria.MaxSpeedKmh;

			if (positionOk && headingOk && speedOk)
				m_StableTimer += _dt;
			else
				m_StableTimer = 0f;

			return m_StableTimer >= _criteria.StableWindowSeconds;
		}

		public void Reset() => m_StableTimer = 0f;

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}

using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Checks whether the vehicle has reached its destination
	/// (position + optional heading).
	/// </summary>
	public sealed class ArrivalController
	{
		private readonly float m_PositionTolerance;
		private readonly float m_HeadingTolerance;

		public ArrivalController(
			float _positionTolerance = 0.6f,
			float _headingTolerance = 8f)
		{
			m_PositionTolerance = _positionTolerance;
			m_HeadingTolerance = _headingTolerance;
		}

		public bool HasArrived(
			Vector3 _position,
			float _yaw,
			Vector3 _destination,
			float? _targetHeading)
		{
			if (FlatDistance(_position, _destination) > m_PositionTolerance)
				return false;

			if (!_targetHeading.HasValue)
				return true;

			return Mathf.Abs(Mathf.DeltaAngle(_yaw, _targetHeading.Value)) <= m_HeadingTolerance;
		}

		public bool HasArrived(
			FeedbackState _state,
			Vector3 _destination,
			ArrivalCriteria _criteria)
		{
			if (FlatDistance(_state.Position, _destination) > _criteria.PositionTolerance)
				return false;

			if (!_criteria.RequireFaceHeading)
				return true;

			float? targetYaw = _criteria.HasTargetForward
				? Quaternion.LookRotation(_criteria.TargetForward, Vector3.up).eulerAngles.y
				: (float?)null;

			if (!targetYaw.HasValue)
				return true;

			return Mathf.Abs(Mathf.DeltaAngle(_state.Yaw, targetYaw.Value)) <= _criteria.HeadingToleranceDeg;
		}

		public bool HasCorrectHeading(FeedbackState _state, float _targetYaw, float _toleranceDeg)
		{
			return Mathf.Abs(Mathf.DeltaAngle(_state.Yaw, _targetYaw)) <= _toleranceDeg;
		}

		public bool IsFacingDestination(FeedbackState _state, Vector3 _destinationForward)
		{
			if (_destinationForward.sqrMagnitude < 0.0001f)
				return true;
			float yaw = Quaternion.LookRotation(_destinationForward, Vector3.up).eulerAngles.y;
			return Mathf.Abs(Mathf.DeltaAngle(_state.Yaw, yaw)) <= m_HeadingTolerance;
		}

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}

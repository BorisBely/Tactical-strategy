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

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}

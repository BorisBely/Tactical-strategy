using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Decides *how* to reach the goal: forward, reverse, turn-around, etc.
	/// Does not generate waypoints — that is ManeuverPlanner's job.
	/// </summary>
	public sealed class DrivingPlanner
	{
		public const float c_DefaultTurnRadius = 6.5f;

		// Front hemisphere: anything up to 90° from nose → follow the path forward.
		private const float c_FrontSectorAngle = 90f;
		// Rear sector: > 135° from nose.
		private const float c_RearSectorAngle = 135f;

		private readonly DecisionEvaluator m_DecisionEvaluator;

		public DrivingPlanner(DecisionEvaluator _decisionEvaluator)
		{
			m_DecisionEvaluator = _decisionEvaluator;
		}

		public DrivingPlan BuildPlan(
			NavigationRequest _request,
			PathResult _path,
			FeedbackState _feedback,
			float _reverseMaxSegment,
			float _reverseAngleDegrees,
			float _turnRadius = c_DefaultTurnRadius,
			DriverContext _ctx = null)
		{
			Vector3 forward = FlatDir(_feedback.Forward);

			Vector3 firstTarget = _request.Destination;
			if (_path.Corners != null && _path.Corners.Length > 1)
				firstTarget = _path.Corners[1];

			Vector3 toFirst = firstTarget - _feedback.Position;
			toFirst.y = 0f;
			float firstSegLen = toFirst.magnitude;
			float firstAngle = firstSegLen > 0.01f
				? Mathf.Abs(Vector3.SignedAngle(forward, toFirst / firstSegLen, Vector3.up))
				: 0f;

			float flatToDest = FlatDistance(_feedback.Position, _request.Destination);
			float reverseRange = Mathf.Max(_reverseMaxSegment * 1.25f, 3f);

			VehicleDrivingMode proposedMode = DecideBaseMode(
				firstAngle,
				firstSegLen,
				flatToDest,
				_reverseMaxSegment,
				reverseRange,
				_reverseAngleDegrees);

			VehicleDrivingMode safeMode = m_DecisionEvaluator != null
				? m_DecisionEvaluator.ChooseSafeMode(
					proposedMode,
					flatToDest,
					_turnRadius,
					_feedback.Geometry,
					_feedback.Memory)
				: proposedMode;

		List<Maneuver> maneuvers = new List<Maneuver>(4);
		BuildManeuverSequence(safeMode, _feedback, maneuvers, _turnRadius, _ctx, _request);

			if (_request.HasHeading)
			{
				float currentYaw = Quaternion.LookRotation(forward, Vector3.up).eulerAngles.y;
				float delta = Mathf.Abs(Mathf.DeltaAngle(currentYaw, _request.HeadingYaw.Value));
				if (delta > 18f)
					maneuvers.Add(new ParkingManeuver(_request.HeadingYaw.Value));
			}

			if (maneuvers.Count == 0)
				maneuvers.Add(new ForwardManeuver());

			string reason =
				$"proposed={proposedMode} safe={safeMode} firstAngle={firstAngle:F0} dist={flatToDest:F1}";
			return new DrivingPlan(maneuvers, reason);
		}

		private static VehicleDrivingMode DecideBaseMode(
			float _firstAngle,
			float _firstSegLen,
			float _flatToDest,
			float _reverseMaxSegment,
			float _reverseRange,
			float _reverseAngleDegrees)
		{
			if (_firstAngle <= c_FrontSectorAngle)
				return VehicleDrivingMode.Forward;

			if (_firstAngle >= Mathf.Min(c_RearSectorAngle, _reverseAngleDegrees * 0.85f) &&
			    _firstSegLen <= _reverseMaxSegment &&
			    _flatToDest <= _reverseRange)
			{
				return VehicleDrivingMode.Reverse;
			}

			if (_firstAngle > c_FrontSectorAngle && _flatToDest > _reverseMaxSegment * 0.5f)
				return VehicleDrivingMode.TurnAround;

			return VehicleDrivingMode.Forward;
		}

		private static void BuildManeuverSequence(
			VehicleDrivingMode _mode,
			FeedbackState _feedback,
			List<Maneuver> _maneuvers,
			float _turnRadius,
			DriverContext _ctx = null,
			NavigationRequest _request = default)
		{
			switch (_mode)
			{
				case VehicleDrivingMode.Reverse:
					if (_ctx != null)
					{
						var reversePath = ReversePathBuilder.Build(_ctx.Path, _ctx);
						_maneuvers.Add(new ReverseIntentManeuver(reversePath));
					}
					else
					{
						_maneuvers.Add(new ReverseManeuver());
					}
					break;

				case VehicleDrivingMode.TurnAround:
					float sign = ChooseTurnSign(_feedback.Geometry);
					_maneuvers.Add(new TurnAroundManeuver(sign));
					_maneuvers.Add(new ForwardManeuver());
					break;

				default:
					_maneuvers.Add(new ForwardManeuver());
					break;
			}
		}

		private static float ChooseTurnSign(VehicleLocalGeometry.Sample _geometry)
		{
			float sign = _geometry.PreferredTurnSign;
			if (Mathf.Abs(sign) < 0.1f)
				sign = _geometry.LeftClearance >= _geometry.RightClearance ? -1f : 1f;

			bool leftOk = _geometry.LeftClearance >= 3f;
			bool rightOk = _geometry.RightClearance >= 3f;
			if (sign < 0f && !leftOk && rightOk)
				return 1f;
			if (sign > 0f && !rightOk && leftOk)
				return -1f;

			return sign;
		}

		private static Vector3 FlatDir(Vector3 _v)
		{
			_v.y = 0f;
			if (_v.sqrMagnitude < 0.0001f)
				return Vector3.forward;
			return _v.normalized;
		}

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}

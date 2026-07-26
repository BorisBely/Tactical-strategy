using UnityEngine;

namespace VehicleNavigation
{
	public sealed class ManeuverFeasibilityChecker
	{
		private readonly TrajectoryPrediction m_Prediction;

		private const float c_MinFrontClearance = 1.8f;
		private const float c_MinRearClearance = 1.8f;
		private const float c_MinSideClearance = 1.0f;
		private const float c_MinCorridorWidth = 2.5f;

		public ManeuverFeasibilityChecker(TrajectoryPrediction _prediction)
		{
			m_Prediction = _prediction;
		}

		public FeasibilityResult CheckPlan(
			DrivingPlan _plan,
			NavigationContext _ctx,
			VehicleParameters _params)
		{
			VehicleLocalGeometry.Sample geo = _ctx?.State.Geometry ?? default;
			return CheckPlanInternal(_plan, geo, _params);
		}

		public FeasibilityResult CheckPlan(
			DrivingPlan _plan,
			VehicleLocalGeometry.Sample _geometry,
			float _turnRadius)
		{
			if (_plan.Maneuvers == null || _plan.Maneuvers.Count == 0)
				return FeasibilityResult.Invalid("empty plan");

			FeasibilityResult worst = FeasibilityResult.Valid;

			for (int i = 0; i < _plan.Maneuvers.Count; i++)
			{
				Maneuver m = _plan.Maneuvers[i];
				if (m is ReverseIntentManeuver)
					continue;

				FeasibilityResult result = CheckManeuverInternal(m, _geometry, _turnRadius);
				if (!result.IsValid)
					return result;
				if (result.RiskScore > worst.RiskScore)
					worst = result;
			}

			return worst;
		}

		private FeasibilityResult CheckPlanInternal(
			DrivingPlan _plan,
			VehicleLocalGeometry.Sample _geometry,
			VehicleParameters _params)
		{
			if (_plan.Maneuvers == null || _plan.Maneuvers.Count == 0)
				return FeasibilityResult.Invalid("empty plan");

			FeasibilityResult worst = FeasibilityResult.Valid;

			for (int i = 0; i < _plan.Maneuvers.Count; i++)
			{
				Maneuver m = _plan.Maneuvers[i];
				if (m is ReverseIntentManeuver)
					continue;

				FeasibilityResult result = CheckManeuverInternal(m, _geometry, _params);
				if (!result.IsValid)
					return result;
				if (result.RiskScore > worst.RiskScore)
					worst = result;
			}

			return worst;
		}

		public FeasibilityResult CheckManeuver(
			Maneuver _maneuver,
			NavigationContext _ctx,
			VehicleParameters _params)
		{
			var geo = _ctx?.State.Geometry ?? default;
			return CheckManeuverInternal(_maneuver, geo, _params);
		}

		private FeasibilityResult CheckManeuverInternal(
			Maneuver _maneuver,
			VehicleLocalGeometry.Sample _geo,
			VehicleParameters _params)
		{
			return CheckManeuverInternal(_maneuver, _geo, _params.MinTurningRadius);
		}

		private FeasibilityResult CheckManeuverInternal(
			Maneuver _maneuver,
			VehicleLocalGeometry.Sample _geo,
			float _turnRadius)
		{
			if (_maneuver == null)
				return FeasibilityResult.Invalid("null maneuver");

			switch (_maneuver.Type)
			{
				case VehicleManeuverType.Forward:
					return CheckForwardPath(_geo);

				case VehicleManeuverType.Reverse:
					return CheckReversePath(_geo);

				case VehicleManeuverType.TurnAround:
				{
					var turn = _maneuver as TurnAroundManeuver;
					float sign = turn != null ? turn.TurnSign : 1f;
					return CheckTurnAroundArc(sign, _turnRadius, _geo);
				}

				case VehicleManeuverType.Parking:
				case VehicleManeuverType.ApproachWithHeading:
					return CheckForwardPath(_geo);

				case VehicleManeuverType.Unstuck:
					return FeasibilityResult.Valid;

				case VehicleManeuverType.Stop:
					return FeasibilityResult.Valid;

				default:
					return FeasibilityResult.Valid;
			}
		}

		public FeasibilityResult CheckForwardPath(VehicleLocalGeometry.Sample _geometry)
		{
			if (_geometry.HasDropAhead)
				return FeasibilityResult.Invalid("drop ahead");

			float safeSpeed = ComputeRecommendedSpeed(_geometry);

			if (_geometry.FrontClearance < c_MinFrontClearance)
			{
				var result = FeasibilityResult.Invalid(
					$"front clearance {_geometry.FrontClearance:F1}m < {c_MinFrontClearance}m");
				result.HasFrontCollision = true;
				return result;
			}

			if (_geometry.HasNarrowPassage && _geometry.FrontClearance < c_MinFrontClearance * 1.5f)
			{
				var result = new FeasibilityResult
				{
					IsValid = true,
					IsFullySafe = false,
					MinClearance = _geometry.FrontClearance,
					RiskScore = 0.3f,
					HasNarrowPassage = true,
					RecommendedMaxSpeedKmh = Mathf.Min(safeSpeed, 8f)
				};
				return result;
			}

			var safe = FeasibilityResult.Valid;
			safe.RecommendedMaxSpeedKmh = safeSpeed;
			return safe;
		}

		private static float ComputeRecommendedSpeed(VehicleLocalGeometry.Sample _geo)
		{
			float byClearance = _geo.FrontClearance * 6f;
			float byDiag = Mathf.Min(_geo.FrontDiagonalLeftClearance, _geo.FrontDiagonalRightClearance) * 5f;
			float speed = Mathf.Min(byClearance, byDiag);
			if (_geo.HasNarrowPassage) speed *= 0.6f;
			if (_geo.HasDropAhead) speed *= 0.5f;
			return Mathf.Clamp(speed, 2f, 50f);
		}

		public FeasibilityResult CheckReversePath(VehicleLocalGeometry.Sample _geometry)
		{
			if (_geometry.HasDropBehind)
				return FeasibilityResult.Invalid("drop behind");

			if (_geometry.RearClearance < c_MinRearClearance)
			{
				var result = FeasibilityResult.Invalid(
					$"rear clearance {_geometry.RearClearance:F1}m < {c_MinRearClearance}m");
				result.HasRearCollision = true;
				return result;
			}

			if (_geometry.LeftClearance < c_MinCorridorWidth * 0.3f
			    && _geometry.RightClearance < c_MinCorridorWidth * 0.3f)
			{
				var result = FeasibilityResult.Invalid("corridor too narrow for reverse");
				result.HasSideCollision = true;
				return result;
			}

			return FeasibilityResult.Valid;
		}

		public FeasibilityResult CheckTurnAroundArc(
			float _turnSign,
			float _turnRadius,
			VehicleLocalGeometry.Sample _geometry)
		{
			float neededFront = _turnRadius * 0.8f;
			float neededBack = _turnRadius * 0.5f;

			if (_geometry.FrontClearance < neededFront)
			{
				var result = FeasibilityResult.Invalid(
					$"front clearance {_geometry.FrontClearance:F1}m < {neededFront:F1}m for turn");
				result.HasFrontCollision = true;
				return result;
			}

			if (_geometry.RearClearance < neededBack)
			{
				var result = FeasibilityResult.Invalid(
					$"rear clearance {_geometry.RearClearance:F1}m < {neededBack:F1}m for turn");
				result.HasRearCollision = true;
				return result;
			}

			bool preferLeft = _turnSign < 0f || _geometry.PreferredTurnSign < 0f;
			bool preferRight = _turnSign > 0f || _geometry.PreferredTurnSign > 0f;

			if (preferLeft)
			{
				if (_geometry.LeftClearance < neededFront * 0.7f &&
				    _geometry.FrontDiagonalLeftClearance < neededFront * 0.6f)
				{
					return FeasibilityResult.Invalid(
						$"left side {_geometry.LeftClearance:F1}m too tight for left turn");
				}
			}
			else if (preferRight)
			{
				if (_geometry.RightClearance < neededFront * 0.7f &&
				    _geometry.FrontDiagonalRightClearance < neededFront * 0.6f)
				{
					return FeasibilityResult.Invalid(
						$"right side {_geometry.RightClearance:F1}m too tight for right turn");
				}
			}

			if (!VehicleLocalGeometry.CanFitTurnRadius(_turnRadius, _geometry))
			{
				return FeasibilityResult.Invalid(
					$"cannot fit turn radius {_turnRadius:F1}m in {_geometry.LeftClearance:F1}m/{_geometry.RightClearance:F1}m");
			}

			return FeasibilityResult.Valid;
		}

		public FeasibilityResult CheckParkingSpot(
			Vector3 _destination,
			float _targetYaw,
			VehicleLocalGeometry.Sample _geometry)
		{
			if (_geometry.HasDropAhead || _geometry.HasDropBehind)
				return FeasibilityResult.Invalid("drop near parking spot");

			if (_geometry.FrontClearance < c_MinFrontClearance * 0.7f
			    && _geometry.RearClearance < c_MinRearClearance * 0.7f)
			{
				return FeasibilityResult.Invalid("parking spot too tight");
			}

			return FeasibilityResult.Valid;
		}
	}
}

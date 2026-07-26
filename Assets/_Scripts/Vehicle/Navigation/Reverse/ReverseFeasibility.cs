using UnityEngine;

namespace VehicleNavigation
{
	public readonly struct ReverseFeasibilityResult
	{
		public readonly bool Feasible;
		public readonly string Reason;

		public ReverseFeasibilityResult(bool _feasible, string _reason)
		{
			Feasible = _feasible;
			Reason = _reason;
		}

		public static ReverseFeasibilityResult Ok => new ReverseFeasibilityResult(true, "ok");
		public static ReverseFeasibilityResult No(string _reason) => new ReverseFeasibilityResult(false, _reason);
	}

	/// <summary>
	/// Checks if there is enough physical space behind the vehicle for reverse driving.
	/// Uses CapsuleCast / OverlapBox along the intended trajectory.
	/// Called BEFORE building ReversePath.
	/// </summary>
	public static class ReverseFeasibility
	{
		private const float c_MinRearClearance = 1.8f;
		private const float c_MinCorridorWidth = 3f;

		public static ReverseFeasibilityResult Check(DriverContext _ctx)
		{
			if (_ctx.Geometry.RearClearance < c_MinRearClearance)
				return ReverseFeasibilityResult.No($"rear clearance {_ctx.Geometry.RearClearance:F1}m < {c_MinRearClearance}m");

			if (_ctx.Geometry.LeftClearance < c_MinCorridorWidth * 0.3f
			    && _ctx.Geometry.RightClearance < c_MinCorridorWidth * 0.3f)
				return ReverseFeasibilityResult.No("corridor too narrow");

			if (_ctx.IsAirborne)
				return ReverseFeasibilityResult.No("airborne");

			return ReverseFeasibilityResult.Ok;
		}
	}
}

using UnityEngine;

namespace VehicleNavigation
{
	public enum FeasibilitySeverity
	{
		Valid,
		Risky,
		Unsafe,
		Impossible
	}

	public sealed class FeasibilityResult
	{
		public bool IsValid { get; set; }
		public bool IsFullySafe { get; set; }
		public FeasibilitySeverity Severity { get; set; }
		public float MinClearance { get; set; }
		public float RiskScore { get; set; }

		public bool HasFrontCollision { get; set; }
		public bool HasRearCollision { get; set; }
		public bool HasSideCollision { get; set; }
		public bool HasCliffRisk { get; set; }
		public bool HasSlopeRisk { get; set; }
		public bool HasNarrowPassage { get; set; }
		public float RecommendedMaxSpeedKmh { get; set; } = float.MaxValue;

		public string FailureReason { get; set; }
		public Vector3 FailurePoint { get; set; }

		public static FeasibilityResult Valid => new FeasibilityResult
		{
			IsValid = true,
			IsFullySafe = true,
			Severity = FeasibilitySeverity.Valid,
			MinClearance = float.MaxValue
		};

		public static FeasibilityResult Impossible(string _reason)
		{
			return new FeasibilityResult
			{
				IsValid = false,
				Severity = FeasibilitySeverity.Impossible,
				FailureReason = _reason
			};
		}

		public static FeasibilityResult Unsafe(string _reason)
		{
			return new FeasibilityResult
			{
				IsValid = false,
				Severity = FeasibilitySeverity.Unsafe,
				FailureReason = _reason
			};
		}

		public static FeasibilityResult Risky(float _risk, string _reason)
		{
			return new FeasibilityResult
			{
				IsValid = true,
				IsFullySafe = false,
				Severity = FeasibilitySeverity.Risky,
				RiskScore = _risk,
				FailureReason = _reason
			};
		}

		public static FeasibilityResult Invalid(string _reason)
		{
			return new FeasibilityResult
			{
				IsValid = false,
				Severity = FeasibilitySeverity.Unsafe,
				FailureReason = _reason
			};
		}

		public static FeasibilityResult Invalid(string _reason, Vector3 _point)
		{
			return new FeasibilityResult
			{
				IsValid = false,
				Severity = FeasibilitySeverity.Unsafe,
				FailureReason = _reason,
				FailurePoint = _point
			};
		}
	}
}

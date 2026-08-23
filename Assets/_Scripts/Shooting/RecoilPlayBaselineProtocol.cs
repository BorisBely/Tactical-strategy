using UnityEngine;

/// <summary>
/// Frozen Play conditions for phase A (M4 ModA1). Does not retune Vertical/Horizontal/Recovery.
/// </summary>
public static class RecoilPlayBaselineProtocol
{
	#region Constants
	public const string ReferenceWeaponAssetName = "Weapon_M4_ModA_1";
	public const string M249WeaponAssetName = "Weapon_M249";
	public const string PkmWeaponAssetName = "Weapon_PKM";
	public const string LogFolder = "Assets/_Docs/Logs/Tests";
	public const string LogFileName = "RecoilPlayBaseline_LAST.txt";

	public const float NeutralRecoilControl = 50f;
	public const float Distance50M = 50f;
	public const float Distance15M = 15f;
	public const float PauseA5Seconds = 0.4f;
	public const int RepeatCount = 3;
	public const int A5BurstShots = 3;
	public const int ComparisonShotCount = 5;

	public const float StandingKickMultiplier = 1f;
	public const float StandingRecoveryMultiplier = 1f;
	public const float WalkKickMultiplier = 1.25f;
	public const float WalkRecoveryMultiplier = 0.85f;
	public const float CrouchKickMultiplier = 0.95f;
	public const float CrouchRecoveryMultiplier = 1.1f;

	public const float Ring10Cm = 0.10f;
	public const float Ring25Cm = 0.25f;
	public const float Ring50Cm = 0.50f;
	public const float Ring100Cm = 1.00f;

	public const float BurstGapSeconds = 0.35f;
	public const float Shot1OffsetWarnDegrees = 0.05f;
	public const float MathVsPlayWarnRatio = 0.35f;
	public const float MathVsPlayFailRatio = 0.75f;
	public const float HitscanBaseSpreadToDegrees = 0.35f;
	public const float HitscanMinHalfAngleDegrees = 0.04f;
	public const float HitscanMaxHalfAngleDegrees = 12f;
	public const float HitscanStandingSpreadMultiplier = 1f;
	public const float HitscanCrouchSpreadMultiplier = 0.9f;
	public const float HitscanMovingSpreadMultiplier = 1.35f;
	public const float BarrelGateIdleDegrees = 3f;
	public const string SimPlayLabel = "SIM_PLAY";
	#endregion

	#region Nested Types
	public enum CaseId
	{
		None = 0,
		A1AimingStand50 = 1,
		A2AimingWalk50 = 2,
		A3HipFireStand15 = 3,
		A4AimingCrouch50 = 4,
		A5Pause04Stand50 = 5,
		N8BarrelGate = 6
	}

	public enum Verdict
	{
		Pass,
		Warn,
		Fail,
		PlayPending,
		Report
	}
	#endregion

	#region Public Methods
	public static string CaseLabel(CaseId _case)
	{
		switch (_case)
		{
			case CaseId.A1AimingStand50:
				return "A1 Aiming Stand 50m";
			case CaseId.A2AimingWalk50:
				return "A2 Aiming Walk 50m";
			case CaseId.A3HipFireStand15:
				return "A3 HipFire Stand 15m";
			case CaseId.A4AimingCrouch50:
				return "A4 Aiming Crouch 50m";
			case CaseId.A5Pause04Stand50:
				return "A5 pause 0.4s shot 4";
			case CaseId.N8BarrelGate:
				return "N8 barrel gate";
			default:
				return _case.ToString();
		}
	}

	public static float CaseDistanceMeters(CaseId _case)
	{
		return _case == CaseId.A3HipFireStand15 ? Distance15M : Distance50M;
	}

	public static WeaponRecoilContext CreateContext(
		WeaponDefinition _weapon,
		WeaponFireMode _fireMode,
		WeaponPoseState _pose,
		float _stanceKick,
		float _stanceRecovery)
	{
		WeaponRecoilContext context = WeaponRecoilContext.CreateBaseline(_weapon, _fireMode);
		context.PoseKickMultiplier = WeaponPoseCombatModifiers.GetKickMultiplier(_pose);
		context.PoseRecoveryMultiplier = WeaponPoseCombatModifiers.GetRecoveryMultiplier(_pose);
		context.StanceKickMultiplier = _stanceKick;
		context.StanceRecoveryMultiplier = _stanceRecovery;
		return context;
	}

	public static float DegreesToCm(float _offsetDegrees, float _distanceMeters)
	{
		return WeaponRecoilMath.OffsetToDisplacementMeters(_offsetDegrees, _distanceMeters) * 100f;
	}

	public static float Median3(float _a, float _b, float _c)
	{
		if (_a > _b)
		{
			float t = _a;
			_a = _b;
			_b = t;
		}

		if (_b > _c)
		{
			float t = _b;
			_b = _c;
			_c = t;
		}

		if (_a > _b)
		{
			float t = _a;
			_a = _b;
			_b = t;
		}

		return _b;
	}
	#endregion
}

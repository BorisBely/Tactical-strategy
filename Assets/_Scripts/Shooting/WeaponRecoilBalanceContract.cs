using System;
using UnityEngine;

/// <summary>
/// Stage-1 recoil balance contract: which weapon fields we tune and which derived metrics we evaluate.
/// Baseline evaluation uses weapon-only multipliers (no attachments, skills, posture, or ammo).
/// Excel and asset numbers are not changed here — only the evaluation structure is fixed.
/// </summary>
public static class WeaponRecoilBalanceContract
{
	#region Constants
	public const string ReferenceWeaponAssetName = "Weapon_M4_ModA_1";

	/// <summary>Representative burst lengths for metric B (|Offset| after N shots, inter-shot recovery).</summary>
	public static readonly int[] AccumulatedShotCounts = { 3, 5, 8, 10 };

	/// <summary>Pause lengths for metric C after <see cref="PauseRecoveryAfterShotCount"/> shots.</summary>
	public static readonly float[] PauseRecoverySeconds = { 0.2f, 0.4f, 0.8f };

	/// <summary>Burst length before pause-recovery samples.</summary>
	public const int PauseRecoveryAfterShotCount = 5;

	/// <summary>Standard distance for offset → displacement conversion in balance tables.</summary>
	public const float EvaluationDistanceMeters = 100f;
	#endregion

	#region Nested Types
	[Serializable]
	public struct Metrics
	{
		public float VerticalRecoilDegrees;
		public float HorizontalRecoilDegrees;
		public float RecoilRecoveryPerSecond;

		public float OffsetMagnitudeAfter3Shots;
		public float OffsetMagnitudeAfter5Shots;
		public float OffsetMagnitudeAfter8Shots;
		public float OffsetMagnitudeAfter10Shots;

		public float OffsetMagnitudeAfterPause020;
		public float OffsetMagnitudeAfterPause040;
		public float OffsetMagnitudeAfterPause080;

		public float DisplacementMetersAfter5ShotsAt100m;
		public float DisplacementMetersAfterPause040At100m;
	}
	#endregion

	#region Public Methods
	public static Metrics EvaluateBaseline(WeaponDefinition _weaponDefinition, WeaponFireMode _fireMode)
	{
		if (_weaponDefinition == null)
			return default;

		float offsetAfter3 = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(
			_weaponDefinition, null, _fireMode, 3);
		float offsetAfter5 = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(
			_weaponDefinition, null, _fireMode, 5);
		float offsetAfter8 = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(
			_weaponDefinition, null, _fireMode, 8);
		float offsetAfter10 = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(
			_weaponDefinition, null, _fireMode, 10);

		float pause020 = WeaponRecoilMath.PredictOffsetMagnitudeAfterBurstAndPause(
			_weaponDefinition, null, _fireMode, PauseRecoveryAfterShotCount, PauseRecoverySeconds[0]);
		float pause040 = WeaponRecoilMath.PredictOffsetMagnitudeAfterBurstAndPause(
			_weaponDefinition, null, _fireMode, PauseRecoveryAfterShotCount, PauseRecoverySeconds[1]);
		float pause080 = WeaponRecoilMath.PredictOffsetMagnitudeAfterBurstAndPause(
			_weaponDefinition, null, _fireMode, PauseRecoveryAfterShotCount, PauseRecoverySeconds[2]);

		return new Metrics
		{
			VerticalRecoilDegrees = _weaponDefinition.VerticalRecoil,
			HorizontalRecoilDegrees = _weaponDefinition.HorizontalRecoil,
			RecoilRecoveryPerSecond = _weaponDefinition.RecoilRecoveryPerSecond,
			OffsetMagnitudeAfter3Shots = offsetAfter3,
			OffsetMagnitudeAfter5Shots = offsetAfter5,
			OffsetMagnitudeAfter8Shots = offsetAfter8,
			OffsetMagnitudeAfter10Shots = offsetAfter10,
			OffsetMagnitudeAfterPause020 = pause020,
			OffsetMagnitudeAfterPause040 = pause040,
			OffsetMagnitudeAfterPause080 = pause080,
			DisplacementMetersAfter5ShotsAt100m =
				WeaponRecoilMath.OffsetToDisplacementMeters(offsetAfter5, EvaluationDistanceMeters),
			DisplacementMetersAfterPause040At100m =
				WeaponRecoilMath.OffsetToDisplacementMeters(pause040, EvaluationDistanceMeters),
		};
	}

	public static WeaponFireMode ResolveBaselineFireMode(WeaponDefinition _weaponDefinition)
	{
		if (_weaponDefinition == null)
			return WeaponFireMode.SemiAuto;

		WeaponFireMode[] modes = _weaponDefinition.AvailableFireModes;
		if (modes == null || modes.Length == 0)
			return WeaponFireMode.SemiAuto;

		for (int i = 0; i < modes.Length; i++)
		{
			if (modes[i] == WeaponFireMode.FullAuto || modes[i] == WeaponFireMode.Auto)
				return modes[i];
		}

		for (int i = 0; i < modes.Length; i++)
		{
			if (modes[i] == WeaponFireMode.Burst)
				return modes[i];
		}

		return WeaponFireMode.SemiAuto;
	}

	public static float OffsetToDisplacementMeters(float _offsetMagnitudeDegrees, float _distanceMeters)
	{
		return WeaponRecoilMath.OffsetToDisplacementMeters(_offsetMagnitudeDegrees, _distanceMeters);
	}
	#endregion
}

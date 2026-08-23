using UnityEngine;

/// <summary>
/// Phase A MATH column: PredictOffsetAfterShots, mean-of-hits, pause A. Does not retune assets.
/// </summary>
public static class RecoilPlayBaselineMath
{
	#region Nested Types
	public readonly struct ShotSample
	{
		public readonly int ShotCount;
		public readonly Vector2 OffsetAfterShotsDeg;
		public readonly Vector2 MeanHitOffsetDeg;
		public readonly float DistanceMeters;
		public readonly float OffsetAfterCm;
		public readonly float MeanHitCm;
		public readonly float SpreadDiameterCm;

		public ShotSample(
			int _shotCount,
			Vector2 _offsetAfterShotsDeg,
			Vector2 _meanHitOffsetDeg,
			float _distanceMeters,
			float _spreadHalfAngleDegrees)
		{
			ShotCount = _shotCount;
			OffsetAfterShotsDeg = _offsetAfterShotsDeg;
			MeanHitOffsetDeg = _meanHitOffsetDeg;
			DistanceMeters = _distanceMeters;
			OffsetAfterCm = RecoilPlayBaselineProtocol.DegreesToCm(
				_offsetAfterShotsDeg.magnitude, _distanceMeters);
			MeanHitCm = RecoilPlayBaselineProtocol.DegreesToCm(
				_meanHitOffsetDeg.magnitude, _distanceMeters);
			SpreadDiameterCm =
				WeaponRecoilMath.SpreadDiameterMeters(_distanceMeters, _spreadHalfAngleDegrees) * 100f;
		}
	}

	public struct CaseMath
	{
		public RecoilPlayBaselineProtocol.CaseId Case;
		public string WeaponName;
		public WeaponFireMode FireMode;
		public WeaponPoseState Pose;
		public float StanceKick;
		public float StanceRecovery;
		public float DistanceMeters;
		public ShotSample After1;
		public ShotSample After3;
		public ShotSample After5;
		public ShotSample After8;
		public Vector2 OffsetAfter3Pause04Deg;
		public float OffsetAfter3Pause04Cm;
	}
	#endregion

	#region Public Methods
	public static CaseMath EvaluateCase(WeaponDefinition _weapon, RecoilPlayBaselineProtocol.CaseId _case)
	{
		WeaponFireMode fireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(_weapon);
		WeaponPoseState pose = _case == RecoilPlayBaselineProtocol.CaseId.A3HipFireStand15
			? WeaponPoseState.HipFire
			: WeaponPoseState.Aiming;
		ResolveStance(_case, out float stanceKick, out float stanceRecovery);
		float distance = RecoilPlayBaselineProtocol.CaseDistanceMeters(_case);
		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_weapon, fireMode, pose, stanceKick, stanceRecovery);
		float spreadHalf = EstimateSpreadHalfAngleDegrees(_weapon, pose);
		Vector2 pauseOffset = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(
			in context,
			RecoilPlayBaselineProtocol.A5BurstShots,
			RecoilPlayBaselineProtocol.PauseA5Seconds);

		return new CaseMath
		{
			Case = _case,
			WeaponName = _weapon != null ? _weapon.name : "?",
			FireMode = fireMode,
			Pose = pose,
			StanceKick = stanceKick,
			StanceRecovery = stanceRecovery,
			DistanceMeters = distance,
			After1 = Sample(in context, 1, distance, spreadHalf),
			After3 = Sample(in context, 3, distance, spreadHalf),
			After5 = Sample(in context, 5, distance, spreadHalf),
			After8 = Sample(in context, 8, distance, spreadHalf),
			OffsetAfter3Pause04Deg = pauseOffset,
			OffsetAfter3Pause04Cm = RecoilPlayBaselineProtocol.DegreesToCm(pauseOffset.magnitude, distance)
		};
	}

	public static Vector2 PredictMeanHitOffsetDegrees(in WeaponRecoilContext _context, int _shotCount)
	{
		if (_shotCount <= 0)
			return Vector2.zero;

		Vector2 sum = Vector2.zero;
		for (int i = 1; i <= _shotCount; i++)
			sum += WeaponRecoilMath.PredictOffsetBeforeShot(in _context, i);
		return sum / _shotCount;
	}

	public static RecoilPlayBaselineProtocol.Verdict EvaluateA1Form(in CaseMath _a1)
	{
		bool shot1NearZero =
			_a1.After1.MeanHitOffsetDeg.magnitude <= RecoilPlayBaselineProtocol.Shot1OffsetWarnDegrees;
		bool rises3 = _a1.After3.OffsetAfterCm > _a1.After1.OffsetAfterCm + 0.5f;
		bool rises8 = _a1.After8.OffsetAfterCm > _a1.After3.OffsetAfterCm + 1f;
		if (shot1NearZero && rises3 && rises8)
			return RecoilPlayBaselineProtocol.Verdict.Pass;
		if (rises3 && rises8)
			return RecoilPlayBaselineProtocol.Verdict.Warn;
		return RecoilPlayBaselineProtocol.Verdict.Fail;
	}

	public static RecoilPlayBaselineProtocol.Verdict EvaluateA2Form(in CaseMath _a1, in CaseMath _a2)
	{
		if (_a2.After5.OffsetAfterCm > _a1.After5.OffsetAfterCm + 0.5f)
			return RecoilPlayBaselineProtocol.Verdict.Pass;
		if (_a2.After5.OffsetAfterCm >= _a1.After5.OffsetAfterCm)
			return RecoilPlayBaselineProtocol.Verdict.Warn;
		return RecoilPlayBaselineProtocol.Verdict.Fail;
	}

	public static RecoilPlayBaselineProtocol.Verdict EvaluateA3Form(in CaseMath _a1, in CaseMath _a3)
	{
		float aimingDeg = _a1.After5.OffsetAfterShotsDeg.magnitude;
		float hipDeg = _a3.After5.OffsetAfterShotsDeg.magnitude;
		if (hipDeg > aimingDeg + 0.01f && _a3.After5.SpreadDiameterCm > _a1.After5.SpreadDiameterCm)
			return RecoilPlayBaselineProtocol.Verdict.Pass;
		if (hipDeg > aimingDeg)
			return RecoilPlayBaselineProtocol.Verdict.Warn;
		return RecoilPlayBaselineProtocol.Verdict.Fail;
	}

	public static RecoilPlayBaselineProtocol.Verdict EvaluateA4Form(in CaseMath _a1, in CaseMath _a4)
	{
		if (_a4.After5.OffsetAfterCm < _a1.After5.OffsetAfterCm - 0.2f)
			return RecoilPlayBaselineProtocol.Verdict.Pass;
		if (_a4.After5.OffsetAfterCm <= _a1.After5.OffsetAfterCm)
			return RecoilPlayBaselineProtocol.Verdict.Warn;
		return RecoilPlayBaselineProtocol.Verdict.Fail;
	}

	public static RecoilPlayBaselineProtocol.Verdict EvaluateA5Form(in CaseMath _a1)
	{
		float remaining = _a1.OffsetAfter3Pause04Deg.magnitude;
		float after3 = _a1.After3.OffsetAfterShotsDeg.magnitude;
		if (after3 > 0.02f && remaining > after3 + 0.01f)
			return RecoilPlayBaselineProtocol.Verdict.Fail;
		return RecoilPlayBaselineProtocol.Verdict.Pass;
	}

	public static RecoilPlayBaselineProtocol.Verdict EvaluateMathVsPlay(
		float _mathOffsetCm,
		float _playGroupCm,
		out string _note)
	{
		if (_playGroupCm < 0f)
		{
			_note = "PLAY_PENDING";
			return RecoilPlayBaselineProtocol.Verdict.PlayPending;
		}

		float denom = Mathf.Max(1f, _mathOffsetCm);
		float rel = Mathf.Abs(_playGroupCm - _mathOffsetCm) / denom;
		if (rel <= RecoilPlayBaselineProtocol.MathVsPlayWarnRatio)
		{
			_note = "Play group vs MATH MeanHit within 35%. Integration OK.";
			return RecoilPlayBaselineProtocol.Verdict.Pass;
		}

		if (rel <= RecoilPlayBaselineProtocol.MathVsPlayFailRatio)
		{
			_note =
				"WARN: Play vs math. Check hitscan / gate / pose / WeaponRecoilContext. Do not retune VerticalRecoil yet.";
			return RecoilPlayBaselineProtocol.Verdict.Warn;
		}

		_note =
			"FAIL: large Play vs math. Integration first (hitscan, barrel gate, pose), not VerticalRecoil.";
		return RecoilPlayBaselineProtocol.Verdict.Fail;
	}
	#endregion

	#region Private Methods
	private static void ResolveStance(
		RecoilPlayBaselineProtocol.CaseId _case,
		out float _kick,
		out float _recovery)
	{
		switch (_case)
		{
			case RecoilPlayBaselineProtocol.CaseId.A2AimingWalk50:
				_kick = RecoilPlayBaselineProtocol.WalkKickMultiplier;
				_recovery = RecoilPlayBaselineProtocol.WalkRecoveryMultiplier;
				return;
			case RecoilPlayBaselineProtocol.CaseId.A4AimingCrouch50:
				_kick = RecoilPlayBaselineProtocol.CrouchKickMultiplier;
				_recovery = RecoilPlayBaselineProtocol.CrouchRecoveryMultiplier;
				return;
			default:
				_kick = RecoilPlayBaselineProtocol.StandingKickMultiplier;
				_recovery = RecoilPlayBaselineProtocol.StandingRecoveryMultiplier;
				return;
		}
	}

	private static ShotSample Sample(
		in WeaponRecoilContext _context,
		int _shotCount,
		float _distanceMeters,
		float _spreadHalfAngleDegrees)
	{
		return new ShotSample(
			_shotCount,
			WeaponRecoilMath.PredictOffsetAfterShots(in _context, _shotCount),
			PredictMeanHitOffsetDegrees(in _context, _shotCount),
			_distanceMeters,
			_spreadHalfAngleDegrees);
	}

	private static float EstimateSpreadHalfAngleDegrees(WeaponDefinition _weapon, WeaponPoseState _pose)
	{
		if (_weapon == null)
			return 0f;
		return _weapon.BaseShotDispersion *
		       WeaponPoseCombatModifiers.GetSpreadMultiplier(_pose) *
		       RecoilPlayBaselineProtocol.HitscanBaseSpreadToDegrees;
	}
	#endregion
}

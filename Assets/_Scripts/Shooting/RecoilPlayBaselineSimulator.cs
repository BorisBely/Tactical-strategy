using UnityEngine;

/// <summary>
/// Self-contained A1–A5 / N8 burst: live WeaponRecoilMath + hitscan cone.
/// Does not hang components on a unit. Does not retune Vertical/Horizontal/Recovery.
/// </summary>
public static class RecoilPlayBaselineSimulator
{
	#region Nested Types
	public struct HitSample
	{
		public float OffsetXCm;
		public float OffsetYCm;
		public Vector2 RecoilOffsetDeg;
		public int RecoilShotIndexAtHitscan;
	}

	public struct BurstResult
	{
		public RecoilPlayBaselineProtocol.CaseId Case;
		public int ShotCount;
		public int InstanceHash;
		public float CenterXCm;
		public float CenterYCm;
		public float CenterAbsCm;
		public float SpreadDiameterCm;
		public Vector2 RecoilOffsetAtLastShotDeg;
		public int RecoilShotIndexAtLastShot;
		public Vector2 RemainingOffsetAfterPauseDeg;
	}
	#endregion

	#region Public Methods
	public static BurstResult SimulateBurst(
		WeaponDefinition _weapon,
		RecoilPlayBaselineProtocol.CaseId _case,
		int _shotCount,
		int _instanceHash,
		int _randomSeed)
	{
		return SimulateBurst(
			_weapon,
			_case,
			_shotCount,
			_instanceHash,
			_randomSeed,
			WeaponRecoilBalanceContract.ResolveBaselineFireMode(_weapon));
	}

	public static BurstResult SimulateBurst(
		WeaponDefinition _weapon,
		RecoilPlayBaselineProtocol.CaseId _case,
		int _shotCount,
		int _instanceHash,
		int _randomSeed,
		WeaponFireMode _fireMode)
	{
		WeaponFireMode fireMode = _fireMode;
		WeaponPoseState pose = _case == RecoilPlayBaselineProtocol.CaseId.A3HipFireStand15
			? WeaponPoseState.HipFire
			: WeaponPoseState.Aiming;
		ResolveStance(_case, out float stanceKick, out float stanceRecovery);
		float distance = RecoilPlayBaselineProtocol.CaseDistanceMeters(_case);
		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_weapon, fireMode, pose, stanceKick, stanceRecovery);
		context.InstanceHash = _instanceHash;

		int shotCount = Mathf.Max(1, _shotCount);
		var hits = new HitSample[shotCount];
		Random.State previous = Random.state;
		Random.InitState(_randomSeed);
		try
		{
			if (_case == RecoilPlayBaselineProtocol.CaseId.A5Pause04Stand50)
				SimulateA5(in context, _weapon, _case, pose, fireMode, distance, hits);
			else
				SimulateContinuous(in context, _weapon, _case, pose, fireMode, distance, hits);
		}
		finally
		{
			Random.state = previous;
		}

		return BuildBurstResult(_case, shotCount, _instanceHash, hits, in context);
	}

	public static Vector2 PredictRemainingAfterPause(
		WeaponDefinition _weapon,
		RecoilPlayBaselineProtocol.CaseId _case,
		int _burstShots,
		float _pauseSeconds)
	{
		WeaponFireMode fireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(_weapon);
		WeaponPoseState pose = _case == RecoilPlayBaselineProtocol.CaseId.A3HipFireStand15
			? WeaponPoseState.HipFire
			: WeaponPoseState.Aiming;
		ResolveStance(_case, out float stanceKick, out float stanceRecovery);
		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_weapon, fireMode, pose, stanceKick, stanceRecovery);
		return WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, _burstShots, _pauseSeconds);
	}
	#endregion

	#region Private Methods
	private static void SimulateContinuous(
		in WeaponRecoilContext _context,
		WeaponDefinition _weapon,
		RecoilPlayBaselineProtocol.CaseId _case,
		WeaponPoseState _pose,
		WeaponFireMode _fireMode,
		float _distanceMeters,
		HitSample[] _hits)
	{
		for (int i = 0; i < _hits.Length; i++)
		{
			int shotIndex = i + 1;
			Vector2 offset = WeaponRecoilMath.PredictOffsetBeforeShot(in _context, shotIndex);
			_hits[i] = FireOne(_weapon, _case, _pose, _fireMode, _distanceMeters, offset, shotIndex - 1);
		}
	}

	private static void SimulateA5(
		in WeaponRecoilContext _context,
		WeaponDefinition _weapon,
		RecoilPlayBaselineProtocol.CaseId _case,
		WeaponPoseState _pose,
		WeaponFireMode _fireMode,
		float _distanceMeters,
		HitSample[] _hits)
	{
		int burst = RecoilPlayBaselineProtocol.A5BurstShots;
		for (int i = 0; i < burst && i < _hits.Length; i++)
		{
			int shotIndex = i + 1;
			Vector2 offset = WeaponRecoilMath.PredictOffsetBeforeShot(in _context, shotIndex);
			_hits[i] = FireOne(_weapon, _case, _pose, _fireMode, _distanceMeters, offset, shotIndex - 1);
		}

		if (_hits.Length <= burst)
			return;

		Vector2 remaining = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(
			in _context,
			burst,
			RecoilPlayBaselineProtocol.PauseA5Seconds);
		_hits[burst] = FireOne(_weapon, _case, _pose, _fireMode, _distanceMeters, remaining, burst);
	}

	private static HitSample FireOne(
		WeaponDefinition _weapon,
		RecoilPlayBaselineProtocol.CaseId _case,
		WeaponPoseState _pose,
		WeaponFireMode _fireMode,
		float _distanceMeters,
		Vector2 _recoilOffsetDeg,
		int _recoilShotIndexAtHitscan)
	{
		Vector3 recoiled = WeaponRecoilMath.ApplyOffsetToDirection(Vector3.forward, _recoilOffsetDeg);
		float halfAngle = ResolveHalfAngleDegrees(
			_weapon, _case, _pose, _fireMode, _distanceMeters, _recoilShotIndexAtHitscan + 1);
		Vector3 dir = ApplyConeSpread(recoiled, halfAngle);
		ProjectHit(dir, _distanceMeters, out float xCm, out float yCm);
		return new HitSample
		{
			OffsetXCm = xCm,
			OffsetYCm = yCm,
			RecoilOffsetDeg = _recoilOffsetDeg,
			RecoilShotIndexAtHitscan = _recoilShotIndexAtHitscan
		};
	}

	private static float ResolveHalfAngleDegrees(
		WeaponDefinition _weapon,
		RecoilPlayBaselineProtocol.CaseId _case,
		WeaponPoseState _pose,
		WeaponFireMode _fireMode,
		float _distanceMeters,
		int _burstShotIndex)
	{
		LocomotionStance stance = _case == RecoilPlayBaselineProtocol.CaseId.A4AimingCrouch50
			? LocomotionStance.Crouch
			: LocomotionStance.Standing;
		bool moving = _case == RecoilPlayBaselineProtocol.CaseId.A2AimingWalk50;
		var input = new WeaponShotAccuracyInput
		{
			WeaponDefinition = _weapon,
			TargetDistanceMeters = _distanceMeters,
			BaseSpreadToDegrees = RecoilPlayBaselineProtocol.HitscanBaseSpreadToDegrees,
			MinHalfAngleDegrees = RecoilPlayBaselineProtocol.HitscanMinHalfAngleDegrees,
			MaxHalfAngleDegrees = RecoilPlayBaselineProtocol.HitscanMaxHalfAngleDegrees,
			Stance = stance,
			IsMoving = moving,
			StandingSpreadMultiplier = RecoilPlayBaselineProtocol.HitscanStandingSpreadMultiplier,
			CrouchSpreadMultiplier = RecoilPlayBaselineProtocol.HitscanCrouchSpreadMultiplier,
			MovingSpreadMultiplier = RecoilPlayBaselineProtocol.HitscanMovingSpreadMultiplier,
			AimProgress01 = 1f,
			SelectedAimMode = WeaponAimMode.FullAim,
			AimMode = WeaponAimMode.FullAim,
			SelectedFireMode = _fireMode,
			FireMode = _fireMode,
			BurstShotIndex = _burstShotIndex,
			WeaponPose = _pose,
			PoseSpreadMultiplier = WeaponPoseCombatModifiers.GetSpreadMultiplier(_pose)
		};
		return WeaponShotAccuracyEvaluator.Evaluate(input).HalfAngleDegrees;
	}

	private static Vector3 ApplyConeSpread(Vector3 _forward, float _halfAngleDegrees)
	{
		Vector3 f = _forward.normalized;
		if (_halfAngleDegrees <= 0.0001f)
			return f;

		float tan = Mathf.Tan(_halfAngleDegrees * Mathf.Deg2Rad);
		Vector2 rnd = Random.insideUnitCircle * tan;
		Vector3 up = Mathf.Abs(Vector3.Dot(f, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
		Vector3 right = Vector3.Cross(up, f).normalized;
		Vector3 upOrtho = Vector3.Cross(f, right).normalized;
		return (f + right * rnd.x + upOrtho * rnd.y).normalized;
	}

	private static void ProjectHit(Vector3 _direction, float _distanceMeters, out float _xCm, out float _yCm)
	{
		Vector3 dir = _direction.normalized;
		if (dir.z <= 1e-5f)
		{
			_xCm = 0f;
			_yCm = 0f;
			return;
		}

		float t = _distanceMeters / dir.z;
		_xCm = dir.x * t * 100f;
		_yCm = dir.y * t * 100f;
	}

	private static BurstResult BuildBurstResult(
		RecoilPlayBaselineProtocol.CaseId _case,
		int _shotCount,
		int _instanceHash,
		HitSample[] _hits,
		in WeaponRecoilContext _context)
	{
		int n = Mathf.Min(_shotCount, _hits.Length);
		float sumX = 0f;
		float sumY = 0f;
		for (int i = 0; i < n; i++)
		{
			sumX += _hits[i].OffsetXCm;
			sumY += _hits[i].OffsetYCm;
		}

		float meanX = n > 0 ? sumX / n : 0f;
		float meanY = n > 0 ? sumY / n : 0f;
		float spread = 0f;
		for (int i = 0; i < n; i++)
		{
			for (int j = i + 1; j < n; j++)
			{
				float dx = _hits[i].OffsetXCm - _hits[j].OffsetXCm;
				float dy = _hits[i].OffsetYCm - _hits[j].OffsetYCm;
				spread = Mathf.Max(spread, Mathf.Sqrt(dx * dx + dy * dy));
			}
		}

		HitSample last = n > 0 ? _hits[n - 1] : default;
		Vector2 remaining = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(
			in _context,
			RecoilPlayBaselineProtocol.A5BurstShots,
			RecoilPlayBaselineProtocol.PauseA5Seconds);

		return new BurstResult
		{
			Case = _case,
			ShotCount = n,
			InstanceHash = _instanceHash,
			CenterXCm = meanX,
			CenterYCm = meanY,
			CenterAbsCm = Mathf.Sqrt(meanX * meanX + meanY * meanY),
			SpreadDiameterCm = spread,
			RecoilOffsetAtLastShotDeg = last.RecoilOffsetDeg,
			RecoilShotIndexAtLastShot = last.RecoilShotIndexAtHitscan,
			RemainingOffsetAfterPauseDeg = remaining
		};
	}

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
	#endregion
}

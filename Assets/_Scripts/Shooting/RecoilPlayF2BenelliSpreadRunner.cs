using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase F2 Benelli pellet spread @15 m / 40 m around one RecoilOffset per shell (§14.10).
/// Deterministic SIM: fixed pattern seed, no second P-cone.
/// </summary>
public static class RecoilPlayF2BenelliSpreadRunner
{
	#region Constants
	public const string BenelliWeaponAssetName = "Weapon_BenelliM4";
	public const string Ammo12GaugeAssetName = "Ammo_12Gauge";

	private const float c_Distance15M = 15f;
	private const float c_Distance40M = 40f;
	private const int c_PatternSeed = 4242;

	private const float c_ShotgunCenterJitterRadius01 = 0.12f;
	private const float c_ShotgunInnerRadiusJitter = 0.18f;
	private const float c_ShotgunOuterRadiusJitter = 0.22f;
	private const float c_ShotgunInnerAngleJitterDegrees = 28f;
	private const float c_ShotgunOuterAngleJitterDegrees = 22f;
	#endregion

	#region Nested Types
	private struct ShellSample
	{
		public float DistanceMeters;
		public int ShotIndex;
		public Vector2 RecoilOffsetDeg;
		public float CenterXCm;
		public float CenterYCm;
		public float CenterAbsCm;
		public float PelletSpreadDiameterCm;
		public int PelletCount;
	}
	#endregion

	#region Public Methods
	public static string Run(WeaponDefinition _benelli, AmmoDefinition _ammo)
	{
		var sb = new StringBuilder(2560);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilPlayF2BenelliSpread SIM");
		sb.AppendLine("Phase F2: 12 ga pellet ring @15 m / 40 m. One RecoilOffset per shell. Seed=" + c_PatternSeed + ".");
		sb.AppendLine("Pellets scatter around offset aim; no second recoil cone.");
		sb.AppendLine();

		ShellSample shot1At15 = SimulateShell(_benelli, _ammo, c_Distance15M, 1);
		ShellSample shot1At40 = SimulateShell(_benelli, _ammo, c_Distance40M, 1);
		ShellSample shot2At15 = SimulateShell(_benelli, _ammo, c_Distance15M, 2);

		AppendShell(sb, in shot1At15, culture);
		AppendShell(sb, in shot1At40, culture);
		AppendShell(sb, in shot2At15, culture);

		float offsetCm15 = RecoilPlayBaselineProtocol.DegreesToCm(shot2At15.RecoilOffsetDeg.magnitude, c_Distance15M);

		sb.AppendLine("Form checks:");
		AppendCheck(sb, "Ammo uses shotgun pellet pattern", _ammo != null && _ammo.UsesShotgunPelletPattern);
		AppendCheck(sb, "Pellet count ≥ 2", shot1At15.PelletCount >= 2);
		AppendCheck(sb, "Shot1 spread @40 m > @15 m", shot1At40.PelletSpreadDiameterCm > shot1At15.PelletSpreadDiameterCm);
		AppendCheck(sb, "Shot1 @15 m center near aim (|Off|=0)", shot1At15.RecoilOffsetDeg.magnitude < 0.01f);
		AppendCheck(sb, "Shot1 @15 m cloud compact", shot1At15.PelletSpreadDiameterCm > 5f && shot1At15.PelletSpreadDiameterCm < 120f);
		AppendCheck(sb, "Shot2 @15 m RecoilOffset > 0", shot2At15.RecoilOffsetDeg.magnitude > 0.01f);
		AppendCheck(sb, "Shot2 cloud center tracks offset",
			Mathf.Abs(shot2At15.CenterAbsCm - offsetCm15) < Mathf.Max(8f, offsetCm15 * 0.5f));
		AppendCheck(sb, "Shot2 spread same shell geometry as shot1 @15 m",
			Mathf.Abs(shot2At15.PelletSpreadDiameterCm - shot1At15.PelletSpreadDiameterCm) < 15f);
		sb.AppendLine("  Assets not changed. F CLOSE. Phase G not opened.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static ShellSample SimulateShell(
		WeaponDefinition _weapon,
		AmmoDefinition _ammo,
		float _distanceMeters,
		int _shotIndex)
	{
		var sample = new ShellSample
		{
			DistanceMeters = _distanceMeters,
			ShotIndex = _shotIndex,
			PelletCount = _ammo != null ? Mathf.Max(1, _ammo.ProjectileCount) : 1
		};
		if (_weapon == null || _ammo == null)
			return sample;

		WeaponFireMode fireMode = WeaponFireMode.SemiAuto;
		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_weapon,
			fireMode,
			WeaponPoseState.Aiming,
			RecoilPlayBaselineProtocol.StandingKickMultiplier,
			RecoilPlayBaselineProtocol.StandingRecoveryMultiplier);

		sample.RecoilOffsetDeg = WeaponRecoilMath.PredictOffsetBeforeShot(in context, _shotIndex);
		Vector3 shotDirection = WeaponRecoilMath.ApplyOffsetToDirection(Vector3.forward, sample.RecoilOffsetDeg);

		float halfAngle = ResolveHalfAngleDegrees(_weapon, _ammo, fireMode, _distanceMeters, _shotIndex);
		float shotgunHalfAngle = halfAngle * _ammo.GetShotgunSpreadDistanceScale(_distanceMeters);

		var pelletX = new float[sample.PelletCount];
		var pelletY = new float[sample.PelletCount];
		Random.State previous = Random.state;
		Random.InitState(c_PatternSeed + Mathf.RoundToInt(_distanceMeters));
		try
		{
			float patternYaw = Random.Range(0f, 360f);
			for (int i = 0; i < sample.PelletCount; i++)
			{
				Vector3 dir = ApplyShotgunPelletOffset(
					shotDirection,
					shotgunHalfAngle,
					i,
					sample.PelletCount,
					_ammo.ShotgunInnerRingRadius01,
					_ammo.ShotgunOuterRingRadius01,
					patternYaw);
				ProjectHit(dir, _distanceMeters, out pelletX[i], out pelletY[i]);
			}
		}
		finally
		{
			Random.state = previous;
		}

		float sumX = 0f;
		float sumY = 0f;
		for (int i = 0; i < sample.PelletCount; i++)
		{
			sumX += pelletX[i];
			sumY += pelletY[i];
		}

		sample.CenterXCm = sumX / sample.PelletCount;
		sample.CenterYCm = sumY / sample.PelletCount;
		sample.CenterAbsCm = Mathf.Sqrt(sample.CenterXCm * sample.CenterXCm + sample.CenterYCm * sample.CenterYCm);
		sample.PelletSpreadDiameterCm = MaxPairwiseDistance(pelletX, pelletY, sample.PelletCount);
		return sample;
	}

	private static float ResolveHalfAngleDegrees(
		WeaponDefinition _weapon,
		AmmoDefinition _ammo,
		WeaponFireMode _fireMode,
		float _distanceMeters,
		int _shotIndex)
	{
		var input = new WeaponShotAccuracyInput
		{
			WeaponDefinition = _weapon,
			AmmoDefinition = _ammo,
			TargetDistanceMeters = _distanceMeters,
			BaseSpreadToDegrees = RecoilPlayBaselineProtocol.HitscanBaseSpreadToDegrees,
			MinHalfAngleDegrees = RecoilPlayBaselineProtocol.HitscanMinHalfAngleDegrees,
			MaxHalfAngleDegrees = RecoilPlayBaselineProtocol.HitscanMaxHalfAngleDegrees,
			Stance = LocomotionStance.Standing,
			IsMoving = false,
			StandingSpreadMultiplier = RecoilPlayBaselineProtocol.HitscanStandingSpreadMultiplier,
			CrouchSpreadMultiplier = RecoilPlayBaselineProtocol.HitscanCrouchSpreadMultiplier,
			MovingSpreadMultiplier = RecoilPlayBaselineProtocol.HitscanMovingSpreadMultiplier,
			AimProgress01 = 1f,
			SelectedAimMode = WeaponAimMode.FullAim,
			AimMode = WeaponAimMode.FullAim,
			SelectedFireMode = _fireMode,
			FireMode = _fireMode,
			BurstShotIndex = _shotIndex,
			WeaponPose = WeaponPoseState.Aiming,
			PoseSpreadMultiplier = WeaponPoseCombatModifiers.AimingSpreadMultiplier
		};
		return WeaponShotAccuracyEvaluator.Evaluate(input).HalfAngleDegrees;
	}

	private static Vector3 ApplyShotgunPelletOffset(
		Vector3 _shotDirection,
		float _halfAngleDegrees,
		int _pelletIndex,
		int _pelletCount,
		float _innerRadius01,
		float _outerRadius01,
		float _patternYawDegrees)
	{
		if (_pelletCount <= 1 || _halfAngleDegrees <= 0.0001f)
			return _shotDirection.normalized;

		GetShotgunPelletRingOffset(
			_pelletIndex,
			_pelletCount,
			_innerRadius01,
			_outerRadius01,
			out float radius01,
			out float angleDegrees);
		angleDegrees += _patternYawDegrees;

		float spreadRadians = _halfAngleDegrees * Mathf.Deg2Rad;
		float offsetRadians = radius01 * spreadRadians;
		Vector3 forward = _shotDirection.normalized;
		Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
		Vector3 right = Vector3.Cross(up, forward).normalized;
		Vector3 upOrtho = Vector3.Cross(forward, right).normalized;
		float rad = angleDegrees * Mathf.Deg2Rad;
		Vector3 offset = right * (Mathf.Sin(rad) * offsetRadians) + upOrtho * (Mathf.Cos(rad) * offsetRadians);
		return (forward + offset).normalized;
	}

	private static void GetShotgunPelletRingOffset(
		int _pelletIndex,
		int _pelletCount,
		float _innerRadius01,
		float _outerRadius01,
		out float _radius01,
		out float _angleDegrees)
	{
		if (_pelletIndex <= 0)
		{
			_radius01 = Random.Range(0f, c_ShotgunCenterJitterRadius01);
			_angleDegrees = Random.Range(0f, 360f);
			return;
		}

		int remaining = Mathf.Max(0, _pelletCount - 1);
		int innerCount = Mathf.Max(1, Mathf.RoundToInt(remaining * 0.45f));
		float innerRadius = Mathf.Clamp01(_innerRadius01);
		float outerRadius = Mathf.Clamp(innerRadius, _outerRadius01, 1.5f);

		if (_pelletIndex <= innerCount)
		{
			float baseAngle = 360f * (_pelletIndex - 1) / innerCount;
			float baseRadius = innerRadius * 0.55f;
			_radius01 = Mathf.Clamp01(
				baseRadius + Random.Range(-c_ShotgunInnerRadiusJitter, c_ShotgunInnerRadiusJitter));
			_angleDegrees = baseAngle + Random.Range(-c_ShotgunInnerAngleJitterDegrees, c_ShotgunInnerAngleJitterDegrees);
			return;
		}

		int outerCount = Mathf.Max(1, remaining - innerCount);
		int outerIndex = _pelletIndex - 1 - innerCount;
		float outerBaseAngle = 360f * outerIndex / outerCount;
		float outerBaseRadius = Mathf.Lerp(innerRadius, outerRadius, 0.72f);
		_radius01 = Mathf.Clamp01(
			outerBaseRadius + Random.Range(-c_ShotgunOuterRadiusJitter, c_ShotgunOuterRadiusJitter));
		_angleDegrees = outerBaseAngle + Random.Range(-c_ShotgunOuterAngleJitterDegrees, c_ShotgunOuterAngleJitterDegrees);
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

	private static float MaxPairwiseDistance(float[] _x, float[] _y, int _count)
	{
		float max = 0f;
		for (int i = 0; i < _count; i++)
		{
			for (int j = i + 1; j < _count; j++)
			{
				float dx = _x[i] - _x[j];
				float dy = _y[i] - _y[j];
				max = Mathf.Max(max, Mathf.Sqrt(dx * dx + dy * dy));
			}
		}

		return max;
	}

	private static void AppendShell(StringBuilder _sb, in ShellSample _sample, CultureInfo _culture)
	{
		_sb.AppendLine(
			"Shot " + _sample.ShotIndex + " @ " + _sample.DistanceMeters.ToString("F0", _culture) + " m");
		_sb.AppendLine(
			"  RecoilOffset=" + _sample.RecoilOffsetDeg.magnitude.ToString("F3", _culture) +
			"°  pellets=" + _sample.PelletCount +
			"  cloud center=" + _sample.CenterAbsCm.ToString("F1", _culture) +
			" cm  spread=" + _sample.PelletSpreadDiameterCm.ToString("F1", _culture) + " cm");
		_sb.AppendLine();
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _ok)
	{
		_sb.AppendLine("  " + (_ok ? "OK  " : "WARN") + "  " + _label);
	}
	#endregion
}

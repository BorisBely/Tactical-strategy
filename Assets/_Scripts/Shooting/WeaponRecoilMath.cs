using UnityEngine;

/// <summary>
/// One successful-shot kick: angular delta in degrees plus the smoothed horizontal pattern.
/// </summary>
public readonly struct WeaponRecoilKick
{
	public readonly Vector2 Delta;
	public readonly float PatternValue;
	public readonly float VisualImpulse;

	public WeaponRecoilKick(Vector2 _delta, float _patternValue, float _visualImpulse)
	{
		Delta = _delta;
		PatternValue = _patternValue;
		VisualImpulse = _visualImpulse;
	}
}

/// <summary>
/// Gameplay recoil: accumulated aim offset in degrees, not a spread-cone penalty.
/// Kick is computed only on a successful shot; recovery runs every frame.
/// </summary>
public static class WeaponRecoilMath
{
	#region Constants
	public const float PatternSmooth = 0.35f;
	public const float VerticalVariationMin = 0.85f;
	public const float VerticalVariationMax = 1.15f;
	public const float MaxHorizontalStepScale = 1.25f;
	public const float MaxVerticalStepScale = 1.20f;
	public const float DefaultMaxOffsetDegrees = 12f;
	public const float PatternFreq1 = 0.73f;
	public const float PatternWeight1 = 0.65f;
	public const float PatternFreq2 = 1.31f;
	public const float PatternWeight2 = 0.35f;
	public const float PatternSeed2Scale = 1.7f;
	public const float RecoveryWhileFiringForPrediction = 0.7f;
	#endregion

	#region Public Methods
	public static float ResolveFireModeMultiplier(WeaponDefinition _weaponDefinition, WeaponFireMode _fireMode)
	{
		if (_weaponDefinition == null)
			return 1f;

		return _fireMode switch
		{
			WeaponFireMode.FullAuto => _weaponDefinition.AutoRecoilMultiplier,
			WeaponFireMode.Burst => _weaponDefinition.AutoRecoilMultiplier,
			WeaponFireMode.Auto => _weaponDefinition.AutoRecoilMultiplier,
			_ => _weaponDefinition.SemiAutoRecoilMultiplier
		};
	}

	public static float ComposeImpulseMultiplier(
		WeaponDefinition _weaponDefinition,
		WeaponFireMode _fireMode,
		AmmoDefinition _ammoDefinition,
		float _attachmentRecoilProduct,
		float _skillMultiplier,
		float _traitsMultiplier,
		float _conditionMultiplier,
		float _postureMultiplier)
	{
		float ammoModifier = _ammoDefinition != null ? _ammoDefinition.RecoilModifier : 1f;
		return ResolveFireModeMultiplier(_weaponDefinition, _fireMode) *
		       ammoModifier *
		       _attachmentRecoilProduct *
		       _skillMultiplier *
		       _traitsMultiplier *
		       _conditionMultiplier *
		       _postureMultiplier;
	}

	public static float CombinePatternSeed(float _weaponSeed, int _instanceHash)
	{
		return _weaponSeed + (_instanceHash & 1023) * 0.017f;
	}

	public static WeaponRecoilKick ComputeKick(
		WeaponDefinition _weaponDefinition,
		float _seed,
		int _shotIndex,
		float _previousPatternValue,
		float _impulseMultiplier)
	{
		if (_weaponDefinition == null)
			return new WeaponRecoilKick(Vector2.zero, _previousPatternValue, 0f);

		int i = Mathf.Max(1, _shotIndex);
		float m = _impulseMultiplier;
		float verticalRecoil = Mathf.Max(0f, _weaponDefinition.VerticalRecoil);
		float horizontalRecoil = Mathf.Max(0f, _weaponDefinition.HorizontalRecoil);

		float verticalVariation = Mathf.Lerp(
			VerticalVariationMin,
			VerticalVariationMax,
			Hash01(_seed, i, 0));
		float rawPattern = EvaluateRawPattern(_seed, i);
		float pattern = Mathf.Lerp(_previousPatternValue, rawPattern, PatternSmooth);

		float deltaY = verticalRecoil * verticalVariation * m;
		float deltaX = horizontalRecoil * pattern * m;

		float maxY = verticalRecoil * MaxVerticalStepScale * m;
		float maxX = horizontalRecoil * MaxHorizontalStepScale * m;
		deltaY = Mathf.Clamp(deltaY, 0f, maxY);
		deltaX = Mathf.Clamp(deltaX, -maxX, maxX);

		float visualImpulse = verticalRecoil > 0.0001f ? deltaY / verticalRecoil : m;
		return new WeaponRecoilKick(new Vector2(deltaX, deltaY), pattern, visualImpulse);
	}

	public static Vector2 ApplyKick(Vector2 _offset, Vector2 _delta, float _maxOffsetDegrees)
	{
		Vector2 next = _offset + _delta;
		float cap = Mathf.Max(0.01f, _maxOffsetDegrees);
		float magnitude = next.magnitude;
		if (magnitude > cap)
			next *= cap / magnitude;
		return next;
	}

	public static Vector2 Recover(Vector2 _offset, float _degreesPerSecond, float _deltaTime)
	{
		if (_offset.sqrMagnitude <= 1e-10f || _degreesPerSecond <= 0f || _deltaTime <= 0f)
			return _offset;

		return Vector2.MoveTowards(_offset, Vector2.zero, _degreesPerSecond * _deltaTime);
	}

	public static Vector3 ApplyOffsetToDirection(Vector3 _baseDirection, Vector2 _offsetDegrees)
	{
		Vector3 forward = _baseDirection.normalized;
		if (_offsetDegrees.sqrMagnitude <= 1e-10f)
			return forward;

		Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
		Vector3 right = Vector3.Cross(up, forward).normalized;
		Quaternion rotation = Quaternion.AngleAxis(_offsetDegrees.x, up) *
		                      Quaternion.AngleAxis(-_offsetDegrees.y, right);
		return (rotation * forward).normalized;
	}

	/// <summary>
	/// Offset used by shot number <paramref name="_shotIndex"/> (1-based): after previous kicks and inter-shot recovery.
	/// </summary>
	public static Vector2 PredictOffsetBeforeShot(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		WeaponFireMode _fireMode,
		int _shotIndex)
	{
		return PredictOffsetBeforeShotInternal(
			_weaponDefinition,
			_attachments,
			_fireMode,
			_shotIndex,
			RecoveryWhileFiringForPrediction);
	}

	private static Vector2 PredictOffsetBeforeShotInternal(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		WeaponFireMode _fireMode,
		int _shotIndex,
		float _recoveryWhileFiringMultiplier)
	{
		if (_weaponDefinition == null || _shotIndex <= 1)
			return Vector2.zero;

		float attachmentProduct = WeaponDistanceAimEvaluator.GetAttachmentRecoilProduct(_attachments, _fireMode);
		float impulse = ComposeImpulseMultiplier(
			_weaponDefinition,
			_fireMode,
			null,
			attachmentProduct,
			1f,
			1f,
			1f,
			1f);
		float seed = _weaponDefinition.RecoilPatternSeed;
		float intervalSeconds = 60f / Mathf.Max(1f, _weaponDefinition.FireRateRpm);
		float recoveryPerShot = Mathf.Max(0f, _weaponDefinition.RecoilRecoveryPerSecond) *
		                        _recoveryWhileFiringMultiplier *
		                        intervalSeconds;

		Vector2 offset = Vector2.zero;
		float pattern = 0f;
		int kicks = Mathf.Max(0, _shotIndex - 1);
		for (int n = 1; n <= kicks; n++)
		{
			WeaponRecoilKick kick = ComputeKick(_weaponDefinition, seed, n, pattern, impulse);
			pattern = kick.PatternValue;
			offset = ApplyKick(offset, kick.Delta, DefaultMaxOffsetDegrees);
			offset = Vector2.MoveTowards(offset, Vector2.zero, recoveryPerShot);
		}

		return offset;
	}

	public static float PredictOffsetMagnitudeBeforeShot(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		WeaponFireMode _fireMode,
		int _shotIndex)
	{
		return PredictOffsetBeforeShot(_weaponDefinition, _attachments, _fireMode, _shotIndex).magnitude;
	}

	/// <summary>
	/// |Offset| after <paramref name="_shotCount"/> successful shots with inter-shot recovery while firing.
	/// Equivalent to offset immediately before shot (<paramref name="_shotCount"/> + 1).
	/// </summary>
	public static Vector2 PredictOffsetAfterShots(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		WeaponFireMode _fireMode,
		int _shotCount,
		float _recoveryWhileFiringMultiplier = RecoveryWhileFiringForPrediction)
	{
		if (_weaponDefinition == null || _shotCount <= 0)
			return Vector2.zero;

		return PredictOffsetBeforeShotInternal(
			_weaponDefinition,
			_attachments,
			_fireMode,
			_shotCount + 1,
			_recoveryWhileFiringMultiplier);
	}

	public static float PredictOffsetMagnitudeAfterShots(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		WeaponFireMode _fireMode,
		int _shotCount,
		float _recoveryWhileFiringMultiplier = RecoveryWhileFiringForPrediction)
	{
		return PredictOffsetAfterShots(
			_weaponDefinition,
			_attachments,
			_fireMode,
			_shotCount,
			_recoveryWhileFiringMultiplier).magnitude;
	}

	/// <summary>
	/// Burst with while-firing recovery, then full-rate recovery during a pause (StopFiring / discipline).
	/// </summary>
	public static Vector2 PredictOffsetAfterBurstAndPause(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		WeaponFireMode _fireMode,
		int _burstShotCount,
		float _pauseSeconds,
		float _recoveryWhileFiringMultiplier = RecoveryWhileFiringForPrediction,
		float _pauseRecoveryMultiplier = 1f)
	{
		Vector2 offset = PredictOffsetAfterShots(
			_weaponDefinition,
			_attachments,
			_fireMode,
			_burstShotCount,
			_recoveryWhileFiringMultiplier);
		if (_weaponDefinition == null || _pauseSeconds <= 0f)
			return offset;

		float recoveryPerSecond = Mathf.Max(0f, _weaponDefinition.RecoilRecoveryPerSecond) *
		                          Mathf.Max(0f, _pauseRecoveryMultiplier);
		return Recover(offset, recoveryPerSecond, _pauseSeconds);
	}

	public static float PredictOffsetMagnitudeAfterBurstAndPause(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		WeaponFireMode _fireMode,
		int _burstShotCount,
		float _pauseSeconds,
		float _recoveryWhileFiringMultiplier = RecoveryWhileFiringForPrediction,
		float _pauseRecoveryMultiplier = 1f)
	{
		return PredictOffsetAfterBurstAndPause(
			_weaponDefinition,
			_attachments,
			_fireMode,
			_burstShotCount,
			_pauseSeconds,
			_recoveryWhileFiringMultiplier,
			_pauseRecoveryMultiplier).magnitude;
	}

	/// <summary>Linear displacement of aim center at distance: d × tan(|Offset|).</summary>
	public static float OffsetToDisplacementMeters(float _offsetMagnitudeDegrees, float _distanceMeters)
	{
		float distance = Mathf.Max(0f, _distanceMeters);
		float angleRadians = Mathf.Max(0f, _offsetMagnitudeDegrees) * Mathf.Deg2Rad;
		return distance * Mathf.Tan(angleRadians);
	}

	public static float SpreadDiameterMeters(float _distanceMeters, float _halfAngleDegrees)
	{
		float distance = Mathf.Max(0f, _distanceMeters);
		float halfAngleRadians = Mathf.Max(0f, _halfAngleDegrees) * Mathf.Deg2Rad;
		return 2f * distance * Mathf.Tan(halfAngleRadians);
	}
	#endregion

	#region Private Methods
	private static float EvaluateRawPattern(float _seed, int _shotIndex)
	{
		return Mathf.Sin(_seed + _shotIndex * PatternFreq1) * PatternWeight1 +
		       Mathf.Sin(_seed * PatternSeed2Scale + _shotIndex * PatternFreq2) * PatternWeight2;
	}

	private static float Hash01(float _seed, int _shotIndex, int _channel)
	{
		float x = Mathf.Sin(_seed * 12.9898f + _shotIndex * 78.233f + _channel * 37.719f) * 43758.5453f;
		return x - Mathf.Floor(x);
	}
	#endregion
}

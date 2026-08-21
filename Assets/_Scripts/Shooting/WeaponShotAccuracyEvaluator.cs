using UnityEngine;

/// <summary>
/// Собирает все множители точности одного выстрела в прозрачный контекст.
/// </summary>
public static class WeaponShotAccuracyEvaluator
{
	#region Public Methods
	public static WeaponShotAccuracyContext Evaluate(WeaponShotAccuracyInput _input)
	{
		WeaponDefinition weaponDefinition = _input.WeaponDefinition;
		WeaponRuntimeState weaponState = _input.WeaponState;
		AmmoDefinition ammoDefinition = _input.AmmoDefinition;

		float baseDispersion = weaponDefinition != null ? weaponDefinition.BaseShotDispersion : 1f;
		float ammoSpread = ammoDefinition != null ? ammoDefinition.SpreadModifier : 1f;

		float weaponDistance = weaponDefinition != null
			? weaponDefinition.GetDistanceDispersionMultiplier(_input.TargetDistanceMeters)
			: 1f;
		float attachmentDistance = weaponState != null
			? GetAttachmentDistanceDispersionProduct(weaponState, _input.TargetDistanceMeters, !_input.ExcludeOpticAttachments)
			: 1f;

		const float recoilFactor = 1f;
		float stanceFactor = GetPostureDispersionMultiplier(_input);
		float movementFactor = 1f;
		float skillFactor = _input.CombatStats != null ? _input.CombatStats.GetDispersionMultiplier() : 1f;
		float individualFactor = _input.IndividualTraits != null ? _input.IndividualTraits.GetDispersionMultiplier() : 1f;
		float conditionFactor = _input.CombatCondition != null ? _input.CombatCondition.GetDispersionMultiplier() : 1f;
		float aimProgressForSpread = ResolveAimProgressForSpread(_input);
		float aimCompletionFactor = WeaponAimModeUtility.GetIncompleteAimSpreadMultiplier(
			aimProgressForSpread,
			_input.TargetDistanceMeters);
		const float autoBurstFactor = 1f;
		float poseFactor = _input.PoseSpreadMultiplier > 0.01f ? _input.PoseSpreadMultiplier : 1f;

		float raw = baseDispersion *
		            ammoSpread *
		            weaponDistance *
		            attachmentDistance *
		            recoilFactor *
		            stanceFactor *
		            movementFactor *
		            skillFactor *
		            individualFactor *
		            conditionFactor *
		            aimCompletionFactor *
		            autoBurstFactor *
		            poseFactor *
		            _input.BaseSpreadToDegrees;

		float halfAngle = Mathf.Clamp(raw, _input.MinHalfAngleDegrees, _input.MaxHalfAngleDegrees);
		float spreadRadiusMeters = CalculateSpreadRadiusMeters(_input.TargetDistanceMeters, halfAngle);
		float spreadDiameterMeters = spreadRadiusMeters * 2f;
		return new WeaponShotAccuracyContext(
			_input.TargetDistanceMeters,
			baseDispersion,
			ammoSpread,
			weaponDistance,
			attachmentDistance,
			recoilFactor,
			stanceFactor,
			movementFactor,
			skillFactor,
			conditionFactor,
			aimCompletionFactor,
			Mathf.Clamp01(_input.AimProgress01),
			_input.SelectedAimMode,
			_input.AimMode,
			WeaponAimModeUtility.ResolveEffectiveMode(_input.AimMode, _input.TargetDistanceMeters),
			_input.SelectedFireMode,
			_input.FireMode,
			_input.BurstShotIndex,
			autoBurstFactor,
			raw,
			halfAngle,
			spreadRadiusMeters,
			spreadDiameterMeters);
	}
	#endregion

	#region Private Methods
	private static float CalculateSpreadRadiusMeters(float _distanceMeters, float _halfAngleDegrees)
	{
		float distance = Mathf.Max(0f, _distanceMeters);
		float halfAngleRadians = Mathf.Max(0f, _halfAngleDegrees) * Mathf.Deg2Rad;
		return distance * Mathf.Tan(halfAngleRadians);
	}

	/// <summary>Auto/Burst: неполное прицеливание влияет только на 1-й выстрел серии.</summary>
	private static float ResolveAimProgressForSpread(WeaponShotAccuracyInput _input)
	{
		if (_input.BurstShotIndex > 1)
			return WeaponAimModeUtility.FullAimProgress01;

		return _input.AimProgress01;
	}

	private static float GetPostureDispersionMultiplier(WeaponShotAccuracyInput _input)
	{
		if (_input.IsSprinting)
			return Mathf.Max(0.01f, _input.SprintSpreadMultiplier);

		if (_input.PostureSpreadMultiplier > 0f)
			return Mathf.Max(0.01f, _input.PostureSpreadMultiplier);

		float stanceFactor = GetLegacyStanceDispersionMultiplier(_input);
		float movementFactor = _input.IsMoving
			? Mathf.Max(0.01f, _input.MovingSpreadMultiplier)
			: 1f;
		return stanceFactor * movementFactor;
	}

	private static float GetLegacyStanceDispersionMultiplier(WeaponShotAccuracyInput _input)
	{
		switch (_input.Stance)
		{
			case LocomotionStance.Crouch:
				return Mathf.Max(0.01f, _input.CrouchSpreadMultiplier);
			case LocomotionStance.Prone:
				return Mathf.Max(0.01f, _input.ProneSpreadMultiplier);
			default:
				return Mathf.Max(0.01f, _input.StandingSpreadMultiplier);
		}
	}

	private static float GetAttachmentDistanceDispersionProduct(
		WeaponRuntimeState _weaponState,
		float _distanceMeters,
		bool _includeOptics)
	{
		WeaponAttachmentDefinition[] attachments = _weaponState.EquippedAttachments;
		if (_includeOptics)
			return _weaponState.GetAttachmentDistanceDispersionProduct(_distanceMeters);
		return WeaponPoseAutoCapabilityBaker.GetAttachmentDistanceDispersionProduct(
			attachments,
			_distanceMeters,
			_includeOptics: false);
	}
	#endregion
}

/// <summary>
/// Данные, необходимые для расчёта геймплейного конуса ошибки выстрела.
/// </summary>
public struct WeaponShotAccuracyInput
{
	public WeaponDefinition WeaponDefinition;
	public WeaponRuntimeState WeaponState;
	public EquippedWeaponTransientState TransientState;
	public AmmoDefinition AmmoDefinition;
	public UnitCombatStats CombatStats;
	public UnitIndividualTraits IndividualTraits;
	public UnitCombatCondition CombatCondition;
	public float TargetDistanceMeters;
	public float BaseSpreadToDegrees;
	public float AutoSpreadMultiplier;
	public float RecoilSpreadScale;
	public float MinHalfAngleDegrees;
	public float MaxHalfAngleDegrees;
	public LocomotionStance Stance;
	public bool IsMoving;
	public bool IsSprinting;
	public float StandingSpreadMultiplier;
	public float CrouchSpreadMultiplier;
	public float ProneSpreadMultiplier;
	public float MovingSpreadMultiplier;
	public float SprintSpreadMultiplier;
	public float PostureSpreadMultiplier;
	public float AimProgress01;
	public WeaponAimMode SelectedAimMode;
	public WeaponAimMode AimMode;
	public WeaponFireMode SelectedFireMode;
	public WeaponFireMode FireMode;
	public int BurstShotIndex;
	/// <summary>Current visual weapon pose (HipFire/PointAim ignore optics).</summary>
	public WeaponPoseState WeaponPose;
	/// <summary>Baked pose spread multiplier (1 = Aiming baseline).</summary>
	public float PoseSpreadMultiplier;
	/// <summary>When true, optic attachments are excluded from distance dispersion.</summary>
	public bool ExcludeOpticAttachments;
}

/// <summary>
/// Разложение итогового конуса ошибки по факторам для debug/UI и балансировки.
/// </summary>
public readonly struct WeaponShotAccuracyContext
{
	public readonly float TargetDistanceMeters;
	public readonly float BaseDispersion;
	public readonly float AmmoSpreadModifier;
	public readonly float WeaponDistanceMultiplier;
	public readonly float AttachmentDistanceMultiplier;
	public readonly float RecoilMultiplier;
	public readonly float StanceMultiplier;
	public readonly float MovementMultiplier;
	public readonly float SkillMultiplier;
	public readonly float ConditionMultiplier;
	public readonly float AimCompletionMultiplier;
	public readonly float AimProgress01;
	public readonly WeaponAimMode SelectedAimMode;
	public readonly WeaponAimMode AimMode;
	public readonly WeaponAimMode EffectiveAimMode;
	public readonly WeaponFireMode SelectedFireMode;
	public readonly WeaponFireMode EffectiveFireMode;
	public readonly int BurstShotIndex;
	public readonly float AutoBurstSpreadMultiplier;
	public readonly float RawHalfAngleDegrees;
	public readonly float HalfAngleDegrees;
	public readonly float SpreadRadiusMeters;
	public readonly float SpreadDiameterMeters;

	public WeaponShotAccuracyContext(
		float _targetDistanceMeters,
		float _baseDispersion,
		float _ammoSpreadModifier,
		float _weaponDistanceMultiplier,
		float _attachmentDistanceMultiplier,
		float _recoilMultiplier,
		float _stanceMultiplier,
		float _movementMultiplier,
		float _skillMultiplier,
		float _conditionMultiplier,
		float _aimCompletionMultiplier,
		float _aimProgress01,
		WeaponAimMode _selectedAimMode,
		WeaponAimMode _aimMode,
		WeaponAimMode _effectiveAimMode,
		WeaponFireMode _selectedFireMode,
		WeaponFireMode _effectiveFireMode,
		int _burstShotIndex,
		float _autoBurstSpreadMultiplier,
		float _rawHalfAngleDegrees,
		float _halfAngleDegrees,
		float _spreadRadiusMeters,
		float _spreadDiameterMeters)
	{
		TargetDistanceMeters = _targetDistanceMeters;
		BaseDispersion = _baseDispersion;
		AmmoSpreadModifier = _ammoSpreadModifier;
		WeaponDistanceMultiplier = _weaponDistanceMultiplier;
		AttachmentDistanceMultiplier = _attachmentDistanceMultiplier;
		RecoilMultiplier = _recoilMultiplier;
		StanceMultiplier = _stanceMultiplier;
		MovementMultiplier = _movementMultiplier;
		SkillMultiplier = _skillMultiplier;
		ConditionMultiplier = _conditionMultiplier;
		AimCompletionMultiplier = _aimCompletionMultiplier;
		AimProgress01 = _aimProgress01;
		SelectedAimMode = _selectedAimMode;
		AimMode = _aimMode;
		EffectiveAimMode = _effectiveAimMode;
		SelectedFireMode = _selectedFireMode;
		EffectiveFireMode = _effectiveFireMode;
		BurstShotIndex = _burstShotIndex;
		AutoBurstSpreadMultiplier = _autoBurstSpreadMultiplier;
		RawHalfAngleDegrees = _rawHalfAngleDegrees;
		HalfAngleDegrees = _halfAngleDegrees;
		SpreadRadiusMeters = _spreadRadiusMeters;
		SpreadDiameterMeters = _spreadDiameterMeters;
	}
}

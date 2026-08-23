using UnityEngine;

/// <summary>
/// Bakes <see cref="WeaponPoseAutoCapabilityCache"/> once on equip.
/// HipFire / PointAim ignore Optic attachments for spread and aim-time; Aiming includes them.
/// </summary>
public static class WeaponPoseAutoCapabilityBaker
{
	public const float DefaultAcceptableHitRadiusMeters = 0.35f;
	public const float DefaultHipFireSpreadMult = WeaponPoseCombatModifiers.HipFireSpreadMultiplier;
	public const float DefaultPointAimSpreadMult = WeaponPoseCombatModifiers.PointAimSpreadMultiplier;
	public const float DefaultAimingSpreadMult = WeaponPoseCombatModifiers.AimingSpreadMultiplier;
	public const float DefaultPreAimSpreadMult = WeaponPoseCombatModifiers.PreAimSpreadMultiplier;
	public const float DefaultBaseSpreadToDegrees = 1f;
	public const float DefaultHipFirePreferredMeters = 6f;
	public const float DefaultPointAimPreferredMeters = 32f;
	private const float c_MaxScanMeters = 500f;
	private const float c_SampleStepMeters = 2f;

	public static WeaponPoseAutoCapabilityCache Bake(
		WeaponDefinition _weapon,
		WeaponAttachmentDefinition[] _attachments,
		UnitCombatStats _combatStats,
		UnitIndividualTraits _traits,
		UnitCombatCondition _condition,
		float _acceptableHitRadiusMeters = DefaultAcceptableHitRadiusMeters,
		float _baseSpreadToDegrees = DefaultBaseSpreadToDegrees,
		float _hipFireSpreadMult = DefaultHipFireSpreadMult,
		float _pointAimSpreadMult = DefaultPointAimSpreadMult,
		float _aimingSpreadMult = DefaultAimingSpreadMult)
	{
		bool hasLaser = WeaponLaserModifiers.HasLaserDesignator(_attachments);
		bool improvedLaser = hasLaser && WeaponLaserModifiers.HasImprovedLaser(_attachments);
		var cache = new WeaponPoseAutoCapabilityCache
		{
			IsValid = true,
			HasLaserDesignator = hasLaser,
			HasImprovedLaser = improvedLaser,
			HipFireSpreadMult = _hipFireSpreadMult,
			PointAimSpreadMult = _pointAimSpreadMult,
			AimingSpreadMult = _aimingSpreadMult,
			PreAimSpreadMult = DefaultPreAimSpreadMult,
			LaserPointAimSpreadMult = WeaponLaserModifiers.GetPointAimSpreadProduct(_attachments, 0f),
			LaserAimingAimTimeMult = WeaponLaserModifiers.GetAimingAimTimeProduct(_attachments),
			TransitionSeconds = WeaponPoseAutoCapabilityCache.BuildDefaultTransitionTable(),
		};

		float skillDisp = _combatStats != null ? _combatStats.GetDispersionMultiplier() : 1f;
		float traitsDisp = _traits != null ? _traits.GetDispersionMultiplier() : 1f;
		float conditionDisp = _condition != null ? _condition.GetDispersionMultiplier() : 1f;
		float unitDispProduct = skillDisp * traitsDisp * conditionDisp;

		float skillAim = _combatStats != null ? _combatStats.GetAimTimeMultiplier() : 1f;
		float traitsAim = _traits != null ? _traits.GetAimTimeMultiplier() : 1f;
		float conditionAim = _condition != null ? _condition.GetAimTimeMultiplier(false) : 1f;
		float unitAimProduct = skillAim * traitsAim * conditionAim;

		float baseDisp = _weapon != null ? Mathf.Max(0.01f, _weapon.BaseShotDispersion) : 1f;
		float baseAim = _weapon != null ? Mathf.Max(0.01f, _weapon.AimTimeSeconds) : 0.28f;

		// Flat attachment aim modifiers (optics excluded for hip/point)
		float aimFlatNoOptic = GetAttachmentAimTimeProduct(_attachments, _includeOptics: false);
		float aimFlatWithOptic = GetAttachmentAimTimeProduct(_attachments, _includeOptics: true);

		cache.HipFireAimTimeMult = Mathf.Max(0.05f, unitAimProduct * aimFlatNoOptic * 0.55f);
		cache.PointAimAimTimeMult = Mathf.Max(0.05f, unitAimProduct * aimFlatNoOptic * 0.85f);
		cache.AimingAimTimeMult = Mathf.Max(0.05f, unitAimProduct * aimFlatWithOptic * cache.LaserAimingAimTimeMult);
		cache.PreAimAimTimeMult = Mathf.Max(0.05f, unitAimProduct * aimFlatNoOptic * PreAimPoseUtility.AimTimeMult);

		float hitRadius = Mathf.Max(0.05f, _acceptableHitRadiusMeters);
		float maxScan = ResolveMaxScanMeters(_weapon);

		cache.HipFireMaxMeters = FindMaxAcceptableDistance(
			_weapon,
			_attachments,
			_includeOptics: false,
			baseDisp,
			unitDispProduct,
			_hipFireSpreadMult,
			_baseSpreadToDegrees,
			hitRadius,
			maxScan);

		cache.PointAimMaxMeters = Mathf.Max(
			cache.HipFireMaxMeters,
			FindMaxAcceptableDistance(
				_weapon,
				_attachments,
				_includeOptics: false,
				baseDisp,
				unitDispProduct,
				_pointAimSpreadMult,
				_baseSpreadToDegrees,
				hitRadius,
				maxScan,
				_applyLaserPointAim: true));

		float marksmanship01 = _combatStats != null
			? Mathf.Clamp01(_combatStats.Marksmanship / 100f)
			: 0.5f;
		cache.HipFirePreferredMeters = Mathf.Lerp(3.5f, 7f, marksmanship01);
		float pointBase = Mathf.Lerp(18f, 45f, marksmanship01);
		if (cache.HasImprovedLaser)
			pointBase += 12f;
		else if (cache.HasLaserDesignator)
			pointBase += 6f;
		cache.PointAimPreferredMeters = Mathf.Max(cache.HipFirePreferredMeters + 1f, pointBase);

		_ = baseAim;

		return cache;
	}

	public static bool HasImprovedLaser(WeaponAttachmentDefinition[] _attachments) =>
		WeaponLaserModifiers.HasImprovedLaser(_attachments);

	public static float EvaluateLaserPointAimSpreadMult(WeaponAttachmentDefinition[] _attachments) =>
		WeaponLaserModifiers.GetPointAimSpreadProduct(_attachments, 0f);

	public static float EvaluateLaserAimingAimTimeMult(WeaponAttachmentDefinition[] _attachments) =>
		WeaponLaserModifiers.GetAimingAimTimeProduct(_attachments);

	public static float ResolveMaxScanMeters(WeaponDefinition _weapon)
	{
		return _weapon != null
			? WeaponDamageRangeMath.MaxHitscanEnvelopeMeters
			: c_MaxScanMeters;
	}

	public static float FindMaxAcceptableDistance(
		WeaponDefinition _weapon,
		WeaponAttachmentDefinition[] _attachments,
		bool _includeOptics,
		float _baseDispersion,
		float _unitDispersionProduct,
		float _poseSpreadMult,
		float _baseSpreadToDegrees,
		float _hitRadiusMeters,
		float _maxScanMeters,
		bool _applyLaserPointAim = false)
	{
		float lastGood = 0f;
		float step = c_SampleStepMeters;
		for (float d = step; d <= _maxScanMeters; d += step)
		{
			float halfAngle = EstimateHalfAngleDegrees(
				_weapon,
				_attachments,
				_includeOptics,
				d,
				_baseDispersion,
				_unitDispersionProduct,
				_poseSpreadMult,
				_baseSpreadToDegrees,
				_applyLaserPointAim);
			float radius = d * Mathf.Tan(halfAngle * Mathf.Deg2Rad);
			if (radius <= _hitRadiusMeters)
				lastGood = d;
			else
				break;
		}

		return lastGood;
	}

	public static float EstimateHalfAngleDegrees(
		WeaponDefinition _weapon,
		WeaponAttachmentDefinition[] _attachments,
		bool _includeOptics,
		float _distanceMeters,
		float _baseDispersion,
		float _unitDispersionProduct,
		float _poseSpreadMult,
		float _baseSpreadToDegrees,
		bool _applyLaserPointAim = false)
	{
		float weaponDist = _weapon != null
			? Mathf.Max(0.01f, _weapon.GetDistanceDispersionMultiplier(_distanceMeters))
			: 1f;
		float attachDist = GetAttachmentDistanceDispersionProduct(_attachments, _distanceMeters, _includeOptics);
		float laser = _applyLaserPointAim
			? WeaponLaserModifiers.GetPointAimSpreadProduct(_attachments, _distanceMeters)
			: 1f;
		return Mathf.Max(
			0.01f,
			_baseDispersion * weaponDist * attachDist * _poseSpreadMult * _unitDispersionProduct * _baseSpreadToDegrees * laser);
	}

	public static bool HasAttachmentType(WeaponAttachmentDefinition[] _attachments, WeaponAttachmentType _type)
	{
		if (_attachments == null)
			return false;
		for (int i = 0; i < _attachments.Length; i++)
		{
			WeaponAttachmentDefinition a = _attachments[i];
			if (a != null && a.AttachmentType == _type)
				return true;
		}

		return false;
	}

	public static float GetAttachmentAimTimeProduct(WeaponAttachmentDefinition[] _attachments, bool _includeOptics)
	{
		if (_attachments == null || _attachments.Length == 0)
			return 1f;
		float product = 1f;
		for (int i = 0; i < _attachments.Length; i++)
		{
			WeaponAttachmentDefinition a = _attachments[i];
			if (a == null)
				continue;
			if (!_includeOptics && a.AttachmentType == WeaponAttachmentType.Optic)
				continue;
			product *= Mathf.Max(0.01f, a.AimTimeModifier);
		}

		return product;
	}

	public static float GetAttachmentDistanceDispersionProduct(
		WeaponAttachmentDefinition[] _attachments,
		float _distanceMeters,
		bool _includeOptics)
	{
		if (_attachments == null || _attachments.Length == 0)
			return 1f;
		float product = 1f;
		for (int i = 0; i < _attachments.Length; i++)
		{
			WeaponAttachmentDefinition a = _attachments[i];
			if (a == null)
				continue;
			if (!_includeOptics && a.AttachmentType == WeaponAttachmentType.Optic)
				continue;
			product *= Mathf.Max(0.01f, a.GetDistanceDispersionMultiplier(_distanceMeters));
		}

		return product;
	}

	public static WeaponAttachmentDefinition[] FilterAttachments(
		WeaponAttachmentDefinition[] _attachments,
		bool _includeOptics)
	{
		if (_attachments == null || _attachments.Length == 0)
			return _attachments;
		if (_includeOptics)
			return _attachments;

		int count = 0;
		for (int i = 0; i < _attachments.Length; i++)
		{
			WeaponAttachmentDefinition a = _attachments[i];
			if (a != null && a.AttachmentType != WeaponAttachmentType.Optic)
				count++;
		}

		if (count == _attachments.Length)
			return _attachments;

		var filtered = new WeaponAttachmentDefinition[count];
		int w = 0;
		for (int i = 0; i < _attachments.Length; i++)
		{
			WeaponAttachmentDefinition a = _attachments[i];
			if (a != null && a.AttachmentType != WeaponAttachmentType.Optic)
				filtered[w++] = a;
		}

		return filtered;
	}
}

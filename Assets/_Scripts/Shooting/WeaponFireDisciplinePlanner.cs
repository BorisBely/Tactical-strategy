using UnityEngine;

/// <summary>
/// Строит случайный, но реалистичный план огневой серии по дистанции, классу оружия,
/// выбранной дисциплине и индивидуальным отклонениям юнита.
/// </summary>
public static class WeaponFireDisciplinePlanner
{
	#region Public Methods
	public static WeaponFireDisciplinePlan CreatePlan(
		WeaponDefinition _weaponDefinition,
		WeaponFireMode _selectedFireMode,
		WeaponFireDisciplineMode _selectedDiscipline,
		float _targetDistanceMeters,
		UnitCombatStats _combatStats,
		UnitIndividualTraits _individualTraits)
	{
		WeaponFireMode[] availableModes = _weaponDefinition != null
			? _weaponDefinition.AvailableFireModes
			: null;
		WeaponClassType weaponClass = _weaponDefinition != null
			? _weaponDefinition.WeaponClass
			: WeaponClassType.Rifle;

		WeaponFireDisciplineMode effectiveDiscipline = ResolveEffectiveDiscipline(
			_selectedDiscipline,
			_targetDistanceMeters,
			weaponClass,
			_combatStats,
			_individualTraits);

		DisciplineBand band = ResolveBand(effectiveDiscipline, weaponClass, _targetDistanceMeters);
		ApplyUnitVariation(ref band, _combatStats, _individualTraits, effectiveDiscipline);

		int seriesShots = Random.Range(band.MinShots, band.MaxShots + 1);
		float pauseSeconds = Random.Range(band.MinPauseSeconds, band.MaxPauseSeconds);
		float requiredAim = Random.Range(band.MinAimProgress01, band.MaxAimProgress01);
		WeaponAimMode aimMode = WeaponFireDisciplineModeUtility.MapToAimMode(effectiveDiscipline, _targetDistanceMeters);
		requiredAim = Mathf.Max(requiredAim, WeaponAimModeUtility.GetBaseRequiredAimProgress01(aimMode, _targetDistanceMeters) * 0.85f);
		requiredAim = Mathf.Clamp01(requiredAim);

		WeaponFireMode effectiveFireMode = ResolveEffectiveFireMode(
			_selectedFireMode,
			availableModes,
			band.PreferredFireMode,
			seriesShots,
			_targetDistanceMeters,
			effectiveDiscipline,
			weaponClass);

		if (effectiveFireMode == WeaponFireMode.SemiAuto)
			seriesShots = Mathf.Clamp(seriesShots, 1, Mathf.Max(1, band.MaxShots));

		// Пулемёты не режем Burst-лимитом винтовки: серии длинные и идут через FullAuto.
		if (effectiveFireMode == WeaponFireMode.Burst &&
		    _weaponDefinition != null &&
		    weaponClass != WeaponClassType.LightMachineGun)
		{
			int weaponBurst = Mathf.Max(2, _weaponDefinition.BurstRounds);
			seriesShots = Mathf.Clamp(seriesShots, 2, Mathf.Max(2, weaponBurst + 1));
		}

		return new WeaponFireDisciplinePlan(
			_selectedDiscipline,
			effectiveDiscipline,
			effectiveFireMode,
			aimMode,
			requiredAim,
			seriesShots,
			pauseSeconds,
			_targetDistanceMeters);
	}
	#endregion

	#region Private Methods
	private static WeaponFireDisciplineMode ResolveEffectiveDiscipline(
		WeaponFireDisciplineMode _selected,
		float _distanceMeters,
		WeaponClassType _weaponClass,
		UnitCombatStats _combatStats,
		UnitIndividualTraits _individualTraits)
	{
		if (_selected != WeaponFireDisciplineMode.Auto)
			return _selected;

		float aggression = _individualTraits != null ? _individualTraits.FireAggressionModifier : 0f;
		float marksmanship = _combatStats != null ? _combatStats.Marksmanship : 50f;
		float distance = Mathf.Max(0f, _distanceMeters);

		if (_weaponClass == WeaponClassType.HeavyMachineGun ||
		    _weaponClass == WeaponClassType.AutomaticGrenadeLauncher)
		{
			return WeaponFireDisciplineMode.Suppressive;
		}

		if (_weaponClass == WeaponClassType.LightMachineGun)
		{
			// Пулемёты по умолчанию давят огнём: Suppressive почти на всех рабочих дистанциях.
			if (distance <= 140f)
				return WeaponFireDisciplineMode.Suppressive;
			if (distance <= 220f)
				return aggression < -0.06f
					? WeaponFireDisciplineMode.Precision
					: WeaponFireDisciplineMode.Suppressive;
			return aggression > 0f
				? WeaponFireDisciplineMode.Precision
				: WeaponFireDisciplineMode.Economical;
		}

		if (_weaponClass == WeaponClassType.Shotgun || _weaponClass == WeaponClassType.Pistol)
		{
			if (distance <= 20f && aggression > 0.03f)
				return WeaponFireDisciplineMode.Suppressive;
			return distance <= 35f ? WeaponFireDisciplineMode.Precision : WeaponFireDisciplineMode.Economical;
		}

		if (distance <= 25f)
		{
			if (aggression > 0.04f || marksmanship < 42f)
				return WeaponFireDisciplineMode.Suppressive;
			return WeaponFireDisciplineMode.Precision;
		}

		if (distance <= 70f)
		{
			if (aggression > 0.06f)
				return WeaponFireDisciplineMode.Suppressive;
			if (marksmanship >= 62f && aggression < 0f)
				return WeaponFireDisciplineMode.Economical;
			return WeaponFireDisciplineMode.Precision;
		}

		if (distance <= 140f)
		{
			if (aggression > 0.07f && marksmanship < 55f)
				return WeaponFireDisciplineMode.Precision;
			return marksmanship >= 58f
				? WeaponFireDisciplineMode.Economical
				: WeaponFireDisciplineMode.Precision;
		}

		return WeaponFireDisciplineMode.Economical;
	}

	private static DisciplineBand ResolveBand(
		WeaponFireDisciplineMode _discipline,
		WeaponClassType _weaponClass,
		float _distanceMeters)
	{
		float distance = Mathf.Max(0f, _distanceMeters);
		bool isLmg = _weaponClass == WeaponClassType.LightMachineGun;
		bool isHmg = _weaponClass == WeaponClassType.HeavyMachineGun;
		bool isAgl = _weaponClass == WeaponClassType.AutomaticGrenadeLauncher;

		switch (_discipline)
		{
			case WeaponFireDisciplineMode.Suppressive:
				if (isHmg || isAgl)
				{
					if (distance <= 30f)
						return Band(25, 40, 0.06f, 0.14f, 0.12f, 0.25f, WeaponFireMode.FullAuto);
					if (distance <= 80f)
						return Band(20, 35, 0.08f, 0.20f, 0.18f, 0.35f, WeaponFireMode.FullAuto);
					if (distance <= 140f)
						return Band(15, 30, 0.12f, 0.28f, 0.25f, 0.45f, WeaponFireMode.FullAuto);
					return Band(10, 20, 0.20f, 0.40f, 0.35f, 0.60f, WeaponFireMode.FullAuto);
				}
				if (distance <= 30f)
					return Band(isLmg ? 8 : 3, isLmg ? 16 : 7, isLmg ? 0.10f : 0.18f, isLmg ? 0.28f : 0.45f, 0.28f, 0.52f, WeaponFireMode.FullAuto);
				if (distance <= 80f)
					return Band(isLmg ? 6 : 3, isLmg ? 14 : 5, isLmg ? 0.14f : 0.28f, isLmg ? 0.40f : 0.65f, 0.40f, 0.68f, WeaponFireMode.FullAuto);
				if (distance <= 140f)
					return Band(isLmg ? 5 : 2, isLmg ? 12 : 4, isLmg ? 0.22f : 0.45f, isLmg ? 0.55f : 0.95f, 0.55f, 0.82f, WeaponFireMode.FullAuto);
				return Band(isLmg ? 3 : 1, isLmg ? 8 : 2, isLmg ? 0.35f : 0.70f, isLmg ? 0.85f : 1.40f, 0.75f, 1.00f, isLmg ? WeaponFireMode.FullAuto : WeaponFireMode.SemiAuto);

			case WeaponFireDisciplineMode.Economical:
				if (isHmg || isAgl)
				{
					if (distance <= 30f)
						return Band(3, 5, 0.35f, 0.70f, 0.30f, 0.55f, WeaponFireMode.FullAuto);
					if (distance <= 80f)
						return Band(2, 4, 0.50f, 1.00f, 0.45f, 0.70f, WeaponFireMode.FullAuto);
					if (distance <= 140f)
						return Band(2, 3, 0.70f, 1.40f, 0.60f, 0.85f, WeaponFireMode.FullAuto);
					return Band(1, 2, 1.00f, 2.00f, 0.80f, 1.00f, WeaponFireMode.FullAuto);
				}
				if (distance <= 30f)
					return Band(isLmg ? 2 : 1, isLmg ? 4 : 2, 0.45f, 0.90f, 0.70f, 0.95f, isLmg ? WeaponFireMode.Burst : WeaponFireMode.SemiAuto);
				if (distance <= 80f)
					return Band(isLmg ? 2 : 1, isLmg ? 3 : 2, 0.60f, 1.20f, 0.85f, 1.00f, isLmg ? WeaponFireMode.Burst : WeaponFireMode.SemiAuto);
				if (distance <= 140f)
					return Band(1, isLmg ? 2 : 1, 0.85f, 1.60f, 0.95f, 1.00f, WeaponFireMode.SemiAuto);
				return Band(1, 1, 1.00f, 2.00f, 1.00f, 1.00f, WeaponFireMode.SemiAuto);

			default:
				if (isHmg || isAgl)
				{
					if (distance <= 30f)
						return Band(6, 10, 0.14f, 0.28f, 0.20f, 0.38f, WeaponFireMode.FullAuto);
					if (distance <= 80f)
						return Band(5, 8, 0.20f, 0.40f, 0.28f, 0.50f, WeaponFireMode.FullAuto);
					if (distance <= 140f)
						return Band(4, 7, 0.28f, 0.55f, 0.40f, 0.65f, WeaponFireMode.FullAuto);
					return Band(3, 5, 0.40f, 0.80f, 0.55f, 0.85f, WeaponFireMode.FullAuto);
				}
				if (distance <= 30f)
					return Band(isLmg ? 5 : 2, isLmg ? 10 : 4, isLmg ? 0.18f : 0.28f, isLmg ? 0.42f : 0.60f, 0.38f, 0.65f, isLmg ? WeaponFireMode.FullAuto : WeaponFireMode.Burst);
				if (distance <= 80f)
					return Band(isLmg ? 4 : 2, isLmg ? 8 : 3, isLmg ? 0.25f : 0.40f, isLmg ? 0.55f : 0.85f, 0.50f, 0.80f, isLmg ? WeaponFireMode.FullAuto : WeaponFireMode.Burst);
				if (distance <= 140f)
					return Band(isLmg ? 3 : 1, isLmg ? 6 : 2, isLmg ? 0.35f : 0.55f, isLmg ? 0.75f : 1.15f, 0.70f, 0.95f, isLmg ? WeaponFireMode.FullAuto : WeaponFireMode.SemiAuto);
				return Band(isLmg ? 2 : 1, isLmg ? 4 : 2, isLmg ? 0.50f : 0.80f, isLmg ? 1.10f : 1.50f, 0.85f, 1.00f, isLmg ? WeaponFireMode.Burst : WeaponFireMode.SemiAuto);
		}
	}

	private static void ApplyUnitVariation(
		ref DisciplineBand _band,
		UnitCombatStats _combatStats,
		UnitIndividualTraits _individualTraits,
		WeaponFireDisciplineMode _discipline)
	{
		float aggression = _individualTraits != null ? _individualTraits.FireAggressionModifier : 0f;
		float cadence = _individualTraits != null ? _individualTraits.FireCadenceModifier : 0f;
		float marksmanship = _combatStats != null ? _combatStats.Marksmanship : 50f;
		float recoilControl = _combatStats != null ? _combatStats.RecoilControl : 50f;

		float skill = (marksmanship + recoilControl) * 0.5f;
		float skillNorm = Mathf.InverseLerp(30f, 75f, skill);

		if (_discipline == WeaponFireDisciplineMode.Suppressive)
		{
			_band.MaxShots += aggression > 0.03f ? 1 : 0;
			_band.MinPauseSeconds *= 1f - aggression * 0.8f;
			_band.MaxPauseSeconds *= 1f - aggression * 0.6f;
		}
		else if (_discipline == WeaponFireDisciplineMode.Economical)
		{
			_band.MaxShots = Mathf.Max(_band.MinShots, _band.MaxShots - (skillNorm > 0.55f ? 0 : 0));
			_band.MinPauseSeconds *= 1f + (1f - skillNorm) * 0.15f + cadence * 0.5f;
			_band.MaxPauseSeconds *= 1f + (1f - skillNorm) * 0.2f + cadence * 0.5f;
			_band.MinAimProgress01 = Mathf.Clamp01(_band.MinAimProgress01 + skillNorm * 0.05f);
			_band.MaxAimProgress01 = Mathf.Clamp01(_band.MaxAimProgress01 + skillNorm * 0.05f);
		}
		else
		{
			if (skillNorm > 0.65f)
				_band.MaxShots = Mathf.Max(_band.MinShots, _band.MaxShots - 1);
			if (aggression > 0.05f)
				_band.MaxShots += 1;
			_band.MinPauseSeconds *= 1f + cadence * 0.7f - aggression * 0.5f;
			_band.MaxPauseSeconds *= 1f + cadence * 0.7f - aggression * 0.4f;
		}

		_band.MinShots = Mathf.Max(1, _band.MinShots);
		_band.MaxShots = Mathf.Max(_band.MinShots, _band.MaxShots);
		_band.MinPauseSeconds = Mathf.Max(0.08f, _band.MinPauseSeconds);
		_band.MaxPauseSeconds = Mathf.Max(_band.MinPauseSeconds, _band.MaxPauseSeconds);
		_band.MinAimProgress01 = Mathf.Clamp01(_band.MinAimProgress01);
		_band.MaxAimProgress01 = Mathf.Max(_band.MinAimProgress01, Mathf.Clamp01(_band.MaxAimProgress01));
	}

	private static WeaponFireMode ResolveEffectiveFireMode(
		WeaponFireMode _selectedFireMode,
		WeaponFireMode[] _availableModes,
		WeaponFireMode _preferred,
		int _seriesShots,
		float _distanceMeters,
		WeaponFireDisciplineMode _discipline,
		WeaponClassType _weaponClass)
	{
		if (_selectedFireMode != WeaponFireMode.Auto)
		{
			return WeaponFireModeUtility.ResolveEffectiveMode(
				_selectedFireMode,
				_distanceMeters,
				_availableModes);
		}

		WeaponFireMode desired = _preferred;
		bool isLmg = _weaponClass == WeaponClassType.LightMachineGun;

		if (isLmg && _seriesShots >= 3 && WeaponFireModeUtility.IsModeSupported(WeaponFireMode.FullAuto, _availableModes))
			desired = WeaponFireMode.FullAuto;
		else if (_seriesShots <= 1)
			desired = WeaponFireMode.SemiAuto;
		else if (_discipline == WeaponFireDisciplineMode.Suppressive && _seriesShots >= 4)
			desired = WeaponFireMode.FullAuto;
		else if (_seriesShots <= 3 && WeaponFireModeUtility.IsModeSupported(WeaponFireMode.Burst, _availableModes))
			desired = WeaponFireMode.Burst;

		if (!WeaponFireModeUtility.IsModeSupported(desired, _availableModes))
		{
			desired = WeaponFireModeUtility.ResolveEffectiveMode(
				desired,
				_distanceMeters,
				_availableModes);
		}

		return desired;
	}

	private static DisciplineBand Band(
		int _minShots,
		int _maxShots,
		float _minPause,
		float _maxPause,
		float _minAim,
		float _maxAim,
		WeaponFireMode _preferred)
	{
		return new DisciplineBand
		{
			MinShots = _minShots,
			MaxShots = _maxShots,
			MinPauseSeconds = _minPause,
			MaxPauseSeconds = _maxPause,
			MinAimProgress01 = _minAim,
			MaxAimProgress01 = _maxAim,
			PreferredFireMode = _preferred
		};
	}
	#endregion

	#region Nested Types
	private struct DisciplineBand
	{
		public int MinShots;
		public int MaxShots;
		public float MinPauseSeconds;
		public float MaxPauseSeconds;
		public float MinAimProgress01;
		public float MaxAimProgress01;
		public WeaponFireMode PreferredFireMode;
	}
	#endregion
}

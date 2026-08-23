using UnityEngine;

/// <summary>
/// Stage 11: план серии по профилю класса и нормализованной дистанции.
/// Не запрещает огонь. Не читает VisionRange. Не стреляет сам.
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
		return CreatePlan(
			_weaponDefinition,
			_selectedFireMode,
			_selectedDiscipline,
			_targetDistanceMeters,
			_combatStats,
			_individualTraits,
			null,
			false);
	}

	public static WeaponFireDisciplinePlan CreatePlan(
		WeaponDefinition _weaponDefinition,
		WeaponFireMode _selectedFireMode,
		WeaponFireDisciplineMode _selectedDiscipline,
		float _targetDistanceMeters,
		UnitCombatStats _combatStats,
		UnitIndividualTraits _individualTraits,
		WeaponFireDisciplineDistanceBand? _previousBand,
		bool _deterministic)
	{
		WeaponFireMode[] availableModes = _weaponDefinition != null
			? _weaponDefinition.AvailableFireModes
			: null;
		WeaponFireDisciplineProfileKind profile = WeaponFireDisciplineProfile.ResolveKind(_weaponDefinition);
		float workingRange = WeaponFireDisciplineProfile.GetWorkingRangeMeters(profile);
		float normalized = WeaponFireDisciplineProfile.NormalizeDistance(_targetDistanceMeters, workingRange);
		WeaponFireDisciplineDistanceBand bandKind =
			WeaponFireDisciplineProfile.ResolveBand(normalized, _previousBand);

		WeaponFireDisciplineMode effectiveDiscipline = ResolveEffectiveDiscipline(
			_selectedDiscipline,
			profile,
			bandKind,
			_combatStats,
			_individualTraits);

		DisciplineBand band = ResolveBand(profile, effectiveDiscipline, bandKind);
		ApplyUnitVariation(ref band, _combatStats, _individualTraits, effectiveDiscipline, profile);

		int seriesShots = PickInt(band.MinShots, band.MaxShots, _deterministic);
		float pauseSeconds = PickFloat(band.MinPauseSeconds, band.MaxPauseSeconds, _deterministic);
		float requiredAim = PickFloat(band.MinAimProgress01, band.MaxAimProgress01, _deterministic);
		WeaponAimMode aimMode = WeaponFireDisciplineModeUtility.MapToAimModeFromNormalized(
			effectiveDiscipline,
			normalized);
		requiredAim = Mathf.Max(
			requiredAim,
			WeaponAimModeUtility.GetBaseRequiredAimProgress01(aimMode, _targetDistanceMeters));
		requiredAim = Mathf.Clamp01(requiredAim);

		WeaponFireMode effectiveFireMode = ResolveEffectiveFireMode(
			_selectedFireMode,
			availableModes,
			band.PreferredFireMode,
			seriesShots,
			_targetDistanceMeters,
			effectiveDiscipline,
			profile);

		if (profile == WeaponFireDisciplineProfileKind.Sniper ||
		    profile == WeaponFireDisciplineProfileKind.Marksman)
		{
			effectiveFireMode = ForceSemiIfSupported(effectiveFireMode, availableModes);
			seriesShots = Mathf.Clamp(seriesShots, 1, profile == WeaponFireDisciplineProfileKind.Sniper ? 1 : 2);
		}

		if (effectiveFireMode == WeaponFireMode.SemiAuto)
			seriesShots = Mathf.Clamp(seriesShots, 1, Mathf.Max(1, band.MaxShots));

		if (effectiveFireMode == WeaponFireMode.Burst &&
		    _weaponDefinition != null &&
		    profile != WeaponFireDisciplineProfileKind.Lmg &&
		    profile != WeaponFireDisciplineProfileKind.Heavy &&
		    profile != WeaponFireDisciplineProfileKind.Grenade)
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
			_targetDistanceMeters,
			profile,
			bandKind,
			normalized,
			workingRange);
	}
	#endregion

	#region Private Methods
	private static WeaponFireDisciplineMode ResolveEffectiveDiscipline(
		WeaponFireDisciplineMode _selected,
		WeaponFireDisciplineProfileKind _profile,
		WeaponFireDisciplineDistanceBand _band,
		UnitCombatStats _combatStats,
		UnitIndividualTraits _individualTraits)
	{
		if (_selected != WeaponFireDisciplineMode.Auto)
			return _selected;

		float aggression = _individualTraits != null ? _individualTraits.FireAggressionModifier : 0f;
		float marksmanship = _combatStats != null ? _combatStats.Marksmanship : 50f;

		if (_profile == WeaponFireDisciplineProfileKind.Heavy ||
		    _profile == WeaponFireDisciplineProfileKind.Grenade)
			return WeaponFireDisciplineMode.Suppressive;

		if (_profile == WeaponFireDisciplineProfileKind.Sniper)
			return _band >= WeaponFireDisciplineDistanceBand.Far
				? WeaponFireDisciplineMode.Economical
				: WeaponFireDisciplineMode.Precision;

		if (_profile == WeaponFireDisciplineProfileKind.Marksman)
		{
			if (_band >= WeaponFireDisciplineDistanceBand.Far)
				return WeaponFireDisciplineMode.Economical;
			return WeaponFireDisciplineMode.Precision;
		}

		if (_profile == WeaponFireDisciplineProfileKind.Lmg)
		{
			if (_band <= WeaponFireDisciplineDistanceBand.Mid)
				return WeaponFireDisciplineMode.Suppressive;
			if (_band == WeaponFireDisciplineDistanceBand.Far)
				return aggression < -0.06f
					? WeaponFireDisciplineMode.Precision
					: WeaponFireDisciplineMode.Suppressive;
			return aggression > 0f
				? WeaponFireDisciplineMode.Precision
				: WeaponFireDisciplineMode.Economical;
		}

		if (_profile == WeaponFireDisciplineProfileKind.Shotgun)
		{
			if (_band == WeaponFireDisciplineDistanceBand.Close && aggression > 0.03f)
				return WeaponFireDisciplineMode.Suppressive;
			return _band <= WeaponFireDisciplineDistanceBand.Near
				? WeaponFireDisciplineMode.Precision
				: WeaponFireDisciplineMode.Economical;
		}

		if (_band == WeaponFireDisciplineDistanceBand.Close)
		{
			if (aggression > 0.04f || marksmanship < 42f || _profile == WeaponFireDisciplineProfileKind.Cqb)
				return WeaponFireDisciplineMode.Suppressive;
			return WeaponFireDisciplineMode.Precision;
		}

		if (_band == WeaponFireDisciplineDistanceBand.Near)
		{
			if (aggression > 0.06f)
				return WeaponFireDisciplineMode.Suppressive;
			if (marksmanship >= 62f && aggression < 0f)
				return WeaponFireDisciplineMode.Economical;
			return WeaponFireDisciplineMode.Precision;
		}

		if (_band == WeaponFireDisciplineDistanceBand.Mid)
			return marksmanship >= 58f && _profile != WeaponFireDisciplineProfileKind.Cqb
				? WeaponFireDisciplineMode.Economical
				: WeaponFireDisciplineMode.Precision;

		return WeaponFireDisciplineMode.Economical;
	}

	private static DisciplineBand ResolveBand(
		WeaponFireDisciplineProfileKind _profile,
		WeaponFireDisciplineMode _discipline,
		WeaponFireDisciplineDistanceBand _band)
	{
		if (_profile == WeaponFireDisciplineProfileKind.Sniper)
			return SniperBand(_band);
		if (_profile == WeaponFireDisciplineProfileKind.Marksman)
			return MarksmanBand(_band);
		if (_profile == WeaponFireDisciplineProfileKind.Lmg ||
		    _profile == WeaponFireDisciplineProfileKind.Heavy ||
		    _profile == WeaponFireDisciplineProfileKind.Grenade)
			return SupportBand(_profile, _discipline, _band);
		if (_profile == WeaponFireDisciplineProfileKind.Shotgun)
			return ShotgunBand(_discipline, _band);
		if (_profile == WeaponFireDisciplineProfileKind.Cqb)
			return CqbBand(_discipline, _band);
		return AssaultBand(_discipline, _band);
	}

	private static DisciplineBand CqbBand(
		WeaponFireDisciplineMode _discipline,
		WeaponFireDisciplineDistanceBand _band)
	{
		bool suppress = _discipline == WeaponFireDisciplineMode.Suppressive;
		bool economy = _discipline == WeaponFireDisciplineMode.Economical;
		return _band switch
		{
			WeaponFireDisciplineDistanceBand.Close => Band(
				suppress ? 5 : 3, suppress ? 8 : 5,
				suppress ? 0.12f : 0.18f, suppress ? 0.32f : 0.45f,
				0.35f, 0.55f, WeaponFireMode.FullAuto),
			WeaponFireDisciplineDistanceBand.Near => Band(
				suppress ? 4 : 3, suppress ? 6 : 5,
				0.20f, 0.50f, 0.45f, 0.68f, WeaponFireMode.FullAuto),
			WeaponFireDisciplineDistanceBand.Mid => Band(
				2, economy ? 3 : 4,
				0.35f, 0.70f, 0.60f, 0.85f, WeaponFireMode.Burst),
			WeaponFireDisciplineDistanceBand.Far => Band(
				1, 2, 0.55f, 1.00f, 0.80f, 0.95f, WeaponFireMode.SemiAuto),
			_ => Band(1, 1, 0.80f, 1.40f, 0.90f, 1.00f, WeaponFireMode.SemiAuto)
		};
	}

	private static DisciplineBand AssaultBand(
		WeaponFireDisciplineMode _discipline,
		WeaponFireDisciplineDistanceBand _band)
	{
		bool suppress = _discipline == WeaponFireDisciplineMode.Suppressive;
		bool economy = _discipline == WeaponFireDisciplineMode.Economical;
		return _band switch
		{
			WeaponFireDisciplineDistanceBand.Close => Band(
				suppress ? 4 : 3, suppress ? 6 : 5,
				0.22f, 0.48f, 0.45f, 0.68f,
				suppress ? WeaponFireMode.FullAuto : WeaponFireMode.Burst),
			WeaponFireDisciplineDistanceBand.Near => Band(
				3, suppress ? 5 : 4,
				0.30f, 0.60f, 0.55f, 0.78f, WeaponFireMode.Burst),
			WeaponFireDisciplineDistanceBand.Mid => Band(
				2, economy ? 2 : 3,
				0.40f, 0.80f, 0.65f, 0.88f, WeaponFireMode.Burst),
			WeaponFireDisciplineDistanceBand.Far => Band(
				1, 2, 0.70f, 1.30f, 0.82f, 0.98f, WeaponFireMode.SemiAuto),
			_ => Band(1, 1, 1.00f, 1.70f, 0.95f, 1.00f, WeaponFireMode.SemiAuto)
		};
	}

	private static DisciplineBand SupportBand(
		WeaponFireDisciplineProfileKind _profile,
		WeaponFireDisciplineMode _discipline,
		WeaponFireDisciplineDistanceBand _band)
	{
		bool heavy = _profile != WeaponFireDisciplineProfileKind.Lmg;
		bool economy = _discipline == WeaponFireDisciplineMode.Economical;
		int extra = heavy ? 4 : 0;
		return _band switch
		{
			WeaponFireDisciplineDistanceBand.Close => Band(
				8 + extra, 16 + extra, 0.10f, 0.28f, 0.28f, 0.52f, WeaponFireMode.FullAuto),
			WeaponFireDisciplineDistanceBand.Near => Band(
				6 + extra, 14 + extra, 0.14f, 0.40f, 0.40f, 0.68f, WeaponFireMode.FullAuto),
			WeaponFireDisciplineDistanceBand.Mid => Band(
				5 + extra, 12 + extra, 0.22f, 0.55f, 0.55f, 0.82f, WeaponFireMode.FullAuto),
			WeaponFireDisciplineDistanceBand.Far => Band(
				economy ? 3 : 4, economy ? 6 : 8 + extra / 2,
				0.35f, 0.75f, 0.70f, 0.92f, WeaponFireMode.FullAuto),
			_ => Band(
				3, 6 + extra / 2, 0.50f, 1.00f, 0.85f, 1.00f,
				economy ? WeaponFireMode.Burst : WeaponFireMode.FullAuto)
		};
	}

	private static DisciplineBand ShotgunBand(
		WeaponFireDisciplineMode _discipline,
		WeaponFireDisciplineDistanceBand _band)
	{
		bool suppress = _discipline == WeaponFireDisciplineMode.Suppressive;
		return _band switch
		{
			WeaponFireDisciplineDistanceBand.Close => Band(
				suppress ? 3 : 2, suppress ? 5 : 3,
				0.18f, 0.40f, 0.35f, 0.60f, WeaponFireMode.SemiAuto),
			WeaponFireDisciplineDistanceBand.Near => Band(
				1, 2, 0.30f, 0.65f, 0.50f, 0.75f, WeaponFireMode.SemiAuto),
			_ => Band(1, 1, 0.55f, 1.20f, 0.70f, 1.00f, WeaponFireMode.SemiAuto)
		};
	}

	private static DisciplineBand MarksmanBand(WeaponFireDisciplineDistanceBand _band)
	{
		return _band switch
		{
			WeaponFireDisciplineDistanceBand.Close => Band(1, 2, 0.55f, 1.00f, 0.70f, 0.88f, WeaponFireMode.SemiAuto),
			WeaponFireDisciplineDistanceBand.Near => Band(1, 2, 0.70f, 1.20f, 0.78f, 0.92f, WeaponFireMode.SemiAuto),
			WeaponFireDisciplineDistanceBand.Mid => Band(1, 1, 0.90f, 1.50f, 0.85f, 0.98f, WeaponFireMode.SemiAuto),
			WeaponFireDisciplineDistanceBand.Far => Band(1, 1, 1.10f, 1.80f, 0.92f, 1.00f, WeaponFireMode.SemiAuto),
			_ => Band(1, 1, 1.30f, 2.10f, 1.00f, 1.00f, WeaponFireMode.SemiAuto)
		};
	}

	private static DisciplineBand SniperBand(WeaponFireDisciplineDistanceBand _band)
	{
		return _band switch
		{
			WeaponFireDisciplineDistanceBand.Close => Band(1, 1, 0.90f, 1.40f, 0.85f, 0.95f, WeaponFireMode.SemiAuto),
			WeaponFireDisciplineDistanceBand.Near => Band(1, 1, 1.10f, 1.70f, 0.90f, 0.98f, WeaponFireMode.SemiAuto),
			WeaponFireDisciplineDistanceBand.Mid => Band(1, 1, 1.30f, 1.90f, 0.94f, 1.00f, WeaponFireMode.SemiAuto),
			WeaponFireDisciplineDistanceBand.Far => Band(1, 1, 1.50f, 2.20f, 0.98f, 1.00f, WeaponFireMode.SemiAuto),
			_ => Band(1, 1, 1.80f, 2.50f, 1.00f, 1.00f, WeaponFireMode.SemiAuto)
		};
	}

	private static void ApplyUnitVariation(
		ref DisciplineBand _band,
		UnitCombatStats _combatStats,
		UnitIndividualTraits _individualTraits,
		WeaponFireDisciplineMode _discipline,
		WeaponFireDisciplineProfileKind _profile)
	{
		float aggression = _individualTraits != null ? _individualTraits.FireAggressionModifier : 0f;
		float cadence = _individualTraits != null ? _individualTraits.FireCadenceModifier : 0f;
		float marksmanship = _combatStats != null ? _combatStats.Marksmanship : 50f;
		float recoilControl = _combatStats != null ? _combatStats.RecoilControl : 50f;
		float skillNorm = Mathf.InverseLerp(30f, 75f, (marksmanship + recoilControl) * 0.5f);

		if (_profile == WeaponFireDisciplineProfileKind.Sniper)
		{
			_band.MinPauseSeconds *= 1f + cadence * 0.4f;
			_band.MaxPauseSeconds *= 1f + cadence * 0.4f;
		}
		else if (_discipline == WeaponFireDisciplineMode.Suppressive)
		{
			_band.MaxShots += aggression > 0.03f ? 1 : 0;
			_band.MinPauseSeconds *= 1f - aggression * 0.8f;
			_band.MaxPauseSeconds *= 1f - aggression * 0.6f;
		}
		else if (_discipline == WeaponFireDisciplineMode.Economical)
		{
			_band.MinPauseSeconds *= 1f + (1f - skillNorm) * 0.15f + cadence * 0.5f;
			_band.MaxPauseSeconds *= 1f + (1f - skillNorm) * 0.2f + cadence * 0.5f;
			_band.MinAimProgress01 = Mathf.Clamp01(_band.MinAimProgress01 + skillNorm * 0.05f);
			_band.MaxAimProgress01 = Mathf.Clamp01(_band.MaxAimProgress01 + skillNorm * 0.05f);
		}
		else
		{
			if (skillNorm > 0.65f && _profile != WeaponFireDisciplineProfileKind.Lmg)
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
		float _targetDistanceMeters,
		WeaponFireDisciplineMode _discipline,
		WeaponFireDisciplineProfileKind _profile)
	{
		if (_selectedFireMode != WeaponFireMode.Auto)
		{
			return WeaponFireModeUtility.ResolveEffectiveMode(
				_selectedFireMode,
				_targetDistanceMeters,
				_availableModes);
		}

		WeaponFireMode desired = _preferred;
		bool support = _profile == WeaponFireDisciplineProfileKind.Lmg ||
		               _profile == WeaponFireDisciplineProfileKind.Heavy ||
		               _profile == WeaponFireDisciplineProfileKind.Grenade;

		if (support && _seriesShots >= 3 &&
		    WeaponFireModeUtility.IsModeSupported(WeaponFireMode.FullAuto, _availableModes))
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
				_targetDistanceMeters,
				_availableModes);
		}

		return desired;
	}

	private static WeaponFireMode ForceSemiIfSupported(WeaponFireMode _desired, WeaponFireMode[] _available)
	{
		if (WeaponFireModeUtility.IsModeSupported(WeaponFireMode.SemiAuto, _available))
			return WeaponFireMode.SemiAuto;
		return _desired;
	}

	private static int PickInt(int _min, int _max, bool _deterministic)
	{
		if (_max < _min)
			_max = _min;
		if (_deterministic)
			return (_min + _max) / 2;
		return Random.Range(_min, _max + 1);
	}

	private static float PickFloat(float _min, float _max, bool _deterministic)
	{
		if (_max < _min)
			_max = _min;
		if (_deterministic)
			return (_min + _max) * 0.5f;
		return Random.Range(_min, _max);
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

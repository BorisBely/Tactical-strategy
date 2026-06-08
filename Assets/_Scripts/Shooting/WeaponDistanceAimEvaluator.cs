using UnityEngine;

/// <summary>
/// Единый расчёт дистанционных множителей и качества для UI-графиков и боевой логики.
/// Формулы: Assets/Docs/CombatBalance/OpticDistanceBalance.md
/// </summary>
public static class WeaponDistanceAimEvaluator
{
	#region Constants
	private const float c_RecoilGraphRecoveryWhileFiringMultiplier = 0.45f;
	#endregion

	#region Public Methods
	public static float GetDistanceDispersionMultiplier(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		float _distanceMeters)
	{
		float multiplier = 1f;
		if (_weaponDefinition != null)
			multiplier *= Mathf.Max(0.01f, _weaponDefinition.GetDistanceDispersionMultiplier(_distanceMeters));

		ApplyAttachmentDistanceMultipliers(_attachments, _distanceMeters, ref multiplier, _aimTime: false);
		return Mathf.Max(0.01f, multiplier);
	}

	public static float GetDistanceAimTimeMultiplier(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		float _distanceMeters)
	{
		float multiplier = 1f;
		if (_weaponDefinition != null)
			multiplier *= Mathf.Max(0.01f, _weaponDefinition.GetDistanceAimTimeMultiplier(_distanceMeters));

		ApplyAttachmentDistanceMultipliers(_attachments, _distanceMeters, ref multiplier, _aimTime: true);
		return Mathf.Max(0.01f, multiplier);
	}

	/// <summary>
	/// Полное время прицеливания: база оружия × дистанционные кривые оружия и модулей × плоский AimTimeModifier модулей.
	/// </summary>
	public static float GetRequiredAimTimeSeconds(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		float _distanceMeters)
	{
		if (_weaponDefinition == null)
			return 0.25f;

		float aimTimeSeconds = _weaponDefinition.AimTimeSeconds;
		aimTimeSeconds *= GetDistanceAimTimeMultiplier(_weaponDefinition, _attachments, _distanceMeters);
		return Mathf.Max(0.01f, aimTimeSeconds);
	}

	public static float EvaluateAccuracyQuality(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		float _distanceMeters)
	{
		return 1f / GetDistanceDispersionMultiplier(_weaponDefinition, _attachments, _distanceMeters);
	}

	public static float EvaluateAimSpeedQuality(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		float _distanceMeters)
	{
		return 1f / GetDistanceAimTimeMultiplier(_weaponDefinition, _attachments, _distanceMeters);
	}

	public static float EvaluateAccuracyQuality(WeaponRuntimeState _weaponState, float _distanceMeters)
	{
		if (_weaponState == null || _weaponState.WeaponDefinition == null)
			return 1f;

		return EvaluateAccuracyQuality(
			_weaponState.WeaponDefinition,
			_weaponState.EquippedAttachments,
			_distanceMeters);
	}

	public static float EvaluateAimSpeedQuality(WeaponRuntimeState _weaponState, float _distanceMeters)
	{
		if (_weaponState == null || _weaponState.WeaponDefinition == null)
			return 1f;

		return EvaluateAimSpeedQuality(
			_weaponState.WeaponDefinition,
			_weaponState.EquippedAttachments,
			_distanceMeters);
	}

	/// <summary>
	/// Базовый контроль отдачи для UI-графика: 1 / (RecoilPerShot оружия × ∏ RecoilModifier модулей).
	/// Не зависит от дистанции и не учитывает накопление серии.
	/// </summary>
	public static float EvaluateRecoilControlQuality(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments)
	{
		return EvaluateRecoilControlQuality(_weaponDefinition, _attachments, WeaponFireMode.FullAuto);
	}

	public static float EvaluateRecoilControlQuality(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		WeaponFireMode _fireMode)
	{
		float recoilAccumulation = _weaponDefinition != null
			? Mathf.Max(0.01f, _weaponDefinition.RecoilPerShot)
			: 1f;
		recoilAccumulation *= GetAttachmentRecoilProduct(_attachments, _fireMode);
		return 1f / Mathf.Max(0.01f, recoilAccumulation);
	}

	/// <summary>
	/// Контроль отдачи по мере продолжения очереди. Ось X графика трактуется как номер выстрела.
	/// Модель повторяет runtime-накопление: между выстрелами отдача частично восстанавливается.
	/// Дробный номер выстрела сглаживается интерполяцией между соседними целыми выстрелами.
	/// </summary>
	public static float EvaluateSustainedRecoilControlQuality(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		float _shotIndex)
	{
		if (_weaponDefinition == null)
			return 1f;

		float shotIndex = Mathf.Max(1f, _shotIndex);
		int shotFloor = Mathf.FloorToInt(shotIndex);
		int shotCeil = Mathf.CeilToInt(shotIndex);
		float qualityFloor = EvaluateSustainedRecoilControlQualityAtIntegerShot(
			_weaponDefinition,
			_attachments,
			shotFloor);
		if (shotCeil <= shotFloor)
			return qualityFloor;

		float qualityCeil = EvaluateSustainedRecoilControlQualityAtIntegerShot(
			_weaponDefinition,
			_attachments,
			shotCeil);
		return Mathf.Lerp(qualityFloor, qualityCeil, shotIndex - shotFloor);
	}

	private static float EvaluateSustainedRecoilControlQualityAtIntegerShot(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		int _shotIndex)
	{
		int shotIndex = Mathf.Max(1, _shotIndex);
		float attachmentRecoilProduct = GetAttachmentRecoilProduct(_attachments, WeaponFireMode.FullAuto);
		float recoilAdded = WeaponDefinition.ComputeAddedRecoilPenalty(
			_weaponDefinition,
			WeaponFireMode.FullAuto,
			ammoDefinition: null,
			attachmentRecoilModifier: attachmentRecoilProduct);
		if (recoilAdded <= 0f)
			return 1f;

		float fireIntervalSeconds = 60f / Mathf.Max(1f, _weaponDefinition.FireRateRpm);
		float recoveryPerShot = _weaponDefinition.RecoilRecoveryPerSecond *
		                        c_RecoilGraphRecoveryWhileFiringMultiplier *
		                        fireIntervalSeconds;
		float accumulatedPenalty = 0f;
		for (int i = 1; i < shotIndex; i++)
		{
			accumulatedPenalty += recoilAdded;
			accumulatedPenalty = Mathf.Max(0f, accumulatedPenalty - recoveryPerShot);
		}

		float burstSpread = _weaponDefinition.GetAutoBurstSpreadMultiplier(shotIndex);
		float sustainedBurden = (1f + accumulatedPenalty) * Mathf.Max(0.01f, burstSpread);
		return EvaluateRecoilControlQuality(_weaponDefinition, _attachments, WeaponFireMode.FullAuto) / sustainedBurden;
	}

	public static float GetAttachmentRecoilProduct(WeaponAttachmentDefinition[] _attachments)
	{
		return GetAttachmentRecoilProduct(_attachments, WeaponFireMode.FullAuto);
	}

	public static float GetAttachmentRecoilProduct(WeaponAttachmentDefinition[] _attachments, WeaponFireMode _fireMode)
	{
		float product = 1f;
		if (_attachments == null)
			return product;

		for (int i = 0; i < _attachments.Length; i++)
		{
			WeaponAttachmentDefinition attachment = _attachments[i];
			if (attachment == null)
				continue;

			product *= attachment.GetRecoilModifier(_fireMode);
		}

		return product;
	}
	#endregion

	#region Private Methods
	private static void ApplyAttachmentDistanceMultipliers(
		WeaponAttachmentDefinition[] _attachments,
		float _distanceMeters,
		ref float _multiplier,
		bool _aimTime)
	{
		if (_attachments == null)
			return;

		for (int i = 0; i < _attachments.Length; i++)
		{
			WeaponAttachmentDefinition attachment = _attachments[i];
			if (attachment == null)
				continue;

			if (_aimTime)
			{
				_multiplier *= Mathf.Max(0.01f, attachment.AimTimeModifier);
				_multiplier *= Mathf.Max(0.01f, attachment.GetDistanceAimTimeMultiplier(_distanceMeters));
			}
			else
			{
				_multiplier *= Mathf.Max(0.01f, attachment.GetDistanceDispersionMultiplier(_distanceMeters));
			}
		}
	}
	#endregion
}

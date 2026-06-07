using UnityEngine;

/// <summary>
/// Единый расчёт дистанционных множителей и качества для UI-графиков и боевой логики.
/// Формулы: Assets/Docs/CombatBalance/OpticDistanceBalance.md
/// </summary>
public static class WeaponDistanceAimEvaluator
{
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

using System.Text;
using UnityEngine;

/// <summary>
/// Логи боевых выстрелов: оружие, нестандартные модули, дистанция, прицеливание, точность и результат.
/// </summary>
public static class WeaponShotCombatLogger
{
	#region Public Methods
	public static void LogShot(
		Object _context,
		string _shooterLabel,
		ItemDefinition _weaponItem,
		WeaponDefinition _weaponDefinition,
		WeaponRuntimeState _weaponState,
		WeaponAttachmentDefinition[] _presetAttachments,
		UnitCombatStats _combatStats,
		WeaponShotAccuracyContext _accuracy,
		WeaponShotPostureLogInfo _posture,
		WeaponShotRecoilLogInfo _recoil,
		float _fullAimTimeSeconds,
		Transform _visibleTarget,
		WeaponShotHitResult _hitResult,
		int _projectileCount)
	{
		// Отключено: боевые логи выстрелов не пишутся в консоль. См. HealthCombatLogger для травм.
	}
	#endregion

	#region Private Methods
	private static string ResolveWeaponLabel(ItemDefinition _weaponItem, WeaponDefinition _weaponDefinition)
	{
		if (_weaponItem != null)
		{
			string localized = _weaponItem.GetLocalizedDisplayName();
			if (!string.IsNullOrWhiteSpace(localized))
				return localized;
		}

		if (_weaponDefinition != null)
			return _weaponDefinition.name;

		return "?";
	}

	private static string BuildNonStandardAttachmentsLabel(
		WeaponRuntimeState _weaponState,
		WeaponAttachmentDefinition[] _presetAttachments)
	{
		WeaponAttachmentDefinition[] equipped = _weaponState != null ? _weaponState.EquippedAttachments : null;
		ItemDefinition[] equippedItems = _weaponState != null ? _weaponState.EquippedAttachmentItems : null;
		if (equipped == null || equipped.Length == 0)
			return "—";

		StringBuilder builder = new StringBuilder(64);
		bool hasAny = false;
		for (int i = 0; i < equipped.Length; i++)
		{
			WeaponAttachmentDefinition attachment = equipped[i];
			if (attachment == null || IsPresetAttachment(attachment, _presetAttachments))
				continue;

			if (hasAny)
				builder.Append(", ");

			builder.Append(ResolveAttachmentLabel(equippedItems, i, attachment));
			hasAny = true;
		}

		return hasAny ? builder.ToString() : "—";
	}

	private static bool IsPresetAttachment(
		WeaponAttachmentDefinition _attachment,
		WeaponAttachmentDefinition[] _presetAttachments)
	{
		if (_presetAttachments == null || _presetAttachments.Length == 0)
			return false;

		for (int i = 0; i < _presetAttachments.Length; i++)
		{
			if (_presetAttachments[i] == _attachment)
				return true;
		}

		return false;
	}

	private static string ResolveAttachmentLabel(
		ItemDefinition[] _equippedItems,
		int _index,
		WeaponAttachmentDefinition _attachment)
	{
		if (_equippedItems != null && _index >= 0 && _index < _equippedItems.Length)
		{
			ItemDefinition item = _equippedItems[_index];
			if (item != null)
			{
				string localized = item.GetLocalizedDisplayName();
				if (!string.IsNullOrWhiteSpace(localized))
					return localized;
			}
		}

		return _attachment != null ? _attachment.name : "?";
	}

	private static string FormatAimModeLabel(WeaponAimMode _aimMode, WeaponAimMode _effectiveAimMode)
	{
		if (_aimMode == WeaponAimMode.Auto)
			return $"{WeaponAimModeUtility.GetDisplayName(_aimMode)}→{WeaponAimModeUtility.GetDisplayName(_effectiveAimMode)}";

		return WeaponAimModeUtility.GetDisplayName(_aimMode);
	}

	private static string FormatFireModeLabel(WeaponFireMode _selectedFireMode, WeaponFireMode _effectiveFireMode)
	{
		if (_selectedFireMode == WeaponFireMode.Auto)
			return $"{WeaponFireModeUtility.GetDisplayName(_selectedFireMode)}→{WeaponFireModeUtility.GetDisplayName(_effectiveFireMode)}";

		return WeaponFireModeUtility.GetDisplayName(_selectedFireMode);
	}

	private static string FormatPostureLabel(WeaponShotPostureLogInfo _posture, WeaponShotAccuracyContext _accuracy)
	{
		if (!_posture.HasValue)
			return "—";

		string spreadPart = _posture.IsSprinting
			? $"разброс применён=×{_accuracy.StanceMultiplier:F2}"
			: $"разброс=×{_posture.SpreadMultiplier:F2}";

		return
			$"{_posture.Label} | {spreadPart} | прицел=×{_posture.AimTimeMultiplier:F2} | отдача=×{_posture.RecoilMultiplier:F2}";
	}

	private static string FormatSpreadFactorsLabel(WeaponShotAccuracyContext _accuracy)
	{
		return
			$"факторы: стойка=×{_accuracy.StanceMultiplier:F2} состояние=×{_accuracy.ConditionMultiplier:F2} " +
			$"отдача=×{_accuracy.RecoilMultiplier:F2} наведение=×{_accuracy.AimCompletionMultiplier:F2}";
	}

	private static string FormatRecoilLabel(WeaponShotRecoilLogInfo _recoil, WeaponShotAccuracyContext _accuracy)
	{
		if (!_recoil.HasPatternData)
			return "—";

		Vector2 after = _recoil.OffsetBeforeShot + _recoil.KickDelta;
		string capLabel = $"{_recoil.OffsetBeforeShot.y:F2}→{after.y:F2}° pitch, yaw {_recoil.OffsetBeforeShot.x:F2}→{after.x:F2}°";
		if (_recoil.IsAtCap)
			capLabel += " (лимит)";

		float offsetMeters = _accuracy.TargetDistanceMeters * Mathf.Tan(after.magnitude * Mathf.Deg2Rad);
		return
			$"offset={capLabel} | kick=({_recoil.KickDelta.x:F3},{_recoil.KickDelta.y:F3})° | " +
			$"смещение≈{offsetMeters:F2} м на {_accuracy.TargetDistanceMeters:F0} м | " +
			$"восст.={_recoil.RecoveryPerSecond:F2}°/с{(_recoil.IsRecoveringWhileFiring ? " (при огне)" : "")}";
	}

	private static string FormatHitResult(WeaponShotHitResult _hitResult, int _projectileCount)
	{
		return _hitResult switch
		{
			WeaponShotHitResult.HitTarget when _projectileCount > 1 => "ПОПАЛ (есть попадания по цели)",
			WeaponShotHitResult.HitTarget => "ПОПАЛ",
			WeaponShotHitResult.Miss => "ПРОМАХ",
			WeaponShotHitResult.BlockedBySelf => "ЗАБЛОКИРОВАН СВОИМ КОЛЛАЙДЕРОМ",
			WeaponShotHitResult.HitOther => "ПОПАЛ В ПРЕПЯТСТВИЕ",
			_ => "?"
		};
	}
	#endregion
}

public enum WeaponShotHitResult
{
	Miss = 0,
	HitTarget = 1,
	HitOther = 2,
	BlockedBySelf = 3
}

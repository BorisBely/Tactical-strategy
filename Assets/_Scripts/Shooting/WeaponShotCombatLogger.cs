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
		float _weaponAimTimeSeconds,
		float _overallAimTimeSeconds,
		Transform _visibleTarget,
		WeaponShotHitResult _hitResult,
		int _projectileCount)
	{
		string weaponLabel = ResolveWeaponLabel(_weaponItem, _weaponDefinition);
		string attachmentsLabel = BuildNonStandardAttachmentsLabel(_weaponState, _presetAttachments);
		string targetLabel = _visibleTarget != null ? _visibleTarget.name : "—";
		string hitLabel = FormatHitResult(_hitResult, _projectileCount);
		string rankLabel = UnitCombatRankCycle.ResolveRankLabel(_combatStats != null ? _combatStats.RankPreset : null);
		float unitAccuracyMultiplier = _accuracy.SkillMultiplier;
		float unitAimTimeMultiplier = _combatStats != null ? _combatStats.GetAimTimeMultiplier() : 1f;

		Debug.Log(
			$"[Выстрел] {_shooterLabel} | ранг: {rankLabel} | оружие: {weaponLabel} | модули: {attachmentsLabel} | " +
			$"дистанция: {_accuracy.TargetDistanceMeters:F1} м | " +
			$"точность юнита: ×{unitAccuracyMultiplier:F2} | разброс общий: {_accuracy.HalfAngleDegrees:F2}° | " +
			$"прицеливание юнита: ×{unitAimTimeMultiplier:F2} | прицеливание оружие: {_weaponAimTimeSeconds:F2} с | " +
			$"прицеливание общее: {_overallAimTimeSeconds:F2} с | прицел: полный | " +
			$"цель: {targetLabel} | {hitLabel}",
			_context);
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

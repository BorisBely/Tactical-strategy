using System.Text;
using UnityEngine;

/// <summary>
/// Лог попаданий по мишеням полигона: юнит, оружие, модули, режимы и точность от центра грани.
/// </summary>
public static class ShootingRangeHitLogger
{
	#region Public Methods
	public static void LogHit(
		Object _context,
		Transform _shooterRoot,
		UnitEquipment _equipment,
		UnitWeaponRuntime _weaponRuntime,
		UnitWeaponHitscanShooting _hitscanShooting,
		AmmoDefinition _ammo,
		ShootingRangeTarget _target,
		RaycastHit _hit,
		float _shotDistanceMeters)
	{
		if (_target == null)
			return;

		if (!_target.TryEvaluateFaceHitAccuracy(_hit.point, _hit.normal, out ShootingRangeFaceHitAccuracy accuracy))
			accuracy = default;

		string unitName = _shooterRoot != null ? _shooterRoot.name : "?";
		ItemDefinition weaponItem = _equipment != null ? _equipment.EquippedDefinition : null;
		WeaponDefinition weaponDefinition = _weaponRuntime != null ? _weaponRuntime.CurrentWeaponDefinition : null;
		WeaponRuntimeState weaponState = _weaponRuntime != null ? _weaponRuntime.RuntimeState : null;

		WeaponFireMode selectedFireMode = weaponState != null
			? weaponState.SelectedFireMode
			: WeaponFireMode.SemiAuto;
		WeaponFireMode effectiveFireMode = selectedFireMode;
		WeaponAimMode selectedAimMode = _weaponRuntime != null
			? _weaponRuntime.SelectedAimMode
			: WeaponAimMode.FullAim;
		WeaponAimMode effectiveAimMode = selectedAimMode;

		if (_hitscanShooting != null && _hitscanShooting.TrySelectAutoModes(_ammo, out WeaponAutoModeSelectionResult autoSelection))
		{
			effectiveFireMode = autoSelection.EffectiveFireMode;
			effectiveAimMode = autoSelection.EffectiveAimMode;
		}
		else if (_weaponRuntime != null)
		{
			effectiveFireMode = _weaponRuntime.ResolveEffectiveFireMode(_shotDistanceMeters);
			effectiveAimMode = WeaponAimModeUtility.ResolveEffectiveMode(selectedAimMode, _shotDistanceMeters);
		}

		string weaponLabel = ResolveWeaponLabel(weaponItem, weaponDefinition);
		string attachmentsLabel = FormatAttachments(weaponState);
		string fireModeLabel = FormatFireModeLabel(selectedFireMode, effectiveFireMode);
		string aimModeLabel = FormatAimModeLabel(selectedAimMode, effectiveAimMode);
		string accuracyLabel = FormatAccuracyLabel(accuracy);

		Debug.Log(
			$"[Полигон] Попадание | юнит: {unitName} | оружие: {weaponLabel} | модули: {attachmentsLabel} | " +
			$"огонь: {fireModeLabel} | прицел: {aimModeLabel} | мишень: {_target.DisplayName} | " +
			$"{accuracyLabel} | дистанция: {_shotDistanceMeters:F1} м",
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

		return "—";
	}

	private static string FormatAttachments(WeaponRuntimeState _weaponState)
	{
		WeaponAttachmentDefinition[] attachments = _weaponState != null ? _weaponState.EquippedAttachments : null;
		ItemDefinition[] items = _weaponState != null ? _weaponState.EquippedAttachmentItems : null;
		if (attachments == null || attachments.Length == 0)
			return "—";

		StringBuilder builder = new StringBuilder(96);
		for (int i = 0; i < attachments.Length; i++)
		{
			if (i > 0)
				builder.Append(", ");

			builder.Append(ResolveAttachmentLabel(items, i, attachments[i]));
		}

		return builder.ToString();
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

	private static string FormatFireModeLabel(WeaponFireMode _selectedFireMode, WeaponFireMode _effectiveFireMode)
	{
		if (_selectedFireMode == WeaponFireMode.Auto)
			return $"{WeaponFireModeUtility.GetDisplayName(_selectedFireMode)}→{WeaponFireModeUtility.GetDisplayName(_effectiveFireMode)}";

		return WeaponFireModeUtility.GetDisplayName(_selectedFireMode);
	}

	private static string FormatAimModeLabel(WeaponAimMode _aimMode, WeaponAimMode _effectiveAimMode)
	{
		if (_aimMode == WeaponAimMode.Auto)
			return $"{WeaponAimModeUtility.GetDisplayName(_aimMode)}→{WeaponAimModeUtility.GetDisplayName(_effectiveAimMode)}";

		return WeaponAimModeUtility.GetDisplayName(_aimMode);
	}

	private static string FormatAccuracyLabel(ShootingRangeFaceHitAccuracy _accuracy)
	{
		if (!_accuracy.IsValid)
			return "точность: —";

		return
			$"от центра: {_accuracy.OffsetFromCenterMeters:F3} м " +
			$"(гориз. {_accuracy.OffsetHorizontalMeters:+#.000;-#.000;0} м, " +
			$"верт. {_accuracy.OffsetVerticalMeters:+#.000;-#.000;0} м, " +
			$"R={_accuracy.FaceHalfExtentMeters:F2} м)";
	}
	#endregion
}

public readonly struct ShootingRangeFaceHitAccuracy
{
	public readonly bool IsValid;
	public readonly float OffsetFromCenterMeters;
	public readonly float OffsetHorizontalMeters;
	public readonly float OffsetVerticalMeters;
	public readonly float FaceHalfExtentMeters;

	public ShootingRangeFaceHitAccuracy(
		float _offsetFromCenterMeters,
		float _offsetHorizontalMeters,
		float _offsetVerticalMeters,
		float _faceHalfExtentMeters)
	{
		IsValid = true;
		OffsetFromCenterMeters = _offsetFromCenterMeters;
		OffsetHorizontalMeters = _offsetHorizontalMeters;
		OffsetVerticalMeters = _offsetVerticalMeters;
		FaceHalfExtentMeters = _faceHalfExtentMeters;
	}
}

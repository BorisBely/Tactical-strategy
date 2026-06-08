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
		string weaponLabel = ResolveWeaponLabel(_weaponItem, _weaponDefinition);
		string attachmentsLabel = BuildNonStandardAttachmentsLabel(_weaponState, _presetAttachments);
		string targetLabel = _visibleTarget != null ? _visibleTarget.name : "—";
		string hitLabel = FormatHitResult(_hitResult, _projectileCount);
		string rankLabel = UnitCombatRankCycle.ResolveRankLabel(_combatStats != null ? _combatStats.RankPreset : null);
		float unitAccuracyMultiplier = _accuracy.SkillMultiplier;
		string postureLabel = FormatPostureLabel(_posture, _accuracy);
		string spreadFactorsLabel = FormatSpreadFactorsLabel(_accuracy);
		string recoilLabel = FormatRecoilLabel(_recoil, _accuracy);
		float requiredProgress = WeaponAimModeUtility.GetRequiredAimProgress01(
			_accuracy.AimMode,
			_accuracy.TargetDistanceMeters);
		float modeAimTimeSeconds = WeaponAimModeUtility.GetRequiredAimTimeSeconds(
			_fullAimTimeSeconds,
			_accuracy.AimMode,
			_accuracy.TargetDistanceMeters);
		string aimModeLabel = FormatAimModeLabel(_accuracy.SelectedAimMode, _accuracy.EffectiveAimMode);
		string fireModeLabel = FormatFireModeLabel(_accuracy.SelectedFireMode, _accuracy.EffectiveFireMode);
		string aimingLabel =
			$"режим={aimModeLabel} | полное наведение={_fullAimTimeSeconds:F2} с (100%) | " +
			$"наведение по режиму={modeAimTimeSeconds:F2} с ({requiredProgress:P0}) | " +
			$"факт на выстреле={_accuracy.AimProgress01:P0} | штраф разброса=×{_accuracy.AimCompletionMultiplier:F2}";
		string fireLabel =
			$"режим={fireModeLabel} | выстрел в серии={_accuracy.BurstShotIndex} | " +
			$"множитель серии=×{_accuracy.AutoBurstSpreadMultiplier:F2}";
		string spreadLabel =
			$"ожидаемый диаметр={_accuracy.SpreadDiameterMeters:F2} м / " +
			$"лимит={WeaponAutoModeSelectionUtility.AcceptableSpreadDiameterMeters:F2} м";

		Debug.Log(
			$"[Выстрел] {_shooterLabel} | ранг: {rankLabel} | стойка: {postureLabel} | оружие: {weaponLabel} | модули: {attachmentsLabel} | " +
			$"дистанция: {_accuracy.TargetDistanceMeters:F1} м | " +
			$"навык: ×{unitAccuracyMultiplier:F2} | разброс: {_accuracy.HalfAngleDegrees:F2}° | {spreadFactorsLabel} | " +
			$"отдача: {recoilLabel} | " +
			$"огонь: {fireLabel} | " +
			$"наведение: {aimingLabel} | " +
			$"Auto-критерий: {spreadLabel} | " +
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
		if (!_recoil.HasPatternData && _recoil.RecoilAddedPerShot <= 0.0001f && _recoil.MaxRecoilPenalty <= 0.0001f)
			return "—";

		float penaltyAfterShot = _recoil.RecoilPenaltyBeforeShot + _recoil.RecoilAddedPerShot;
		if (_recoil.MaxRecoilPenalty > 0.0001f)
			penaltyAfterShot = Mathf.Min(penaltyAfterShot, _recoil.MaxRecoilPenalty);

		string capLabel = _recoil.MaxRecoilPenalty > 0.0001f
			? $"{_recoil.RecoilPenaltyBeforeShot:F2}→{penaltyAfterShot:F2}/{_recoil.MaxRecoilPenalty:F1}"
			: $"{_recoil.RecoilPenaltyBeforeShot:F2}→{penaltyAfterShot:F2}";
		if (_recoil.IsAtCap)
			capLabel += " (лимит)";

		string patternLabel = _recoil.PatternApplied
			? $"паттерн: pitch={_recoil.PatternPitchDegrees:F2}° yaw={_recoil.PatternYawDegrees:F2}° | смещение≈{_recoil.PatternVerticalOffsetMeters:F2} м на {_accuracy.TargetDistanceMeters:F0} м"
			: _recoil.RecoilPenaltyBeforeShot <= 0.0001f
				? "паттерн: нет (штраф=0)"
				: "паттерн: нет (режим без подъёма)";

		string balanceLabel = FormatRecoilBalanceLabel(_recoil);
		return
			$"штраф={capLabel} | {patternLabel} | +за выстрел={_recoil.RecoilAddedPerShot:F2} | " +
			$"восст.={_recoil.RecoveryPerSecond:F2}/с{(_recoil.IsRecoveringWhileFiring ? " (при огне)" : "")} | " +
			$"баланс≈{balanceLabel} | разброс-штраф=×{_recoil.RecoilSpreadMultiplier:F2} (scale={_recoil.RecoilSpreadScale:F2})";
	}

	private static string FormatRecoilBalanceLabel(WeaponShotRecoilLogInfo _recoil)
	{
		float net = _recoil.EstimatedNetPenaltyPerSecond;
		if (Mathf.Abs(net) <= 0.05f)
			return "0/с (равновесие)";

		string sign = net > 0f ? "+" : "";
		return $"{sign}{net:F2}/с";
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

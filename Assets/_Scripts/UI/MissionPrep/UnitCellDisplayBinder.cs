using UnityEngine;

/// <summary>
/// Единая привязка имени, ранга и пресета к <see cref="MissionPrepUnitCellView"/>.
/// </summary>
public static class UnitCellDisplayBinder
{
	#region Public Methods
	public static void Apply(MissionPrepUnitCellView _cell, GameObject _unitRoot)
	{
		if (_cell == null)
			return;

		if (_unitRoot == null)
		{
			_cell.ClearBinding();
			return;
		}

		_cell.BindToUnit(_unitRoot, ResolveUnitName(_unitRoot));
		_cell.SetRankDisplayName(ResolveRankName(_unitRoot));
		_cell.SetPresetDisplayName(ResolvePresetName(_unitRoot));
		_cell.SetHealthStatusText(ResolveHealthSummary(_unitRoot));
		_cell.SetArmorStatusText(ResolveArmorSummary(_unitRoot));
	}

	public static string ResolveHealthSummary(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return string.Empty;

		if (_unitRoot.TryGetComponent(out UnitHealth health))
			return health.GetLocalizedOverallStatusText();

		return LocalizationManager.Get("health.status.ok", "В норме");
	}

	public static string ResolveArmorSummary(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return string.Empty;

		return _unitRoot.TryGetComponent(out UnitArmor armor)
			? armor.GetLocalizedStatusText()
			: string.Empty;
	}

	public static string ResolveUnitName(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return string.Empty;

		if (_unitRoot.TryGetComponent(out UnitRosterDisplayState roster))
			return roster.FullName;

		return _unitRoot.name;
	}

	public static string ResolveRankName(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return string.Empty;

		UnitCombatStats combatStats = _unitRoot.GetComponentInChildren<UnitCombatStats>(true);
		UnitCombatRankDefinition rank = combatStats != null ? combatStats.RankPreset : null;
		return rank != null ? rank.GetLocalizedDisplayName() : string.Empty;
	}

	public static string ResolvePresetName(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return string.Empty;

		MissionPrepScreenController screenController =
			Object.FindAnyObjectByType<MissionPrepScreenController>(FindObjectsInactive.Include);
		if (screenController != null)
			return screenController.GetPresetLabelForUnit(_unitRoot);

		return ResolvePresetNameWithoutScreenController(_unitRoot);
	}
	#endregion

	#region Private Methods
	private static string ResolvePresetNameWithoutScreenController(GameObject _unitRoot)
	{
		if (!_unitRoot.TryGetComponent(out MissionPrepUnitPresetState presetState))
		{
			MissionPrepEquipmentPresetCatalog catalog =
				Object.FindAnyObjectByType<MissionPrepEquipmentPresetCatalog>(FindObjectsInactive.Include);
			return catalog != null && catalog.PresetCount > 0 ? catalog.GetPresetLabel(0) : string.Empty;
		}

		MissionPrepLoadoutCoordinator coordinator =
			Object.FindAnyObjectByType<MissionPrepLoadoutCoordinator>(FindObjectsInactive.Include);
		if (coordinator != null && coordinator.TryGetPresetLabelForUnit(presetState, out string label))
			return label;

		MissionPrepEquipmentPresetCatalog presetCatalog =
			Object.FindAnyObjectByType<MissionPrepEquipmentPresetCatalog>(FindObjectsInactive.Include);
		if (presetCatalog == null)
			return string.Empty;

		return presetCatalog.GetPresetLabel(presetCatalog.ClampPresetIndex(presetState.PresetCatalogIndex));
	}
	#endregion
}

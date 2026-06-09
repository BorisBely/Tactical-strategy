using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class UnitHealth : MonoBehaviour
{
	#region Events
	public event Action Changed;
	#endregion

	#region Serialized Fields
	[SerializeField] private string m_OverallStatusLocalizationKey = "health.status.ok";
	[SerializeField] private string m_OverallStatusDisplayName = "В норме";
	[SerializeField] private List<InjuryUiEntry> m_Injuries = new List<InjuryUiEntry>();
	#endregion

	#region Public Properties
	public bool HasInjuries => m_Injuries != null && m_Injuries.Count > 0;

	public string OverallStatusLocalizationKey => HasInjuries
		? m_OverallStatusLocalizationKey
		: "health.status.ok";

	public string OverallStatusDisplayName => HasInjuries
		? m_OverallStatusDisplayName
		: LocalizationManager.Get("health.status.ok", "В норме");
	#endregion

	#region Public Methods
	public string GetLocalizedOverallStatusText()
	{
		if (!HasInjuries)
			return LocalizationManager.Get("health.status.ok", "В норме");

		if (!string.IsNullOrWhiteSpace(m_OverallStatusLocalizationKey))
			return LocalizationManager.Get(m_OverallStatusLocalizationKey, m_OverallStatusDisplayName);

		return m_OverallStatusDisplayName ?? string.Empty;
	}

	public IReadOnlyList<InjuryUiEntry> GetSortedInjuryEntries()
	{
		return InjuryUiEntryUtility.SortByPriority(m_Injuries);
	}

	public void ClearInjuries()
	{
		if (m_Injuries.Count == 0)
			return;

		m_Injuries.Clear();
		m_OverallStatusLocalizationKey = "health.status.ok";
		m_OverallStatusDisplayName = LocalizationManager.Get("health.status.ok", "В норме");
		NotifyChanged();
	}

	public void AddInjury(InjuryUiEntry _entry)
	{
		m_Injuries.Add(_entry);
		RecalculateOverallStatus();
		NotifyChanged();
	}

	public void SetInjuries(IEnumerable<InjuryUiEntry> _entries)
	{
		m_Injuries.Clear();
		if (_entries != null)
			m_Injuries.AddRange(_entries);

		RecalculateOverallStatus();
		NotifyChanged();
	}

#if UNITY_EDITOR
	[ContextMenu("Add Test Injury: Arm Bleeding")]
	private void AddTestArmBleedingInjury()
	{
		AddInjury(InjuryUiEntry.FromLocalizedKeys(
			"health.injury.arm_bleeding",
			"health.condition.moderate_bleeding",
			"health.injury.arm_bleeding.desc",
			new[]
			{
				"health.debuff.aim_penalty",
				"health.debuff.reload_slow"
			},
			_sortPriority: 40));
	}

	[ContextMenu("Add Test Injury: Leg Fracture")]
	private void AddTestLegFractureInjury()
	{
		AddInjury(InjuryUiEntry.FromLocalizedKeys(
			"health.injury.leg_fracture",
			"health.condition.fracture",
			"health.injury.leg_fracture.desc",
			new[]
			{
				"health.debuff.no_sprint",
				"health.debuff.movement_slow"
			},
			_sortPriority: 30));
	}

	[ContextMenu("Add Test Injury: Lung Damage")]
	private void AddTestLungDamageInjury()
	{
		AddInjury(InjuryUiEntry.FromLocalizedKeys(
			"health.injury.lung_damage",
			"health.condition.internal",
			"health.injury.lung_damage.desc",
			new[]
			{
				"health.debuff.oxygen_loss",
				"health.debuff.no_long_run"
			},
			_sortPriority: 20));
	}

	[ContextMenu("Clear All Injuries")]
	private void ContextClearInjuries()
	{
		ClearInjuries();
	}
#endif
	#endregion

	#region Private Methods
	private void RecalculateOverallStatus()
	{
		if (!HasInjuries)
		{
			m_OverallStatusLocalizationKey = "health.status.ok";
			m_OverallStatusDisplayName = LocalizationManager.Get("health.status.ok", "В норме");
			return;
		}

		int minPriority = int.MaxValue;
		for (int i = 0; i < m_Injuries.Count; i++)
			minPriority = Mathf.Min(minPriority, m_Injuries[i].SortPriority);

		if (minPriority <= 10)
		{
			m_OverallStatusLocalizationKey = "health.status.critical";
			m_OverallStatusDisplayName = "Критическое состояние";
			return;
		}

		if (minPriority <= 25)
		{
			m_OverallStatusLocalizationKey = "health.status.seriously_wounded";
			m_OverallStatusDisplayName = "Тяжело ранен";
			return;
		}

		m_OverallStatusLocalizationKey = "health.status.wounded";
		m_OverallStatusDisplayName = "Ранен";
	}

	private void NotifyChanged()
	{
		Changed?.Invoke();
		InventoryScreenBindings.Instance?.RefreshHealthUi();
	}
	#endregion
}

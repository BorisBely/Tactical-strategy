using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class UnitHealth : MonoBehaviour
{
	#region Events
	public event Action Changed;
	public event Action VitalsChanged;
	#endregion

	#region Serialized Fields
	[SerializeField] private string m_OverallStatusLocalizationKey = "health.status.ok";
	[SerializeField] private string m_OverallStatusDisplayName = "В норме";
	[SerializeField] private List<InjuryUiEntry> m_Injuries = new List<InjuryUiEntry>();
	[SerializeField] private bool m_IsDead;
	#endregion

	#region Public Properties
	public bool HasInjuries => m_Injuries != null && m_Injuries.Count > 0;
	public bool IsDead => m_IsDead;
	public bool HasUnstabilizedInjuries
	{
		get
		{
			if (!HasInjuries)
				return false;

			for (int i = 0; i < m_Injuries.Count; i++)
			{
				if (!m_Injuries[i].IsStabilized)
					return true;
			}

			return false;
		}
	}

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
		if (m_IsDead)
			return LocalizationManager.Get("health.status.dead", "Погиб");

		if (!HasInjuries)
			return LocalizationManager.Get("health.status.ok", "В норме");

		string statusText = !string.IsNullOrWhiteSpace(m_OverallStatusLocalizationKey)
			? LocalizationManager.Get(m_OverallStatusLocalizationKey, m_OverallStatusDisplayName)
			: m_OverallStatusDisplayName ?? string.Empty;

		string survivalText = GetLocalizedSurvivalEstimateText();
		if (string.IsNullOrWhiteSpace(survivalText))
			return statusText;

		return $"{statusText} · {survivalText}";
	}

	public string GetLocalizedSurvivalEstimateText()
	{
		if (m_IsDead || !HasInjuries)
			return string.Empty;

		bool isUnconscious = TryGetComponent(out UnitConsciousness consciousness) && !consciousness.IsConscious;
		float secondsToLethal = InjuryDeteriorationTable.EstimateUnitSecondsToLethal(this, isUnconscious);
		return InjuryDeteriorationTable.FormatRoundedSurvivalEstimate(secondsToLethal);
	}

	public void NotifyVitalsChanged()
	{
		VitalsChanged?.Invoke();
		InventoryScreenBindings.Instance?.RefreshHealthVitalsSummary();
	}

	public IReadOnlyList<InjuryUiEntry> GetSortedInjuryEntries()
	{
		return InjuryUiEntryUtility.SortByPriority(m_Injuries);
	}

	public IReadOnlyList<InjuryIndexedEntry> GetSortedIndexedInjuryEntries()
	{
		if (!HasInjuries)
			return Array.Empty<InjuryIndexedEntry>();

		var indexed = new List<InjuryIndexedEntry>(m_Injuries.Count);
		for (int i = 0; i < m_Injuries.Count; i++)
			indexed.Add(new InjuryIndexedEntry(i, m_Injuries[i]));

		indexed.Sort(CompareIndexedInjuries);
		return indexed;
	}

	public int InjuryCount => m_Injuries != null ? m_Injuries.Count : 0;

	public int MinInjurySortPriority
	{
		get
		{
			if (!HasInjuries)
				return int.MaxValue;

			int minPriority = int.MaxValue;
			for (int i = 0; i < m_Injuries.Count; i++)
				minPriority = Mathf.Min(minPriority, m_Injuries[i].SortPriority);

			return minPriority;
		}
	}

	public bool IsCriticallyWounded => MinInjurySortPriority <= 10;
	public bool IsLethallyCritical => m_IsDead;

	public int CountInjuriesWithPriorityAtMost(int _maxPriority)
	{
		if (!HasInjuries)
			return 0;

		int count = 0;
		for (int i = 0; i < m_Injuries.Count; i++)
		{
			if (m_Injuries[i].SortPriority <= _maxPriority)
				count++;
		}

		return count;
	}

	public bool TryGetWorstUnstabilizedInjury(out InjuryUiEntry _injury, out int _index)
	{
		_injury = default;
		_index = -1;

		if (!HasInjuries)
			return false;

		int bestPriority = int.MaxValue;
		for (int i = 0; i < m_Injuries.Count; i++)
		{
			InjuryUiEntry injury = m_Injuries[i];
			if (injury.IsStabilized || injury.SortPriority >= bestPriority)
				continue;

			bestPriority = injury.SortPriority;
			_injury = injury;
			_index = i;
		}

		return _index >= 0;
	}

	public bool TryGetInjury(int _index, out InjuryUiEntry _injury)
	{
		_injury = default;
		if (m_Injuries == null || _index < 0 || _index >= m_Injuries.Count)
			return false;

		_injury = m_Injuries[_index];
		return true;
	}

	public bool TryMarkInjuryStabilized(int _index)
	{
		if (m_Injuries == null || _index < 0 || _index >= m_Injuries.Count)
			return false;

		InjuryUiEntry injury = m_Injuries[_index];
		if (injury.IsStabilized)
			return false;

		injury.IsStabilized = true;
		injury.ConditionLocalizationKey = "health.condition.stabilized";
		injury.ConditionDisplayName = LocalizationManager.Get("health.condition.stabilized", "Стабилизировано");
		m_Injuries[_index] = injury;
		RecalculateOverallStatus();
		NotifyChanged();
		return true;
	}

	public void AddLethalPressure(int _index, float _amount)
	{
		if (m_Injuries == null || _index < 0 || _index >= m_Injuries.Count || _amount <= 0f)
			return;

		InjuryUiEntry injury = m_Injuries[_index];
		injury.AccumulatedLethalPressure += _amount;
		m_Injuries[_index] = injury;
	}

	public float GetTotalLethalPressure()
	{
		if (!HasInjuries)
			return 0f;

		float total = 0f;
		for (int i = 0; i < m_Injuries.Count; i++)
			total += Mathf.Max(0f, m_Injuries[i].AccumulatedLethalPressure);

		return total;
	}

	public void EnterDead()
	{
		if (m_IsDead)
			return;

		m_IsDead = true;
		m_OverallStatusLocalizationKey = "health.status.dead";
		m_OverallStatusDisplayName = LocalizationManager.Get("health.status.dead", "Погиб");
		if (UnitActionLog.Enabled)
		{
			string payload = "dead=1 pos=" + UnitActionLog.Vec(transform.position);
			UnitActionLog.Write(this, UnitActionLog.Death, payload);
			UnitActionLog.Timeline(UnitActionLog.Death, "actor=" + UnitActionLog.Slot(this) + " " + payload);
		}

		NotifyChanged();
		CombatEventHub.Publish(CombatEvent.Death(this, null, this, transform.position));
	}

	public void ClearInjuries()
	{
		if (m_Injuries.Count == 0)
			return;

		m_Injuries.Clear();
		m_IsDead = false;
		m_OverallStatusLocalizationKey = "health.status.ok";
		m_OverallStatusDisplayName = LocalizationManager.Get("health.status.ok", "В норме");
		NotifyChanged();
	}

	/// <summary>Play-harness revive: clear wounds and death without changing injury rules.</summary>
	public void ResetToHealthy()
	{
		if (m_Injuries.Count == 0 && !m_IsDead)
			return;

		m_Injuries.Clear();
		m_IsDead = false;
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

	public void AddDebugInjuryArmBleeding()
	{
		AddInjury(CreateDebugArmBleedingInjury());
	}

	public void AddDebugInjuryLegFracture()
	{
		AddInjury(CreateDebugLegFractureInjury());
	}

	public void AddDebugInjuryLungDamage()
	{
		AddInjury(CreateDebugLungDamageInjury());
	}

	/// <summary>
	/// Debug полигона: случайная рана + мгновенная потеря сознания для теста стабилизации.
	/// </summary>
	public void AddDebugRandomWoundAndKnockout()
	{
		BodyPartType bodyPart = (BodyPartType)UnityEngine.Random.Range((int)BodyPartType.Head, (int)BodyPartType.RightLeg + 1);
		InjuryUiEntry injury = InjuryRollTable.Roll(bodyPart, DamageSourceType.Bullet);
		AddInjury(injury);

		if (TryGetComponent(out UnitConsciousness consciousness) && consciousness.IsConscious)
			consciousness.EnterUnconscious();
	}

#if UNITY_EDITOR
	[ContextMenu("Add Test Injury: Arm Bleeding")]
	private void AddTestArmBleedingInjury()
	{
		AddDebugInjuryArmBleeding();
	}

	[ContextMenu("Add Test Injury: Leg Fracture")]
	private void AddTestLegFractureInjury()
	{
		AddDebugInjuryLegFracture();
	}

	[ContextMenu("Add Test Injury: Lung Damage")]
	private void AddTestLungDamageInjury()
	{
		AddDebugInjuryLungDamage();
	}

	[ContextMenu("Add Test Injury: Random Wound + Knockout")]
	private void AddTestRandomWoundAndKnockout()
	{
		AddDebugRandomWoundAndKnockout();
	}

	[ContextMenu("Clear All Injuries")]
	private void ContextClearInjuries()
	{
		ClearInjuries();
	}
#endif

	private static InjuryUiEntry CreateDebugArmBleedingInjury()
	{
		return InjuryUiEntry.FromLocalizedKeys(
			"health.injury.arm_bleeding",
			"health.condition.moderate_bleeding",
			"health.injury.arm_bleeding.desc",
			new[]
			{
				"health.debuff.aim_penalty",
				"health.debuff.reload_slow"
			},
			_sortPriority: 40);
	}

	private static InjuryUiEntry CreateDebugLegFractureInjury()
	{
		return InjuryUiEntry.FromLocalizedKeys(
			"health.injury.leg_fracture",
			"health.condition.fracture",
			"health.injury.leg_fracture.desc",
			new[]
			{
				"health.debuff.no_sprint",
				"health.debuff.movement_slow"
			},
			_sortPriority: 30);
	}

	private static InjuryUiEntry CreateDebugLungDamageInjury()
	{
		return InjuryUiEntry.FromLocalizedKeys(
			"health.injury.lung_damage",
			"health.condition.internal",
			"health.injury.lung_damage.desc",
			new[]
			{
				"health.debuff.oxygen_loss",
				"health.debuff.no_long_run"
			},
			_sortPriority: 20);
	}
	#endregion

	#region Private Methods
	private void RecalculateOverallStatus()
	{
		if (m_IsDead)
		{
			m_OverallStatusLocalizationKey = "health.status.dead";
			m_OverallStatusDisplayName = LocalizationManager.Get("health.status.dead", "Погиб");
			return;
		}

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

	private static int CompareIndexedInjuries(InjuryIndexedEntry _a, InjuryIndexedEntry _b)
	{
		int priorityCompare = _a.Entry.SortPriority.CompareTo(_b.Entry.SortPriority);
		if (priorityCompare != 0)
			return priorityCompare;

		int statusCompare = string.Compare(
			_a.Entry.GetLocalizedStatusText(),
			_b.Entry.GetLocalizedStatusText(),
			StringComparison.Ordinal);
		if (statusCompare != 0)
			return statusCompare;

		return _a.Index.CompareTo(_b.Index);
	}
	#endregion
}

public readonly struct InjuryIndexedEntry
{
	public int Index { get; }
	public InjuryUiEntry Entry { get; }

	public InjuryIndexedEntry(int _index, InjuryUiEntry _entry)
	{
		Index = _index;
		Entry = _entry;
	}
}

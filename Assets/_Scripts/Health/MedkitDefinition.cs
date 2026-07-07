using System;
using UnityEngine;

/// <summary>
/// Данные аптечки: ёмкость «лечебного ресурса» (как HP в Tarkov) и стоимость стабилизации по травмам.
/// Лечение пока не применяется — только расчёт и списание ресурса.
/// </summary>
[CreateAssetMenu(fileName = "MedkitDefinition", menuName = "Polygone/Health/Medkit Definition", order = 20)]
public sealed class MedkitDefinition : ScriptableObject
{
	#region Serializable Types
	[Serializable]
	public struct InjuryCostEntry
	{
		[Tooltip("Ключ локализации травмы (InjuryUiEntry.StatusLocalizationKey).")]
		public string InjuryStatusLocalizationKey;

		[Tooltip("Сколько единиц ресурса аптечки тратится на стабилизацию этой травмы.")]
		[Min(1)]
		public int ResourceCost;
	}

	[Serializable]
	public struct SeverityFallbackEntry
	{
		[Tooltip("Максимальный SortPriority травмы для этого тарифа (включительно).")]
		[Min(1)]
		public int MaxSortPriority;

		[Min(1)]
		public int ResourceCost;
	}
	#endregion

	#region Serialized Fields
	[Header("Capacity")]
	[Tooltip("Полный запас ресурса аптечки. IFAK в Tarkov ≈ 300 HP.")]
	[SerializeField, Min(1)] private int m_MaxResourcePoints = 300;

	[Header("Per-Injury Costs")]
	[Tooltip("Точные значения по ключу травмы. Приоритетнее fallback по SortPriority.")]
	[SerializeField] private InjuryCostEntry[] m_InjuryCostOverrides = CreateDefaultIfakInjuryCosts();

	[Header("Fallback By Severity")]
	[Tooltip("Если ключ травмы не найден — стоимость по SortPriority (меньше = тяжелее).")]
	[SerializeField] private SeverityFallbackEntry[] m_SeverityFallbackCosts = CreateDefaultIfakSeverityFallbacks();

	[Header("Heal Audio")]
	[Tooltip("Звук перевязывания на каждый цикл использования аптечки (animation event).")]
	[SerializeField] private WeaponRandomAudioClipSet m_BandageUseCycleSounds = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_BandageUseCycleSoundVolume = 0.85f;
	[SerializeField, Min(1f)] private float m_BandageUseCycleSoundMaxDistance = 25f;
	#endregion

	#region Public Properties
	public int MaxResourcePoints => m_MaxResourcePoints;
	#endregion

	#region Public Methods
	public int GetInjuryTreatCost(in InjuryUiEntry _injury)
	{
		if (!string.IsNullOrWhiteSpace(_injury.StatusLocalizationKey) && m_InjuryCostOverrides != null)
		{
			for (int i = 0; i < m_InjuryCostOverrides.Length; i++)
			{
				InjuryCostEntry entry = m_InjuryCostOverrides[i];
				if (entry.ResourceCost <= 0)
					continue;

				if (string.Equals(
					    entry.InjuryStatusLocalizationKey,
					    _injury.StatusLocalizationKey,
					    StringComparison.Ordinal))
					return entry.ResourceCost;
			}
		}

		return GetFallbackCostBySortPriority(_injury.SortPriority);
	}

	public int GetFallbackCostBySortPriority(int _sortPriority)
	{
		if (m_SeverityFallbackCosts == null || m_SeverityFallbackCosts.Length == 0)
			return 35;

		int bestCost = m_SeverityFallbackCosts[m_SeverityFallbackCosts.Length - 1].ResourceCost;
		int bestPriority = int.MaxValue;

		for (int i = 0; i < m_SeverityFallbackCosts.Length; i++)
		{
			SeverityFallbackEntry entry = m_SeverityFallbackCosts[i];
			if (entry.ResourceCost <= 0)
				continue;

			if (_sortPriority <= entry.MaxSortPriority && entry.MaxSortPriority < bestPriority)
			{
				bestPriority = entry.MaxSortPriority;
				bestCost = entry.ResourceCost;
			}
		}

		return Mathf.Max(1, bestCost);
	}

	public bool TryPlayBandageUseCycleSound(Vector3 _position)
	{
		if (!m_BandageUseCycleSounds.TryPickClip(out AudioClip clip))
			return false;

		UnitNonFireAudioUtility.PlayAtPoint(
			clip,
			_position,
			m_BandageUseCycleSoundVolume,
			m_BandageUseCycleSoundMaxDistance);
		return true;
	}
	#endregion

	#region Static Defaults
	public static InjuryCostEntry[] CreateDefaultIfakInjuryCosts()
	{
		// Баланс по мотивам Tarkov IFAK (300 HP): шея/живот — почти весь заряд, конечности — дешевле.
		return new[]
		{
			Entry("health.injury.neck_bleeding", 120),
			Entry("health.injury.internal_bleeding", 95),
			Entry("health.injury.lung_damage", 85),
			Entry("health.injury.leg_fracture", 70),
			Entry("health.injury.right_leg_fracture", 70),
			Entry("health.injury.chest_bleeding", 65),
			Entry("health.injury.concussion", 55),
			Entry("health.injury.left_leg_bleeding", 40),
			Entry("health.injury.right_leg_bleeding", 40),
			Entry("health.injury.arm_bleeding", 35),
			Entry("health.injury.left_arm_bleeding", 35),
			Entry("health.injury.head_wound", 45),
			Entry("health.injury.generic_wound", 25)
		};
	}

	public static SeverityFallbackEntry[] CreateDefaultIfakSeverityFallbacks()
	{
		return new[]
		{
			new SeverityFallbackEntry { MaxSortPriority = 10, ResourceCost = 110 },
			new SeverityFallbackEntry { MaxSortPriority = 20, ResourceCost = 80 },
			new SeverityFallbackEntry { MaxSortPriority = 30, ResourceCost = 55 },
			new SeverityFallbackEntry { MaxSortPriority = 40, ResourceCost = 35 },
			new SeverityFallbackEntry { MaxSortPriority = 999, ResourceCost = 25 }
		};
	}

	private static InjuryCostEntry Entry(string _statusKey, int _cost)
	{
		return new InjuryCostEntry
		{
			InjuryStatusLocalizationKey = _statusKey,
			ResourceCost = _cost
		};
	}
	#endregion
}

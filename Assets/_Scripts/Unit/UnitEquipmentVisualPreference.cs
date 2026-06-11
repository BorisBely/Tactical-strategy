using System;
using UnityEngine;

/// <summary>
/// Пол юнита для выбора gender-specific декора экипировки.
/// </summary>
public enum CharacterGender
{
	Male = 0,
	Female = 1
}

/// <summary>
/// Предпочитаемый визуальный вариант профиля экипировки (save-ready).
/// </summary>
[Serializable]
public struct UnitEquipmentVisualPreferenceEntry
{
	[SerializeField] private string m_ProfileId;
	[SerializeField, Min(0)] private int m_PrimaryVariant;
	[SerializeField] private bool m_UseChinStrap;

	public string ProfileId => m_ProfileId;
	public int PrimaryVariant => m_PrimaryVariant;
	public bool UseChinStrap => m_UseChinStrap;

	public UnitEquipmentVisualPreferenceEntry(string _profileId, int _primaryVariant, bool _useChinStrap)
	{
		m_ProfileId = _profileId ?? string.Empty;
		m_PrimaryVariant = Mathf.Max(0, _primaryVariant);
		m_UseChinStrap = _useChinStrap;
	}

	public bool MatchesProfile(string _profileId)
	{
		return !string.IsNullOrWhiteSpace(m_ProfileId) &&
		       string.Equals(m_ProfileId, _profileId, StringComparison.Ordinal);
	}
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Индивидуальные отклонения юнита от базовых навыков ранга и предпочтения визуала экипировки.
/// Генерируются при старте сессии, без сохранения между запусками.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10)]
public sealed class UnitIndividualTraits : MonoBehaviour
{
	#region Constants
	public const float MaxCombatModifier = 0.10f;
	#endregion

	#region Serialized Fields
	[Header("Generation")]
	[SerializeField] private bool m_RollOnAwake = true;
	[SerializeField] private bool m_IsInitialized;
	[SerializeField] private EquipmentVisualProfileCatalog m_EquipmentVisualProfileCatalog;

	[Header("Combat Modifiers (±10%)")]
	[Tooltip("Положительное значение улучшает меткость (уменьшает разброс).")]
	[SerializeField, Range(-MaxCombatModifier, MaxCombatModifier)] private float m_MarksmanshipModifier;
	[Tooltip("Положительное значение ускоряет прицеливание.")]
	[SerializeField, Range(-MaxCombatModifier, MaxCombatModifier)] private float m_WeaponHandlingModifier;
	[Tooltip("Положительное значение улучшает контроль отдачи.")]
	[SerializeField, Range(-MaxCombatModifier, MaxCombatModifier)] private float m_RecoilControlModifier;

	[Header("Equipment Visual Preferences")]
	[SerializeField] private UnitEquipmentVisualPreferenceEntry[] m_EquipmentVisualPreferences =
		Array.Empty<UnitEquipmentVisualPreferenceEntry>();
	#endregion

	#region Public Properties
	public bool IsInitialized => m_IsInitialized;
	public float MarksmanshipModifier => m_MarksmanshipModifier;
	public float WeaponHandlingModifier => m_WeaponHandlingModifier;
	public float RecoilControlModifier => m_RecoilControlModifier;
	public IReadOnlyList<UnitEquipmentVisualPreferenceEntry> EquipmentVisualPreferences => m_EquipmentVisualPreferences;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_RollOnAwake && !m_IsInitialized)
			RollRandomTraits();
	}
	#endregion

	#region Public Methods
	[ContextMenu("Roll Random Individual Traits")]
	public void RollRandomTraits()
	{
		m_MarksmanshipModifier = UnityEngine.Random.Range(-MaxCombatModifier, MaxCombatModifier);
		m_WeaponHandlingModifier = UnityEngine.Random.Range(-MaxCombatModifier, MaxCombatModifier);
		m_RecoilControlModifier = UnityEngine.Random.Range(-MaxCombatModifier, MaxCombatModifier);
		RollEquipmentVisualPreferences();
		m_IsInitialized = true;
	}

	public void ApplyCombatModifiers(
		float _marksmanshipModifier,
		float _weaponHandlingModifier,
		float _recoilControlModifier)
	{
		m_MarksmanshipModifier = ClampCombatModifier(_marksmanshipModifier);
		m_WeaponHandlingModifier = ClampCombatModifier(_weaponHandlingModifier);
		m_RecoilControlModifier = ClampCombatModifier(_recoilControlModifier);
		m_IsInitialized = true;
	}

	public float GetDispersionMultiplier()
	{
		return Mathf.Max(0.01f, 1f - m_MarksmanshipModifier);
	}

	public float GetAimTimeMultiplier()
	{
		return Mathf.Max(0.01f, 1f - m_WeaponHandlingModifier);
	}

	public float GetRecoilAddedMultiplier()
	{
		return Mathf.Max(0.01f, 1f - m_RecoilControlModifier);
	}

	public float GetRecoilRecoveryMultiplier()
	{
		return Mathf.Max(0.01f, 1f + m_RecoilControlModifier);
	}

	public bool TryGetPreference(string _profileId, out UnitEquipmentVisualPreferenceEntry _preference)
	{
		if (m_EquipmentVisualPreferences != null && !string.IsNullOrWhiteSpace(_profileId))
		{
			for (int i = 0; i < m_EquipmentVisualPreferences.Length; i++)
			{
				if (!m_EquipmentVisualPreferences[i].MatchesProfile(_profileId))
					continue;

				_preference = m_EquipmentVisualPreferences[i];
				return true;
			}
		}

		_preference = default;
		return false;
	}

	public void SetPreference(UnitEquipmentVisualPreferenceEntry _preference)
	{
		if (string.IsNullOrWhiteSpace(_preference.ProfileId))
			return;

		if (m_EquipmentVisualPreferences == null || m_EquipmentVisualPreferences.Length == 0)
		{
			m_EquipmentVisualPreferences = new[] { _preference };
			return;
		}

		for (int i = 0; i < m_EquipmentVisualPreferences.Length; i++)
		{
			if (!m_EquipmentVisualPreferences[i].MatchesProfile(_preference.ProfileId))
				continue;

			m_EquipmentVisualPreferences[i] = _preference;
			return;
		}

		UnitEquipmentVisualPreferenceEntry[] expanded =
			new UnitEquipmentVisualPreferenceEntry[m_EquipmentVisualPreferences.Length + 1];
		for (int i = 0; i < m_EquipmentVisualPreferences.Length; i++)
			expanded[i] = m_EquipmentVisualPreferences[i];

		expanded[expanded.Length - 1] = _preference;
		m_EquipmentVisualPreferences = expanded;
	}

	public static UnitIndividualTraits GetOrCreate(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return null;

		if (!_unitRoot.TryGetComponent(out UnitIndividualTraits traits))
			traits = _unitRoot.AddComponent<UnitIndividualTraits>();

		return traits;
	}
	#endregion

	#region Private Methods
	private void RollEquipmentVisualPreferences()
	{
		EquipmentVisualProfileDefinition[] profiles = ResolveCatalogProfiles();
		if (profiles == null || profiles.Length == 0)
		{
			m_EquipmentVisualPreferences = Array.Empty<UnitEquipmentVisualPreferenceEntry>();
			return;
		}

		var rolled = new UnitEquipmentVisualPreferenceEntry[profiles.Length];
		for (int i = 0; i < profiles.Length; i++)
		{
			EquipmentVisualProfileDefinition profile = profiles[i];
			rolled[i] = profile != null
				? profile.RollRandomPreference()
				: default;
		}

		m_EquipmentVisualPreferences = rolled;
	}

	private EquipmentVisualProfileDefinition[] ResolveCatalogProfiles()
	{
		return m_EquipmentVisualProfileCatalog != null
			? m_EquipmentVisualProfileCatalog.Profiles
			: Array.Empty<EquipmentVisualProfileDefinition>();
	}

	private static float ClampCombatModifier(float _value)
	{
		return Mathf.Clamp(_value, -MaxCombatModifier, MaxCombatModifier);
	}
	#endregion
}

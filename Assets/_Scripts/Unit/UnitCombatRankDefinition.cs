using UnityEngine;

/// <summary>
/// Пресет ранга юнита из CombatBalanceTables.md — навыки для <see cref="UnitCombatStats"/>.
/// </summary>
[CreateAssetMenu(fileName = "UnitCombatRank", menuName = "Polygone/Combat/Unit Combat Rank", order = 20)]
public sealed class UnitCombatRankDefinition : ScriptableObject
{
	#region Serialized Fields
	[Tooltip("Ключ локализации названия ранга.")]
	[SerializeField] private string m_LocalizationKey;
	[SerializeField] private string m_DisplayName = "Soldier";
	[SerializeField, Range(0f, 100f)] private float m_Marksmanship = 50f;
	[SerializeField, Range(0f, 100f)] private float m_WeaponHandling = 50f;
	[SerializeField, Range(0f, 100f)] private float m_RecoilControl = 50f;
	#endregion

	#region Public Properties
	public string LocalizationKey => m_LocalizationKey;
	public string DisplayName => m_DisplayName;
	public float Marksmanship => m_Marksmanship;
	public float WeaponHandling => m_WeaponHandling;
	public float RecoilControl => m_RecoilControl;
	#endregion

	#region Public Methods
	public string GetLocalizedDisplayName()
	{
		if (string.IsNullOrWhiteSpace(m_LocalizationKey))
			return m_DisplayName;

		return LocalizationManager.Get(m_LocalizationKey, m_DisplayName);
	}

	public void ApplyTo(UnitCombatStats _stats)
	{
		if (_stats == null)
			return;

		_stats.ApplySkills(m_Marksmanship, m_WeaponHandling, m_RecoilControl);
	}
	#endregion
}

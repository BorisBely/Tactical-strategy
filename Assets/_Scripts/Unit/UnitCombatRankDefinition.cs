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
	[Tooltip("Минимальная задержка реакции на приказы / обнаружение цели (сек). Опытные бойцы реагируют быстрее.")]
	[SerializeField, Range(0.05f, 1.5f)] private float m_ReactionTimeMinSeconds = 0.32f;
	[Tooltip("Максимальная задержка реакции (сек). У низких рангов разброс шире и дольше.")]
	[SerializeField, Range(0.05f, 1.5f)] private float m_ReactionTimeMaxSeconds = 0.5f;
	[Tooltip("Минимальный интервал сканов целей (сек). Опытные бойцы сканируют чаще.")]
	[SerializeField, Min(0.05f)] private float m_VisionScanIntervalMinSeconds = 0.45f;
	[Tooltip("Максимальный интервал сканов целей (сек).")]
	[SerializeField, Min(0.05f)] private float m_VisionScanIntervalMaxSeconds = 0.6f;
	[Tooltip("Снижение штрафов веса (0-1). 0 = без бонуса, 0.3 = -30% к эффективной загрузке.")]
	[SerializeField, Range(0f, 1f)] private float m_WeightPenaltyReduction;
	#endregion

	#region Public Properties
	public string LocalizationKey => m_LocalizationKey;
	public string DisplayName => m_DisplayName;
	public float Marksmanship => m_Marksmanship;
	public float WeaponHandling => m_WeaponHandling;
	public float RecoilControl => m_RecoilControl;
	public float ReactionTimeMinSeconds => m_ReactionTimeMinSeconds;
	public float ReactionTimeMaxSeconds => m_ReactionTimeMaxSeconds;
	public float VisionScanIntervalMinSeconds => m_VisionScanIntervalMinSeconds;
	public float VisionScanIntervalMaxSeconds => m_VisionScanIntervalMaxSeconds;
	public float WeightPenaltyReduction => m_WeightPenaltyReduction;
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
		_stats.SetReactionTimeRange(m_ReactionTimeMinSeconds, m_ReactionTimeMaxSeconds);
		_stats.SetVisionScanIntervals(m_VisionScanIntervalMinSeconds, m_VisionScanIntervalMaxSeconds);
	}
	#endregion
}

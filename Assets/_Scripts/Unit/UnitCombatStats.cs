using System;
using UnityEngine;

/// <summary>
/// Навыки юнита, которые влияют на боевое прицеливание, точность и контроль отдачи.
/// Значения 50 считаются обученным базовым уровнем и дают нейтральные множители.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitCombatStats : MonoBehaviour
{
	#region Events
	public event Action<UnitCombatRankDefinition> RankPresetChanged;
	#endregion

	#region Constants
	private const float c_NeutralSkill = 50f;
	private const float c_MaxSkill = 100f;
	#endregion

	#region Serialized Fields
	[Header("Rank Preset")]
	[SerializeField] private UnitCombatRankDefinition m_RankPreset;
	[SerializeField] private bool m_ApplyRankPresetOnAwake = true;

	[Header("Shooting Skills")]
	[SerializeField, Range(0f, c_MaxSkill)] private float m_Marksmanship = c_NeutralSkill;
	[SerializeField, Range(0f, c_MaxSkill)] private float m_WeaponHandling = c_NeutralSkill;
	[SerializeField, Range(0f, c_MaxSkill)] private float m_RecoilControl = c_NeutralSkill;

	[Header("Reaction")]
	[Tooltip("Минимальная задержка реакции на приказы / обнаружение цели (сек).")]
	[SerializeField, Range(0.05f, 1.5f)] private float m_ReactionTimeMinSeconds = 0.32f;
	[Tooltip("Максимальная задержка реакции (сек).")]
	[SerializeField, Range(0.05f, 1.5f)] private float m_ReactionTimeMaxSeconds = 0.5f;

	[Header("Vision Scan")]
	[Tooltip("Минимальный интервал между сканами целей (сек). Меньше — быстрее переключение на новую цель.")]
	[SerializeField, Min(0.05f)] private float m_VisionScanIntervalMinSeconds = 0.25f;
	[Tooltip("Максимальный интервал между сканами целей (сек).")]
	[SerializeField, Min(0.05f)] private float m_VisionScanIntervalMaxSeconds = 0.45f;

	[Header("Skill Ranges")]
	[Tooltip("Множитель разброса при Marksmanship = 0.")]
	[SerializeField, Min(0.01f)] private float m_WorstMarksmanshipDispersionMultiplier = 1.25f;
	[Tooltip("Множитель разброса при Marksmanship = 100.")]
	[SerializeField, Min(0.01f)] private float m_BestMarksmanshipDispersionMultiplier = 0.75f;
	[Tooltip("Множитель времени прицеливания при WeaponHandling = 0.")]
	[SerializeField, Min(0.01f)] private float m_WorstHandlingAimTimeMultiplier = 1.25f;
	[Tooltip("Множитель времени прицеливания при WeaponHandling = 100.")]
	[SerializeField, Min(0.01f)] private float m_BestHandlingAimTimeMultiplier = 0.75f;
	[Tooltip("Множитель накопления отдачи при RecoilControl = 0.")]
	[SerializeField, Min(0.01f)] private float m_WorstRecoilAddedMultiplier = 1.2f;
	[Tooltip("Множитель накопления отдачи при RecoilControl = 100.")]
	[SerializeField, Min(0.01f)] private float m_BestRecoilAddedMultiplier = 0.8f;
	[Tooltip("Множитель восстановления отдачи при RecoilControl = 0.")]
	[SerializeField, Min(0.01f)] private float m_WorstRecoilRecoveryMultiplier = 0.8f;
	[Tooltip("Множитель восстановления отдачи при RecoilControl = 100.")]
	[SerializeField, Min(0.01f)] private float m_BestRecoilRecoveryMultiplier = 1.2f;
	#endregion

	#region Public Properties
	public UnitCombatRankDefinition RankPreset => m_RankPreset;
	public float Marksmanship => m_Marksmanship;
	public float WeaponHandling => m_WeaponHandling;
	public float RecoilControl => m_RecoilControl;
	public float ReactionTimeMinSeconds => m_ReactionTimeMinSeconds;
	public float ReactionTimeMaxSeconds => m_ReactionTimeMaxSeconds;
	public float VisionScanIntervalMinSeconds => m_VisionScanIntervalMinSeconds;
	public float VisionScanIntervalMaxSeconds => m_VisionScanIntervalMaxSeconds;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_ApplyRankPresetOnAwake && m_RankPreset != null)
			m_RankPreset.ApplyTo(this);
	}

	private void OnValidate()
	{
		NormalizeReactionTimeRange();
		NormalizeVisionScanIntervalRange();
	}
	#endregion

	#region Public Methods
	public void ApplyRankPreset(UnitCombatRankDefinition _rankPreset)
	{
		bool changed = m_RankPreset != _rankPreset;
		m_RankPreset = _rankPreset;
		if (_rankPreset != null)
			_rankPreset.ApplyTo(this);

		if (changed)
			RankPresetChanged?.Invoke(m_RankPreset);
	}

	public void ApplySkills(float _marksmanship, float _weaponHandling, float _recoilControl)
	{
		m_Marksmanship = Mathf.Clamp(_marksmanship, 0f, c_MaxSkill);
		m_WeaponHandling = Mathf.Clamp(_weaponHandling, 0f, c_MaxSkill);
		m_RecoilControl = Mathf.Clamp(_recoilControl, 0f, c_MaxSkill);
	}

	public void SetReactionTimeRange(float _minSeconds, float _maxSeconds)
	{
		m_ReactionTimeMinSeconds = Mathf.Clamp(_minSeconds, 0.05f, 1.5f);
		m_ReactionTimeMaxSeconds = Mathf.Clamp(_maxSeconds, 0.05f, 1.5f);
		NormalizeReactionTimeRange();
	}

	public void SetVisionScanIntervals(float _minSeconds, float _maxSeconds)
	{
		m_VisionScanIntervalMinSeconds = Mathf.Max(0.05f, _minSeconds);
		m_VisionScanIntervalMaxSeconds = Mathf.Max(0.05f, _maxSeconds);
		NormalizeVisionScanIntervalRange();
	}

	public float GetReactionDelaySeconds()
	{
		return SampleReactionDelaySeconds();
	}

	public float SampleReactionDelaySeconds()
	{
		NormalizeReactionTimeRange();
		return UnityEngine.Random.Range(m_ReactionTimeMinSeconds, m_ReactionTimeMaxSeconds);
	}

	public float GetVisionScanIntervalMinSeconds()
	{
		return m_VisionScanIntervalMinSeconds;
	}

	public float GetVisionScanIntervalMaxSeconds()
	{
		return m_VisionScanIntervalMaxSeconds;
	}

	public float SampleVisionScanIntervalSeconds()
	{
		return UnityEngine.Random.Range(m_VisionScanIntervalMinSeconds, m_VisionScanIntervalMaxSeconds);
	}

	public float GetCommandVisionStaggerDelaySeconds()
	{
		return SampleVisionScanIntervalSeconds() * 0.5f;
	}

	public float GetDispersionMultiplier()
	{
		return EvaluateSkillMultiplier(
			m_Marksmanship,
			m_WorstMarksmanshipDispersionMultiplier,
			m_BestMarksmanshipDispersionMultiplier);
	}

	public float GetAimTimeMultiplier()
	{
		return EvaluateSkillMultiplier(
			m_WeaponHandling,
			m_WorstHandlingAimTimeMultiplier,
			m_BestHandlingAimTimeMultiplier);
	}

	public float GetRecoilAddedMultiplier()
	{
		return EvaluateSkillMultiplier(
			m_RecoilControl,
			m_WorstRecoilAddedMultiplier,
			m_BestRecoilAddedMultiplier);
	}

	public float GetRecoilRecoveryMultiplier()
	{
		return EvaluateSkillMultiplier(
			m_RecoilControl,
			m_WorstRecoilRecoveryMultiplier,
			m_BestRecoilRecoveryMultiplier);
	}
	#endregion

	#region Private Methods
	private void NormalizeReactionTimeRange()
	{
		m_ReactionTimeMinSeconds = Mathf.Clamp(m_ReactionTimeMinSeconds, 0.05f, 1.5f);
		m_ReactionTimeMaxSeconds = Mathf.Clamp(m_ReactionTimeMaxSeconds, 0.05f, 1.5f);
		if (m_ReactionTimeMaxSeconds < m_ReactionTimeMinSeconds)
			m_ReactionTimeMaxSeconds = m_ReactionTimeMinSeconds;
	}

	private void NormalizeVisionScanIntervalRange()
	{
		m_VisionScanIntervalMinSeconds = Mathf.Max(0.05f, m_VisionScanIntervalMinSeconds);
		m_VisionScanIntervalMaxSeconds = Mathf.Max(m_VisionScanIntervalMinSeconds, m_VisionScanIntervalMaxSeconds);
	}

	private static float EvaluateSkillMultiplier(float _skill, float _worst, float _best)
	{
		float normalized = Mathf.InverseLerp(0f, c_MaxSkill, _skill);
		return Mathf.Max(0.01f, Mathf.Lerp(_worst, _best, normalized));
	}
	#endregion
}

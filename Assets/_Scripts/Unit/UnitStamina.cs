using System;
using UnityEngine;

[DisallowMultipleComponent]
public class UnitStamina : MonoBehaviour
{
	#region Events
	public event Action<float> StaminaChanged;
	#endregion

	#region Constants
	private const float MaxStamina = 100f;
	private const float ExhaustedRecoveryThreshold = 10f;

	private const float LoadLight = 0.3f;
	private const float LoadHeavy = 0.6f;

	private const float DrainLight = 1.15f;
	private const float DrainMedium = 1.85f;
	private const float DrainHeavy = 3.85f;

	private const float RecoveryStanding = 1.65f;
	private const float RecoveryCrouch = 2.2f;
	private const float RecoveryProne = 2.5f;

	private const float MaxReadyStamina = 100f;
	private const float ReadyStaminaRecoveryRate = 3f;
	private const float JuniorReadyDrainRate = 100f / (30f * 60f);
	private const float SeniorReadyDrainRate = 100f / (50f * 60f);
	#endregion

	#region Serialized Fields
	[SerializeField, Range(0f, 100f)] private float m_Stamina = 100f;

	[Header("Debug (read-only)")]
	[SerializeField, Range(0f, 1f)] private float m_LoadRatio;
	[SerializeField] private float m_CurrentDrainRate;
	[SerializeField] private float m_CurrentRecoveryRate;
#pragma warning disable CS0414 // Assigned for Inspector display only.
	[SerializeField] private bool m_IsRunning;
	[SerializeField] private bool m_IsRecovering;
#pragma warning restore CS0414
	[SerializeField] private float m_CargoWeightDebug;
	[SerializeField] private float m_MaxWeightDebug;

	[Header("Ready Mode Stamina")]
	[SerializeField, Range(0f, 100f)] private float m_ReadyStamina = 100f;
	[SerializeField] private float m_ReadyStaminaDrainRate;
	[SerializeField] private bool m_IsReadyStaminaExhausted;
	#endregion

	#region Private Fields
	private CharacterInventory m_Inventory;
	private UnitClickToMove m_ClickToMove;
	private UnitNavLocomotionDriver m_NavDriver;
	private UnitAnimatorStance m_Stance;
	private Animator m_Animator;
	private UnitCombatStats m_CombatStats;
	private UnitWeaponReadyHandsLayer m_ReadyHands;
	private UnitCombatCondition m_CombatCondition;
	private bool m_ForceWalkActive;
	private int m_LastStaminaBucket;
	private float m_BaseRotateSpeed = 6f;
	private float m_RankReduction;
	#endregion

	#region Public Properties
	public float Stamina => m_Stamina;
	public float StaminaRatio => m_Stamina / MaxStamina;
	public bool IsExhausted => m_Stamina <= 0f;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Inventory = GetComponent<CharacterInventory>();
		m_ClickToMove = GetComponent<UnitClickToMove>();
		m_NavDriver = GetComponent<UnitNavLocomotionDriver>();
		m_Stance = GetComponent<UnitAnimatorStance>();
		m_Animator = GetComponentInChildren<Animator>(true);
		m_CombatStats = GetComponent<UnitCombatStats>();
		m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		m_CombatCondition = GetComponent<UnitCombatCondition>();

		if (m_ClickToMove != null)
			m_BaseRotateSpeed = m_ClickToMove.RotateSpeed;
		else if (m_NavDriver != null)
			m_BaseRotateSpeed = m_NavDriver.RotateSpeed;

		UnitCombatRankDefinition rank = m_CombatStats != null ? m_CombatStats.RankPreset : null;
		m_RankReduction = rank != null ? rank.WeightPenaltyReduction : 0f;
		InitReadyStaminaDrainRate();
	}

	private void Update()
	{
		ApplyLoadBasedRotateSpeed();

		bool running = IsRunning();
		if (running)
		{
			m_IsRunning = true;
			m_IsRecovering = false;
			m_CurrentRecoveryRate = 0f;
			DrainStamina();
		}
		else if (!IsMoving())
		{
			m_IsRunning = false;
			m_IsRecovering = true;
			m_CurrentDrainRate = 0f;
			RecoverStamina();
		}
		else
		{
			m_IsRunning = false;
			m_IsRecovering = false;
			m_CurrentDrainRate = 0f;
			m_CurrentRecoveryRate = 0f;
		}

		UpdateReadyStamina();
		ApplyReadyExhaustionPenalties();

		int bucket = ResolveStaminaBucket();
		if (bucket != m_LastStaminaBucket)
		{
			m_LastStaminaBucket = bucket;
			StaminaChanged?.Invoke(m_Stamina);
		}
	}

	private int ResolveStaminaBucket()
	{
		float ratio = StaminaRatio;
		if (ratio > 0.6f) return 2;
		if (ratio > 0.3f) return 1;
		return 0;
	}
	#endregion

	#region Public Methods
	public string GetLocalizedStaminaStatus()
	{
		float ratio = StaminaRatio;
		if (ratio > 0.6f)
			return string.Empty;
		if (ratio > 0.3f)
			return LocalizationManager.Get("stamina.tired", "Устал");
		if (ratio > 0f)
			return LocalizationManager.Get("stamina.exhausted", "Выдохся");
		return LocalizationManager.Get("stamina.depleted", "Истощён");
	}
	#endregion

	#region Private Methods
	private bool IsRunning()
	{
		if (m_ClickToMove == null || !m_ClickToMove.HasMoveIntent)
			return false;

		return m_ClickToMove.IsSprintMoveMode || IsRunTier();
	}

	private bool IsRunTier()
	{
		if (m_Animator == null)
			return false;
		return m_Animator.GetInteger("LocomotionTier") == 1;
	}

	private bool IsMoving()
	{
		return m_ClickToMove != null && m_ClickToMove.HasMoveIntent;
	}

	private void DrainStamina()
	{
		m_LoadRatio = GetEffectiveLoadRatio();
		m_CurrentDrainRate = m_LoadRatio <= LoadLight ? DrainLight : m_LoadRatio <= LoadHeavy ? DrainMedium : DrainHeavy;
		m_Stamina = Mathf.Max(0f, m_Stamina - m_CurrentDrainRate * Time.deltaTime);

		if (m_Stamina <= 0f && !m_ForceWalkActive)
			ApplyForceWalk();
		else if (m_Stamina > ExhaustedRecoveryThreshold && m_ForceWalkActive)
			ClearForceWalk();
	}

	private void RecoverStamina()
	{
		m_CurrentRecoveryRate = GetRecoveryRate();
		m_Stamina = Mathf.Min(MaxStamina, m_Stamina + m_CurrentRecoveryRate * Time.deltaTime);

		if (m_Stamina > ExhaustedRecoveryThreshold && m_ForceWalkActive)
			ClearForceWalk();
	}

	private float GetRecoveryRate()
	{
		if (m_Stance == null)
			return RecoveryStanding;

		return m_Stance.CurrentStance switch
		{
			LocomotionStance.Crouch => RecoveryCrouch,
			LocomotionStance.Prone => RecoveryProne,
			_ => RecoveryStanding
		};
	}

	private float GetEffectiveLoadRatio()
	{
		if (m_Inventory == null)
			return 0f;

		m_CargoWeightDebug = m_Inventory.CargoWeightKg;
		m_MaxWeightDebug = m_Inventory.MaxBagWeightKg;

		float max = m_Inventory.MaxBagWeightKg;
		if (max <= 0f)
			return 0f;

		float rawLoad = Mathf.Clamp01(m_Inventory.CargoWeightKg / max);
		return rawLoad * (1f - m_RankReduction);
	}

	private void ApplyLoadBasedRotateSpeed()
	{
		float load = GetEffectiveLoadRatio();
		float speed = load <= LoadLight ? m_BaseRotateSpeed :
		              load <= LoadHeavy ? 4f :
		              2.5f;

		if (m_ClickToMove != null)
			m_ClickToMove.RotateSpeed = speed;
		if (m_NavDriver != null)
			m_NavDriver.RotateSpeed = speed;
	}

	private void ApplyForceWalk()
	{
		m_ForceWalkActive = true;
		if (m_ClickToMove != null)
			m_ClickToMove.ForceWalkMoveMode();
	}

	private void ClearForceWalk()
	{
		m_ForceWalkActive = false;
	}

	private void InitReadyStaminaDrainRate()
	{
		if (m_CombatStats == null || m_CombatStats.RankPreset == null)
		{
			m_ReadyStaminaDrainRate = JuniorReadyDrainRate;
			return;
		}

		m_ReadyStaminaDrainRate = IsJuniorRank(m_CombatStats.RankPreset) ? JuniorReadyDrainRate : SeniorReadyDrainRate;
	}

	private bool IsJuniorRank(UnitCombatRankDefinition _rank)
	{
		if (_rank == null)
			return true;

		string key = _rank.LocalizationKey ?? "";
		return key.Contains("Recruit") || key.Contains("Soldier");
	}

	private void UpdateReadyStamina()
	{
		if (m_ReadyHands != null && m_ReadyHands.WantsReady && m_ReadyHands.IsWeaponEquipped())
		{
			m_ReadyStamina -= m_ReadyStaminaDrainRate * Time.deltaTime;
		}
		else
		{
			m_ReadyStamina = Mathf.Min(MaxReadyStamina, m_ReadyStamina + ReadyStaminaRecoveryRate * Time.deltaTime);
		}

		bool exhausted = m_ReadyStamina <= 0f;
		if (exhausted && !m_IsReadyStaminaExhausted)
		{
			m_IsReadyStaminaExhausted = true;
			m_ReadyStamina = 0f;
		}
		else if (!exhausted && m_IsReadyStaminaExhausted)
		{
			m_IsReadyStaminaExhausted = false;
		}
	}

	private void ApplyReadyExhaustionPenalties()
	{
		float speedMul = m_IsReadyStaminaExhausted ? 0.8f : 1f;
		if (m_ClickToMove != null)
			m_ClickToMove.StaminaSpeedMultiplier = speedMul;
		if (m_NavDriver != null)
			m_NavDriver.StaminaSpeedMultiplier = speedMul;

		m_CombatCondition?.SetReadyStaminaExhausted(m_IsReadyStaminaExhausted);
	}
	#endregion
}

using UnityEngine;

[DisallowMultipleComponent]
public class UnitStamina : MonoBehaviour
{
	#region Constants
	private const float MaxStamina = 100f;
	private const float ExhaustedRecoveryThreshold = 10f;

	private const float LoadLight = 0.3f;
	private const float LoadHeavy = 0.6f;

	private const float DrainLight = 2.3f;
	private const float DrainMedium = 3.7f;
	private const float DrainHeavy = 7.7f;

	private const float RecoveryStanding = 1.65f;
	private const float RecoveryCrouch = 2.2f;
	private const float RecoveryProne = 2.5f;
	#endregion

	#region Serialized Fields
	[SerializeField, Range(0f, 100f)] private float m_Stamina = 100f;
	#endregion

	#region Private Fields
	private CharacterInventory m_Inventory;
	private UnitClickToMove m_ClickToMove;
	private UnitAnimatorStance m_Stance;
	private Animator m_Animator;
	private bool m_ForceWalkActive;
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
		m_Stance = GetComponent<UnitAnimatorStance>();
		m_Animator = GetComponentInChildren<Animator>(true);
	}

	private void Update()
	{
		if (IsRunning())
			DrainStamina();
		else if (!IsMoving())
			RecoverStamina();
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
		float load = GetLoadRatio();
		float rate = load <= LoadLight ? DrainLight : load <= LoadHeavy ? DrainMedium : DrainHeavy;
		m_Stamina = Mathf.Max(0f, m_Stamina - rate * Time.deltaTime);

		if (m_Stamina <= 0f && !m_ForceWalkActive)
			ApplyForceWalk();
		else if (m_Stamina > ExhaustedRecoveryThreshold && m_ForceWalkActive)
			ClearForceWalk();
	}

	private void RecoverStamina()
	{
		float rate = GetRecoveryRate();
		m_Stamina = Mathf.Min(MaxStamina, m_Stamina + rate * Time.deltaTime);

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

	private float GetLoadRatio()
	{
		if (m_Inventory == null)
			return 0f;

		float max = m_Inventory.MaxBagWeightKg;
		if (max <= 0f)
			return 0f;

		return Mathf.Clamp01(m_Inventory.CargoWeightKg / max);
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
	#endregion
}

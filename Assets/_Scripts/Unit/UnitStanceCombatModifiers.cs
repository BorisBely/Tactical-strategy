using UnityEngine;

/// <summary>
/// Множители разброса, времени прицеливания, kick и recovery по стойке и движению.
/// Лёжа использует числа сидя: locomotion Prone отключён.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitStanceCombatModifiers : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitFallenDragController m_FallenDragController;

	[Header("Standing Still")]
	[SerializeField, Min(0.01f)] private float m_StandingSpreadMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_StandingAimTimeMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_StandingRecoilMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_StandingRecoilRecoveryMultiplier = 1f;

	[Header("Crouch Still")]
	[SerializeField, Min(0.01f)] private float m_CrouchSpreadMultiplier = 0.92f;
	[SerializeField, Min(0.01f)] private float m_CrouchAimTimeMultiplier = 1.1f;
	[SerializeField, Min(0.01f)] private float m_CrouchRecoilMultiplier = 0.95f;
	[SerializeField, Min(0.01f)] private float m_CrouchRecoilRecoveryMultiplier = 1.1f;

	[Header("Walk Standing")]
	[SerializeField, Min(0.01f)] private float m_WalkStandingSpreadMultiplier = 1.25f;
	[SerializeField, Min(0.01f)] private float m_WalkStandingAimTimeMultiplier = 1.55f;
	[SerializeField, Min(0.01f)] private float m_WalkStandingRecoilMultiplier = 1.25f;
	[SerializeField, Min(0.01f)] private float m_WalkStandingRecoilRecoveryMultiplier = 0.85f;

	[Header("Walk Crouch")]
	[SerializeField, Min(0.01f)] private float m_WalkCrouchSpreadMultiplier = 1.12f;
	[SerializeField, Min(0.01f)] private float m_WalkCrouchAimTimeMultiplier = 1.3f;
	[SerializeField, Min(0.01f)] private float m_WalkCrouchRecoilMultiplier = 1.1f;
	[SerializeField, Min(0.01f)] private float m_WalkCrouchRecoilRecoveryMultiplier = 0.95f;

	[Header("Sprint")]
	[SerializeField, Min(0.01f)] private float m_SprintSpreadMultiplier = 2.5f;
	[SerializeField, Min(0.01f)] private float m_SprintAimTimeMultiplier = 1.8f;
	[SerializeField, Min(0.01f)] private float m_SprintRecoilMultiplier = 1.6f;
	[SerializeField, Min(0.01f)] private float m_SprintRecoilRecoveryMultiplier = 0.5f;

	[Header("Dragging Fallen")]
	[SerializeField, Min(0.01f)] private float m_DraggingFallenSpreadMultiplier = 1.04f;
	[SerializeField, Min(0.01f)] private float m_DraggingFallenAimTimeMultiplier = 1.1f;
	[SerializeField, Min(0.01f)] private float m_DraggingFallenRecoilMultiplier = 1.05f;
	[SerializeField, Min(0.01f)] private float m_DraggingFallenRecoilRecoveryMultiplier = 0.9f;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_FallenDragController == null)
			m_FallenDragController = GetComponent<UnitFallenDragController>();
	}
	#endregion

	#region Public Methods
	public float GetSpreadMultiplier()
	{
		ResolveCurrentPostureMultipliers(out float spread, out _, out _, out _);
		return spread;
	}

	public float GetAimTimeMultiplier()
	{
		ResolveCurrentPostureMultipliers(out _, out float aimTime, out _, out _);
		return aimTime;
	}

	public float GetRecoilAddedMultiplier()
	{
		ResolveCurrentPostureMultipliers(out _, out _, out float recoil, out _);
		return recoil;
	}

	public float GetRecoilRecoveryMultiplier()
	{
		ResolveCurrentPostureMultipliers(out _, out _, out _, out float recovery);
		return recovery;
	}

	public WeaponShotPostureLogInfo GetPostureLogInfo(bool _isSprinting)
	{
		ResolveCurrentPostureMultipliers(out float spread, out float aimTime, out float recoil, out _);
		return new WeaponShotPostureLogInfo(
			ResolvePostureLabel(_isSprinting),
			spread,
			aimTime,
			recoil,
			_isSprinting);
	}
	#endregion

	#region Private Methods
	private void ResolveCurrentPostureMultipliers(
		out float _spread,
		out float _aimTime,
		out float _recoil,
		out float _recovery)
	{
		if (m_FallenDragController != null && m_FallenDragController.IsDragging)
		{
			_spread = m_DraggingFallenSpreadMultiplier;
			_aimTime = m_DraggingFallenAimTimeMultiplier;
			_recoil = m_DraggingFallenRecoilMultiplier;
			_recovery = m_DraggingFallenRecoilRecoveryMultiplier;
			return;
		}

		if (IsSprinting())
		{
			_spread = m_SprintSpreadMultiplier;
			_aimTime = m_SprintAimTimeMultiplier;
			_recoil = m_SprintRecoilMultiplier;
			_recovery = m_SprintRecoilRecoveryMultiplier;
			return;
		}

		bool moving = IsMoving();
		LocomotionStance stance = m_Stance != null ? m_Stance.CurrentStance : LocomotionStance.Standing;

		// Prone locomotion is disabled: Crouch and Prone share crouch numbers.
		if (stance == LocomotionStance.Crouch || stance == LocomotionStance.Prone)
		{
			if (moving)
			{
				_spread = m_WalkCrouchSpreadMultiplier;
				_aimTime = m_WalkCrouchAimTimeMultiplier;
				_recoil = m_WalkCrouchRecoilMultiplier;
				_recovery = m_WalkCrouchRecoilRecoveryMultiplier;
				return;
			}

			_spread = m_CrouchSpreadMultiplier;
			_aimTime = m_CrouchAimTimeMultiplier;
			_recoil = m_CrouchRecoilMultiplier;
			_recovery = m_CrouchRecoilRecoveryMultiplier;
			return;
		}

		if (moving)
		{
			_spread = m_WalkStandingSpreadMultiplier;
			_aimTime = m_WalkStandingAimTimeMultiplier;
			_recoil = m_WalkStandingRecoilMultiplier;
			_recovery = m_WalkStandingRecoilRecoveryMultiplier;
			return;
		}

		_spread = m_StandingSpreadMultiplier;
		_aimTime = m_StandingAimTimeMultiplier;
		_recoil = m_StandingRecoilMultiplier;
		_recovery = m_StandingRecoilRecoveryMultiplier;
	}

	private bool IsMoving()
	{
		if (m_LocomotionDriver != null && m_LocomotionDriver.enabled)
			return m_LocomotionDriver.HasMoveIntent;
		return m_ClickToMove != null && m_ClickToMove.enabled && m_ClickToMove.HasMoveIntent;
	}

	private bool IsSprinting()
	{
		if (m_LocomotionDriver != null && m_LocomotionDriver.enabled)
			return m_LocomotionDriver.IsSprintMoveMode;
		return m_ClickToMove != null && m_ClickToMove.enabled && m_ClickToMove.IsSprintMoveMode;
	}

	private string ResolvePostureLabel(bool _isSprinting)
	{
		if (_isSprinting)
			return "спринт";

		if (m_FallenDragController != null && m_FallenDragController.IsDragging)
			return "тащит";

		bool moving = IsMoving();
		LocomotionStance stance = m_Stance != null ? m_Stance.CurrentStance : LocomotionStance.Standing;
		if (stance == LocomotionStance.Prone)
			return moving ? "ползок" : "лёжа";
		if (stance == LocomotionStance.Crouch)
			return moving ? "шаг сидя" : "сидя";

		return moving ? "шаг стоя" : "стоя";
	}
	#endregion
}

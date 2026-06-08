using UnityEngine;

/// <summary>
/// Множители разброса, времени прицеливания и отдачи по комбинации стойки и движения.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitStanceCombatModifiers : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;

	[Header("Standing Still")]
	[SerializeField, Min(0.01f)] private float m_StandingSpreadMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_StandingAimTimeMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_StandingRecoilMultiplier = 1f;

	[Header("Crouch Still")]
	[SerializeField, Min(0.01f)] private float m_CrouchSpreadMultiplier = 0.92f;
	[SerializeField, Min(0.01f)] private float m_CrouchAimTimeMultiplier = 1.1f;
	[SerializeField, Min(0.01f)] private float m_CrouchRecoilMultiplier = 0.95f;

	[Header("Walk Standing")]
	[SerializeField, Min(0.01f)] private float m_WalkStandingSpreadMultiplier = 1.25f;
	[SerializeField, Min(0.01f)] private float m_WalkStandingAimTimeMultiplier = 1.55f;
	[SerializeField, Min(0.01f)] private float m_WalkStandingRecoilMultiplier = 1.25f;

	[Header("Walk Crouch")]
	[SerializeField, Min(0.01f)] private float m_WalkCrouchSpreadMultiplier = 1.12f;
	[SerializeField, Min(0.01f)] private float m_WalkCrouchAimTimeMultiplier = 1.3f;
	[SerializeField, Min(0.01f)] private float m_WalkCrouchRecoilMultiplier = 1.1f;
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
	}
	#endregion

	#region Public Methods
	public float GetSpreadMultiplier()
	{
		ResolveCurrentPostureMultipliers(out float spread, out _, out _);
		return spread;
	}

	public float GetAimTimeMultiplier()
	{
		ResolveCurrentPostureMultipliers(out _, out float aimTime, out _);
		return aimTime;
	}

	public float GetRecoilAddedMultiplier()
	{
		ResolveCurrentPostureMultipliers(out _, out _, out float recoil);
		return recoil;
	}

	public WeaponShotPostureLogInfo GetPostureLogInfo(bool _isSprinting)
	{
		ResolveCurrentPostureMultipliers(out float spread, out float aimTime, out float recoil);
		return new WeaponShotPostureLogInfo(
			ResolvePostureLabel(_isSprinting),
			spread,
			aimTime,
			recoil,
			_isSprinting);
	}
	#endregion

	#region Private Methods
	private void ResolveCurrentPostureMultipliers(out float _spread, out float _aimTime, out float _recoil)
	{
		bool moving = IsMoving();
		LocomotionStance stance = m_Stance != null ? m_Stance.CurrentStance : LocomotionStance.Standing;

		if (stance == LocomotionStance.Crouch || stance == LocomotionStance.Prone)
		{
			if (moving)
			{
				_spread = m_WalkCrouchSpreadMultiplier;
				_aimTime = m_WalkCrouchAimTimeMultiplier;
				_recoil = m_WalkCrouchRecoilMultiplier;
				return;
			}

			_spread = m_CrouchSpreadMultiplier;
			_aimTime = m_CrouchAimTimeMultiplier;
			_recoil = m_CrouchRecoilMultiplier;
			return;
		}

		if (moving)
		{
			_spread = m_WalkStandingSpreadMultiplier;
			_aimTime = m_WalkStandingAimTimeMultiplier;
			_recoil = m_WalkStandingRecoilMultiplier;
			return;
		}

		_spread = m_StandingSpreadMultiplier;
		_aimTime = m_StandingAimTimeMultiplier;
		_recoil = m_StandingRecoilMultiplier;
	}

	private bool IsMoving()
	{
		if (m_LocomotionDriver != null)
			return m_LocomotionDriver.HasMoveIntent;
		return m_ClickToMove != null && m_ClickToMove.HasMoveIntent;
	}

	private string ResolvePostureLabel(bool _isSprinting)
	{
		if (_isSprinting)
			return "спринт";

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

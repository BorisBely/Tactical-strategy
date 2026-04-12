using UnityEngine;

/// <summary>
/// Синхронизирует int <c>WeaponMode</c> на <see cref="Animator"/> с экипировкой в <see cref="UnitEquipment"/>.
/// Без оружия — <see cref="LocomotionWeaponMode.Unarmed"/> (0).
/// При наличии <see cref="UnitWeaponReadyHandsLayer"/> в «не на готове» в допустимом контексте граф остаётся безоружным, модель оружия — из экипировки.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitAnimatorWeaponMode : MonoBehaviour
{
	public const string ParamWeaponMode = "WeaponMode";
	public const string ParamStance = "Stance";

	private static readonly int s_WeaponMode = Animator.StringToHash(ParamWeaponMode);
	private static readonly int s_Stance = Animator.StringToHash(ParamStance);

	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoadingController;
	[SerializeField] private UnitWeaponReloadController m_WeaponReloadController;

	[Header("Плавность")]
	[SerializeField, Min(0.02f)] private float m_WeaponModeCrossFadeSeconds = 0.22f;

	private int m_LastWeaponModeValue = -1;

	private void Awake()
	{
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_MagazineLoadingController == null)
			m_MagazineLoadingController = GetComponent<UnitMagazineLoadingController>();
		if (m_WeaponReloadController == null)
			m_WeaponReloadController = GetComponent<UnitWeaponReloadController>();
	}

	private void OnEnable()
	{
		m_LastWeaponModeValue = -1;
		PushWeaponModeIfChanged();
	}

	private void LateUpdate()
	{
		PushWeaponModeIfChanged();
	}

	private void PushWeaponModeIfChanged()
	{
		if (m_Animator == null)
			return;

		ItemDefinition current = m_Equipment != null ? m_Equipment.EquippedDefinition : null;
		int value = ComputeEffectiveWeaponMode(current);

		if (value != m_LastWeaponModeValue)
		{
			m_LastWeaponModeValue = value;
			m_Animator.SetInteger(s_WeaponMode, value);
			ForceIdleStateForCurrentMode();
		}
	}

	private int ComputeEffectiveWeaponMode(ItemDefinition current)
	{
		if (current == null || !current.IsEquipment)
			return (int)LocomotionWeaponMode.Unarmed;

		if (current.EquipmentKind != EquipmentKind.Weapon)
			return (int)LocomotionWeaponMode.Unarmed;

		bool isMagazineLoading = m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine;
		bool isWeaponReloading = m_WeaponReloadController != null && m_WeaponReloadController.IsReloadingWeapon;
		if (!isMagazineLoading && !isWeaponReloading &&
			m_ReadyHands != null && m_ReadyHands.ShouldUseUnarmedLocomotionBranch())
			return (int)LocomotionWeaponMode.Unarmed;

		return current.WeaponType == WeaponType.Secondary
			? (int)LocomotionWeaponMode.Pistol
			: (int)LocomotionWeaponMode.Rifle;
	}

	private void ForceIdleStateForCurrentMode()
	{
		if (m_Animator == null)
			return;

		int stance = m_Animator.GetInteger(s_Stance);
		string targetState;

		if (m_LastWeaponModeValue == (int)LocomotionWeaponMode.Rifle)
		{
			targetState = stance switch
			{
				1 => "RifleCrouch_Idle",
				2 => "RifleProne_Idle",
				_ => "RifleStand_Idle"
			};
		}
		else if (m_LastWeaponModeValue == (int)LocomotionWeaponMode.Pistol)
		{
			targetState = stance switch
			{
				1 => "PistolCrouch_Idle",
				2 => "PistolProne_Idle",
				_ => "PistolStand_Idle"
			};
		}
		else
		{
			targetState = stance switch
			{
				1 => "Crouch_Idle",
				2 => "Prone_Idle",
				_ => "Stand_Idle"
			};
		}

		// Переключаемся на корректный idle в ветке, иначе можно застрять в прежнем подграфе даже при смене WeaponMode.
		m_Animator.CrossFadeInFixedTime(targetState, m_WeaponModeCrossFadeSeconds, 0);
	}
}

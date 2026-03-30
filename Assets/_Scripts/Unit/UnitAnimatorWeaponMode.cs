using UnityEngine;

/// <summary>
/// Синхронизирует int <c>WeaponMode</c> на <see cref="Animator"/> с экипировкой в <see cref="UnitEquipment"/>.
/// Без оружия — <see cref="LocomotionWeaponMode.Unarmed"/> (0). Значения заданы в <see cref="ItemDefinition"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitAnimatorWeaponMode : MonoBehaviour
{
	public const string ParamWeaponMode = "WeaponMode";

	private static readonly int s_WeaponMode = Animator.StringToHash(ParamWeaponMode);

	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitEquipment m_Equipment;

	private ItemDefinition m_LastEquipped;

	private void Awake()
	{
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
	}

	private void OnEnable()
	{
		m_LastEquipped = null;
		PushWeaponModeIfChanged(force: true);
	}

	private void LateUpdate()
	{
		PushWeaponModeIfChanged(force: false);
	}

	private void PushWeaponModeIfChanged(bool force)
	{
		if (m_Animator == null)
			return;

		ItemDefinition current = m_Equipment != null ? m_Equipment.EquippedDefinition : null;
		if (!force && ReferenceEquals(current, m_LastEquipped))
			return;

		m_LastEquipped = current;

		int value = current != null && current.IsEquipment
			? (int)current.LocomotionWeaponMode
			: (int)LocomotionWeaponMode.Unarmed;

		m_Animator.SetInteger(s_WeaponMode, value);
	}
}

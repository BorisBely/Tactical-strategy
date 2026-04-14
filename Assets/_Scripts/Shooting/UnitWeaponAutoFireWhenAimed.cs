using UnityEngine;

/// <summary>
/// Удерживает «курок» виртуально: когда <see cref="EquippedWeaponTransientState.AimProgress01"/> достигает порога
/// и выполнены те же базовые условия, что и у <see cref="UnitWeaponFireController"/>, вызывает
/// <see cref="UnitWeaponFireController.StartFiring"/>; иначе <see cref="UnitWeaponFireController.StopFiring"/>.
/// Должен выполняться раньше <see cref="UnitWeaponFireController"/> (порядок 54 &lt; 56).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(54)]
public sealed class UnitWeaponAutoFireWhenAimed : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitVision m_Vision;
	[SerializeField] private UnitBusyState m_BusyState;

	[Header("Условия")]
	[Tooltip("Считаем «прицелился», когда AimProgress >= этого значения (0..1).")]
	[SerializeField, Range(0.5f, 1f)] private float m_MinAimProgress01 = 0.98f;
	[Tooltip("Дублирует смысл UnitWeaponFireController: не стрелять без ready.")]
	[SerializeField] private bool m_RequireReady = true;
	[Tooltip("Дублирует смысл UnitWeaponFireController: не стрелять без видимой цели.")]
	[SerializeField] private bool m_RequireVisibleTarget = true;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
	}

	private void Update()
	{
		if (m_FireController == null)
			return;

		if (ShouldHoldVirtualTrigger())
			m_FireController.StartFiring();
		else
			m_FireController.StopFiring();
	}

	private void OnDisable()
	{
		m_FireController?.StopFiring();
	}
	#endregion

	#region Private Methods
	private bool ShouldHoldVirtualTrigger()
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			return false;

		if (m_WeaponRuntime.CurrentWeaponDefinition == null)
			return false;

		if (!m_WeaponRuntime.HasAmmoInMagazine)
			return false;

		if (m_RequireReady && (m_ReadyHands == null || !m_ReadyHands.IsWeaponReadyToFire()))
			return false;

		if (m_BusyState != null && m_BusyState.HasReason(UnitBusyState.BusyReason.Reload))
			return false;

		if (m_RequireVisibleTarget && (m_Vision == null || m_Vision.VisibleTarget == null))
			return false;

		return m_WeaponRuntime.TransientState.AimProgress01 >= m_MinAimProgress01;
	}
	#endregion
}

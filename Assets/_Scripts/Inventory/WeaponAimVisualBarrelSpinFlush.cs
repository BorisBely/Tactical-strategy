using UnityEngine;

/// <summary>
/// Emits the deferred WeaponSpin line after visual recoil overlay (order 200).
/// AimBarrel is captured at UnitWeaponAiming 65; VisualBarrel is measured here.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitWeaponAiming))]
[DefaultExecutionOrder(201)]
public sealed class WeaponAimVisualBarrelSpinFlush : MonoBehaviour
{
	[SerializeField] private UnitWeaponAiming m_Aiming;

	private void Awake()
	{
		if (m_Aiming == null)
			m_Aiming = GetComponent<UnitWeaponAiming>();
	}

	private void LateUpdate()
	{
		if (m_Aiming == null)
			m_Aiming = GetComponent<UnitWeaponAiming>();
		m_Aiming?.FlushWeaponSpinLogAfterVisualRecoil();
	}
}

using UnityEngine;

/// <summary>
/// Удерживает «курок» виртуально: когда прицеливание достаточно для выбранного режима
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
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
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
		return m_FireController != null && m_FireController.ShouldHoldVirtualTrigger();
	}
	#endregion
}

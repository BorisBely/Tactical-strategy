using UnityEngine;

/// <summary>
/// Устаревший драйвер бесконечного удержания спуска.
/// Заменён на <see cref="UnitWeaponFireDisciplineController"/>: при наличии дисциплины этот компонент
/// отключается сам и не вмешивается в серии/паузы.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(54)]
public sealed class UnitWeaponAutoFireWhenAimed : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponFireDisciplineController m_FireDisciplineController;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_FireDisciplineController == null)
			m_FireDisciplineController = GetComponent<UnitWeaponFireDisciplineController>();

		if (m_FireDisciplineController != null)
			enabled = false;
	}

	private void Update()
	{
		if (m_FireDisciplineController != null && m_FireDisciplineController.enabled)
		{
			enabled = false;
			return;
		}

		if (m_FireController == null)
			return;

		if (ShouldHoldVirtualTrigger())
			m_FireController.StartFiring();
		else
			m_FireController.StopFiring();
	}

	private void OnDisable()
	{
		if (m_FireDisciplineController != null && m_FireDisciplineController.enabled)
			return;

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

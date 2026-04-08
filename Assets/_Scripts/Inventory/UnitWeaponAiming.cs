using UnityEngine;

/// <summary>
/// Заглушка: поворот к цели только у корня юнита (<see cref="UnitClickToMove"/> и т.п.).
/// Оружие этим скриптом не крутится — ориентация задаётся при спавне в <see cref="UnitEquipment"/>.
/// Поля инспектора сохранены для ссылок и будущих доработок.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitWeaponAiming : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private UnitVision m_Vision;
	[Tooltip("Forward — направление юнита (корень, бёдра).")]
	[SerializeField] private Transform m_UnitForwardSource;

	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;

	[Header("Выравнивание ствола (не используется)")]
	[SerializeField, Range(0f, 1f)] private float m_AlignBlend = 0.85f;
	[SerializeField, Min(0.02f)] private float m_YawSmoothTime = 0.12f;
	[SerializeField] private bool m_AimAtVisibleTarget = true;
	[SerializeField, Min(0f)] private float m_DeadZoneDegrees = 0.35f;
	[SerializeField] private bool m_HorizontalOnly = true;

	[Header("Аниматор (не используется)")]
	[SerializeField, Min(0f)] private float m_PitchSmoothTime = 0.08f;
	[SerializeField, Min(0f)] private float m_LayerWeightSmoothSeconds = 0.08f;

	[Header("Отладка (не используется)")]
	[SerializeField] private bool m_DrawDebugAlignmentRays;
	[SerializeField, Min(0.1f)] private float m_DebugRayLength = 2f;
	[SerializeField] private bool m_DrawBarrelForwardRay = true;
	[SerializeField, Min(0.1f)] private float m_BarrelForwardRayLength = 4f;
	[SerializeField] private Color m_BarrelForwardRayColor = new Color(1f, 0.85f, 0f, 0.95f);
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_UnitForwardSource == null)
			m_UnitForwardSource = transform;
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
	}
	#endregion
}

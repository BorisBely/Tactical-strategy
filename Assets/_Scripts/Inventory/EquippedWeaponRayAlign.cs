using UnityEngine;

/// <summary>
/// Выравнивает оружие: луч ствола (в горизонтали) к направлению юнита.
/// Идеальное направление ствола считается из позы руки + базового поворота из ItemDefinition (без петли обратной связи),
/// угол коррекции сглаживается — убирает дрожание от анимации и шума float.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public class EquippedWeaponRayAlign : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[Tooltip("Forward — направление юнита (корень, бёдра).")]
	[SerializeField] private Transform m_UnitForwardSource;
	[Tooltip("Дочерний объект оружия: его forward — ось ствола. Пусто — корень визуала.")]
	[SerializeField] private string m_BarrelForwardChildName;
	[SerializeField, Range(0f, 1f)] private float m_AlignBlend = 0.85f;
	[Tooltip("Сглаживание поворота (сек). Больше — плавнее, меньше дёрганий.")]
	[SerializeField, Min(0.02f)] private float m_YawSmoothTime = 0.12f;
	[Tooltip("Углы меньше этого не тянут коррекцию (градусы).")]
	[SerializeField, Min(0f)] private float m_DeadZoneDegrees = 0.35f;
	[SerializeField] private bool m_HorizontalOnly = true;
	[Header("Отладка")]
	[SerializeField] private bool m_DrawDebugRays;
	[SerializeField, Min(0.1f)] private float m_DebugRayLength = 2f;
	#endregion

	#region Private Fields
	private ItemDefinition m_LastEquippedDefinition;
	private Quaternion m_BaseWeaponLocalRotation = Quaternion.identity;
	private Transform m_BarrelTransform;
	/// <summary>Направление ствола в локальном пространстве корня оружия (кэш при смене предмета).</summary>
	private Vector3 m_BarrelForwardInWeaponLocal = Vector3.forward;
	private float m_SmoothedYawCorrection;
	private float m_YawSmoothVelocity;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();

		if (m_UnitForwardSource == null)
			m_UnitForwardSource = transform;
	}

	private void LateUpdate()
	{
		if (m_UnitEquipment == null || m_AlignBlend <= 0f || m_UnitForwardSource == null)
			return;

		Transform weaponRoot = m_UnitEquipment.MainWeaponRoot;
		ItemDefinition def = m_UnitEquipment.EquippedDefinition;
		if (weaponRoot == null || def == null)
		{
			ResetEquipmentState();
			return;
		}

		if (def != m_LastEquippedDefinition)
		{
			m_LastEquippedDefinition = def;
			m_BaseWeaponLocalRotation = def.RightHandLocalRotation;
			ResolveBarrelTransform(weaponRoot);
			weaponRoot.localRotation = m_BaseWeaponLocalRotation;
			m_BarrelForwardInWeaponLocal = weaponRoot.InverseTransformDirection(m_BarrelTransform.forward);
			if (m_BarrelForwardInWeaponLocal.sqrMagnitude < 1e-8f)
				m_BarrelForwardInWeaponLocal = Vector3.forward;
			else
				m_BarrelForwardInWeaponLocal.Normalize();

			m_SmoothedYawCorrection = 0f;
			m_YawSmoothVelocity = 0f;
		}

		Transform hand = m_UnitEquipment.RightHandAnchor;
		if (hand == null || m_BarrelTransform == null)
			return;

		Quaternion handTimesBase = hand.rotation * m_BaseWeaponLocalRotation;
		Vector3 aimIdeal = handTimesBase * m_BarrelForwardInWeaponLocal;

		Vector3 unitFwd = m_UnitForwardSource.forward;
		if (m_HorizontalOnly)
		{
			aimIdeal.y = 0f;
			unitFwd.y = 0f;
		}

		if (aimIdeal.sqrMagnitude < 1e-8f || unitFwd.sqrMagnitude < 1e-8f)
			return;

		aimIdeal.Normalize();
		unitFwd.Normalize();

		float rawDeltaYaw = Vector3.SignedAngle(aimIdeal, unitFwd, Vector3.up);
		if (Mathf.Abs(rawDeltaYaw) < m_DeadZoneDegrees)
			rawDeltaYaw = 0f;

		m_SmoothedYawCorrection = Mathf.SmoothDampAngle(m_SmoothedYawCorrection, rawDeltaYaw, ref m_YawSmoothVelocity,
			m_YawSmoothTime, Mathf.Infinity, Time.deltaTime);

		Quaternion correction = Quaternion.AngleAxis(m_SmoothedYawCorrection * m_AlignBlend, Vector3.up);
		weaponRoot.rotation = correction * hand.rotation * m_BaseWeaponLocalRotation;

		if (m_DrawDebugRays)
		{
			Vector3 showAim = m_BarrelTransform.forward;
			Vector3 showUnit = m_UnitForwardSource.forward;
			if (m_HorizontalOnly)
			{
				showAim.y = 0f;
				showUnit.y = 0f;
				showAim.Normalize();
				showUnit.Normalize();
			}

			Debug.DrawRay(m_BarrelTransform.position, showAim * m_DebugRayLength, Color.red);
			Debug.DrawRay(m_UnitForwardSource.position, showUnit * m_DebugRayLength, Color.green);
			Debug.DrawRay(m_BarrelTransform.position, aimIdeal * m_DebugRayLength, new Color(1f, 0.5f, 0f));
		}
	}
	#endregion

	#region Private Methods
	private void ResetEquipmentState()
	{
		m_LastEquippedDefinition = null;
		m_BarrelTransform = null;
		m_SmoothedYawCorrection = 0f;
		m_YawSmoothVelocity = 0f;
	}

	private void ResolveBarrelTransform(Transform _weaponRoot)
	{
		if (string.IsNullOrWhiteSpace(m_BarrelForwardChildName))
		{
			m_BarrelTransform = _weaponRoot;
			return;
		}

		foreach (Transform t in _weaponRoot.GetComponentsInChildren<Transform>(true))
		{
			if (t != _weaponRoot && t.name == m_BarrelForwardChildName)
			{
				m_BarrelTransform = t;
				return;
			}
		}

		m_BarrelTransform = _weaponRoot;
	}
	#endregion
}

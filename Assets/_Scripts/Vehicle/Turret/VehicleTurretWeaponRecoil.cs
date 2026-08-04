using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class VehicleTurretWeaponRecoil : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private VehicleTurretGunnerBridge m_Bridge;
	[SerializeField] private VehicleTurretHierarchyBinder m_Hierarchy;
	[SerializeField] private VehicleTurretEquipmentController m_Equipment;

	[Header("Barrel (M2)")]
	[Tooltip("На сколько ствол уходит назад по Z при выстреле.")]
	[SerializeField] private float m_BarrelKickZ = -0.03f;
	[Tooltip("Скорость возврата ствола (ед/сек).")]
	[SerializeField] private float m_BarrelReturnSpeed = 0.15f;

	[Header("Gun (M2)")]
	[Tooltip("На сколько орудие уходит назад по Z при выстреле.")]
	[SerializeField] private float m_GunKickZ = -0.025f;
	[Tooltip("Предельное смещение орудия назад (clamp).")]
	[SerializeField] private float m_GunMaxKickZ = -0.04f;
	[Tooltip("Скорость возврата орудия (ед/сек).")]
	[SerializeField] private float m_GunReturnSpeed = 0.04f;

	[Header("Angular Kick")]
	[Tooltip("Макс. отклонение по Pitch (X) в градусах за выстрел.")]
	[SerializeField] private float m_PitchKickDeg = 0.4f;
	[Tooltip("Макс. отклонение по Yaw (Y) в градусах за выстрел.")]
	[SerializeField] private float m_YawKickDeg = 0.25f;
	[Tooltip("Скорость возврата угла (град/сек).")]
	[SerializeField] private float m_AngularReturnSpeed = 2f;

	[Header("MK19 Recoil")]
	[Tooltip("Смещение MK19 вверх по Y за выстрел (основной «прыжок»).")]
	[SerializeField] private float m_Mk19KickY = 0.065f;
	[Tooltip("Смещение MK19 назад по Z за выстрел.")]
	[SerializeField] private float m_Mk19KickZ = -0.022f;
	[Tooltip("Случайный разброс MK19 по X за выстрел.")]
	[SerializeField] private float m_Mk19KickXJitter = 0.008f;
	[Tooltip("Подброс ствола вверх (Pitch, градусы) за выстрел.")]
	[SerializeField] private float m_Mk19PitchKickDeg = 1.8f;
	[Tooltip("Случайный разброс по Yaw (градусы) за выстрел.")]
	[SerializeField] private float m_Mk19YawKickDeg = 0.25f;
	[Tooltip("Скорость возврата MK19 по позиции (ед/сек).")]
	[SerializeField] private float m_Mk19ReturnSpeed = 0.28f;
	[Tooltip("Скорость возврата MK19 по углу (град/сек).")]
	[SerializeField] private float m_Mk19AngularReturnSpeed = 2.2f;
	#endregion

	#region Private Fields
	private Transform m_BarrelTransform;
	private Transform m_GunTransform;
	private Transform m_Mk19Transform;
	private Vector3 m_BarrelRestLocalPos;
	private Vector3 m_GunRestLocalPos;
	private Vector3 m_Mk19RestLocalPos;
	private Quaternion m_GunRestLocalRot;
	private Quaternion m_Mk19RestLocalRot;
	private float m_BarrelCurrentZ;
	private float m_GunCurrentZ;
	private float m_PitchCurrentDeg;
	private float m_YawCurrentDeg;
	private float m_Mk19CurrentX;
	private float m_Mk19CurrentY;
	private float m_Mk19CurrentZ;
	private float m_Mk19PitchDeg;
	private float m_Mk19YawDeg;
	private bool m_Subscribed;
	private UnitWeaponFireController m_SubscribedFireController;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Bridge == null)
			TryGetComponent(out m_Bridge);
		if (m_Hierarchy == null)
			TryGetComponent(out m_Hierarchy);
		if (m_Equipment == null)
			TryGetComponent(out m_Equipment);
		m_Hierarchy?.EnsureBound();
		CaptureRestTransforms();
	}

	private void OnEnable()
	{
		TrySubscribeToGunner();
	}

	private void OnDisable()
	{
		TryUnsubscribeFromGunner();
	}

	private void Update()
	{
		TrySubscribeToGunner();
		ApplyReturn(Time.deltaTime);
	}

	private void LateUpdate()
	{
		ApplyCurrentRecoilPose();
	}
	#endregion

	#region Private Methods
	private void CaptureRestTransforms()
	{
		m_Hierarchy?.EnsureBound();

		Transform m2Pitch = m_Hierarchy?.GetActiveWeaponPitch(TurretWeaponVariant.Browning127);
		if (m2Pitch != null)
		{
			VehicleTurretCombatSockets.PrepareM2PitchRuntime(m2Pitch);

			m_GunTransform = VehicleTurretCombatSockets.FindInnerGun127(m2Pitch);
			if (m_GunTransform != null)
			{
				m_GunRestLocalPos = m_GunTransform.localPosition;
				m_GunRestLocalRot = m_GunTransform.localRotation;
				m_GunCurrentZ = 0f;
			}

			m_BarrelTransform = VehicleTurretCombatSockets.FindBarrelRecoil(m2Pitch);
			if (m_BarrelTransform != null)
			{
				m_BarrelRestLocalPos = m_BarrelTransform.localPosition;
				m_BarrelCurrentZ = 0f;
			}
		}

		m_Mk19Transform = m_Hierarchy?.Mk19;
		if (m_Mk19Transform != null)
		{
			m_Mk19RestLocalPos = m_Mk19Transform.localPosition;
			m_Mk19RestLocalRot = m_Mk19Transform.localRotation;
			m_Mk19CurrentX = 0f;
			m_Mk19CurrentY = 0f;
			m_Mk19CurrentZ = 0f;
			m_Mk19PitchDeg = 0f;
			m_Mk19YawDeg = 0f;
		}
	}

	private void TrySubscribeToGunner()
	{
		if (m_Subscribed)
			return;
		if (m_Bridge == null || !m_Bridge.HasBoundGunner)
			return;

		UnitWeaponFireController fireCtrl = m_Bridge.BoundGunner.GetComponent<UnitWeaponFireController>();
		if (fireCtrl == null)
			return;

		fireCtrl.ShotFired += HandleShotFired;
		m_SubscribedFireController = fireCtrl;
		m_Subscribed = true;
	}

	private void TryUnsubscribeFromGunner()
	{
		if (!m_Subscribed)
			return;
		if (m_SubscribedFireController != null)
			m_SubscribedFireController.ShotFired -= HandleShotFired;
		m_SubscribedFireController = null;
		m_Subscribed = false;
	}

	private void HandleShotFired(AmmoDefinition _ammo)
	{
		if (m_Bridge == null || !m_Bridge.HasBoundGunner)
			return;
		ItemDefinition activeWeapon = m_Equipment != null ? m_Equipment.ActiveWeaponItem : null;
		bool isMk19 = activeWeapon != null && activeWeapon.TurretWeaponVariant == TurretWeaponVariant.Mk19;

		if (isMk19)
		{
			ApplyMk19Kick();
		}
		else
		{
			if (m_GunTransform == null || m_BarrelTransform == null)
				CaptureRestTransforms();
			ApplyBarrelKick();
			ApplyGunKick();
		}
	}

	private void ApplyMk19Kick()
	{
		if (m_Mk19Transform == null)
			CaptureRestTransforms();
		if (m_Mk19Transform == null)
			return;

		m_Mk19CurrentY = m_Mk19KickY;
		m_Mk19CurrentZ = m_Mk19KickZ;
		m_Mk19CurrentX = Random.Range(-m_Mk19KickXJitter, m_Mk19KickXJitter);
		m_Mk19PitchDeg += m_Mk19PitchKickDeg;
		m_Mk19YawDeg += Random.Range(-m_Mk19YawKickDeg, m_Mk19YawKickDeg);

		ApplyCurrentMk19Pose();
	}

	private void ApplyBarrelKick()
	{
		if (m_BarrelTransform == null)
			return;

		if (m_BarrelCurrentZ > m_BarrelKickZ)
			m_BarrelCurrentZ = m_BarrelKickZ;

		ApplyCurrentBarrelPose();
	}

	private void ApplyGunKick()
	{
		if (m_GunTransform == null)
			return;

		float recoveryThreshold = m_GunMaxKickZ * 0.8f;
		if (m_GunCurrentZ < 0f && m_GunCurrentZ > recoveryThreshold)
		{
			m_GunCurrentZ = m_GunMaxKickZ;
		}
		else
		{
			m_GunCurrentZ = Mathf.Max(m_GunMaxKickZ, m_GunCurrentZ + m_GunKickZ);
		}

		m_PitchCurrentDeg += Random.Range(-m_PitchKickDeg, m_PitchKickDeg);
		m_YawCurrentDeg += Random.Range(-m_YawKickDeg, m_YawKickDeg);

		ApplyCurrentGunPose();
	}

	private void ApplyCurrentRecoilPose()
	{
		ApplyCurrentBarrelPose();
		ApplyCurrentGunPose();
		ApplyCurrentMk19Pose();
	}

	private void ApplyCurrentBarrelPose()
	{
		if (m_BarrelTransform == null)
			return;

		m_BarrelTransform.localPosition = new Vector3(
			m_BarrelRestLocalPos.x,
			m_BarrelRestLocalPos.y,
			m_BarrelRestLocalPos.z + m_BarrelCurrentZ);
	}

	private void ApplyCurrentGunPose()
	{
		if (m_GunTransform == null)
			return;

		m_GunTransform.localPosition = new Vector3(
			m_GunRestLocalPos.x,
			m_GunRestLocalPos.y,
			m_GunRestLocalPos.z + m_GunCurrentZ);
		m_GunTransform.localRotation = m_GunRestLocalRot
			* Quaternion.Euler(m_PitchCurrentDeg, 0f, 0f);
	}

	private void ApplyCurrentMk19Pose()
	{
		if (m_Mk19Transform == null)
			return;

		m_Mk19Transform.localPosition = new Vector3(
			m_Mk19RestLocalPos.x + m_Mk19CurrentX,
			m_Mk19RestLocalPos.y + m_Mk19CurrentY,
			m_Mk19RestLocalPos.z + m_Mk19CurrentZ);
		m_Mk19Transform.localRotation = m_Mk19RestLocalRot
			* Quaternion.Euler(m_Mk19PitchDeg, m_Mk19YawDeg, 0f);
	}

	private void ApplyReturn(float _dt)
	{
		if (m_BarrelTransform != null)
			m_BarrelCurrentZ = Mathf.MoveTowards(m_BarrelCurrentZ, 0f, m_BarrelReturnSpeed * _dt);

		if (m_GunTransform != null)
		{
			m_GunCurrentZ = Mathf.MoveTowards(m_GunCurrentZ, 0f, m_GunReturnSpeed * _dt);
			m_PitchCurrentDeg = Mathf.MoveTowards(m_PitchCurrentDeg, 0f, m_AngularReturnSpeed * _dt);
			m_YawCurrentDeg = Mathf.MoveTowards(m_YawCurrentDeg, 0f, m_AngularReturnSpeed * _dt);
		}

		if (m_Mk19Transform != null)
		{
			m_Mk19CurrentX = Mathf.MoveTowards(m_Mk19CurrentX, 0f, m_Mk19ReturnSpeed * _dt);
			m_Mk19CurrentY = Mathf.MoveTowards(m_Mk19CurrentY, 0f, m_Mk19ReturnSpeed * _dt);
			m_Mk19CurrentZ = Mathf.MoveTowards(m_Mk19CurrentZ, 0f, m_Mk19ReturnSpeed * _dt);
			m_Mk19PitchDeg = Mathf.MoveTowards(m_Mk19PitchDeg, 0f, m_Mk19AngularReturnSpeed * _dt);
			m_Mk19YawDeg = Mathf.MoveTowards(m_Mk19YawDeg, 0f, m_Mk19AngularReturnSpeed * _dt);
		}

		ApplyCurrentRecoilPose();
	}
	#endregion
}

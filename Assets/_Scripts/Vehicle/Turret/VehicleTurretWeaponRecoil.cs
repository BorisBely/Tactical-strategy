using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class VehicleTurretWeaponRecoil : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private VehicleTurretGunnerBridge m_Bridge;
	[SerializeField] private VehicleTurretHierarchyBinder m_Hierarchy;

	[Header("Barrel")]
	[Tooltip("На сколько ствол уходит назад по Z при выстреле.")]
	[SerializeField] private float m_BarrelKickZ = -0.03f;
	[Tooltip("Скорость возврата ствола (ед/сек).")]
	[SerializeField] private float m_BarrelReturnSpeed = 0.15f;

	[Header("Gun")]
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
	#endregion

	#region Private Fields
	private Transform m_BarrelTransform;
	private Transform m_GunTransform;
	private Vector3 m_BarrelRestLocalPos;
	private Vector3 m_GunRestLocalPos;
	private Quaternion m_GunRestLocalRot;
	private float m_BarrelCurrentZ;
	private float m_GunCurrentZ;
	private float m_PitchCurrentDeg;
	private float m_YawCurrentDeg;
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
		// Push current recoil pose again before AnimatorHandIk (order 250) reads IK targets.
		ApplyCurrentRecoilPose();
	}
	#endregion

	#region Private Methods
	private void CaptureRestTransforms()
	{
		m_Hierarchy?.EnsureBound();
		Transform pitch = m_Hierarchy?.GetActiveWeaponPitch(TurretWeaponVariant.Browning127);
		if (pitch == null)
			return;

		VehicleTurretCombatSockets.PrepareM2PitchRuntime(pitch);

		m_GunTransform = VehicleTurretCombatSockets.FindInnerGun127(pitch);
		if (m_GunTransform != null)
		{
			m_GunRestLocalPos = m_GunTransform.localPosition;
			m_GunRestLocalRot = m_GunTransform.localRotation;
			m_GunCurrentZ = 0f;
		}

		m_BarrelTransform = VehicleTurretCombatSockets.FindBarrelRecoil(pitch);
		if (m_BarrelTransform != null)
		{
			m_BarrelRestLocalPos = m_BarrelTransform.localPosition;
			m_BarrelCurrentZ = 0f;
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
		if (m_GunTransform == null || m_BarrelTransform == null)
			CaptureRestTransforms();
		ApplyBarrelKick();
		ApplyGunKick();
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

		ApplyCurrentRecoilPose();
	}
	#endregion
}

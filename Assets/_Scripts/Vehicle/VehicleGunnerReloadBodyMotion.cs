using UnityEngine;

/// <summary>
/// Во время Stand_Gunner_Reload применяет root motion клипа к localPosition юнита на слоте стрелка
/// (applyRootMotion выключен в <see cref="UnitVehicleSeatPoseController"/>).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(95)]
public sealed class VehicleGunnerReloadBodyMotion : MonoBehaviour
{
	#region Private Fields
	private Animator m_Animator;
	private Transform m_MountRoot;
	private UnitVehicleTurretReloadEvents m_ReloadEvents;
	private bool m_ReloadMotionActive;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Animator = GetComponent<Animator>();
		m_ReloadEvents = GetComponentInParent<UnitVehicleTurretReloadEvents>();
		m_MountRoot = transform;
		RtsUnitMember member = GetComponentInParent<RtsUnitMember>();
		if (member != null)
			m_MountRoot = member.transform;
	}

	private void LateUpdate()
	{
		if (m_ReloadEvents == null)
			m_ReloadEvents = GetComponentInParent<UnitVehicleTurretReloadEvents>();
		if (m_Animator == null)
			return;

		bool shouldMove = m_ReloadEvents != null && m_ReloadEvents.IsReloadAnimationActive;
		if (shouldMove && !m_ReloadMotionActive)
		{
			m_ReloadMotionActive = true;
			m_Animator.applyRootMotion = true;
		}
		else if (!shouldMove && m_ReloadMotionActive)
		{
			m_ReloadMotionActive = false;
			m_Animator.applyRootMotion = false;
			if (m_MountRoot != null)
				m_MountRoot.localPosition = Vector3.zero;
		}
	}

	private void OnAnimatorMove()
	{
		if (!m_ReloadMotionActive || m_Animator == null || m_MountRoot == null)
			return;

		Vector3 delta = m_Animator.deltaPosition;
		if (delta.sqrMagnitude <= 0f)
			return;

		Transform parent = m_MountRoot.parent;
		if (parent != null)
			delta = parent.InverseTransformVector(delta);

		m_MountRoot.localPosition += delta;
	}
	#endregion

	#region Public Methods
	public static VehicleGunnerReloadBodyMotion GetOrAdd(GameObject _unitObject)
	{
		if (_unitObject == null)
			return null;

		Animator animator = _unitObject.GetComponentInChildren<Animator>(true);
		GameObject host = animator != null ? animator.gameObject : _unitObject;
		if (!host.TryGetComponent(out VehicleGunnerReloadBodyMotion motion))
			motion = host.AddComponent<VehicleGunnerReloadBodyMotion>();
		return motion;
	}
	#endregion
}

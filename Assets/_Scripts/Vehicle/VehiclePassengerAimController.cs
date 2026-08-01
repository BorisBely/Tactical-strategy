using UnityEngine;

/// <summary>
/// Вычисляет угол прицеливания пассажира относительно машины.
/// Цель → локальный угол → clamp в сектор → VehiclePassengerState.AimYaw → Animator.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(60)]
public sealed class VehiclePassengerAimController : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private VehiclePassengerState m_State;
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField] private UnitVision m_Vision;
	[SerializeField] private Animator m_Animator;
	[SerializeField, Range(0.5f, 10f)] private float m_AimYawSmoothTime = 0.15f;

	[Header("Gizmos")]
	[SerializeField] private bool m_DrawGizmos = true;
	[SerializeField] private Color m_PerpendicularColor = new Color(0f, 0.8f, 0.4f, 1f);
	[SerializeField] private Color m_SectorBorderColor = new Color(1f, 0.7f, 0f, 1f);
	[SerializeField] private Color m_AimDirectionColor = new Color(1f, 0.3f, 0.1f, 1f);
	[SerializeField, Min(0.5f)] private float m_GizmoRayLength = 3f;
	[SerializeField, Min(0.2f)] private float m_SectorRayLength = 2f;

	[Header("Diagnostics")]
	[SerializeField] private bool m_LogDiagnostics = true;
	#endregion

	#region Private Fields
	private static readonly int s_VehicleAimYaw = Animator.StringToHash(
		UnitVehicleSeatPoseController.ParamVehicleAimYaw);
	private float m_CurrentAimYaw;
	private float m_AimYawVelocity;

	private Vector3 m_DebugHeadPos;
	private bool m_DebugIsActive;

	private Transform m_LastLoggedTarget;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_State == null)
			m_State = GetComponent<VehiclePassengerState>();
		if (m_Vehicle == null)
			m_Vehicle = GetComponentInParent<VehicleController>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
	}

	private void OnEnable()
	{
		m_CurrentAimYaw = 0f;
		m_AimYawVelocity = 0f;
	}

	private void LateUpdate()
	{
		if (m_State == null || !m_State.IsVehicleReady)
		{
			m_DebugIsActive = false;
			if (m_Animator != null)
				m_Animator.SetFloat(s_VehicleAimYaw, 0f);
			m_LastLoggedTarget = null;
			return;
		}

		m_DebugIsActive = true;
		m_DebugHeadPos = GetHeadPosition();

		Transform vehicleTransform = m_Vehicle != null ? m_Vehicle.transform : null;

		Vector3? target = ResolveAimTarget();
		float rawAngle = 0f;

		if (target.HasValue)
		{
			rawAngle = WorldDirectionToVehicleLocalAngle(target.Value, transform.position, vehicleTransform);
			m_State.RawAimYaw = rawAngle;
			rawAngle = Mathf.Clamp(rawAngle, m_State.AimSectorMin, m_State.AimSectorMax);

			if (m_LogDiagnostics)
			{
				Transform targetTransform = ResolveAimTargetTransform();
				if (targetTransform != m_LastLoggedTarget)
				{
					string targetName = targetTransform != null ? targetTransform.name : "none";
					float dist = targetTransform != null ? Vector3.Distance(transform.position, targetTransform.position) : 0f;
					float noseAngle = GetVehicleNoseAngle(target.Value, transform.position, vehicleTransform);
					string side = m_State.IsLeftSide ? "LEFT" : "RIGHT";
					Debug.Log($"[VehPassAim] {name} TARGET: '{targetName}' dist={dist:F1}m | noseAngle={noseAngle:F1}° (0°=вперёд ±180°) | rawPerp={m_State.RawAimYaw:F1}° sector=[{m_State.AimSectorMin}°..{m_State.AimSectorMax}°] | clampTo={rawAngle:F1}° | side={side}", this);
					m_LastLoggedTarget = targetTransform;
				}
			}
		}
		else if (m_LogDiagnostics && m_LastLoggedTarget != null)
		{
			Debug.Log($"[VehPassAim] {name} TARGET LOST", this);
			m_LastLoggedTarget = null;
		}

		if (!target.HasValue)
			m_State.RawAimYaw = 0f;

		float prevYaw = m_CurrentAimYaw;
		m_CurrentAimYaw = Mathf.SmoothDamp(m_CurrentAimYaw, rawAngle,
			ref m_AimYawVelocity, m_AimYawSmoothTime);

		if (m_LogDiagnostics && Mathf.Abs(m_CurrentAimYaw - prevYaw) > 0.5f)
			Debug.Log($"[VehPassAim] {name} aimYaw: {prevYaw:F1}° → {m_CurrentAimYaw:F1}° (target={rawAngle:F1}°)", this);

		m_State.AimYaw = m_CurrentAimYaw;

		if (m_Animator != null)
			m_Animator.SetFloat(s_VehicleAimYaw, m_CurrentAimYaw);
	}
	#endregion

	#region Private Methods
	private Vector3? ResolveAimTarget()
	{
		if (m_Vision == null)
			return null;

		Transform engageable = m_Vision.GetEngageableVisibleTarget();
		if (engageable != null)
			return engageable.position;

		Transform visible = m_Vision.VisibleTarget;
		if (visible != null)
			return visible.position;

		return null;
	}

	private Transform ResolveAimTargetTransform()
	{
		if (m_Vision == null)
			return null;

		Transform engageable = m_Vision.GetEngageableVisibleTarget();
		if (engageable != null)
			return engageable;

		return m_Vision.VisibleTarget;
	}

	private float WorldDirectionToVehicleLocalAngle(
		Vector3 _worldTarget,
		Vector3 _shooterPosition,
		Transform _vehicleTransform)
	{
		Vector3 toTarget = _worldTarget - _shooterPosition;
		toTarget.y = 0f;

		if (toTarget.sqrMagnitude < 0.0001f)
			return 0f;

		toTarget.Normalize();

		Vector3 reference = GetPerpendicularDirection(_vehicleTransform);

		float angle = Vector3.SignedAngle(reference, toTarget, Vector3.up);
		return angle;
	}

	private float GetVehicleNoseAngle(
		Vector3 _worldTarget,
		Vector3 _shooterPosition,
		Transform _vehicleTransform)
	{
		Vector3 toTarget = _worldTarget - _shooterPosition;
		toTarget.y = 0f;

		if (toTarget.sqrMagnitude < 0.0001f)
			return 0f;

		toTarget.Normalize();

		Vector3 nose = _vehicleTransform != null ? _vehicleTransform.forward : Vector3.forward;
		nose.y = 0f;
		if (nose.sqrMagnitude < 0.001f)
			nose = Vector3.forward;
		nose.Normalize();

		return Vector3.SignedAngle(nose, toTarget, Vector3.up);
	}

	private Vector3 GetPerpendicularDirection(Transform _vehicleTransform)
	{
		if (_vehicleTransform == null)
			return Vector3.right;

		bool isLeft = m_State != null && m_State.IsLeftSide;
		Vector3 dir = isLeft ? -_vehicleTransform.right : _vehicleTransform.right;
		dir.y = 0f;
		if (dir.sqrMagnitude < 0.001f)
			dir = isLeft ? Vector3.left : Vector3.right;
		dir.Normalize();
		return dir;
	}
	private Vector3 GetHeadPosition()
	{
		if (m_Animator != null && m_Animator.isHuman)
		{
			Transform head = m_Animator.GetBoneTransform(HumanBodyBones.Head);
			if (head != null)
				return head.position;
		}

		return transform.position + Vector3.up * 1.6f;
	}
	private void OnDrawGizmos()
	{
		if (!m_DrawGizmos)
			return;

		Transform vehicleTransform = m_Vehicle != null ? m_Vehicle.transform : transform;
		Vector3 origin = Application.isPlaying && m_DebugIsActive
			? m_DebugHeadPos
			: GetHeadPosition();

		Vector3 forward = vehicleTransform.forward;
		forward.y = 0f;
		if (forward.sqrMagnitude < 0.001f)
			forward = vehicleTransform.rotation * Vector3.forward;
		forward.Normalize();

		Vector3 right = vehicleTransform.right;
		right.y = 0f;
		right.Normalize();

		bool isLeft = m_State != null && m_State.IsLeftSide;
		Vector3 perpDir = isLeft ? -right : right;

		float sectorMin = m_State != null ? m_State.AimSectorMin : -10f;
		float sectorMax = m_State != null ? m_State.AimSectorMax : 45f;
		float aimYaw = Application.isPlaying && m_DebugIsActive ? m_CurrentAimYaw : 0f;

		// Перпендикуляр к машине (направление в окно, 0° сектора)
		Gizmos.color = m_PerpendicularColor;
		Gizmos.DrawRay(origin, perpDir * m_GizmoRayLength);

		// Границы сектора
		Gizmos.color = m_SectorBorderColor;
		Gizmos.DrawRay(origin, Quaternion.AngleAxis(-sectorMin, Vector3.up) * perpDir * m_SectorRayLength);
		Gizmos.DrawRay(origin, Quaternion.AngleAxis(-sectorMax, Vector3.up) * perpDir * m_SectorRayLength);

		// Направление взгляда и оружия
		if (Application.isPlaying)
		{
			Gizmos.color = m_AimDirectionColor;
			Vector3 aimDir = Quaternion.AngleAxis(-aimYaw, Vector3.up) * perpDir;
			Gizmos.DrawRay(origin, aimDir * m_GizmoRayLength);
			Gizmos.DrawSphere(origin + aimDir * m_GizmoRayLength, 0.06f);
		}
	}
	#endregion
}

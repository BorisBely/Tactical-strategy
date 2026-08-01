using UnityEngine;

/// <summary>
/// Проверяет условия стрельбы для пассажира в машине:
/// угол в секторе, стекло открыто, луч не пересекает кузов, дистанция.
/// Выставляет <c>VehiclePassengerState.CanFire</c>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(65)]
public sealed class VehiclePassengerFireValidator : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private VehiclePassengerState m_State;
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField] private VehicleGlassController m_GlassController;
	[SerializeField] private UnitVision m_Vision;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private LayerMask m_VehicleBodyMask = -1;
	[SerializeField] private float m_MinFireRange = 0f;
	[SerializeField] private float m_MaxFireRange = 100f;

	[Header("Diagnostics")]
	[SerializeField] private bool m_LogDiagnostics = true;
	#endregion

	#region Private Fields
	private readonly RaycastHit[] m_RaycastBuffer = new RaycastHit[8];

	private WeaponShotAttemptResult m_LastLoggedResult = (WeaponShotAttemptResult)(-1);
	private string m_LastFailReason;
	private float m_NextStatusLogTime;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_State == null)
			m_State = GetComponent<VehiclePassengerState>();
		if (m_Vehicle == null)
			m_Vehicle = GetComponentInParent<VehicleController>();
		if (m_GlassController == null && m_Vehicle != null)
			m_GlassController = m_Vehicle.GlassController;
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
	}

	private void LateUpdate()
	{
		if (m_State == null)
		{
			SetCanFire(false);
			return;
		}

		if (!m_State.IsVehicleReady || m_State.IsPreparing)
		{
			SetCanFire(false);
			return;
		}

		bool canFire = EvaluateConditions();
		bool wasCanFire = m_State.CanFire;

		if (m_LogDiagnostics && canFire != wasCanFire)
		{
			if (canFire)
				Debug.Log($"[VehPassFire] {name} >>> CAN FIRE <<<", this);
			else
				Debug.Log($"[VehPassFire] {name} CANNOT FIRE: {m_LastFailReason}", this);
		}

		SetCanFire(canFire);

		TryLogFireResult();
		TryLogStatus();
	}

	private void TryLogStatus()
	{
		if (!m_LogDiagnostics || m_State == null)
			return;

		if (Time.time < m_NextStatusLogTime)
			return;

		m_NextStatusLogTime = Time.time + 1f;

		string readyState = m_State.IsVehicleReady ? "READY" : (m_State.IsPreparing ? "PREPARING" : "NOT_READY");
		string canFireStr = m_State.CanFire ? "YES" : $"NO ({m_LastFailReason ?? "?"})";
		float aimYaw = m_State.AimYaw;
		float raw = m_State.RawAimYaw;
		string aimSector = $"[{m_State.AimSectorMin}°..{m_State.AimSectorMax}°]";
		string aimStr = (Mathf.Abs(raw - aimYaw) > 0.1f)
			? $"{aimYaw:F1}° (raw={raw:F1}°){aimSector}"
			: $"{aimYaw:F1}°{aimSector}";
		Transform target = ResolveTarget();
		string targetName = target != null ? target.name : "none";
		float dist = target != null ? Vector3.Distance(transform.position, target.position) : 0f;
		string glassState = (m_GlassController != null && !m_GlassController.IsFullyOpen) ? "CLOSED" : "OPEN";

		Debug.Log(
			$"[VehPassFire] {name} seat={m_State.Seat} STATUS: ready={readyState} canFire={canFireStr} aim={aimStr} " +
			$"target='{targetName}' dist={dist:F1}m glass={glassState} " +
			$"fireCtrl.Active={m_FireController != null && m_FireController.IsFiringCommandActive}",
			this);
	}

	private void TryLogFireResult()
	{
		if (!m_LogDiagnostics || m_FireController == null)
			return;

		if (m_State == null || !m_State.IsVehicleReady)
			return;

		WeaponShotAttemptResult result = m_FireController.LastShotAttemptResult;
		if (result == m_LastLoggedResult)
			return;

		m_LastLoggedResult = result;

		switch (result)
		{
			case WeaponShotAttemptResult.Success:
				Debug.Log($"[VehPassFire] {name} SHOT FIRED — SUCCESS!", this);
				break;
			case WeaponShotAttemptResult.NotReady:
				Debug.Log($"[VehPassFire] {name} SHOT FAIL: NotReady", this);
				break;
			case WeaponShotAttemptResult.NoVisibleTarget:
				Debug.Log($"[VehPassFire] {name} SHOT FAIL: NoVisibleTarget", this);
				break;
			case WeaponShotAttemptResult.NotAimed:
				Debug.Log($"[VehPassFire] {name} SHOT FAIL: NotAimed", this);
				break;
			case WeaponShotAttemptResult.EmptyMagazine:
				Debug.Log($"[VehPassFire] {name} SHOT FAIL: EmptyMagazine", this);
				break;
			case WeaponShotAttemptResult.NoMagazine:
				Debug.Log($"[VehPassFire] {name} SHOT FAIL: NoMagazine", this);
				break;
			case WeaponShotAttemptResult.FireRateLimited:
				break;
			case WeaponShotAttemptResult.Busy:
				break;
			case WeaponShotAttemptResult.LineOfFireBlocked:
				Debug.Log($"[VehPassFire] {name} SHOT FAIL: LineOfFireBlocked (friendly in crossfire)", this);
				break;
			case WeaponShotAttemptResult.NoWeapon:
				Debug.Log($"[VehPassFire] {name} SHOT FAIL: NoWeapon", this);
				break;
			case WeaponShotAttemptResult.NeedsBoltCycle:
				Debug.Log($"[VehPassFire] {name} SHOT FAIL: NeedsBoltCycle", this);
				break;
			default:
				Debug.Log($"[VehPassFire] {name} SHOT FAIL: {result}", this);
				break;
		}
	}
	#endregion

	#region Private Methods
	private void SetCanFire(bool _value)
	{
		if (m_State != null)
			m_State.CanFire = _value;

		if (m_FireController != null)
		{
			m_FireController.RequireReady = !_value;
			m_FireController.RequireBarrelAlignedToFire = !_value;
		}
	}

	private bool EvaluateConditions()
	{
		if (!m_State.IsVehicleReady)
		{
			m_LastFailReason = "!IsVehicleReady";
			return false;
		}

		if (!m_State.IsFireCapable)
		{
			m_LastFailReason = "!IsFireCapable";
			return false;
		}

		Transform target = ResolveTarget();
		if (target == null)
		{
			m_LastFailReason = "no visible target";
			return false;
		}

		float rawAimYaw = m_State.RawAimYaw;
		float aimYaw = m_State.AimYaw;
		if (rawAimYaw < m_State.AimSectorMin - 0.5f || rawAimYaw > m_State.AimSectorMax + 0.5f)
		{
			m_LastFailReason = $"aim out of sector: raw={rawAimYaw:F1}° sector=[{m_State.AimSectorMin}°..{m_State.AimSectorMax}°] clamp={aimYaw:F1}°";
			return false;
		}

		if (m_GlassController != null && !m_GlassController.IsFullyOpen)
		{
			m_LastFailReason = "glass not fully open";
			return false;
		}

		float distance = Vector3.Distance(transform.position, target.position);
		if (distance < m_MinFireRange || distance > m_MaxFireRange)
		{
			m_LastFailReason = $"distance out of range: {distance:F1}m [{m_MinFireRange}..{m_MaxFireRange}]";
			return false;
		}

		if (!IsLineOfSightClear(target.position))
		{
			m_LastFailReason = "line of sight blocked (vehicle body)";
			return false;
		}

		m_LastFailReason = null;
		return true;
	}

	private Transform ResolveTarget()
	{
		if (m_Vision == null)
			return null;

		Transform engageable = m_Vision.GetEngageableVisibleTarget();
		if (engageable != null)
			return engageable;

		return m_Vision.VisibleTarget;
	}

	private bool IsLineOfSightClear(Vector3 _targetPosition)
	{
		if (m_Equipment == null || m_Equipment.MainWeaponRoot == null)
		{
			if (m_LogDiagnostics)
				Debug.Log($"[VehPassFire] {name} LOS: no weapon root — skip check", this);
			return true;
		}

		Vector3 origin = m_Equipment.MainWeaponRoot.position;
		Vector3 direction = _targetPosition - origin;
		float distance = direction.magnitude;

		if (distance < 0.01f)
			return false;

		direction /= distance;

		Transform vehicleRoot = m_Vehicle != null ? m_Vehicle.transform : null;
		Transform targetRoot = ResolveTarget();
		int count = Physics.RaycastNonAlloc(origin, direction, m_RaycastBuffer, distance,
			~0, QueryTriggerInteraction.Ignore);

		Transform blocker = null;
		float blockerDist = 0f;

		for (int i = 0; i < count; i++)
		{
			Transform hitTransform = m_RaycastBuffer[i].transform;
			if (hitTransform == transform || hitTransform.IsChildOf(transform))
				continue;
			if (targetRoot != null && (hitTransform == targetRoot || hitTransform.IsChildOf(targetRoot)))
				continue;
			Transform hitRoot = m_RaycastBuffer[i].transform.root;
			if (vehicleRoot != null && hitRoot == vehicleRoot)
				continue;
			blocker = hitTransform;
			blockerDist = m_RaycastBuffer[i].distance;
			break;
		}

		if (blocker != null)
		{
			if (m_LogDiagnostics)
				Debug.Log($"[VehPassFire] {name} LOS BLOCKED by '{blocker.name}' (root={blocker.root.name}) at {blockerDist:F2}m, total dist={distance:F1}m", this);
			return false;
		}

		if (m_LogDiagnostics && count > 0)
			Debug.Log($"[VehPassFire] {name} LOS CLEAR: {count} hits, all skipped (vehicle/self/target)", this);

		return true;
	}
	#endregion
}

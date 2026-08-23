using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Logical optic yaw sweep inside an assigned sector. Does not write Animator, Hand_R, or weapon TRS.
/// Sector center is body facing supplied by <see cref="UnitVision"/> — never the previous sweep forward.
/// ScopeFOV (8°) is not this component's clamp; AssignedSector (default 120°) is.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScopeScanController : MonoBehaviour
{
	#region Constants
	public const float DefaultAssignedSectorHalfDegrees = 60f;
	public const float DefaultScanSpeedDegrees = 25f;
	public const float DefaultWalkSpeedScale = 0.5f;
	public const float DefaultStepDegrees = 2f;
	public const float DefaultMaxScanIntervalSeconds = 0.22f;
	public const float DefaultLostTargetHoldSeconds = 0.85f;
	public const float DefaultCoverageFreshSeconds = 1f;
	#endregion

	#region Serialized
	[SerializeField, Range(0.5f, 179f)] private float m_AssignedSectorHalfDegrees = DefaultAssignedSectorHalfDegrees;
	[SerializeField] private float m_AssignedSectorCenterYawDegrees;
	[SerializeField, Min(1f)] private float m_ScanSpeedDegrees = DefaultScanSpeedDegrees;
	[SerializeField, Range(0.1f, 1f)] private float m_WalkSpeedScale = DefaultWalkSpeedScale;
	[SerializeField, Range(0.5f, 8f)] private float m_StepDegrees = DefaultStepDegrees;
	[SerializeField, Range(0.05f, 1f)] private float m_MaxScanIntervalSeconds = DefaultMaxScanIntervalSeconds;
	[SerializeField, Range(0.1f, 3f)] private float m_LostTargetHoldSeconds = DefaultLostTargetHoldSeconds;
	#endregion

	#region Private Fields
	private float m_ScanYawDegrees = -DefaultAssignedSectorHalfDegrees;
	private float m_LastEmittedYawDegrees = -999f;
	private float m_PreviousScanYawDegrees = -999f;
	private float m_LastScanTime = -999f;
	private int m_Direction = 1;
	private ScopeScanMode m_Mode = ScopeScanMode.Sweep;
	private bool m_FrozenForTest;
	private float m_LostHoldStartTime = -999f;
	private Transform m_LastContactTarget;
	private Vector3 m_LastContactWorld;
	private bool m_HasLastContact;
	private NavMeshAgent m_Agent;
	private ScopeScanMode m_LastLoggedMode = (ScopeScanMode)(-1);
	private static bool s_TestLogging;
	#endregion

	#region Public Properties
	public float ScanYawDegrees => m_ScanYawDegrees;
	public float LastScanYawDegrees => m_LastEmittedYawDegrees;
	public float PreviousScanYawDegrees => m_PreviousScanYawDegrees;
	public float LastScanTime => m_LastScanTime;
	public int Direction => m_Direction;
	public ScopeScanMode Mode => m_Mode;
	public bool IsLockedOnContact => m_Mode == ScopeScanMode.TrackTarget;
	public bool IsFrozenForTest => m_FrozenForTest;
	public float AssignedSectorHalfDegrees => m_AssignedSectorHalfDegrees;
	public float AssignedSectorCenterYawDegrees => m_AssignedSectorCenterYawDegrees;
	public float StepDegrees => m_StepDegrees;
	public float LostTargetHoldSeconds => m_LostTargetHoldSeconds;
	public float MaxScanIntervalSeconds => m_MaxScanIntervalSeconds;
	public float CoverageAge => m_LastScanTime < -100f ? 999f : Mathf.Max(0f, Time.time - m_LastScanTime);
	public bool HasLastContact => m_HasLastContact;
	public Vector3 LastContactWorld => m_LastContactWorld;
	public Transform LastContactTarget => m_LastContactTarget;

	public static bool TestLogging
	{
		get => s_TestLogging;
		set => s_TestLogging = value;
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		TryGetComponent(out m_Agent);
		m_ScanYawDegrees = -m_AssignedSectorHalfDegrees;
	}
	#endregion

	#region Public Methods
	public void ResetSweep()
	{
		m_ScanYawDegrees = -m_AssignedSectorHalfDegrees;
		m_Direction = 1;
		m_Mode = ScopeScanMode.Sweep;
		m_LastEmittedYawDegrees = -999f;
		m_PreviousScanYawDegrees = -999f;
		m_HasLastContact = false;
		m_LastContactTarget = null;
		m_LastLoggedMode = (ScopeScanMode)(-1);
	}

	/// <summary>
	/// Future hold-sector order hook. Center is local yaw offset from body facing.
	/// </summary>
	public void SetAssignedSector(float _centerLocalYawDegrees, float _halfAngleDegrees)
	{
		m_AssignedSectorCenterYawDegrees = _centerLocalYawDegrees;
		m_AssignedSectorHalfDegrees = Mathf.Clamp(_halfAngleDegrees, 0.5f, 179f);
		m_ScanYawDegrees = Mathf.Clamp(m_ScanYawDegrees, -m_AssignedSectorHalfDegrees, m_AssignedSectorHalfDegrees);
	}

	public void SetFrozenForTest(bool _frozen)
	{
		m_FrozenForTest = _frozen;
	}

	public void SetScanYawForTest(float _yawDegrees)
	{
		m_ScanYawDegrees = Mathf.Clamp(_yawDegrees, -m_AssignedSectorHalfDegrees, m_AssignedSectorHalfDegrees);
		m_LastEmittedYawDegrees = m_ScanYawDegrees;
		m_LastScanTime = Time.time;
	}

	public void SetDirectionForTest(int _direction)
	{
		m_Direction = _direction >= 0 ? 1 : -1;
	}

	public Vector3 GetSweepForwardXZ(Vector3 _bodyForwardXZ)
	{
		return RotateBody(_bodyForwardXZ, m_AssignedSectorCenterYawDegrees + m_ScanYawDegrees);
	}

	public Vector3 GetQueryForwardXZ(Vector3 _bodyForwardXZ, Vector3 _origin)
	{
		if ((m_Mode == ScopeScanMode.TrackTarget || m_Mode == ScopeScanMode.LostHold) && m_HasLastContact)
		{
			Vector3 to = m_LastContactWorld - _origin;
			to.y = 0f;
			if (to.sqrMagnitude > 1e-8f)
				return to.normalized;
		}

		return GetSweepForwardXZ(_bodyForwardXZ);
	}

	/// <summary>
	/// Advances sweep yaw every frame. Returns true when a scope query should run (not a full eye Detail).
	/// </summary>
	public bool Tick(float _deltaTime, bool _scopeActive, bool _requestedSearch)
	{
		return Tick(_deltaTime, _scopeActive, _requestedSearch, Time.time);
	}

	public bool Tick(float _deltaTime, bool _scopeActive, bool _requestedSearch, float _now)
	{
		if (!_scopeActive)
		{
			m_Mode = ScopeScanMode.Sweep;
			m_HasLastContact = false;
			return _requestedSearch;
		}

		if (m_Mode == ScopeScanMode.LostHold &&
		    _now - m_LostHoldStartTime >= m_LostTargetHoldSeconds)
		{
			m_Mode = ScopeScanMode.Sweep;
		}

		if (!m_FrozenForTest && m_Mode == ScopeScanMode.Sweep)
			AdvanceYaw(_deltaTime);

		bool stepMoved = Mathf.Abs(m_ScanYawDegrees - m_LastEmittedYawDegrees) >= m_StepDegrees - 0.001f;
		bool timerDue = _now - m_LastScanTime >= m_MaxScanIntervalSeconds;
		bool shouldScan;
		if (m_Mode == ScopeScanMode.TrackTarget)
			shouldScan = _requestedSearch;
		else if (m_Mode == ScopeScanMode.LostHold)
			shouldScan = _requestedSearch || timerDue;
		else if (m_FrozenForTest)
			shouldScan = _requestedSearch || timerDue;
		else
			shouldScan = _requestedSearch || stepMoved || timerDue;

		if (s_TestLogging && m_Mode != m_LastLoggedMode)
		{
			m_LastLoggedMode = m_Mode;
			Debug.Log(
				$"[SCOPE SCAN] yaw={m_ScanYawDegrees:F1} mode={m_Mode} dir={m_Direction} t={_now:F2}",
				this);
		}

		return shouldScan;
	}

	public void NotifyScopeContact(bool _hasContact, Transform _target, Vector3 _worldPosition, float _now)
	{
		if (m_FrozenForTest && _hasContact)
			return;

		if (_hasContact && _target != null)
		{
			m_Mode = ScopeScanMode.TrackTarget;
			m_LastContactTarget = _target;
			m_LastContactWorld = _worldPosition;
			m_HasLastContact = true;
			return;
		}

		if (m_Mode == ScopeScanMode.TrackTarget)
		{
			m_Mode = ScopeScanMode.LostHold;
			m_LostHoldStartTime = _now;
		}
	}

	public void MarkScanEmitted()
	{
		MarkScanEmitted(Time.time);
	}

	public void MarkScanEmitted(float _now)
	{
		m_PreviousScanYawDegrees = m_LastEmittedYawDegrees;
		m_LastEmittedYawDegrees = m_ScanYawDegrees;
		m_LastScanTime = _now;
	}

	public bool IsCoverageFresh(float _now)
	{
		if (m_LastScanTime < -100f)
			return false;
		return _now - m_LastScanTime < DefaultCoverageFreshSeconds;
	}
	#endregion

	#region Private Methods
	private void AdvanceYaw(float _deltaTime)
	{
		float speed = m_ScanSpeedDegrees;
		if (IsWalking())
			speed *= m_WalkSpeedScale;

		m_ScanYawDegrees += m_Direction * speed * Mathf.Max(0f, _deltaTime);
		float half = m_AssignedSectorHalfDegrees;
		if (m_ScanYawDegrees >= half)
		{
			m_ScanYawDegrees = half;
			m_Direction = -1;
		}
		else if (m_ScanYawDegrees <= -half)
		{
			m_ScanYawDegrees = -half;
			m_Direction = 1;
		}
	}

	private static Vector3 RotateBody(Vector3 _bodyForwardXZ, float _yawDegrees)
	{
		Vector3 fwd = _bodyForwardXZ;
		fwd.y = 0f;
		if (fwd.sqrMagnitude < 1e-8f)
			return Vector3.forward;
		fwd.Normalize();
		return Quaternion.AngleAxis(_yawDegrees, Vector3.up) * fwd;
	}

	private bool IsWalking()
	{
		if (m_Agent == null)
			TryGetComponent(out m_Agent);
		if (m_Agent == null || !m_Agent.enabled)
			return false;
		Vector3 v = m_Agent.velocity;
		v.y = 0f;
		return v.sqrMagnitude > 0.36f;
	}
	#endregion
}

public enum ScopeScanMode
{
	Sweep = 0,
	TrackTarget = 1,
	LostHold = 2
}

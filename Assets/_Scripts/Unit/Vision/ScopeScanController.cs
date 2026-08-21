using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Logical optic yaw sweep. Does not write Animator, Hand_R, or weapon local TRS.
/// Axis is the current sight / bore forward supplied by <see cref="UnitVision"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScopeScanController : MonoBehaviour
{
	#region Constants
	public const float DefaultScanHalfAngleDegrees = 4f;
	public const float DefaultScanSpeedDegrees = 25f;
	public const float DefaultWalkSpeedScale = 0.5f;
	public const float DefaultStepDegrees = 2f;
	public const float DefaultMaxHz = 15f;
	#endregion

	#region Serialized
	[SerializeField, Range(0.5f, 20f)] private float m_ScanHalfAngleDegrees = DefaultScanHalfAngleDegrees;
	[SerializeField, Min(1f)] private float m_ScanSpeedDegrees = DefaultScanSpeedDegrees;
	[SerializeField, Range(0.1f, 1f)] private float m_WalkSpeedScale = DefaultWalkSpeedScale;
	[SerializeField, Range(0.5f, 8f)] private float m_StepDegrees = DefaultStepDegrees;
	[SerializeField, Range(1f, 30f)] private float m_MaxHz = DefaultMaxHz;
	#endregion

	#region Private Fields
	private float m_ScanYawDegrees = -DefaultScanHalfAngleDegrees;
	private float m_LastEmittedYawDegrees = -999f;
	private float m_LastScanTime = -999f;
	private int m_Direction = 1;
	private bool m_LockedOnContact;
	private bool m_FrozenForTest;
	private NavMeshAgent m_Agent;
	private static bool s_TestLogging;
	#endregion

	#region Public Properties
	public float ScanYawDegrees => m_ScanYawDegrees;
	public float LastScanTime => m_LastScanTime;
	public bool IsLockedOnContact => m_LockedOnContact;
	public bool IsFrozenForTest => m_FrozenForTest;
	public float ScanHalfAngleDegrees => m_ScanHalfAngleDegrees;
	public float StepDegrees => m_StepDegrees;

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
		m_ScanYawDegrees = -m_ScanHalfAngleDegrees;
	}
	#endregion

	#region Public Methods
	public void ResetSweep()
	{
		m_ScanYawDegrees = -m_ScanHalfAngleDegrees;
		m_Direction = 1;
		m_LockedOnContact = false;
		m_LastEmittedYawDegrees = -999f;
	}

	public void SetFrozenForTest(bool _frozen)
	{
		m_FrozenForTest = _frozen;
	}

	public void SetScanYawForTest(float _yawDegrees)
	{
		m_ScanYawDegrees = Mathf.Clamp(_yawDegrees, -m_ScanHalfAngleDegrees, m_ScanHalfAngleDegrees);
		m_LastEmittedYawDegrees = m_ScanYawDegrees;
	}

	public Vector3 GetSweepForwardXZ(Vector3 _boreForwardXZ)
	{
		Vector3 fwd = _boreForwardXZ;
		fwd.y = 0f;
		if (fwd.sqrMagnitude < 1e-8f)
			return Vector3.forward;
		fwd.Normalize();
		return Quaternion.AngleAxis(m_ScanYawDegrees, Vector3.up) * fwd;
	}

	/// <summary>
	/// Advances the sawtooth when the optic is active. Returns true when a Detail scan should run.
	/// </summary>
	public bool Tick(float _deltaTime, bool _scopeActive, bool _requestedSearch)
	{
		if (!_scopeActive)
		{
			m_LockedOnContact = false;
			return _requestedSearch;
		}

		if (m_FrozenForTest)
			return _requestedSearch;

		if (!m_LockedOnContact)
			AdvanceYaw(_deltaTime);

		float minInterval = 1f / Mathf.Max(1f, m_MaxHz);
		bool stepMoved = Mathf.Abs(m_ScanYawDegrees - m_LastEmittedYawDegrees) >= m_StepDegrees - 0.001f;
		bool timerDue = Time.time - m_LastScanTime >= minInterval;
		bool shouldScan = _requestedSearch || stepMoved || (timerDue && !m_LockedOnContact);

		if (s_TestLogging && shouldScan && stepMoved)
		{
			Debug.Log(
				$"[SCOPE SCAN] yaw={m_ScanYawDegrees:F1} locked={(m_LockedOnContact ? 1 : 0)} " +
				$"step=1 t={Time.time:F2}",
				this);
		}

		return shouldScan;
	}

	public void NotifyScopeContact(bool _hasContact, float _yawToTargetDegrees)
	{
		if (m_FrozenForTest)
			return;

		if (!_hasContact)
		{
			m_LockedOnContact = false;
			return;
		}

		m_LockedOnContact = true;
		m_ScanYawDegrees = Mathf.Clamp(_yawToTargetDegrees, -m_ScanHalfAngleDegrees, m_ScanHalfAngleDegrees);
	}

	public void MarkScanEmitted()
	{
		m_LastEmittedYawDegrees = m_ScanYawDegrees;
		m_LastScanTime = Time.time;
	}
	#endregion

	#region Private Methods
	private void AdvanceYaw(float _deltaTime)
	{
		float speed = m_ScanSpeedDegrees;
		if (IsWalking())
			speed *= m_WalkSpeedScale;

		m_ScanYawDegrees += m_Direction * speed * Mathf.Max(0f, _deltaTime);
		float half = m_ScanHalfAngleDegrees;
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

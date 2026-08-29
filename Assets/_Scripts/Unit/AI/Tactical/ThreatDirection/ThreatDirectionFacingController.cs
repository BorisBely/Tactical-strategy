using UnityEngine;

/// <summary>
/// Why DesiredFacing last changed. Event-driven, not per-tick.
/// </summary>
public enum ThreatDirectionFacingReason
{
	None = 0,
	ThreatDirectionChanged = 1,
	CoverAcquired = 2,
	ReadinessChanged = 3
}

/// <summary>
/// #14C.1 facing request from threat sector center. #14C.2: slack grows with uncertainty / low confidence.
/// Does not slerp Transform every frame. Does not write Readiness or Search.
/// </summary>
public sealed class ThreatDirectionFacingController
{
	#region Constants
	public const float DeadbandDegrees = 12f;
	#endregion

	#region Private Fields
	private bool m_HasFacing;
	private Vector3 m_DesiredFacing = Vector3.forward;
	private float m_CommittedYaw;
	private string m_LastLogPayload = string.Empty;
	private int m_LogCount;
	private int m_UpdateCount;
	private Component m_LogActor;
	#endregion

	#region Public Properties
	public Component LogActor
	{
		get => m_LogActor;
		set => m_LogActor = value;
	}

	public bool HasDesiredFacing => m_HasFacing;

	public Vector3 DesiredFacing => m_DesiredFacing;

	public float DesiredYaw => m_CommittedYaw;

	public string LastLogPayload => m_LastLogPayload;

	public int LogCount => m_LogCount;

	public int UpdateCount => m_UpdateCount;
	#endregion

	#region Public Methods
	public void Reset()
	{
		m_HasFacing = false;
		m_DesiredFacing = Vector3.forward;
		m_CommittedYaw = 0f;
		m_LastLogPayload = string.Empty;
		m_LogCount = 0;
		m_UpdateCount = 0;
	}

	public bool TryGetDesiredFacing(out Vector3 _direction)
	{
		_direction = m_DesiredFacing;
		return m_HasFacing;
	}

	public bool Notify(
		in ThreatDirectionKnowledge _knowledge,
		ThreatDirectionFacingReason _reason,
		float _currentYawDegrees)
	{
		if (!_knowledge.HasValue || _reason == ThreatDirectionFacingReason.None)
			return false;

		_ = _currentYawDegrees;
		Vector3 desired = _knowledge.Direction;
		desired.y = 0f;
		if (desired.sqrMagnitude < ThreatDirectionMath.DirectionEpsilonSqr)
			return false;
		desired.Normalize();
		float desiredYaw = YawFrom(desired);
		float slack = FacingSlackDegrees(in _knowledge);

		if (m_HasFacing &&
		    Mathf.Abs(Mathf.DeltaAngle(m_CommittedYaw, desiredYaw)) < slack)
			return false;

		m_HasFacing = true;
		m_DesiredFacing = desired;
		m_CommittedYaw = desiredYaw;
		m_UpdateCount++;
		m_LastLogPayload = ThreatDirectionCoverLog.FormatFacing(in _knowledge);
		m_LogCount++;
		ThreatDirectionCoverLog.EmitFacing(m_LogActor, m_LastLogPayload);
		return true;
	}

	public static float FacingSlackDegrees(in ThreatDirectionKnowledge _knowledge)
	{
		if (!_knowledge.HasValue)
			return DeadbandDegrees;
		float cone = Mathf.Max(DeadbandDegrees, _knowledge.UncertaintyDegrees * 0.4f);
		float lowConfidence = (1f - Mathf.Clamp01(_knowledge.Confidence)) * DeadbandDegrees;
		return cone + lowConfidence;
	}

	public static float YawFrom(Vector3 _direction)
	{
		Vector3 flat = _direction;
		flat.y = 0f;
		if (flat.sqrMagnitude < ThreatDirectionMath.DirectionEpsilonSqr)
			return 0f;
		return Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
	}
	#endregion
}

using UnityEngine;

/// <summary>
/// One #14C.4 observe pass. Facing and ThreatFit are independent of Move.
/// </summary>
public struct ThreatDirectionReorientationResult
{
	public bool TacticalChanged;
	public bool FacingUpdated;
	public bool ThreatFitChanged;
	public CoverThreatFit ThreatFit;
	public float AngleDeltaDegrees;
}

/// <summary>
/// #14C.4 Dynamic Threat Reorientation. Event-driven.
/// Updates DesiredFacing and ThreatFit. Does not Move / Release / scan / Fire.
/// </summary>
public sealed class ThreatDirectionReorientation
{
	#region Private Fields
	private readonly ThreatDirectionFacingController m_Facing;
	private bool m_HasTactical;
	private Vector3 m_LastTacticalDirection = Vector3.forward;
	private ThreatDirectionCompass m_LastTacticalCompass = ThreatDirectionCompass.North;
	private bool m_HasFit;
	private CoverThreatFit m_LastFit;
	private int m_ChangeCount;
	private int m_FacingLogCount;
	private int m_FitLogCount;
	private string m_LastChangePayload = string.Empty;
	private string m_LastFacingPayload = string.Empty;
	private string m_LastFitPayload = string.Empty;
	private Component m_LogActor;
	#endregion

	#region Constructors
	public ThreatDirectionReorientation(ThreatDirectionFacingController _facing)
	{
		m_Facing = _facing ?? new ThreatDirectionFacingController();
	}
	#endregion

	#region Public Properties
	public Component LogActor
	{
		get => m_LogActor;
		set
		{
			m_LogActor = value;
			m_Facing.LogActor = value;
		}
	}

	public ThreatDirectionFacingController Facing => m_Facing;

	public int ChangeCount => m_ChangeCount;

	public int FacingLogCount => m_FacingLogCount;

	public int FitLogCount => m_FitLogCount;

	public string LastChangePayload => m_LastChangePayload;

	public string LastFacingPayload => m_LastFacingPayload;

	public string LastFitPayload => m_LastFitPayload;

	public CoverThreatFit LastFit => m_HasFit ? m_LastFit : CoverThreatFit.Unknown;

	public bool HasTacticalDirection => m_HasTactical;

	public Vector3 LastTacticalDirection => m_LastTacticalDirection;

	public float LastAngleDeltaDegrees { get; private set; }
	#endregion

	#region Public Methods
	public void Reset()
	{
		m_HasTactical = false;
		m_LastTacticalDirection = Vector3.forward;
		m_LastTacticalCompass = ThreatDirectionCompass.North;
		LastAngleDeltaDegrees = 0f;
		m_HasFit = false;
		m_LastFit = CoverThreatFit.Unknown;
		m_ChangeCount = 0;
		m_FacingLogCount = 0;
		m_FitLogCount = 0;
		m_LastChangePayload = string.Empty;
		m_LastFacingPayload = string.Empty;
		m_LastFitPayload = string.Empty;
		m_Facing.Reset();
	}

	public ThreatDirectionReorientationResult Observe(
		in ThreatDirectionKnowledge _knowledge,
		CoverCandidate _occupying = null,
		float _currentYawDegrees = 0f,
		ThreatDirectionFacingReason _reason = ThreatDirectionFacingReason.ThreatDirectionChanged)
	{
		var result = new ThreatDirectionReorientationResult
		{
			ThreatFit = CoverThreatFit.Unknown
		};
		if (!_knowledge.HasValue)
			return result;

		result.ThreatFit = StampFit(in _knowledge, _occupying, ref result);
		result.AngleDeltaDegrees = m_HasTactical
			? ThreatDirectionReorientationMath.AngleDegrees(m_LastTacticalDirection, _knowledge.Direction)
			: 0f;
		LastAngleDeltaDegrees = result.AngleDeltaDegrees;

		if (m_HasTactical &&
		    _reason == ThreatDirectionFacingReason.ThreatDirectionChanged &&
		    ThreatDirectionReorientationMath.IsSignificantChange(
			    m_LastTacticalDirection,
			    _knowledge.Direction,
			    _knowledge.Confidence))
		{
			result.TacticalChanged = true;
			m_ChangeCount++;
			m_LastChangePayload = ThreatDirectionReorientationLog.FormatChanged(
				m_LastTacticalCompass,
				_knowledge.Compass,
				_knowledge.Confidence,
				result.AngleDeltaDegrees);
			m_LastTacticalDirection = _knowledge.Direction;
			m_LastTacticalCompass = _knowledge.Compass;
			ThreatDirectionReorientationLog.EmitChanged(m_LogActor, m_LastChangePayload);
		}
		else if (!m_HasTactical)
		{
			m_HasTactical = true;
			m_LastTacticalDirection = _knowledge.Direction;
			m_LastTacticalCompass = _knowledge.Compass;
		}

		bool allowFacing = _reason != ThreatDirectionFacingReason.ThreatDirectionChanged ||
		                   ThreatDirectionReorientationMath.AllowsFacingUpdate(
			                   _knowledge.Confidence,
			                   m_Facing.HasDesiredFacing);
		if (!allowFacing)
			return result;

		ThreatDirectionCompass fromFacing = m_Facing.HasDesiredFacing
			? ThreatDirectionEstimator.CompassFrom(m_Facing.DesiredFacing)
			: ThreatDirectionEstimator.CompassFrom(
				ThreatDirectionReorientationMath.DirectionFromYaw(_currentYawDegrees));
		if (!m_Facing.Notify(in _knowledge, _reason, _currentYawDegrees))
			return result;

		result.FacingUpdated = true;
		m_FacingLogCount++;
		m_LastFacingPayload = ThreatDirectionReorientationLog.FormatFacing(
			fromFacing,
			ThreatDirectionEstimator.CompassFrom(_knowledge.Direction),
			_reason);
		ThreatDirectionReorientationLog.EmitFacing(m_LogActor, m_LastFacingPayload);
		return result;
	}
	#endregion

	#region Private Methods
	private CoverThreatFit StampFit(
		in ThreatDirectionKnowledge _knowledge,
		CoverCandidate _occupying,
		ref ThreatDirectionReorientationResult _result)
	{
		if (_occupying == null)
			return CoverThreatFit.Unknown;

		CoverThreatFit fit = ThreatDirectionReorientationMath.ClassifyFit(
			_occupying.Normal,
			_knowledge.Direction);
		_result.ThreatFit = fit;
		if (m_HasFit && fit == m_LastFit)
			return fit;

		m_HasFit = true;
		m_LastFit = fit;
		_result.ThreatFitChanged = true;
		m_FitLogCount++;
		m_LastFitPayload = ThreatDirectionReorientationLog.FormatFit(
			_occupying.CandidateId,
			_knowledge.Compass,
			fit);
		ThreatDirectionReorientationLog.EmitFit(m_LogActor, m_LastFitPayload);
		return fit;
	}
	#endregion
}

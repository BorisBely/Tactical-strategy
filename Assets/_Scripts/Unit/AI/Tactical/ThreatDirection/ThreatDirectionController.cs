using UnityEngine;

/// <summary>
/// #14C event-driven threat bearing. Does not choose Cover, move, rotate, Aim, or Fire.
/// Does not poll enemy position. Age / expiry use the supplied clock only.
/// </summary>
public sealed class ThreatDirectionController
{
	#region Private Fields
	private Vector3 m_Direction;
	private ThreatDirectionSource m_Source = ThreatDirectionSource.InitialEstimate;
	private ThreatDirectionState m_State = ThreatDirectionState.None;
	private float m_AcquiredTime;
	private float m_StateEnterTime;
	private float m_Now;
	private bool m_HasExpected;
	private Vector3 m_ExpectedDirection;
	private bool m_PrevVisible;
	private bool m_PrevSound;
	private bool m_PrevReport;
	private float m_LastSoundTime = float.NegativeInfinity;
	private float m_LastReportTime = float.NegativeInfinity;
	private int m_LogKey = int.MinValue;
	private int m_QualityKey = int.MinValue;
	private string m_LastLogPayload = string.Empty;
	private string m_LastQualityPayload = string.Empty;
	private int m_LogCount;
	private int m_QualityLogCount;
	private Component m_LogActor;
	#endregion

	#region Public Properties
	public Component LogActor
	{
		get => m_LogActor;
		set => m_LogActor = value;
	}

	public string LastLogPayload => m_LastLogPayload;

	public string LastQualityPayload => m_LastQualityPayload;

	public int LogCount => m_LogCount;

	public int QualityLogCount => m_QualityLogCount;

	public bool HasThreatDirection => m_State != ThreatDirectionState.None;

	public ThreatDirectionState CurrentState => m_State;

	public ThreatDirectionSource CurrentSource => m_Source;
	#endregion

	#region Public Methods
	public void Reset()
	{
		m_Direction = Vector3.zero;
		m_Source = ThreatDirectionSource.InitialEstimate;
		m_State = ThreatDirectionState.None;
		m_AcquiredTime = 0f;
		m_StateEnterTime = 0f;
		m_Now = 0f;
		m_HasExpected = false;
		m_ExpectedDirection = Vector3.zero;
		m_PrevVisible = false;
		m_PrevSound = false;
		m_PrevReport = false;
		m_LastSoundTime = float.NegativeInfinity;
		m_LastReportTime = float.NegativeInfinity;
		m_LogKey = int.MinValue;
		m_QualityKey = int.MinValue;
		m_LastLogPayload = string.Empty;
		m_LastQualityPayload = string.Empty;
		m_LogCount = 0;
		m_QualityLogCount = 0;
	}

	public bool TryGetThreatDirection(out ThreatDirectionKnowledge _knowledge)
	{
		_knowledge = BuildSnapshot();
		return _knowledge.HasValue;
	}

	public Vector3 GetThreatDirection()
	{
		return HasThreatDirection ? m_Direction : Vector3.zero;
	}

	public ThreatDirectionSector GetThreatSector()
	{
		return BuildSnapshot().Sector;
	}

	public float GetThreatConfidence()
	{
		return BuildSnapshot().Confidence;
	}

	public float GetThreatUncertainty()
	{
		return BuildSnapshot().UncertaintyDegrees;
	}

	public ThreatDirectionCompass GetThreatCompass()
	{
		return ThreatDirectionEstimator.CompassFrom(m_Direction);
	}

	public bool ApplyBattleStart(Vector3 _ownSpawnCenter, Vector3 _enemySpawnCenter, float _now)
	{
		m_Now = _now;
		if (!ThreatDirectionEstimator.TryExpectedDirection(
			    _ownSpawnCenter,
			    _enemySpawnCenter,
			    out Vector3 expected))
			return false;

		m_ExpectedDirection = expected;
		m_HasExpected = true;
		if (m_State != ThreatDirectionState.None)
			return true;

		Commit(
			expected,
			ThreatDirectionSource.InitialEstimate,
			ThreatDirectionState.Expected,
			_now);
		return true;
	}

	public bool ApplyHostileVisible(Vector3 _selfPosition, Vector3 _lastKnown, float _now)
	{
		m_Now = _now;
		if (!ThreatDirectionEstimator.TryDirection(_selfPosition, _lastKnown, out Vector3 direction))
			return false;

		Commit(direction, ThreatDirectionSource.Visual, ThreatDirectionState.Known, _now);
		return true;
	}

	public bool ApplyHostileLost(float _now)
	{
		m_Now = _now;
		if (m_Source != ThreatDirectionSource.Visual || m_State != ThreatDirectionState.Known)
			return false;

		EnterStale(_now);
		return true;
	}

	public bool ApplyGunshot(Vector3 _selfPosition, Vector3 _soundPosition, float _now)
	{
		m_Now = _now;
		m_LastSoundTime = _now;
		if (!ThreatDirectionMath.CanOverride(m_State, m_Source, ThreatDirectionSource.Sound))
			return false;
		if (!ThreatDirectionEstimator.TryDirection(_selfPosition, _soundPosition, out Vector3 direction))
			return false;

		Commit(direction, ThreatDirectionSource.Sound, ThreatDirectionState.Known, _now);
		return true;
	}

	public bool ApplyAllyReport(Vector3 _selfPosition, Vector3 _reportPosition, float _now)
	{
		m_Now = _now;
		m_LastReportTime = _now;
		if (!ThreatDirectionMath.CanOverride(m_State, m_Source, ThreatDirectionSource.AllyReport))
			return false;
		if (!ThreatDirectionEstimator.TryDirection(_selfPosition, _reportPosition, out Vector3 direction))
			return false;

		Commit(direction, ThreatDirectionSource.AllyReport, ThreatDirectionState.Known, _now);
		return true;
	}

	public void Tick(float _now)
	{
		m_Now = _now;
		TickExpiry(_now);
		EmitQualityIfChanged();
	}

	public void Tick(float _now, Vector3 _selfPosition, in AIPerceptionFrame _frame)
	{
		m_Now = _now;
		bool visible = ThreatDirectionStimulusMath.TryGetVisual(in _frame, out Vector3 lastKnown);
		bool sound = ThreatDirectionStimulusMath.TryGetSound(in _frame, out Vector3 soundPos, out float soundTime);
		bool report = ThreatDirectionStimulusMath.TryGetReport(in _frame, out Vector3 reportPos, out float reportTime);

		if (visible && !m_PrevVisible)
			ApplyHostileVisible(_selfPosition, lastKnown, _now);
		else if (!visible && m_PrevVisible)
			ApplyHostileLost(_now);

		if (sound && (!m_PrevSound || soundTime > m_LastSoundTime + 0.0001f))
		{
			m_LastSoundTime = soundTime;
			ApplyGunshot(_selfPosition, soundPos, _now);
		}

		if (report && (!m_PrevReport || reportTime > m_LastReportTime + 0.0001f))
		{
			m_LastReportTime = reportTime;
			ApplyAllyReport(_selfPosition, reportPos, _now);
		}

		TickExpiry(_now);
		m_PrevVisible = visible;
		m_PrevSound = sound;
		m_PrevReport = report;
		EmitQualityIfChanged();
	}
	#endregion

	#region Private Methods
	private ThreatDirectionKnowledge BuildSnapshot()
	{
		if (m_State == ThreatDirectionState.None)
			return default;

		float staleAge = m_State == ThreatDirectionState.Stale
			? Mathf.Max(0f, m_Now - m_StateEnterTime)
			: 0f;
		ThreatDirectionMath.QualityAt(
			m_State,
			m_Source,
			staleAge,
			out float confidence,
			out float uncertainty);
		return new ThreatDirectionKnowledge(
			m_Direction,
			ThreatDirectionEstimator.CompassFrom(m_Direction),
			confidence,
			uncertainty,
			Mathf.Max(0f, m_Now - m_AcquiredTime),
			m_Source,
			m_State);
	}

	private void Commit(
		Vector3 _direction,
		ThreatDirectionSource _source,
		ThreatDirectionState _state,
		float _now)
	{
		m_Direction = _direction;
		m_Source = _source;
		m_State = _state;
		m_AcquiredTime = _now;
		m_StateEnterTime = _now;
		m_Now = _now;
		EmitIfChanged();
	}

	private void EnterStale(float _now)
	{
		m_State = ThreatDirectionState.Stale;
		m_StateEnterTime = _now;
		m_Now = _now;
		EmitIfChanged();
	}

	private void TickExpiry(float _now)
	{
		if (m_State == ThreatDirectionState.Known &&
		    m_Source != ThreatDirectionSource.Visual &&
		    m_Source != ThreatDirectionSource.InitialEstimate)
		{
			if (_now - m_StateEnterTime >= ThreatDirectionMath.KnownToStaleSeconds(m_Source))
				EnterStale(_now);
		}

		if (m_State != ThreatDirectionState.Stale)
			return;
		if (_now - m_StateEnterTime < ThreatDirectionMath.StaleToFallbackSeconds(m_Source))
			return;

		RestoreExpected(_now);
	}

	private void RestoreExpected(float _now)
	{
		if (m_HasExpected)
		{
			Commit(
				m_ExpectedDirection,
				ThreatDirectionSource.InitialEstimate,
				ThreatDirectionState.Expected,
				_now);
			return;
		}

		m_State = ThreatDirectionState.None;
		m_Direction = Vector3.zero;
		m_Source = ThreatDirectionSource.InitialEstimate;
		m_AcquiredTime = _now;
		m_StateEnterTime = _now;
		m_Now = _now;
		EmitIfChanged();
	}

	private void EmitIfChanged()
	{
		ThreatDirectionKnowledge snapshot = BuildSnapshot();
		int key = ((int)snapshot.State << 8) | ((int)snapshot.Source << 4) | (int)snapshot.Compass;
		bool stateChanged = key != m_LogKey;
		if (stateChanged)
		{
			m_LogKey = key;
			m_LastLogPayload = ThreatDirectionLog.Format(in snapshot);
			m_LogCount++;
			ThreatDirectionLog.Emit(m_LogActor, m_LastLogPayload);
		}

		EmitQuality(in snapshot, stateChanged);
	}

	private void EmitQualityIfChanged()
	{
		if (m_State == ThreatDirectionState.None)
			return;
		ThreatDirectionKnowledge snapshot = BuildSnapshot();
		EmitQuality(in snapshot, false);
	}

	private void EmitQuality(in ThreatDirectionKnowledge _snapshot, bool _force)
	{
		if (!_snapshot.HasValue)
			return;

		int qualityKey = ThreatDirectionMath.QualityLogKey(
			_snapshot.Confidence,
			_snapshot.UncertaintyDegrees);
		if (!_force && qualityKey == m_QualityKey)
			return;

		m_QualityKey = qualityKey;
		m_LastQualityPayload = ThreatDirectionLog.Format(in _snapshot);
		m_QualityLogCount++;
		ThreatDirectionLog.EmitUpdate(m_LogActor, m_LastQualityPayload);
	}
	#endregion
}

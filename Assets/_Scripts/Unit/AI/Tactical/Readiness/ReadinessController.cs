using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14B readiness owner. 14B.0–14B.7: stimuli, hold/step-down, arm fatigue, combat integration.
/// Does not Fire, SetPose, or write UnitAIState / CombatIntent / Cover / Route / G6.
/// Rank changes durations, not the state machine. Fatigue does not change ReadinessState.
/// </summary>
public sealed class ReadinessController
{
	#region Constants
	private const int c_MaxLogLines = 48;
	#endregion

	#region Private Fields
	private ReadinessProfile m_Profile;
	private ReadinessContext m_Context;
	private ReadinessDecision m_Last;
	private ReadinessTransitionRequest m_LastRequest;
	private float m_LastTickTime;
	private bool m_HasTime;
	private bool m_HasProfile;
	private bool m_Allowed = true;
	private int m_TransitionRequestCount;
	private Component m_LogActor;
	private string m_LastLogPayload = string.Empty;
	private string m_LastEventPayload = string.Empty;
	private string m_LastTransitionPayload = string.Empty;
	private string m_LastDecayPayload = string.Empty;
	private string m_LastDecayHoldPayload = string.Empty;
	private bool m_PrevHostileVisible;
	private bool m_PrevGunshotHeard;
	private bool m_PrevHostileLost;
	private bool m_PrevCombatActivity;
	private bool m_HoldLogged;
	private ReadinessState m_HoldLoggedState;
	private string m_LastFatiguePayload = string.Empty;
	private string m_LastFatigueEffectPayload = string.Empty;
	private string m_LastFatigueValuePayload = string.Empty;
	private string m_LastReadinessEffectPayload = string.Empty;
	private int m_FatigueBand;
	private bool m_FatigueWasLoaded;
	private readonly List<string> m_LogLines = new List<string>(16);
	#endregion

	#region Public Properties
	public ReadinessProfile Profile => m_Profile;
	public ReadinessContext Context => m_Context;
	public ReadinessDecision Last => m_Last;
	public ReadinessState CurrentState => m_Context.CurrentState;
	public bool RequestsFire => false;
	public bool Allowed => m_Allowed;
	public bool HasCombatActivity => m_Context.HasActiveCombatActivity;
	public float LastCombatActivityTime => m_Context.LastCombatActivityTime;
	public ReadinessTransitionRequest LastRequest => m_LastRequest;
	public ReadinessPoseRequest PoseRequest => ReadinessPoseMath.FromController(this);
	public int TransitionRequestCount => m_TransitionRequestCount;
	public string LastLogPayload => m_LastLogPayload;
	public string LastEventPayload => m_LastEventPayload;
	public string LastTransitionPayload => m_LastTransitionPayload;
	public string LastDecayPayload => m_LastDecayPayload;
	public string LastDecayHoldPayload => m_LastDecayHoldPayload;
	public string LastFatiguePayload => m_LastFatiguePayload;
	public string LastFatigueEffectPayload => m_LastFatigueEffectPayload;
	public string LastFatigueValuePayload => m_LastFatigueValuePayload;
	public string LastReadinessEffectPayload => m_LastReadinessEffectPayload;
	public float ArmFatigue => m_Context.ArmFatigue;
	public ArmFatigueEffects FatigueEffects =>
		ArmFatigueMath.Evaluate(m_Context.ArmFatigue, m_Profile.ArmFatigue);
	public IReadOnlyList<string> LogLines => m_LogLines;

	public Component LogActor
	{
		get => m_LogActor;
		set => m_LogActor = value;
	}
	#endregion

	#region Public Methods
	public void Reset(ReadinessRankKind _rank, float _now)
	{
		Reset(ReadinessProfile.Instant(_rank), _now);
	}

	public void Reset(in ReadinessProfile _profile, float _now)
	{
		m_Profile = _profile;
		m_HasProfile = true;
		m_HasTime = true;
		m_Allowed = true;
		m_LastTickTime = _now;
		m_TransitionRequestCount = 0;
		m_LastRequest = default;
		m_LastEventPayload = string.Empty;
		m_LastTransitionPayload = string.Empty;
		m_LastDecayPayload = string.Empty;
		m_LastDecayHoldPayload = string.Empty;
		m_PrevHostileVisible = false;
		m_PrevGunshotHeard = false;
		m_PrevHostileLost = false;
		m_PrevCombatActivity = false;
		m_HoldLogged = false;
		m_HoldLoggedState = default;
		m_LastFatiguePayload = string.Empty;
		m_LastFatigueEffectPayload = string.Empty;
		m_LastFatigueValuePayload = string.Empty;
		m_LastReadinessEffectPayload = string.Empty;
		m_FatigueBand = 0;
		m_FatigueWasLoaded = false;
		m_LogLines.Clear();
		m_Context = new ReadinessContext
		{
			CurrentState = _profile.CalmState,
			PreviousState = _profile.CalmState,
			StateEnterTime = _now,
			LastCombatActivityTime = _now,
			HasActiveCombatActivity = false,
			CalmDownRemaining = 0f,
			Rank = _profile.Rank,
			ArmFatigue = 0f,
			ArmFatigueModifier = _profile.ArmFatigue.ArmLoadMultiplier < 0.01f
				? 1f
				: _profile.ArmFatigue.ArmLoadMultiplier,
			LastChangeReason = ReadinessChangeReason.Initial,
			ChangeCount = 1
		};
		m_Last = new ReadinessDecision
		{
			State = m_Context.CurrentState,
			Reason = ReadinessChangeReason.Initial,
			Changed = true,
			TransitionProgress = 1f
		};
		PushLog(ReadinessLog.FormatState(m_Context.CurrentState, ReadinessChangeReason.Initial));
	}

	public void SetAllowed(bool _allowed)
	{
		m_Allowed = _allowed;
	}

	public void SetArmFatigue(float _fatigue, float _modifier)
	{
		m_Context.ArmFatigue = ArmFatigueMath.Clamp01(_fatigue);
		m_Context.ArmFatigueModifier = _modifier < 0.01f ? 0.01f : _modifier;
		m_FatigueBand = ArmFatigueMath.ThresholdBand(m_Context.ArmFatigue);
	}

	public bool RequestTransition(ReadinessState _to, ReadinessChangeReason _reason, float _now)
	{
		if (!m_Allowed)
			return false;
		if (!m_HasProfile)
			Reset(ReadinessRankKind.Soldier, _now);

		return BeginTransition(_to, _now, _reason);
	}

	public ReadinessDecision Tick(float _now, ReadinessStimulus _stimulus)
	{
		return Tick(_now, ReadinessFrame.FromStimulus(_stimulus));
	}

	public ReadinessDecision Tick(float _now, in ReadinessFrame _frame)
	{
		if (!m_HasProfile)
		{
			if (!m_Allowed)
				return m_Last;
			Reset(ReadinessRankKind.Soldier, _now);
		}

		m_Last.Changed = false;

		if (!m_Allowed)
		{
			m_LastTickTime = _now;
			m_HasTime = true;
			return SnapshotLast();
		}

		float dt = 0f;
		if (m_HasTime && _now > m_LastTickTime)
			dt = _now - m_LastTickTime;
		m_LastTickTime = _now;
		m_HasTime = true;

		EmitRisingEdges(in _frame);

		bool activity = ReadinessCombatActivity.FromFrame(in _frame);
		m_Context.HasActiveCombatActivity = activity;
		if (activity)
			m_Context.LastCombatActivityTime = _now;

		if (activity)
			CancelPendingDecay();

		if (_frame.HostileVisible)
			BeginTransition(ReadinessState.Aim, _now, ReadinessChangeReason.HostileVisible);
		else if (_frame.GunshotHeard)
			TryHearGunshot(_now);

		AdvanceTransition(dt, _now);

		if (!activity)
			TryDecay(_now, _frame.CombatActivityExpired);

		TickFatigue(dt, _frame.Firing);
		UpdateCalmDownRemaining(_now);
		MaybeLogHold();
		return SnapshotLast();
	}
	#endregion

	#region Private Methods
	private ReadinessDecision SnapshotLast()
	{
		m_Last.State = m_Context.CurrentState;
		m_Last.Reason = m_Context.LastChangeReason;
		m_Last.HasPendingTransition = m_Context.HasPendingTransition;
		m_Last.TransitionProgress = m_Context.HasPendingTransition
			? m_Context.TransitionProgress
			: 1f;
		m_Last.HasActiveCombatActivity = m_Context.HasActiveCombatActivity;
		m_Last.CalmDownRemaining = m_Context.CalmDownRemaining;
		m_Last.DecayPhase = m_Context.DecayPhase;
		return m_Last;
	}

	private void EmitRisingEdges(in ReadinessFrame _frame)
	{
		if (_frame.HostileVisible && !m_PrevHostileVisible)
			PushEvent(ReadinessLog.FormatEvent(ReadinessStimulus.HostileVisible, _frame.StimulusTarget));
		if (_frame.GunshotHeard && !m_PrevGunshotHeard)
			PushEvent(ReadinessLog.FormatEvent(ReadinessStimulus.GunshotHeard, null));
		if (_frame.HostileLost && !m_PrevHostileLost)
			PushEvent(ReadinessLog.FormatEvent(ReadinessStimulus.HostileLost, null));
		if (_frame.CombatActivity && !m_PrevCombatActivity)
			PushEvent(ReadinessLog.FormatEvent(ReadinessStimulus.CombatActivity, null));

		m_PrevHostileVisible = _frame.HostileVisible;
		m_PrevGunshotHeard = _frame.GunshotHeard;
		m_PrevHostileLost = _frame.HostileLost;
		m_PrevCombatActivity = _frame.CombatActivity;
	}

	private void TryHearGunshot(float _now)
	{
		ReadinessState heard = m_Profile.GunshotReadyState;
		ReadinessState pendingOrCurrent = m_Context.HasPendingTransition
			? m_Context.TransitionTo
			: m_Context.CurrentState;
		if (ReadinessMath.Level(pendingOrCurrent) >= ReadinessMath.Level(heard))
			return;

		BeginTransition(heard, _now, ReadinessChangeReason.Gunshot);
	}

	private bool BeginTransition(ReadinessState _target, float _now, ReadinessChangeReason _reason)
	{
		if (!m_Allowed)
			return false;

		if (ReadinessMath.IsPendingDecay(in m_Context) &&
		    ReadinessMath.IsRaise(m_Context.CurrentState, _target))
			CancelPendingDecay();

		if (m_Context.HasPendingTransition && m_Context.TransitionTo == _target)
			return false;
		if (m_Context.CurrentState == _target && !m_Context.HasPendingTransition)
			return false;

		float profileDuration;
		float rankModifier;
		if (_target == ReadinessState.Aim)
		{
			profileDuration = ReadinessMath.AimProfileDuration(m_Context.CurrentState, in m_Profile);
			rankModifier = m_Profile.RankReactionModifier;
		}
		else if (ReadinessMath.IsRaise(m_Context.CurrentState, _target))
		{
			profileDuration = m_Profile.ReadyRaiseDuration;
			rankModifier = m_Profile.ToReadySpeed;
		}
		else
		{
			profileDuration = ReadinessMath.DecayProfileDuration(m_Context.CurrentState, in m_Profile);
			rankModifier = m_Profile.RankCalmDownModifier;
		}

		float duration = ReadinessMath.TransitionDuration(
			m_Context.CurrentState,
			_target,
			in m_Profile);

		m_LastRequest = new ReadinessTransitionRequest
		{
			FromState = m_Context.CurrentState,
			ToState = _target,
			Reason = _reason,
			StartTime = _now,
			Duration = duration,
			Progress = 0f,
			ProfileDuration = profileDuration,
			RankModifier = rankModifier
		};
		m_TransitionRequestCount++;

		if (duration <= 0f)
		{
			ApplyState(_target, _now, _reason, 0f, true);
			return true;
		}

		m_Context.HasPendingTransition = true;
		m_Context.TransitionFrom = m_Context.CurrentState;
		m_Context.TransitionTo = _target;
		m_Context.TransitionStartTime = _now;
		m_Context.TransitionDuration = duration;
		m_Context.TransitionProgress = 0f;
		PushLog(ReadinessLog.FormatTransition(
			m_Context.TransitionFrom,
			_target,
			_reason,
			duration,
			m_Profile.Rank,
			profileDuration,
			rankModifier));
		if (ReadinessLog.IsDecayReason(_reason))
			PushDecay(
				m_Context.TransitionFrom,
				_target,
				_reason,
				duration,
				profileDuration,
				rankModifier);
		else
			PushChannelTransition(
				m_Context.TransitionFrom,
				_target,
				_reason,
				duration,
				profileDuration,
				rankModifier);
		return true;
	}

	private void AdvanceTransition(float _dt, float _now)
	{
		if (!m_Context.HasPendingTransition)
			return;

		if (m_Context.TransitionDuration <= 0f)
		{
			ApplyState(m_Context.TransitionTo, _now, CompleteReason(), 0f, true);
			return;
		}

		m_Context.TransitionProgress += _dt / m_Context.TransitionDuration;
		m_LastRequest.Progress = Mathf.Clamp01(m_Context.TransitionProgress);
		if (m_Context.TransitionProgress < 1f)
			return;

		ApplyState(
			m_Context.TransitionTo,
			_now,
			CompleteReason(),
			m_Context.TransitionDuration,
			true);
	}

	private ReadinessChangeReason CompleteReason()
	{
		return ReadinessLog.IsDecayReason(m_LastRequest.Reason)
			? m_LastRequest.Reason
			: ReadinessChangeReason.TransitionComplete;
	}

	private void CancelPendingDecay()
	{
		if (!ReadinessMath.IsPendingDecay(in m_Context))
			return;

		m_Context.HasPendingTransition = false;
		m_Context.TransitionProgress = 1f;
		m_LastRequest.Progress = 1f;
	}

	private void TryDecay(float _now, bool _force)
	{
		if (m_Context.HasPendingTransition)
			return;
		if (m_Context.CurrentState == m_Profile.CalmState)
			return;

		float delay = ReadinessMath.EffectiveHoldTime(m_Context.CurrentState, in m_Profile);

		if (!_force && _now - m_Context.LastCombatActivityTime < delay)
			return;

		ReadinessState next = ReadinessMath.NextDecayState(m_Context.CurrentState, in m_Profile);
		if (next == m_Context.CurrentState)
			return;

		ReadinessChangeReason reason;
		if (m_Context.CurrentState == ReadinessState.Aim)
			reason = ReadinessChangeReason.CombatActivityExpired;
		else if (next == m_Profile.CalmState)
			reason = ReadinessChangeReason.Calm;
		else
			reason = ReadinessChangeReason.CalmDown;

		BeginTransition(next, _now, reason);
	}

	private void ApplyState(
		ReadinessState _state,
		float _now,
		ReadinessChangeReason _reason,
		float _duration,
		bool _log)
	{
		float loggedDuration = m_Context.HasPendingTransition
			? m_Context.TransitionDuration
			: _duration;
		ReadinessState from = m_Context.CurrentState;

		m_Context.HasPendingTransition = false;
		m_Context.TransitionProgress = 1f;
		m_Last.Changed = _state != m_Context.CurrentState;
		if (!m_Last.Changed)
			return;

		m_Context.PreviousState = from;
		m_Context.CurrentState = _state;
		m_Context.StateEnterTime = _now;
		m_Context.LastChangeReason = _reason;
		m_Context.ChangeCount++;
		m_LastRequest.Progress = 1f;
		m_HoldLogged = false;
		if (ReadinessMath.IsSingleStepDecay(from, _state))
			m_Context.LastCombatActivityTime = _now;

		if (!_log)
			return;

		float profileDuration = m_LastRequest.ProfileDuration;
		float rankModifier = m_LastRequest.RankModifier;
		if (ReadinessLog.IsDecayReason(_reason))
		{
			profileDuration = ReadinessMath.EffectiveCalmDownDelay(in m_Profile);
			rankModifier = m_Profile.CalmDownDelayModifier;
		}

		PushLog(ReadinessLog.FormatTransition(
			from,
			_state,
			_reason,
			loggedDuration,
			m_Profile.Rank,
			profileDuration,
			rankModifier));
		if (ReadinessLog.IsDecayReason(_reason))
			PushDecay(from, _state, _reason, loggedDuration, profileDuration, rankModifier);
		else
			PushChannelTransition(from, _state, _reason, loggedDuration, profileDuration, rankModifier);
	}

	private void UpdateCalmDownRemaining(float _now)
	{
		m_Context.DecayPhase = ReadinessMath.DecayPhase(in m_Context, in m_Profile);

		if (m_Context.CurrentState == m_Profile.CalmState)
		{
			m_Context.CalmDownRemaining = 0f;
			return;
		}

		if (ReadinessMath.IsPendingDecay(in m_Context))
		{
			float left = (1f - Mathf.Clamp01(m_Context.TransitionProgress)) * m_Context.TransitionDuration;
			m_Context.CalmDownRemaining = left < 0f ? 0f : left;
			return;
		}

		float delay = ReadinessMath.EffectiveHoldTime(m_Context.CurrentState, in m_Profile);

		if (m_Context.HasActiveCombatActivity)
		{
			m_Context.CalmDownRemaining = delay;
			return;
		}

		float elapsed = _now - m_Context.LastCombatActivityTime;
		m_Context.CalmDownRemaining = elapsed >= delay ? 0f : delay - elapsed;
	}

	private void TickFatigue(float _dt, bool _firing)
	{
		ArmFatigueProfile profile = m_Profile.ArmFatigue;
		profile.ArmLoadMultiplier = m_Context.ArmFatigueModifier;
		bool loaded;
		float next = ArmFatigueMath.Step(
			m_Context.ArmFatigue,
			_dt,
			m_Context.CurrentState,
			_firing,
			m_Allowed,
			in profile,
			out loaded);

		int band = ArmFatigueMath.ThresholdBand(next);
		if (band > m_FatigueBand)
		{
			m_LastFatiguePayload = ArmFatigueLog.FormatThreshold(band);
			AppendLine(m_LastFatiguePayload);
			ArmFatigueLog.Emit(m_LogActor, m_LastFatiguePayload);
			PushFatigueEffect(next, in profile);
		}
		else if (m_FatigueWasLoaded && !loaded && next > 0.0001f)
		{
			m_LastFatiguePayload = ArmFatigueLog.FormatRecoveryStart();
			AppendLine(m_LastFatiguePayload);
			ArmFatigueLog.Emit(m_LogActor, m_LastFatiguePayload);
			PushFatigueEffect(next, in profile);
		}

		m_Context.ArmFatigue = next;
		m_FatigueBand = band;
		m_FatigueWasLoaded = loaded;
	}

	private void PushFatigueEffect(float _fatigue, in ArmFatigueProfile _profile)
	{
		ArmFatigueEffects effects = ArmFatigueMath.Evaluate(_fatigue, in _profile);
		m_LastFatigueEffectPayload = ArmFatigueLog.FormatEffect(in effects);
		ArmFatigueLog.EmitEffect(m_LogActor, m_LastFatigueEffectPayload);
	}

	private void MaybeLogHold()
	{
		if (m_Context.DecayPhase != ReadinessDecayPhase.Hold)
			return;
		if (m_HoldLogged && m_HoldLoggedState == m_Context.CurrentState)
			return;

		m_HoldLogged = true;
		m_HoldLoggedState = m_Context.CurrentState;
		m_LastDecayHoldPayload = ReadinessLog.FormatDecayHold(
			m_Context.CurrentState,
			m_Context.CalmDownRemaining);
		AppendLine(m_LastDecayHoldPayload);
		ReadinessLog.EmitDecay(m_LogActor, m_LastDecayHoldPayload);
	}

	private void PushEvent(string _payload)
	{
		m_LastEventPayload = _payload ?? string.Empty;
		AppendLine(m_LastEventPayload);
		ReadinessLog.EmitEvent(m_LogActor, m_LastEventPayload);
	}

	private void PushChannelTransition(
		ReadinessState _from,
		ReadinessState _to,
		ReadinessChangeReason _reason,
		float _duration,
		float _profileDuration,
		float _rankModifier)
	{
		m_LastTransitionPayload = ReadinessLog.FormatChannelTransition(
			_from,
			_to,
			_reason,
			m_Profile.Rank,
			_duration,
			_profileDuration,
			_rankModifier);
		ReadinessLog.EmitTransition(m_LogActor, m_LastTransitionPayload);
		EmitCombatChain();
	}

	private void PushDecay(
		ReadinessState _from,
		ReadinessState _to,
		ReadinessChangeReason _reason,
		float _duration,
		float _profileDuration,
		float _rankModifier)
	{
		m_LastDecayPayload = ReadinessLog.FormatChannelTransition(
			_from,
			_to,
			_reason,
			m_Profile.Rank,
			_duration,
			_profileDuration,
			_rankModifier);
		ReadinessLog.EmitDecay(m_LogActor, m_LastDecayPayload);
		EmitCombatChain();
	}

	private void EmitCombatChain()
	{
		ArmFatigueEffects effects = FatigueEffects;
		m_LastFatigueValuePayload = ArmFatigueLog.FormatValue(effects.Fatigue);
		ArmFatigueLog.Emit(m_LogActor, m_LastFatigueValuePayload);
		m_LastReadinessEffectPayload = ArmFatigueLog.FormatEffect(in effects);
		ArmFatigueLog.EmitReadinessEffect(m_LogActor, m_LastReadinessEffectPayload);
	}

	private void PushLog(string _payload)
	{
		m_LastLogPayload = _payload ?? string.Empty;
		AppendLine(m_LastLogPayload);
		ReadinessLog.Emit(m_LogActor, m_LastLogPayload);
	}

	private void AppendLine(string _payload)
	{
		if (m_LogLines.Count >= c_MaxLogLines)
			m_LogLines.RemoveAt(0);
		m_LogLines.Add(_payload ?? string.Empty);
	}
	#endregion
}

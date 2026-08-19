using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Per-observer detection / identity / memory layer (G1–G7). Vision Freeze / AI Handoff.
/// Produces <see cref="PerceivedContact"/>; does not issue orders, search, or fire.
/// Contact is promoted when DetectionProgress > 0, or created from sound/shared (G7).
/// G5: TargetSelector reads Contacts via <see cref="IPerceivedContactRegistry"/>.
/// Identity uses ObservableAffiliation cues / IdentityAppearance — never UnitTeam.
/// G4 decays LastSeenConfidence only; G7 decays SoundConfidence / SharedConfidence separately.
/// Baseline numbers: <see cref="VisionFreezeBaseline"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10)]
[RequireComponent(typeof(UnitPerception))]
public sealed class DetectionProcessor : MonoBehaviour, IPerceivedContactRegistry
{
	#region Constants
	private const float c_TimeComparisonEpsilonSeconds = 0.0001f;
	#endregion

	#region Nested
	private sealed class PendingTrack
	{
		public float Progress;
		public DetectionEvaluation Evaluation;
		public VisionObservation LastObservation;
		public Vector3 LastSeenPosition;
		public float LastSeenTime;
		public bool HasEvidenceThisFrame;
	}
	#endregion

	#region Serialized
	[Header("Acquire / Lose")]
	[SerializeField, Min(0.05f)] private float m_AcquireTimeSeconds = 0.35f;
	[SerializeField, Min(0.1f)] private float m_LossTimeSeconds = 2.5f;
	[SerializeField, Range(0f, 1f)] private float m_AcquireThreshold = 0.25f;
	[SerializeField, Range(0f, 1f)] private float m_LoseThreshold = 0.20f;

	[Header("G2 Observation lifecycle")]
	[SerializeField, Min(0.1f)] private float m_RecentlyLostDurationSeconds = 5f;
	[Tooltip("If true, remove contact only when Undetected AND Lost. Prefer false to keep LastSeen for G4.")]
	[SerializeField] private bool m_RemoveContactWhenUndetectedAndLost;

	[Header("G3 Identity")]
	[SerializeField, Min(0.1f)] private float m_IdentifyTimeSeconds = 4f;

	[Header("G4 Memory")]
	[SerializeField, Min(0.1f)] private float m_MemoryHorizonSeconds = 30f;
	[SerializeField, Range(0.01f, 1f)] private float m_MemoryStaleThreshold = 0.25f;
	[SerializeField, Min(0.01f)] private float m_MemoryShapeExponent = 1.5f;

	[Header("G7 Sound / Shared")]
	[SerializeField, Min(0.1f)] private float m_SoundHorizonSeconds = 3f;
	[SerializeField, Min(0.01f)] private float m_SoundShapeExponent = 1.5f;
	[SerializeField, Min(0.1f)] private float m_SharedHorizonSeconds = 8f;
	[SerializeField, Min(0.01f)] private float m_SharedShapeExponent = 1.5f;

	[Header("Distance factor")]
	[SerializeField, Min(1f)] private float m_DistanceNearMeters = 20f;
	[SerializeField, Min(1f)] private float m_DistanceFarMeters = 500f;
	[SerializeField, Range(0f, 1f)] private float m_DistanceFarFactor = 0.08f;

	[Header("FOV factor")]
	[SerializeField, Min(1f)] private float m_FovHalfReferenceDegrees = 60f;
	[SerializeField, Range(0f, 1f)] private float m_FovEdgeFactor = 0.15f;

	[Header("Movement factor (>=1 bonus only)")]
	[SerializeField, Min(0f)] private float m_WalkSpeedThreshold = 0.6f;
	[SerializeField, Min(0f)] private float m_RunSpeedThreshold = 3.2f;
	[SerializeField, Min(1f)] private float m_WalkMovementMultiplier = 1.15f;
	[SerializeField, Min(1f)] private float m_RunMovementMultiplier = 1.35f;
	[SerializeField, Min(1f)] private float m_MovementMultiplierCap = 1.5f;

	[Header("Debug")]
	[SerializeField] private bool m_DrawDebugHud = true;
	[SerializeField] private bool m_LogFactorBreakdown;
	#endregion

	#region Private Fields
	private UnitPerception m_Perception;
	private readonly Dictionary<Transform, PerceivedContact> m_Contacts = new Dictionary<Transform, PerceivedContact>(16);
	private readonly Dictionary<Transform, PendingTrack> m_Pending = new Dictionary<Transform, PendingTrack>(8);
	private readonly Dictionary<Transform, Vector3> m_LastKnownPositions = new Dictionary<Transform, Vector3>(16);
	private readonly Dictionary<Transform, float> m_LastPositionSampleTime = new Dictionary<Transform, float>(16);
	private readonly Dictionary<Transform, float> m_LostSinceTime = new Dictionary<Transform, float>(16);
	private readonly Dictionary<Transform, ObservableAffiliation> m_AffiliationCues =
		new Dictionary<Transform, ObservableAffiliation>(8);
	private readonly List<Transform> m_ScratchKeys = new List<Transform>(16);
	private readonly HashSet<Transform> m_ObservedThisScan = new HashSet<Transform>();

	private PerceivedContact m_DebugFocus;
	private float m_SimulatedTime = -1f;
	private bool m_HasSimulatedClock;
	private VisionScanStats m_ScanStats;
	#endregion

	#region Public Properties
	public IReadOnlyDictionary<Transform, PerceivedContact> Contacts => m_Contacts;
	public event Action ContactsChanged;
	public float AcquireTimeSeconds => m_AcquireTimeSeconds;
	public float LossTimeSeconds => m_LossTimeSeconds;
	public float AcquireThreshold => m_AcquireThreshold;
	public float LoseThreshold => m_LoseThreshold;
	public float FovHalfReferenceDegrees => m_FovHalfReferenceDegrees;
	public float FovEdgeFactor => m_FovEdgeFactor;
	public float RecentlyLostDurationSeconds => m_RecentlyLostDurationSeconds;
	public float IdentifyTimeSeconds => m_IdentifyTimeSeconds;
	public float MemoryHorizonSeconds => m_MemoryHorizonSeconds;
	public float MemoryStaleThreshold => m_MemoryStaleThreshold;
	public float MemoryShapeExponent => m_MemoryShapeExponent;
	public float SoundHorizonSeconds => m_SoundHorizonSeconds;
	public float SharedHorizonSeconds => m_SharedHorizonSeconds;

	public bool HasRecentlyLostContact
	{
		get
		{
			foreach (KeyValuePair<Transform, PerceivedContact> pair in m_Contacts)
			{
				if (pair.Value != null && pair.Value.ObservationState == ObservationState.RecentlyLost)
					return true;
			}

			return false;
		}
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Perception = GetComponent<UnitPerception>();
	}

	private void OnEnable()
	{
		if (m_Perception == null)
			m_Perception = GetComponent<UnitPerception>();
		if (m_Perception != null)
		{
			m_Perception.PerceptionFrameApplied -= OnPerceptionFrameApplied;
			m_Perception.SoundEventsApplied -= OnSoundEventsApplied;
			m_Perception.SharedEventsApplied -= OnSharedEventsApplied;
			m_Perception.PerceptionFrameApplied += OnPerceptionFrameApplied;
			m_Perception.SoundEventsApplied += OnSoundEventsApplied;
			m_Perception.SharedEventsApplied += OnSharedEventsApplied;
		}
	}

	private void OnDisable()
	{
		if (m_Perception != null)
		{
			m_Perception.PerceptionFrameApplied -= OnPerceptionFrameApplied;
			m_Perception.SoundEventsApplied -= OnSoundEventsApplied;
			m_Perception.SharedEventsApplied -= OnSharedEventsApplied;
		}
	}

	private void Update()
	{
		if (m_HasSimulatedClock)
			return;
		Tick(Time.deltaTime, Time.time);
	}

	private void OnGUI()
	{
		if (!m_DrawDebugHud || m_DebugFocus == null)
			return;

		PerceivedContact c = m_DebugFocus;
		DetectionEvaluation e = c.CurrentEvaluation;
		string targetName = c.Target != null ? c.Target.name : "(null)";
		float age = Mathf.Max(0f, NowTime - c.LastSeenTime);

		GUI.Box(new Rect(12f, 12f, 420f, 400f), "Detection G1–G7");
		GUI.Label(new Rect(24f, 36f, 390f, 350f),
			$"Target: {targetName}\n" +
			$"Detection: P={c.DetectionProgress:F2}  {c.State}\n" +
			$"Observation: {c.ObservationState}\n" +
			$"Identity: {c.Identity}  C={c.IdentityConfidence:F2}\n" +
			$"Rel={c.Relationship}  Threat={c.Threat}\n" +
			$"Memory: conf={c.LastSeenConfidence:F2} stale={c.IsMemoryStale(m_MemoryStaleThreshold)}\n" +
			$"Sound: conf={c.SoundConfidence:F2}  Shared: conf={c.SharedConfidence:F2}\n" +
			$"Knowledge={c.HasKnowledge} mixed={c.EvidenceIsMixed}\n" +
			$"LastKnown={c.LastKnownPosition}\n" +
			$"Q={e.VisibilityQuality:F3}  D={e.DistanceFactor:F2} F={e.FovFactor:F2} E={e.ExposureFactor:F2} M={e.MovementFactor:F2}\n" +
			$"LastSeen: {age:F2}s ago  pos={c.LastSeenPosition}\n" +
			$"Contact: alive");
	}
	#endregion

	#region Public Methods
	public bool TryGetContact(Transform _target, out PerceivedContact _contact)
	{
		_contact = null;
		if (_target == null)
			return false;
		return m_Contacts.TryGetValue(_target, out _contact);
	}

	public void ClearContacts()
	{
		m_Contacts.Clear();
		m_Pending.Clear();
		m_LastKnownPositions.Clear();
		m_LastPositionSampleTime.Clear();
		m_LostSinceTime.Clear();
		m_ObservedThisScan.Clear();
		m_DebugFocus = null;
		ContactsChanged?.Invoke();
	}

	public void SetSimulatedTime(float _timeSeconds)
	{
		m_HasSimulatedClock = true;
		m_SimulatedTime = _timeSeconds;
	}

	public void ClearSimulatedTime()
	{
		m_HasSimulatedClock = false;
		m_SimulatedTime = -1f;
	}

	public void ApplyMemoryCalibrationBaseline()
	{
		m_RecentlyLostDurationSeconds = MemoryDecayMath.DefaultRecentlyLostSeconds;
		m_MemoryHorizonSeconds = MemoryDecayMath.DefaultHorizonSeconds;
		m_MemoryShapeExponent = MemoryDecayMath.DefaultShapeExponent;
		m_MemoryStaleThreshold = MemoryDecayMath.DefaultStaleThreshold;
	}

	public void ApplyIdentityCalibrationBaseline()
	{
		m_IdentifyTimeSeconds = IdentityKnowledgeMath.DefaultIdentifyTimeSeconds;
	}

	public void Advance(float _dt, float _nowTime)
	{
		if (m_HasSimulatedClock)
			m_SimulatedTime = _nowTime;
		Tick(_dt, _nowTime);
	}

	public bool TryGetLostSinceTime(Transform _target, out float _lostSince)
	{
		if (_target != null && m_LostSinceTime.TryGetValue(_target, out _lostSince))
			return true;
		_lostSince = 0f;
		return false;
	}

	public void SetAffiliationCue(Transform _target, ObservableAffiliation _affiliation)
	{
		if (_target == null)
			return;
		m_AffiliationCues[_target] = _affiliation;
	}

	public void ClearAffiliationCue(Transform _target)
	{
		if (_target == null)
			return;
		m_AffiliationCues.Remove(_target);
	}

	public void ApplySyntheticObservation(
		Transform _target,
		float _distanceMeters,
		float _fovOffsetDegrees,
		float _exposure01,
		Vector3 _worldPosition)
	{
		if (m_Perception == null || _target == null)
			return;

		float dist = Mathf.Max(0.01f, _distanceMeters);
		VisionObservation obs = new VisionObservation
		{
			Target = _target,
			Position = _worldPosition,
			AimPoint = _worldPosition + Vector3.up * 1.2f,
			HasAimPoint = true,
			DistanceSq = dist * dist,
			IsVisible = true,
			FovOffsetDegrees = _fovOffsetDegrees,
			Exposure01 = Mathf.Clamp01(_exposure01)
		};

		m_Perception.ApplyVisionFrame(new[] { obs });
	}

	public void ApplyEmptyObservationFrame()
	{
		if (m_Perception == null)
			return;
		m_Perception.ApplyVisionFrame(System.Array.Empty<VisionObservation>());
	}

	public void ApplySyntheticSound(
		Transform _source,
		Vector3 _worldPosition,
		float _confidence,
		SoundEventType _type = SoundEventType.Gunshot)
	{
		if (m_Perception == null || _source == null)
			return;

		float now = NowTime;
		SoundObservation obs = new SoundObservation
		{
			Source = _source,
			Position = _worldPosition,
			Direction = Vector3.zero,
			Loudness = 1f,
			Type = _type,
			Time = now,
			SourceConfidence = Mathf.Clamp01(_confidence)
		};
		m_Perception.ApplySoundEvents(new[] { obs });
	}

	public void ApplySyntheticShared(
		Transform _subject,
		Vector3 _worldPosition,
		float _confidence,
		Transform _sourceUnit = null)
	{
		if (m_Perception == null || _subject == null)
			return;

		float now = NowTime;
		SharedObservation obs = new SharedObservation
		{
			Subject = _subject,
			SourceUnit = _sourceUnit,
			Position = _worldPosition,
			Time = now,
			SourceConfidence = Mathf.Clamp01(_confidence),
			InformationType = SharedInformationType.ContactReport,
			FreshnessSeconds = m_SharedHorizonSeconds
		};
		m_Perception.ApplySharedEvents(new[] { obs });
	}
	#endregion

	#region Private Methods
	private float NowTime => m_HasSimulatedClock ? m_SimulatedTime : Time.time;

	private void Tick(float _dt, float _nowTime)
	{
		float dt = Mathf.Max(0f, _dt);
		float acquire = Mathf.Max(m_LoseThreshold, m_AcquireThreshold);
		float lose = Mathf.Min(m_LoseThreshold, m_AcquireThreshold);

		if (dt > 0f)
			PromotePending(dt, _nowTime, acquire, lose);
		TickContacts(dt, _nowTime, acquire, lose);
		RefreshDebugFocus();
		ContactsChanged?.Invoke();
	}

	private void PromotePending(float _dt, float _nowTime, float _acquire, float _lose)
	{
		m_ScratchKeys.Clear();
		foreach (KeyValuePair<Transform, PendingTrack> pair in m_Pending)
			m_ScratchKeys.Add(pair.Key);

		for (int i = 0; i < m_ScratchKeys.Count; i++)
		{
			Transform key = m_ScratchKeys[i];
			if (key == null)
			{
				m_Pending.Remove(key);
				continue;
			}

			PendingTrack pending = m_Pending[key];
			float q = pending.HasEvidenceThisFrame ? pending.Evaluation.VisibilityQuality : 0f;
			pending.Progress = DetectionQualityMath.IntegrateProgress(
				pending.Progress, q, _dt, m_AcquireTimeSeconds, m_LossTimeSeconds, _acquire, _lose);

			if (pending.Progress <= 0f)
			{
				if (!pending.HasEvidenceThisFrame)
					m_Pending.Remove(key);
				continue;
			}

			PerceivedContact contact;
			if (!m_Contacts.TryGetValue(key, out contact) || contact == null)
				contact = new PerceivedContact { Target = key };

			contact.Target = key;
			contact.DetectionProgress = pending.Progress;
			contact.State = DetectionQualityMath.ResolveState(pending.Progress);
			contact.CurrentEvaluation = pending.HasEvidenceThisFrame
				? pending.Evaluation
				: DetectionEvaluation.ClearedIdleMovement();
			contact.LastObservation = pending.LastObservation;
			contact.LastSeenPosition = pending.LastSeenPosition;
			contact.LastSeenTime = pending.LastSeenTime;
			contact.LastKnownPosition = pending.LastSeenPosition;
			contact.LastSeenConfidence = pending.HasEvidenceThisFrame ? 1f : 0f;
			contact.ObservationState = pending.HasEvidenceThisFrame
				? ObservationState.Observed
				: ObservationState.RecentlyLost;
			if (!pending.HasEvidenceThisFrame)
				m_LostSinceTime[key] = _nowTime;

			m_Contacts[key] = contact;
			m_Pending.Remove(key);
			ResolveScanStats()?.NotifyContactCreated();
		}
	}

	private void TickContacts(float _dt, float _nowTime, float _acquire, float _lose)
	{
		m_ScratchKeys.Clear();
		foreach (KeyValuePair<Transform, PerceivedContact> pair in m_Contacts)
			m_ScratchKeys.Add(pair.Key);

		for (int i = 0; i < m_ScratchKeys.Count; i++)
		{
			Transform key = m_ScratchKeys[i];
			if (key == null)
			{
				RemoveContact(key);
				continue;
			}

			PerceivedContact contact = m_Contacts[key];
			contact.DetectionProgress = DetectionQualityMath.IntegrateProgress(
				contact.DetectionProgress,
				contact.CurrentEvaluation.VisibilityQuality,
				_dt,
				m_AcquireTimeSeconds,
				m_LossTimeSeconds,
				_acquire,
				_lose);
			contact.State = DetectionQualityMath.ResolveState(contact.DetectionProgress);

			if (contact.ObservationState == ObservationState.RecentlyLost)
			{
				if (!m_LostSinceTime.TryGetValue(key, out float lostSince))
				{
					lostSince = _nowTime;
					m_LostSinceTime[key] = lostSince;
				}

				if (_nowTime + c_TimeComparisonEpsilonSeconds >=
				    lostSince + m_RecentlyLostDurationSeconds)
					contact.ObservationState = ObservationState.Lost;
			}

			if (m_RemoveContactWhenUndetectedAndLost &&
			    contact.State == DetectionState.Undetected &&
			    contact.ObservationState == ObservationState.Lost)
			{
				RemoveContact(key);
				continue;
			}

			TickIdentity(contact, key, _dt);
			TickMemory(contact, _nowTime);
			TickSound(contact, _nowTime);
			TickShared(contact, _nowTime);
			m_Contacts[key] = contact;
		}
	}

	private void OnPerceptionFrameApplied()
	{
		if (m_Perception == null)
			return;

		float now = NowTime;
		m_ObservedThisScan.Clear();

		// Clear evidence flags on pending; contacts cleared via missing-scan pass.
		foreach (KeyValuePair<Transform, PendingTrack> pair in m_Pending)
			pair.Value.HasEvidenceThisFrame = false;

		IReadOnlyList<VisionObservation> observations = m_Perception.Observations;
		for (int i = 0; i < observations.Count; i++)
		{
			VisionObservation obs = observations[i];
			if (obs.Target == null || !obs.IsVisible)
				continue;

			DetectionEvaluation eval = BuildEvaluation(obs);
			m_ObservedThisScan.Add(obs.Target);

			if (m_Contacts.TryGetValue(obs.Target, out PerceivedContact contact) && contact != null)
			{
				contact.CurrentEvaluation = eval;
				contact.LastObservation = obs;
				contact.Target = obs.Target;
				contact.ObservationState = ObservationState.Observed;
				contact.LastSeenTime = now;
				contact.LastSeenPosition = obs.Position;
				contact.LastKnownPosition = obs.Position;
				contact.LastSeenConfidence = 1f;
				m_LostSinceTime.Remove(obs.Target);
				m_Contacts[obs.Target] = contact;
				ResolveScanStats()?.NotifyContactUpdated();
			}
			else
			{
				if (!m_Pending.TryGetValue(obs.Target, out PendingTrack pending) || pending == null)
				{
					pending = new PendingTrack();
					m_Pending[obs.Target] = pending;
				}

				pending.HasEvidenceThisFrame = true;
				pending.Evaluation = eval;
				pending.LastObservation = obs;
				pending.LastSeenPosition = obs.Position;
				pending.LastSeenTime = now;
			}

			if (m_LogFactorBreakdown)
			{
				Debug.Log(
					$"[DetectionProcessor] {obs.Target.name} Q={eval.VisibilityQuality:F3}",
					this);
			}
		}

		// Missing from scan: freeze LastSeen, clear Q, enter RecentlyLost if was Observed.
		m_ScratchKeys.Clear();
		foreach (KeyValuePair<Transform, PerceivedContact> pair in m_Contacts)
			m_ScratchKeys.Add(pair.Key);

		for (int i = 0; i < m_ScratchKeys.Count; i++)
		{
			Transform key = m_ScratchKeys[i];
			if (key == null || m_ObservedThisScan.Contains(key))
				continue;

			PerceivedContact contact = m_Contacts[key];
			contact.CurrentEvaluation = DetectionEvaluation.ClearedIdleMovement();
			if (contact.ObservationState == ObservationState.Observed)
			{
				contact.ObservationState = ObservationState.RecentlyLost;
				m_LostSinceTime[key] = now;
			}

			m_Contacts[key] = contact;
		}
	}

	private void OnSoundEventsApplied()
	{
		if (m_Perception == null)
			return;

		float now = NowTime;
		IReadOnlyList<SoundObservation> events = m_Perception.SoundEvents;
		for (int i = 0; i < events.Count; i++)
		{
			SoundObservation obs = events[i];
			if (obs.Source == null)
				continue;

			float confidence = Mathf.Clamp01(obs.SourceConfidence);
			if (confidence <= 0f)
				continue;

			PerceivedContact contact = EnsureContact(obs.Source);
			contact.SoundConfidenceInitial = confidence;
			contact.SoundConfidence = confidence;
			contact.SoundTime = obs.Time > 0f ? obs.Time : now;
			contact.SoundPosition = obs.Position;
			if (contact.ObservationState != ObservationState.Observed)
				contact.LastKnownPosition = obs.Position;
			m_Contacts[obs.Source] = contact;
		}

		ContactsChanged?.Invoke();
	}

	private void OnSharedEventsApplied()
	{
		if (m_Perception == null)
			return;

		float now = NowTime;
		IReadOnlyList<SharedObservation> events = m_Perception.SharedEvents;
		for (int i = 0; i < events.Count; i++)
		{
			SharedObservation obs = events[i];
			if (obs.Subject == null)
				continue;

			float confidence = Mathf.Clamp01(obs.SourceConfidence);
			if (confidence <= 0f)
				continue;

			PerceivedContact contact = EnsureContact(obs.Subject);
			contact.SharedConfidenceInitial = confidence;
			contact.SharedConfidence = confidence;
			contact.SharedTime = obs.Time > 0f ? obs.Time : now;
			contact.SharedPosition = obs.Position;
			if (contact.ObservationState != ObservationState.Observed)
				contact.LastKnownPosition = obs.Position;
			m_Contacts[obs.Subject] = contact;
		}

		ContactsChanged?.Invoke();
	}

	private PerceivedContact EnsureContact(Transform _key)
	{
		if (m_Contacts.TryGetValue(_key, out PerceivedContact existing) && existing != null)
		{
			existing.Target = _key;
			return existing;
		}

		PerceivedContact contact = new PerceivedContact
		{
			Target = _key,
			DetectionProgress = 0f,
			State = DetectionState.Undetected,
			ObservationState = ObservationState.NotObserved,
			LastSeenConfidence = 0f
		};
		m_Contacts[_key] = contact;
		return contact;
	}

	private void TickIdentity(PerceivedContact _contact, Transform _target, float _dt)
	{
		if (_contact == null || _target == null)
			return;

		bool observed = _contact.ObservationState == ObservationState.Observed;
		ObservableAffiliation cue = ResolveAffiliationCue(_target);
		IdentityKnowledgeMath.ApplyToContact(
			_contact, observed, cue, _dt, m_IdentifyTimeSeconds);
	}

	private void TickMemory(PerceivedContact _contact, float _nowTime)
	{
		if (_contact == null)
			return;

		if (_contact.ObservationState == ObservationState.Observed)
		{
			_contact.LastSeenConfidence = 1f;
			_contact.LastKnownPosition = _contact.LastSeenPosition;
			return;
		}

		if (_contact.ObservationState == ObservationState.NotObserved)
		{
			_contact.LastSeenConfidence = 0f;
			return;
		}

		float elapsed = Mathf.Max(0f, _nowTime - _contact.LastSeenTime);
		_contact.LastSeenConfidence = MemoryDecayMath.Evaluate(
			elapsed, 1f, m_MemoryHorizonSeconds, m_MemoryShapeExponent);
		if (_contact.LastKnownPosition == Vector3.zero)
			_contact.LastKnownPosition = _contact.LastSeenPosition;
	}

	private void TickSound(PerceivedContact _contact, float _nowTime)
	{
		if (_contact == null)
			return;
		if (_contact.SoundConfidenceInitial <= 0f && _contact.SoundConfidence <= 0f)
			return;

		float elapsed = Mathf.Max(0f, _nowTime - _contact.SoundTime);
		_contact.SoundConfidence = SoundKnowledgeMath.Evaluate(
			elapsed,
			_contact.SoundConfidenceInitial,
			m_SoundHorizonSeconds,
			m_SoundShapeExponent);
		if (_contact.SoundConfidence <= 0f)
		{
			_contact.SoundConfidence = 0f;
			_contact.SoundConfidenceInitial = 0f;
		}
	}

	private void TickShared(PerceivedContact _contact, float _nowTime)
	{
		if (_contact == null)
			return;
		if (_contact.SharedConfidenceInitial <= 0f && _contact.SharedConfidence <= 0f)
			return;

		float elapsed = Mathf.Max(0f, _nowTime - _contact.SharedTime);
		_contact.SharedConfidence = SharedKnowledgeMath.Evaluate(
			elapsed,
			_contact.SharedConfidenceInitial,
			m_SharedHorizonSeconds,
			m_SharedShapeExponent);
		if (_contact.SharedConfidence <= 0f)
		{
			_contact.SharedConfidence = 0f;
			_contact.SharedConfidenceInitial = 0f;
		}
	}

	private ObservableAffiliation ResolveAffiliationCue(Transform _target)
	{
		if (_target == null)
			return ObservableAffiliation.Unknown;

		if (m_AffiliationCues.TryGetValue(_target, out ObservableAffiliation cue))
			return cue;

		if (_target.TryGetComponent(out IdentityAppearance appearance) && appearance != null)
			return appearance.Affiliation;

		return ObservableAffiliation.Unknown;
	}

	private void RemoveContact(Transform _key)
	{
		m_Contacts.Remove(_key);
		m_Pending.Remove(_key);
		m_LastKnownPositions.Remove(_key);
		m_LastPositionSampleTime.Remove(_key);
		m_LostSinceTime.Remove(_key);
	}

	private DetectionEvaluation BuildEvaluation(in VisionObservation _obs)
	{
		float distance = Mathf.Sqrt(Mathf.Max(0f, _obs.DistanceSq));
		float distanceFactor = DetectionQualityMath.DistanceFactor(
			distance, m_DistanceNearMeters, m_DistanceFarMeters, m_DistanceFarFactor);
		float fovFactor = DetectionQualityMath.FovFactor(
			_obs.FovOffsetDegrees, m_FovHalfReferenceDegrees, m_FovEdgeFactor);
		float exposureFactor = Mathf.Clamp01(_obs.Exposure01);
		float movementFactor = EvaluateMovementFactor(_obs.Target, _obs.Position);
		float q = DetectionQualityMath.VisibilityQuality(
			distanceFactor, fovFactor, exposureFactor, movementFactor);

		return new DetectionEvaluation
		{
			DistanceFactor = distanceFactor,
			FovFactor = fovFactor,
			ExposureFactor = exposureFactor,
			MovementFactor = movementFactor,
			VisibilityQuality = q
		};
	}

	private float EvaluateMovementFactor(Transform _target, Vector3 _position)
	{
		float speed = EstimateSpeed(_target, _position);
		return DetectionQualityMath.MovementFactor(
			speed,
			m_WalkSpeedThreshold,
			m_RunSpeedThreshold,
			m_WalkMovementMultiplier,
			m_RunMovementMultiplier,
			m_MovementMultiplierCap);
	}

	private float EstimateSpeed(Transform _target, Vector3 _position)
	{
		if (_target != null && _target.TryGetComponent(out NavMeshAgent agent) && agent.enabled)
		{
			Vector3 v = agent.velocity;
			v.y = 0f;
			m_LastKnownPositions[_target] = _position;
			m_LastPositionSampleTime[_target] = NowTime;
			return v.magnitude;
		}

		float now = NowTime;
		if (m_LastKnownPositions.TryGetValue(_target, out Vector3 lastPos) &&
		    m_LastPositionSampleTime.TryGetValue(_target, out float lastTime))
		{
			float dt = Mathf.Max(now - lastTime, 0.0001f);
			float dist = Vector3.Distance(
				new Vector3(lastPos.x, 0f, lastPos.z),
				new Vector3(_position.x, 0f, _position.z));
			m_LastKnownPositions[_target] = _position;
			m_LastPositionSampleTime[_target] = now;
			return dist / dt;
		}

		m_LastKnownPositions[_target] = _position;
		m_LastPositionSampleTime[_target] = now;
		return 0f;
	}

	private VisionScanStats ResolveScanStats()
	{
		if (m_ScanStats != null)
			return m_ScanStats;

		UnitVision vision = m_Perception != null
			? m_Perception.GetComponent<UnitVision>()
			: GetComponent<UnitVision>();
		if (vision != null)
			m_ScanStats = vision.ScanStats;
		return m_ScanStats;
	}

	private void RefreshDebugFocus()
	{
		m_DebugFocus = null;
		float bestProgress = -1f;
		foreach (KeyValuePair<Transform, PerceivedContact> pair in m_Contacts)
		{
			if (pair.Key == null || pair.Value == null)
				continue;
			if (pair.Value.DetectionProgress >= bestProgress)
			{
				bestProgress = pair.Value.DetectionProgress;
				m_DebugFocus = pair.Value;
			}
		}
	}
	#endregion
}

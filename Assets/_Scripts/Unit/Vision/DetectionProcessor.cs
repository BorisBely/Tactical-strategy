using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Per-observer detection / identity / memory layer (G1–G7). Vision Freeze / AI Handoff.
/// Produces <see cref="PerceivedContact"/>; does not issue orders, search, or fire.
/// Contact is promoted when DetectionProgress > 0, or created from sound/shared (G7).
/// G5: TargetSelector reads Contacts via <see cref="IPerceivedContactRegistry"/>.
/// Identity uses per-observer cues or VisualIdentityEvidence mapped by observer side — never target UnitTeam.
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

	private struct ContactLogFingerprint : System.IEquatable<ContactLogFingerprint>
	{
		public ObservationState Observation;
		public DetectionState Detection;
		public PerceivedIdentity Identity;
		public ThreatLevel Threat;
		public bool HasSound;
		public bool HasShared;

		public static ContactLogFingerprint From(PerceivedContact _contact)
		{
			return new ContactLogFingerprint
			{
				Observation = _contact.ObservationState,
				Detection = _contact.State,
				Identity = _contact.Identity,
				Threat = _contact.Threat,
				HasSound = _contact.SoundConfidence > 0f,
				HasShared = _contact.SharedConfidence > 0f
			};
		}

		public bool Equals(ContactLogFingerprint _other)
		{
			return Observation == _other.Observation &&
			       Detection == _other.Detection &&
			       Identity == _other.Identity &&
			       Threat == _other.Threat &&
			       HasSound == _other.HasSound &&
			       HasShared == _other.HasShared;
		}
	}

	private struct SoundLogFingerprint : System.IEquatable<SoundLogFingerprint>
	{
		public SoundEventType Type;
		public Vector3 Position;
		public float Confidence;

		public static SoundLogFingerprint From(PerceivedContact _contact)
		{
			return new SoundLogFingerprint
			{
				Type = _contact.SoundType,
				Position = _contact.SoundPosition,
				Confidence = _contact.SoundConfidence
			};
		}

		public bool Equals(SoundLogFingerprint _other)
		{
			return Type == _other.Type &&
			       (Position - _other.Position).sqrMagnitude < 0.01f &&
			       Mathf.Abs(Confidence - _other.Confidence) < 0.02f;
		}
	}

	private struct SharedLogFingerprint : System.IEquatable<SharedLogFingerprint>
	{
		public PerceivedIdentity Identity;
		public Vector3 Position;
		public float Confidence;

		public static SharedLogFingerprint From(PerceivedContact _contact)
		{
			return new SharedLogFingerprint
			{
				Identity = _contact.SharedIdentity,
				Position = _contact.SharedPosition,
				Confidence = _contact.SharedConfidence
			};
		}

		public bool Equals(SharedLogFingerprint _other)
		{
			return Identity == _other.Identity &&
			       (_other.Position - Position).sqrMagnitude < 0.01f &&
			       Mathf.Abs(Confidence - _other.Confidence) < 0.02f;
		}
	}

	private struct AllyReportThrottle
	{
		public float Time;
		public Vector3 Position;
		public PerceivedIdentity Identity;
	}
	#endregion

	#region Serialized
	[Header("Acquire / Lose")]
	[SerializeField, Min(0.05f)] private float m_AcquireTimeSeconds = 0.35f;
	[SerializeField, Min(0.1f)] private float m_LossTimeSeconds = 2.5f;
	[SerializeField, Range(0f, 1f)] private float m_AcquireThreshold = 0.25f;
	[SerializeField, Range(0f, 1f)] private float m_LoseThreshold = 0.20f;
	[Tooltip("1 = legacy linear Q. Production 3.8 slows low-Q accumulation without changing Q.")]
	[SerializeField, Min(1f)] private float m_AcquisitionExponent = 3.8f;

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
	[SerializeField, Min(1f)] private float m_DistanceFarMeters = 150f;
	// Prefab leftovers: DistanceFactor is t = d / ResolvedMaxRange, not near/far SmoothStep.
#pragma warning disable CS0414
	[SerializeField, Min(1f)] private float m_DistanceNearMeters = 20f;
	[SerializeField, Range(0f, 1f)] private float m_DistanceFarFactor = 0.30f;
#pragma warning restore CS0414

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
	[SerializeField] private bool m_DrawDebugHud;
	[SerializeField] private bool m_LogFactorBreakdown;
	#endregion

	#region Private Fields
	private UnitPerception m_Perception;
	private UnitVision m_Vision;
	private UnitTeam m_ObserverTeam;
	private readonly Dictionary<Transform, PerceivedContact> m_Contacts = new Dictionary<Transform, PerceivedContact>(16);
	private readonly Dictionary<Transform, PendingTrack> m_Pending = new Dictionary<Transform, PendingTrack>(8);
	private readonly Dictionary<Transform, Vector3> m_LastKnownPositions = new Dictionary<Transform, Vector3>(16);
	private readonly Dictionary<Transform, float> m_LastPositionSampleTime = new Dictionary<Transform, float>(16);
	private readonly Dictionary<Transform, float> m_LostSinceTime = new Dictionary<Transform, float>(16);
	private readonly Dictionary<Transform, ObservableAffiliation> m_AffiliationCues =
		new Dictionary<Transform, ObservableAffiliation>(8);
	private readonly List<Transform> m_ScratchKeys = new List<Transform>(16);
	private readonly HashSet<Transform> m_ObservedThisScan = new HashSet<Transform>();
	private readonly Dictionary<EntityId, ContactLogFingerprint> m_LoggedContacts =
		new Dictionary<EntityId, ContactLogFingerprint>(16);
	private readonly Dictionary<EntityId, SoundLogFingerprint> m_LoggedSounds =
		new Dictionary<EntityId, SoundLogFingerprint>(16);
	private readonly Dictionary<EntityId, SharedLogFingerprint> m_LoggedShared =
		new Dictionary<EntityId, SharedLogFingerprint>(16);
	private readonly Dictionary<Transform, AllyReportThrottle> m_AllyReportThrottle =
		new Dictionary<Transform, AllyReportThrottle>(8);

	private PerceivedContact m_DebugFocus;
	private float m_SimulatedTime = -1f;
	private bool m_HasSimulatedClock;
	private bool m_PerceptionEventsBound;
	private VisionScanStats m_ScanStats;
	private bool m_ContactsDirty;
	private bool m_HasRecentlyLostContact;
	#endregion

	#region Public Properties
	public IReadOnlyDictionary<Transform, PerceivedContact> Contacts => m_Contacts;
	public event Action ContactsChanged;
	public float AcquireTimeSeconds => m_AcquireTimeSeconds;
	public float LossTimeSeconds => m_LossTimeSeconds;
	public float AcquireThreshold => m_AcquireThreshold;
	public float LoseThreshold => m_LoseThreshold;
	public float AcquisitionExponent => m_AcquisitionExponent;
	public float FovHalfReferenceDegrees => m_FovHalfReferenceDegrees;
	public float FovEdgeFactor => m_FovEdgeFactor;
	public float RecentlyLostDurationSeconds => m_RecentlyLostDurationSeconds;
	public float IdentifyTimeSeconds => m_IdentifyTimeSeconds;
	public float MemoryHorizonSeconds => m_MemoryHorizonSeconds;
	public float MemoryStaleThreshold => m_MemoryStaleThreshold;
	public float MemoryShapeExponent => m_MemoryShapeExponent;
	public float SoundHorizonSeconds => m_SoundHorizonSeconds;
	public float PerceptionClock => NowTime;
	public float SharedHorizonSeconds => m_SharedHorizonSeconds;

	public bool HasRecentlyLostContact => m_HasRecentlyLostContact;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsurePerceptionBound();
		TryGetComponent(out m_ObserverTeam);
		TryGetComponent(out m_Vision);
	}

	private void OnEnable()
	{
		EnsurePerceptionBound();
		WorldSoundHub.Register(this);
		WorldAllyReportHub.Register(this);
	}

	private void OnDisable()
	{
		WorldSoundHub.Unregister(this);
		WorldAllyReportHub.Unregister(this);
		UnbindPerceptionEvents();
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
		m_LoggedContacts.Clear();
		m_LoggedSounds.Clear();
		m_DebugFocus = null;
		m_HasRecentlyLostContact = false;
		m_ContactsDirty = false;
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
		EnsurePerceptionBound();
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
		EnsurePerceptionBound();
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
		EnsurePerceptionBound();
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

	public void ReceiveWorldSound(in WorldSoundEvent _evt, float _confidence)
	{
		if (!isActiveAndEnabled)
			return;
		if (_evt.Source == null)
			return;
		if (IsOwnSoundSource(_evt.Source))
			return;
		ApplySyntheticSound(_evt.Source, _evt.Position, _confidence, _evt.Type);
	}

	public void ReceiveWorldAllyReport(in WorldAllyReportEvent _evt, float _confidence)
	{
		if (!isActiveAndEnabled)
			return;
		if (_evt.Subject == null || _evt.Reporter == null)
			return;
		if (IsOwnAllyReporter(_evt.Reporter))
			return;
		ApplySyntheticShared(
			_evt.Subject,
			_evt.Position,
			_confidence,
			_evt.Reporter,
			_evt.ReportedIdentity);
	}

	public void ApplySyntheticShared(
		Transform _subject,
		Vector3 _worldPosition,
		float _confidence,
		Transform _sourceUnit = null,
		PerceivedIdentity _reportedIdentity = PerceivedIdentity.Unknown)
	{
		EnsurePerceptionBound();
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
			FreshnessSeconds = m_SharedHorizonSeconds,
			ReportedIdentity = _reportedIdentity
		};
		m_Perception.ApplySharedEvents(new[] { obs });
	}
	#endregion

	#region Private Methods
	private float NowTime => m_HasSimulatedClock ? m_SimulatedTime : Time.time;

	private void EnsurePerceptionBound()
	{
		if (m_Perception == null)
			TryGetComponent(out m_Perception);
		if (m_Perception == null || m_PerceptionEventsBound)
			return;

		m_Perception.PerceptionFrameApplied -= OnPerceptionFrameApplied;
		m_Perception.SoundEventsApplied -= OnSoundEventsApplied;
		m_Perception.SharedEventsApplied -= OnSharedEventsApplied;
		m_Perception.PerceptionFrameApplied += OnPerceptionFrameApplied;
		m_Perception.SoundEventsApplied += OnSoundEventsApplied;
		m_Perception.SharedEventsApplied += OnSharedEventsApplied;
		m_PerceptionEventsBound = true;
	}

	private void UnbindPerceptionEvents()
	{
		if (m_Perception != null)
		{
			m_Perception.PerceptionFrameApplied -= OnPerceptionFrameApplied;
			m_Perception.SoundEventsApplied -= OnSoundEventsApplied;
			m_Perception.SharedEventsApplied -= OnSharedEventsApplied;
		}

		m_PerceptionEventsBound = false;
	}

	private void Tick(float _dt, float _nowTime)
	{
		using (InfantryProfilerMarkers.DetectionTick.Auto())
		{
			float dt = Mathf.Max(0f, _dt);
			float acquire = Mathf.Max(m_LoseThreshold, m_AcquireThreshold);
			float lose = Mathf.Min(m_LoseThreshold, m_AcquireThreshold);

			if (dt > 0f)
				PromotePending(dt, _nowTime, acquire, lose);
			TickContacts(dt, _nowTime, acquire, lose);
			TryPublishAllyReports(_nowTime);
			RefreshRecentlyLostCache();
			RefreshDebugFocus();
			RaiseContactsChangedIfDirty();
		}
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
			float attention = ResolveAttentionMultiplier(pending.LastObservation);
			pending.Progress = DetectionQualityMath.IntegrateProgress(
				pending.Progress, q, _dt, m_AcquireTimeSeconds, m_LossTimeSeconds, _acquire, _lose,
				m_AcquisitionExponent, attention);

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
			if (pending.HasEvidenceThisFrame)
				contact.ObservationState = ObservationState.Observed;
			else
			{
				contact.ObservationState = ResolveUnobservedState(key);
				if (contact.ObservationState == ObservationState.RecentlyLost)
					m_LostSinceTime[key] = _nowTime;
			}

			m_Contacts[key] = contact;
			m_Pending.Remove(key);
			MarkContactsChanged();
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
			ObservationState prevObservation = contact.ObservationState;
			DetectionState prevState = contact.State;
			PerceivedIdentity prevIdentity = contact.Identity;
			PerceivedRelationship prevRelationship = contact.Relationship;
			ThreatLevel prevThreat = contact.Threat;
			bool hadKnowledge = contact.HasKnowledge;

			float attention = ResolveAttentionMultiplier(contact.LastObservation);
			contact.DetectionProgress = DetectionQualityMath.IntegrateProgress(
				contact.DetectionProgress,
				contact.CurrentEvaluation.VisibilityQuality,
				_dt,
				m_AcquireTimeSeconds,
				m_LossTimeSeconds,
				_acquire,
				_lose,
				m_AcquisitionExponent,
				attention);
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

			if (contact.ObservationState == ObservationState.RecentlyLost &&
			    !TargetEngageability.IsEngageable(key))
				contact.ObservationState = ObservationState.Lost;

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
			if (prevObservation != contact.ObservationState ||
			    prevState != contact.State ||
			    prevIdentity != contact.Identity ||
			    prevRelationship != contact.Relationship ||
			    prevThreat != contact.Threat ||
			    hadKnowledge != contact.HasKnowledge)
			{
				MarkContactsChanged();
			}

			LogContactIfChanged(contact, false);
		}
	}

	private void OnPerceptionFrameApplied()
	{
		if (m_Perception == null)
			return;

		float now = NowTime;
		m_ObservedThisScan.Clear();
		bool changed = false;

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
			changed = true;

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
				contact.ObservationState = ResolveUnobservedState(key);
				if (contact.ObservationState == ObservationState.RecentlyLost)
					m_LostSinceTime[key] = now;
				changed = true;
			}

			m_Contacts[key] = contact;
		}

		if (changed)
			MarkContactsChanged();
		RefreshRecentlyLostCache();
	}

	private void OnSoundEventsApplied()
	{
		if (m_Perception == null)
			return;

		float now = NowTime;
		IReadOnlyList<SoundObservation> events = m_Perception.SoundEvents;
		bool changed = false;
		for (int i = 0; i < events.Count; i++)
		{
			SoundObservation obs = events[i];
			if (obs.Source == null)
				continue;

			float confidence = Mathf.Clamp01(obs.SourceConfidence);
			if (confidence <= 0f)
				continue;

			PerceivedContact contact = EnsureContact(obs.Source);
			bool hadSound = contact.HasUsefulSound;
			contact.SoundConfidenceInitial = confidence;
			contact.SoundConfidence = confidence;
			contact.SoundTime = obs.Time > 0f ? obs.Time : now;
			contact.SoundPosition = obs.Position;
			contact.SoundType = obs.Type;
			m_Contacts[obs.Source] = contact;
			LogSoundIfChanged(contact, hadSound, false);
			changed = true;
		}

		if (!changed)
			return;

		RefreshRecentlyLostCache();
		m_ContactsDirty = false;
		ContactsChanged?.Invoke();
	}

	private void OnSharedEventsApplied()
	{
		if (m_Perception == null)
			return;

		float now = NowTime;
		IReadOnlyList<SharedObservation> events = m_Perception.SharedEvents;
		bool changed = false;
		for (int i = 0; i < events.Count; i++)
		{
			SharedObservation obs = events[i];
			if (obs.Subject == null)
				continue;

			float confidence = Mathf.Clamp01(obs.SourceConfidence);
			if (confidence <= 0f)
				continue;

			PerceivedContact contact = EnsureContact(obs.Subject);
			bool hadShared = contact.HasUsefulShared;
			contact.SharedConfidenceInitial = confidence;
			contact.SharedConfidence = confidence;
			contact.SharedTime = obs.Time > 0f ? obs.Time : now;
			contact.SharedPosition = obs.Position;
			contact.SharedIdentity = obs.ReportedIdentity;
			contact.SharedReporter = obs.SourceUnit;
			m_Contacts[obs.Subject] = contact;
			LogSharedIfChanged(contact, hadShared, false);
			changed = true;
		}

		if (!changed)
			return;

		RefreshRecentlyLostCache();
		m_ContactsDirty = false;
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

		bool hadSound = _contact.SoundConfidence > 0f;
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
			if (hadSound)
				LogSoundIfChanged(_contact, true, true);
		}
	}

	private void TickShared(PerceivedContact _contact, float _nowTime)
	{
		if (_contact == null)
			return;
		if (_contact.SharedConfidenceInitial <= 0f && _contact.SharedConfidence <= 0f)
			return;

		bool hadShared = _contact.SharedConfidence > 0f;
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
			_contact.SharedIdentity = PerceivedIdentity.Unknown;
			if (hadShared)
				LogSharedIfChanged(_contact, true, true);
		}
	}

	private ObservableAffiliation ResolveAffiliationCue(Transform _target)
	{
		if (_target == null)
			return ObservableAffiliation.Unknown;

		if (m_AffiliationCues.TryGetValue(_target, out ObservableAffiliation cue))
			return cue;

		if (!_target.TryGetComponent(out VisualIdentityEvidence evidence) || evidence == null)
			return ObservableAffiliation.Unknown;

		if (m_ObserverTeam == null)
			TryGetComponent(out m_ObserverTeam);

		UnitTeamId observerSide = m_ObserverTeam != null ? m_ObserverTeam.Team : UnitTeamId.Neutral;
		return VisualAffiliationMapping.ToCue(evidence.PrimaryAffiliation, observerSide);
	}

	private void RemoveContact(Transform _key)
	{
		if (_key != null && UnitActionLog.Enabled)
		{
			UnitActionLog.Write(this, UnitActionLog.Vision, "tgt=" + UnitActionLog.Slot(_key) + " gone=1");
			m_LoggedContacts.Remove(_key.GetEntityId());
		}

		m_Contacts.Remove(_key);
		m_Pending.Remove(_key);
		m_LastKnownPositions.Remove(_key);
		m_LastPositionSampleTime.Remove(_key);
		m_LostSinceTime.Remove(_key);
		if (_key != null)
		{
			m_LoggedSounds.Remove(_key.GetEntityId());
			m_LoggedShared.Remove(_key.GetEntityId());
			m_AllyReportThrottle.Remove(_key);
		}
		MarkContactsChanged();
	}

	private bool IsOwnSoundSource(Transform _source)
	{
		if (_source == null)
			return false;
		if (_source == transform)
			return true;
		return _source.IsChildOf(transform);
	}

	private void LogSoundIfChanged(PerceivedContact _contact, bool _hadSound, bool _expired)
	{
		if (!UnitActionLog.Enabled || _contact == null || _contact.Target == null)
			return;

		EntityId id = _contact.Target.GetEntityId();
		SoundLogFingerprint next = SoundLogFingerprint.From(_contact);
		if (_expired)
		{
			m_LoggedSounds.Remove(id);
			UnitActionLog.Write(
				this,
				UnitActionLog.Sound,
				"expired type=" + _contact.SoundType +
				" pos=" + UnitActionLog.Vec(_contact.SoundPosition) +
				" tgt=" + UnitActionLog.Slot(_contact.Target));
			return;
		}

		bool created = !m_LoggedSounds.TryGetValue(id, out SoundLogFingerprint prev);
		if (!created && prev.Equals(next))
			return;

		m_LoggedSounds[id] = next;
		string verb = !_hadSound || created ? "received" : "updated";
		UnitActionLog.Write(
			this,
			UnitActionLog.Sound,
			verb + " type=" + _contact.SoundType +
			" pos=" + UnitActionLog.Vec(_contact.SoundPosition) +
			" conf=" + UnitActionLog.F2(_contact.SoundConfidence) +
			" tgt=" + UnitActionLog.Slot(_contact.Target));
	}

	private void TryPublishAllyReports(float _nowTime)
	{
		if (!isActiveAndEnabled)
			return;
		if (m_ObserverTeam == null)
			TryGetComponent(out m_ObserverTeam);
		if (m_ObserverTeam == null || m_ObserverTeam.Team == UnitTeamId.Neutral)
			return;

		foreach (KeyValuePair<Transform, PerceivedContact> pair in m_Contacts)
		{
			PerceivedContact contact = pair.Value;
			if (contact == null || pair.Key == null)
				continue;
			if (contact.ObservationState != ObservationState.Observed)
				continue;
			if (contact.Identity == PerceivedIdentity.Friendly)
				continue;
			if (pair.Key == transform || pair.Key.IsChildOf(transform))
				continue;

			Vector3 position = contact.LastSeenPosition.sqrMagnitude > 0.0001f
				? contact.LastSeenPosition
				: pair.Key.position;
			PerceivedIdentity identity = contact.Identity;
			bool hasPrev = m_AllyReportThrottle.TryGetValue(pair.Key, out AllyReportThrottle prev);
			if (!AllyReportEvidenceMath.ShouldPublish(
				    hasPrev,
				    _nowTime,
				    prev.Time,
				    prev.Position,
				    prev.Identity,
				    position,
				    identity))
				continue;

			m_AllyReportThrottle[pair.Key] = new AllyReportThrottle
			{
				Time = _nowTime,
				Position = position,
				Identity = identity
			};
			WorldAllyReportHub.Publish(AllyReportEvidenceMath.Create(
				transform,
				pair.Key,
				position,
				identity,
				1f));
		}
	}

	private bool IsOwnAllyReporter(Transform _reporter)
	{
		if (_reporter == null)
			return false;
		if (_reporter == transform)
			return true;
		return _reporter.IsChildOf(transform);
	}

	private void LogSharedIfChanged(PerceivedContact _contact, bool _hadShared, bool _expired)
	{
		if (!UnitActionLog.Enabled || _contact == null || _contact.Target == null)
			return;

		EntityId id = _contact.Target.GetEntityId();
		SharedLogFingerprint next = SharedLogFingerprint.From(_contact);
		if (_expired)
		{
			m_LoggedShared.Remove(id);
			UnitActionLog.Write(
				this,
				UnitActionLog.Shared,
				"expired reporter=" + UnitActionLog.Slot(_contact.SharedReporter) +
				" pos=" + UnitActionLog.Vec(_contact.SharedPosition) +
				" tgt=" + UnitActionLog.Slot(_contact.Target));
			return;
		}

		bool created = !m_LoggedShared.TryGetValue(id, out SharedLogFingerprint prev);
		if (!created && prev.Equals(next))
			return;

		m_LoggedShared[id] = next;
		string verb = !_hadShared || created ? "received" : "updated";
		UnitActionLog.Write(
			this,
			UnitActionLog.Shared,
			verb + " reporter=" + UnitActionLog.Slot(_contact.SharedReporter) +
			" pos=" + UnitActionLog.Vec(_contact.SharedPosition) +
			" conf=" + UnitActionLog.F2(_contact.SharedConfidence) +
			" identity=" + _contact.SharedIdentity +
			" tgt=" + UnitActionLog.Slot(_contact.Target));
	}

	private void LogContactIfChanged(PerceivedContact _contact, bool _force)
	{
		if (!UnitActionLog.Enabled || _contact == null || _contact.Target == null)
			return;

		EntityId id = _contact.Target.GetEntityId();
		ContactLogFingerprint next = ContactLogFingerprint.From(_contact);
		bool created = !m_LoggedContacts.TryGetValue(id, out ContactLogFingerprint prev);
		if (!created && !_force && prev.Equals(next))
			return;

		m_LoggedContacts[id] = next;
		string line = UnitActionLog.ContactLine(_contact);
		if (created)
			line += " new=1";
		else if (prev.Identity != next.Identity)
			line += " idWas=" + prev.Identity;
		if (!created && prev.Observation != next.Observation)
			line += " obsWas=" + prev.Observation;
		if (m_Vision != null)
		{
			line += " source=" + m_Vision.CurrentVisionSource;
			line += " resolvedRange=" + m_Vision.ResolvedMaxRange.ToString("0");
		}
		UnitActionLog.Write(this, UnitActionLog.Vision, line);

		if (created || (next.Observation == ObservationState.Observed && prev.Observation != ObservationState.Observed))
		{
			UnitActionLog.Timeline(
				UnitActionLog.Vision,
				"observer=" + UnitActionLog.Slot(this) + " " + line);
		}
	}

	private DetectionEvaluation BuildEvaluation(in VisionObservation _obs)
	{
		float distance = Mathf.Sqrt(Mathf.Max(0f, _obs.DistanceSq));
		if (m_Vision == null)
			TryGetComponent(out m_Vision);

		float resolvedMax = m_DistanceFarMeters;
		if (m_Vision != null)
			resolvedMax = m_Vision.ResolvedMaxRange;

		float distanceFactor = DetectionQualityMath.DistanceFactor(distance, resolvedMax);

		float fovHalf = m_FovHalfReferenceDegrees;
		if (m_Vision != null)
		{
			ResolvedVisionProfile profile = m_Vision.CurrentVisionProfile;
			fovHalf = _obs.Source == VisionObservationSource.Optic
				? profile.ScopeHalfFovDegrees
				: profile.EyeHalfFovDegrees;
		}

		float fovFactor = DetectionQualityMath.FovFactor(
			_obs.FovOffsetDegrees, fovHalf, m_FovEdgeFactor);
		float exposureFactor = Mathf.Clamp01(_obs.Exposure01);
		float movementFactor = EvaluateMovementFactor(_obs.Target, _obs.Position);
		float q = DetectionQualityMath.VisibilityQuality(
			distanceFactor, fovFactor, exposureFactor, movementFactor);

		ResolveScanStats()?.AddQualityEval();

		return new DetectionEvaluation
		{
			DistanceFactor = distanceFactor,
			FovFactor = fovFactor,
			ExposureFactor = exposureFactor,
			MovementFactor = movementFactor,
			VisibilityQuality = q
		};
	}

	private float ResolveAttentionMultiplier(in VisionObservation _lastObservation)
	{
		return AttentionMath.EvaluateMultiplier(ResolveAttentionAngleDegrees(_lastObservation));
	}

	private float ResolveAttentionAngleDegrees(in VisionObservation _lastObservation)
	{
		bool hasPoint = _lastObservation.HasAimPoint ||
			_lastObservation.IsVisible ||
			_lastObservation.DistanceSq > 0.01f;
		if (!hasPoint)
			return AttentionMath.NeutralDegrees;

		if (m_Vision == null)
			TryGetComponent(out m_Vision);

		Vector3 point = _lastObservation.HasAimPoint
			? _lastObservation.AimPoint
			: _lastObservation.Position;
		if (m_Vision != null)
		{
			Vector3 to = point - m_Vision.GetGameplayVisionOriginWorld();
			to.y = 0f;
			if (to.sqrMagnitude > 1e-6f)
			{
				return Mathf.Abs(VisionGeometry.HorizontalAngleDegrees(
					m_Vision.GetGameplayVisionForwardXZ(),
					to));
			}
		}

		return Mathf.Abs(_lastObservation.FovOffsetDegrees);
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

	private void MarkContactsChanged()
	{
		m_ContactsDirty = true;
	}

	private void RaiseContactsChangedIfDirty()
	{
		if (!m_ContactsDirty)
			return;

		m_ContactsDirty = false;
		ContactsChanged?.Invoke();
	}

	/// <summary>
	/// Live LOS loss is RecentlyLost. A dead / untargetable body is not a trackable miss.
	/// </summary>
	private static ObservationState ResolveUnobservedState(Transform _target)
	{
		return TargetEngageability.IsEngageable(_target)
			? ObservationState.RecentlyLost
			: ObservationState.Lost;
	}

	private void RefreshRecentlyLostCache()
	{
		m_HasRecentlyLostContact = false;
		foreach (KeyValuePair<Transform, PerceivedContact> pair in m_Contacts)
		{
			if (pair.Value != null && pair.Value.ObservationState == ObservationState.RecentlyLost)
			{
				m_HasRecentlyLostContact = true;
				return;
			}
		}
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

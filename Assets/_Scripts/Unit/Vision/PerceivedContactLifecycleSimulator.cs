using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Deterministic contact lifecycle for EditMode tests (controlled dt / clock).
/// Mirrors DetectionProcessor: progress&gt;0 creates contact; Lost kept; LastSeen frozen on loss;
/// G3 identity from per-simulator affiliation cues (never UnitTeam);
/// G4 memory decay of LastSeenConfidence.
/// </summary>
public sealed class PerceivedContactLifecycleSimulator
{
	private const float c_TimeComparisonEpsilonSeconds = 0.0001f;

	public float AcquireTimeSeconds { get; }
	public float LossTimeSeconds { get; }
	public float AcquireThreshold { get; }
	public float LoseThreshold { get; }
	public float RecentlyLostDurationSeconds { get; }
	public float IdentifyTimeSeconds { get; }
	public float MemoryHorizonSeconds { get; }
	public float MemoryStaleThreshold { get; }
	public float MemoryShapeExponent { get; }

	private sealed class PendingTrack
	{
		public float Progress;
		public DetectionEvaluation Evaluation;
		public VisionObservation LastObservation;
		public Vector3 LastSeenPosition;
		public float LastSeenTime;
		public bool HasEvidence;
	}

	private readonly Dictionary<Transform, PerceivedContact> m_Contacts =
		new Dictionary<Transform, PerceivedContact>(4);
	private readonly Dictionary<Transform, PendingTrack> m_Pending =
		new Dictionary<Transform, PendingTrack>(4);
	private readonly Dictionary<Transform, float> m_LostSince =
		new Dictionary<Transform, float>(4);
	private readonly Dictionary<Transform, ObservableAffiliation> m_AffiliationCues =
		new Dictionary<Transform, ObservableAffiliation>(4);

	public PerceivedContactLifecycleSimulator(
		float acquireTimeSeconds = -1f,
		float lossTimeSeconds = -1f,
		float acquireThreshold = -1f,
		float loseThreshold = -1f,
		float recentlyLostDurationSeconds = -1f,
		float identifyTimeSeconds = -1f,
		float memoryHorizonSeconds = -1f,
		float memoryStaleThreshold = -1f,
		float memoryShapeExponent = -1f)
	{
		AcquireTimeSeconds = acquireTimeSeconds > 0f
			? acquireTimeSeconds
			: DetectionQualityMath.DefaultAcquireTime;
		LossTimeSeconds = lossTimeSeconds > 0f
			? lossTimeSeconds
			: DetectionQualityMath.DefaultLossTime;
		AcquireThreshold = acquireThreshold >= 0f
			? acquireThreshold
			: DetectionQualityMath.DefaultAcquireThreshold;
		LoseThreshold = loseThreshold >= 0f
			? loseThreshold
			: DetectionQualityMath.DefaultLoseThreshold;
		RecentlyLostDurationSeconds = recentlyLostDurationSeconds > 0f
			? Mathf.Max(0.01f, recentlyLostDurationSeconds)
			: MemoryDecayMath.DefaultRecentlyLostSeconds;
		IdentifyTimeSeconds = identifyTimeSeconds > 0f
			? identifyTimeSeconds
			: IdentityKnowledgeMath.DefaultIdentifyTimeSeconds;
		MemoryHorizonSeconds = memoryHorizonSeconds > 0f
			? memoryHorizonSeconds
			: MemoryDecayMath.DefaultHorizonSeconds;
		MemoryStaleThreshold = memoryStaleThreshold >= 0f
			? memoryStaleThreshold
			: MemoryDecayMath.DefaultStaleThreshold;
		MemoryShapeExponent = memoryShapeExponent > 0f
			? memoryShapeExponent
			: MemoryDecayMath.DefaultShapeExponent;
	}

	public void Reset()
	{
		m_Contacts.Clear();
		m_Pending.Clear();
		m_LostSince.Clear();
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

	public bool TryGet(Transform _target, out PerceivedContact _contact)
	{
		_contact = null;
		if (_target == null)
			return false;
		return m_Contacts.TryGetValue(_target, out _contact);
	}

	public void ApplyEvidence(Transform _target, float _quality, Vector3 _position, float _nowTime)
	{
		if (_target == null)
			return;

		float q = Mathf.Clamp01(_quality);
		var eval = new DetectionEvaluation
		{
			VisibilityQuality = q,
			DistanceFactor = q,
			FovFactor = 1f,
			ExposureFactor = 1f,
			MovementFactor = 1f
		};
		var obs = new VisionObservation
		{
			Target = _target,
			Position = _position,
			AimPoint = _position + Vector3.up,
			HasAimPoint = true,
			DistanceSq = _position.sqrMagnitude,
			IsVisible = true,
			FovOffsetDegrees = 0f,
			Exposure01 = 1f
		};

		if (m_Contacts.TryGetValue(_target, out PerceivedContact contact) && contact != null)
		{
			contact.CurrentEvaluation = eval;
			contact.LastObservation = obs;
			contact.ObservationState = ObservationState.Observed;
			contact.LastSeenTime = _nowTime;
			contact.LastSeenPosition = _position;
			contact.LastKnownPosition = _position;
			contact.LastSeenConfidence = 1f;
			contact.Target = _target;
			m_LostSince.Remove(_target);
			return;
		}

		if (!m_Pending.TryGetValue(_target, out PendingTrack pending) || pending == null)
		{
			pending = new PendingTrack();
			m_Pending[_target] = pending;
		}

		pending.HasEvidence = true;
		pending.Evaluation = eval;
		pending.LastObservation = obs;
		pending.LastSeenPosition = _position;
		pending.LastSeenTime = _nowTime;
	}

	public void SoftLose(Transform _target, float _nowTime)
	{
		if (_target == null)
			return;

		if (m_Contacts.TryGetValue(_target, out PerceivedContact contact) && contact != null)
		{
			contact.CurrentEvaluation = DetectionEvaluation.ClearedIdleMovement();
			if (contact.ObservationState == ObservationState.Observed)
			{
				contact.ObservationState = ObservationState.RecentlyLost;
				m_LostSince[_target] = _nowTime;
			}

			return;
		}

		if (m_Pending.TryGetValue(_target, out PendingTrack pending) && pending != null)
		{
			pending.HasEvidence = false;
			pending.Evaluation = DetectionEvaluation.ClearedIdleMovement();
		}
	}

	public void Advance(float _dt, float _nowTime)
	{
		if (_dt <= 0f)
			return;

		PromotePending(_dt, _nowTime);
		TickContacts(_dt, _nowTime);
	}

	private void PromotePending(float _dt, float _nowTime)
	{
		if (m_Pending.Count == 0)
			return;

		var keys = new List<Transform>(m_Pending.Keys);
		for (int i = 0; i < keys.Count; i++)
		{
			Transform key = keys[i];
			if (key == null || !m_Pending.TryGetValue(key, out PendingTrack pending) || pending == null)
			{
				m_Pending.Remove(key);
				continue;
			}

			float q = pending.HasEvidence ? pending.Evaluation.VisibilityQuality : 0f;
			pending.Progress = DetectionQualityMath.IntegrateProgress(
				pending.Progress,
				q,
				_dt,
				AcquireTimeSeconds,
				LossTimeSeconds,
				AcquireThreshold,
				LoseThreshold);

			if (pending.Progress <= 0f)
			{
				if (!pending.HasEvidence)
					m_Pending.Remove(key);
				continue;
			}

			var contact = new PerceivedContact
			{
				Target = key,
				DetectionProgress = pending.Progress,
				State = DetectionQualityMath.ResolveState(pending.Progress),
				CurrentEvaluation = pending.HasEvidence
					? pending.Evaluation
					: DetectionEvaluation.ClearedIdleMovement(),
				LastObservation = pending.LastObservation,
				LastSeenPosition = pending.LastSeenPosition,
				LastSeenTime = pending.LastSeenTime,
				LastKnownPosition = pending.LastSeenPosition,
				LastSeenConfidence = pending.HasEvidence ? 1f : 0f,
				ObservationState = pending.HasEvidence
					? ObservationState.Observed
					: ObservationState.RecentlyLost
			};
			if (!pending.HasEvidence)
				m_LostSince[key] = _nowTime;

			m_Contacts[key] = contact;
			m_Pending.Remove(key);
		}
	}

	private void TickContacts(float _dt, float _nowTime)
	{
		if (m_Contacts.Count == 0)
			return;

		var keys = new List<Transform>(m_Contacts.Keys);
		for (int i = 0; i < keys.Count; i++)
		{
			Transform key = keys[i];
			if (key == null || !m_Contacts.TryGetValue(key, out PerceivedContact contact) || contact == null)
			{
				m_Contacts.Remove(key);
				m_LostSince.Remove(key);
				continue;
			}

			contact.DetectionProgress = DetectionQualityMath.IntegrateProgress(
				contact.DetectionProgress,
				contact.CurrentEvaluation.VisibilityQuality,
				_dt,
				AcquireTimeSeconds,
				LossTimeSeconds,
				AcquireThreshold,
				LoseThreshold);
			contact.State = DetectionQualityMath.ResolveState(contact.DetectionProgress);

			if (contact.ObservationState == ObservationState.RecentlyLost &&
			    m_LostSince.TryGetValue(key, out float lostSince) &&
			    _nowTime + c_TimeComparisonEpsilonSeconds >=
			    lostSince + RecentlyLostDurationSeconds)
			{
				contact.ObservationState = ObservationState.Lost;
			}

			TickIdentity(contact, key, _dt);
			TickMemory(contact, _nowTime);
		}
	}

	private void TickIdentity(PerceivedContact _contact, Transform _target, float _dt)
	{
		if (_contact == null || _target == null)
			return;

		bool observed = _contact.ObservationState == ObservationState.Observed;
		ObservableAffiliation cue = ObservableAffiliation.Unknown;
		if (m_AffiliationCues.TryGetValue(_target, out ObservableAffiliation stored))
			cue = stored;

		IdentityKnowledgeMath.ApplyToContact(
			_contact, observed, cue, _dt, IdentifyTimeSeconds);
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

		float elapsed = Mathf.Max(0f, _nowTime - _contact.LastSeenTime);
		_contact.LastSeenConfidence = MemoryDecayMath.Evaluate(
			elapsed, 1f, MemoryHorizonSeconds, MemoryShapeExponent);
		if (_contact.LastKnownPosition == Vector3.zero)
			_contact.LastKnownPosition = _contact.LastSeenPosition;
	}
}

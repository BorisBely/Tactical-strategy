using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Chooses a combat target from observer-local <see cref="PerceivedContact"/> knowledge (G5).
/// Owns: eligibility/priority, hysteresis, ForcedPriority, LoF suppress, reload/malfunction retain, selected velocity.
/// Candidate list is <see cref="IPerceivedContactRegistry"/> — not <see cref="UnitPerception.Observations"/>.
/// LastKnown is never a fire aim point. Selected ≠ Engageable ≠ Fire. High Threat ≠ Fire.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(20)]
[RequireComponent(typeof(UnitPerception))]
[RequireComponent(typeof(UnitObservationSource))]
[RequireComponent(typeof(DetectionProcessor))]
public sealed class TargetSelector : MonoBehaviour
{
	#region Constants
	private const int c_RaycastHitBuffer = 16;
	private const int c_AimCandidateCapacity = 32;
	#endregion

	#region Private Fields
	[SerializeField] private UnitPerception m_Perception;
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitObservationSource m_ObservationSource;
	[SerializeField] private DetectionProcessor m_ContactRegistry;
	[SerializeField] private UnitVision m_Vision;

	[Header("G5 selection policy")]
	[SerializeField] private bool m_ExcludeFriendly = true;
	[SerializeField] private bool m_ExcludeNeutralIdentity = true;
	[SerializeField] private bool m_AllowUnknownIdentity = true;
	[SerializeField] private bool m_StaleEligible = true;
	[SerializeField, Range(0.01f, 1f)] private float m_MemoryStaleThreshold = 0.25f;
	[SerializeField, Min(0f)] private float m_ObservedBonus = 10f;
	[SerializeField, Min(0f)] private float m_ConfidenceWeight = 2f;
	[SerializeField, Min(0f)] private float m_ThreatWeight = 1f;
	[SerializeField, Min(0f)] private float m_DistanceWeight = 1f;
	[SerializeField, Min(0f)] private float m_StalePenalty = 3f;
	[SerializeField, Min(0f)] private float m_HostileBonus = 0.5f;
	[SerializeField, Min(0f)] private float m_SwitchThreshold = TargetSwitchMath.DefaultSwitchThreshold;
	[SerializeField, Min(0f)] private float m_WeaponSuitabilityWeight = TargetSelectionMath.DefaultWeaponSuitabilityWeight;
	[SerializeField, Min(0f)] private float m_MissionBonus = TargetSelectionMath.DefaultMissionBonus;

	[Header("Physics / retain LOS")]
	[SerializeField] private LayerMask m_LayerMask = ~0;
	[SerializeField] private QueryTriggerInteraction m_QueryTriggerInteraction = QueryTriggerInteraction.Ignore;

	[Tooltip("While reload / bolt / malfunction — keep current engage target without FOV (ResolvedMaxRange + LOS still required).")]
	[SerializeField] private bool m_RetainTargetDuringReloadOrMalfunction = true;

	[SerializeField, Range(0.05f, 1f)] private float m_LineOfFireSafetyRadius = 0.35f;
	[SerializeField, Range(0.05f, 1f)] private float m_LineOfFireBlockedRetrySeconds = 0.15f;
	[SerializeField] private bool m_LogLineOfFireSuppression;

	[SerializeField, Min(0f)] private float m_AimPointVelocitySmoothTime = 0.15f;
	[SerializeField, Min(0.01f)] private float m_AimPointMaxProjectionSeconds = 0.5f;

	private readonly Dictionary<Transform, float> m_LineOfFireSuppressedTargets = new Dictionary<Transform, float>();
	private readonly List<UnitBodyHitZoneVisionUtility.VisionAimCandidate> m_AimCandidateScratch =
		new List<UnitBodyHitZoneVisionUtility.VisionAimCandidate>(c_AimCandidateCapacity);
	private readonly List<(Vector3 from, Vector3 to, bool hitTarget)> m_DebugRays =
		new List<(Vector3, Vector3, bool)>(16);

	private readonly HashSet<Transform> m_LineOfFireSeenRoots = new HashSet<Transform>();
	private readonly List<Transform> m_ExpiredSuppressedKeys = new List<Transform>(8);

	private RaycastHit[] m_Hits;
	private VisibilityChecker m_VisibilityChecker;

	private Transform m_SelectedTarget;
	private bool m_HasSelectedAimPoint;
	private Vector3 m_SelectedAimPointWorld;
	private Transform m_ForcedPriorityTarget;

	private Transform m_VelocityTrackedTarget;
	private Vector3 m_PreviousAimPointForVelocity;
	private Vector3 m_TargetVelocityEstimate;
	private Vector3 m_LastVelocityRaw;
	private float m_LastAimPointUpdateTime;
	private Transform m_LastLoggedSelected;
	private float m_LastLoggedScore = float.MinValue;
	private TargetSwitchReason m_LastLoggedSwitchReason;
	private Transform m_LastLoggedRunnerUp;
	private readonly System.Text.StringBuilder m_SelectLogScratch = new System.Text.StringBuilder(256);
	private readonly List<ScoredCandidate> m_ScoredScratch = new List<ScoredCandidate>(16);
	private TargetSelectionSnapshot m_LastSelection;
	private Transform m_MissionTarget;
	private WeaponClassType m_WeaponClassOverride = WeaponClassType.Unknown;
	private float m_EffectiveRangeOverride;
	#endregion

	private struct ScoredCandidate
	{
		public Transform Target;
		public float Score;
		public bool HasAim;
		public Vector3 AimPoint;
	}

	#region Public Properties
	public Transform SelectedTarget => m_SelectedTarget;
	public bool HasSelectedAimPoint => m_HasSelectedAimPoint;
	public Vector3 SelectedAimPointWorld => m_SelectedAimPointWorld;

	public Transform ForcedPriorityTarget
	{
		get => m_ForcedPriorityTarget;
		set => m_ForcedPriorityTarget = value;
	}

	public Vector3 SelectedTargetVelocity
	{
		get
		{
			if (m_VelocityTrackedTarget == m_SelectedTarget && m_SelectedTarget != null)
				return m_TargetVelocityEstimate;
			return Vector3.zero;
		}
	}

	public float LastAimPointUpdateTime => m_LastAimPointUpdateTime;
	public Transform VelocityTrackedTarget => m_VelocityTrackedTarget;
	public TargetSelectionSnapshot LastSelection => m_LastSelection;

	/// <summary>Attack/Defense TargetEntity bonus. Not ForcedPriority. Not AI.EngageTarget.</summary>
	public Transform MissionTarget
	{
		get => m_MissionTarget;
		set => m_MissionTarget = value;
	}

	public float SwitchThreshold
	{
		get => m_SwitchThreshold;
		set => m_SwitchThreshold = Mathf.Max(0f, value);
	}

	public WeaponClassType WeaponClassOverride
	{
		get => m_WeaponClassOverride;
		set => m_WeaponClassOverride = value;
	}

	public float EffectiveRangeOverride
	{
		get => m_EffectiveRangeOverride;
		set => m_EffectiveRangeOverride = Mathf.Max(0f, value);
	}

	/// <summary>Reload/misfire retain envelope: current VisionSource ResolvedMaxRange. Not SELECT ranking.</summary>
	public float RetainRangeMeters => ResolveRetainRangeMeters();

	/// <summary>Selected target if it is currently engageable and has a LOS-confirmed aim point.</summary>
	public Transform GetEngageableSelectedTarget()
	{
		if (m_SelectedTarget == null || !m_HasSelectedAimPoint)
			return null;
		return TargetEngageability.IsEngageable(m_SelectedTarget) ? m_SelectedTarget : null;
	}

	/// <summary>World aim point for the engageable selected target. No LastKnown / collider fallback.</summary>
	public bool TryGetEngageableAimPointWorld(out Vector3 _aimPoint)
	{
		if (!m_HasSelectedAimPoint)
		{
			_aimPoint = Vector3.zero;
			return false;
		}

		_aimPoint = GetEngageableAimPointWorld();
		return true;
	}

	public Vector3 GetEngageableAimPointWorld()
	{
		Transform selected = GetEngageableSelectedTarget();
		if (selected == null || !m_HasSelectedAimPoint)
			return Vector3.zero;

		Vector3 basePoint = m_SelectedAimPointWorld;
		if (m_VelocityTrackedTarget == selected && m_TargetVelocityEstimate.sqrMagnitude > 0.0001f)
		{
			float dt = Mathf.Min(Time.time - m_LastAimPointUpdateTime, m_AimPointMaxProjectionSeconds);
			if (dt > 0.001f)
				basePoint += m_TargetVelocityEstimate * dt;
		}

		return basePoint;
	}

	public bool IsTrackingTarget(Transform _targetRoot)
	{
		if (_targetRoot == null || m_SelectedTarget == null)
			return false;

		return m_SelectedTarget == _targetRoot ||
		       m_SelectedTarget.IsChildOf(_targetRoot) ||
		       _targetRoot.IsChildOf(m_SelectedTarget);
	}

	public bool ShouldReacquireAimAfterSwitch(Transform _previousEngageable, Transform _nextEngageable)
	{
		if (_nextEngageable == null || _nextEngageable == _previousEngageable)
			return false;

		if (_previousEngageable == null)
			return true;

		return TargetEngageability.IsEngageable(_previousEngageable);
	}

	/// <summary>Clear selection without event if empty; invoke null event when had a target.</summary>
	public void ClearSelectionAndNotifyIfHadTarget()
	{
		bool had = m_SelectedTarget != null;
		ClearSelection(false);
		if (had)
			SelectedTargetChanged?.Invoke(null);
	}

	/// <summary>
	/// Закрепить цель для диагностики без кадра perception.
	/// </summary>
	public void SetSelectedTargetForDiagnostics(Transform _target, Vector3 _aimPointWorld)
	{
		bool changed = m_SelectedTarget != _target;
		m_SelectedTarget = _target;
		m_HasSelectedAimPoint = _target != null;
		m_SelectedAimPointWorld = _aimPointWorld;
		if (changed)
			SelectedTargetChanged?.Invoke(m_SelectedTarget);
	}

	/// <summary>
	/// EditMode / harness: LoF SphereCast must not depend on the currently open scene.
	/// Production combat keeps the serialized mask. 0 = no LoF hits.
	/// </summary>
	public void SetLineOfFireLayerMaskForDiagnostics(LayerMask _mask)
	{
		m_LayerMask = _mask;
	}
	#endregion

	#region Public Events
	public event Action<Transform> SelectedTargetChanged;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Hits = new RaycastHit[c_RaycastHitBuffer];
		if (m_Perception == null)
			m_Perception = GetComponent<UnitPerception>();
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_ObservationSource == null)
			m_ObservationSource = GetComponent<UnitObservationSource>() ?? gameObject.AddComponent<UnitObservationSource>();
		if (m_ContactRegistry == null)
			m_ContactRegistry = GetComponent<DetectionProcessor>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();

		m_VisibilityChecker = new VisibilityChecker(transform, m_Hits, m_AimCandidateScratch, m_DebugRays);
		RefreshVisibilityCheckerConfig();
	}

	private void OnEnable()
	{
		if (m_ContactRegistry == null)
			m_ContactRegistry = GetComponent<DetectionProcessor>();
		if (m_ContactRegistry != null)
			m_ContactRegistry.ContactsChanged += HandleContactsChanged;
	}

	private void OnDisable()
	{
		if (m_ContactRegistry != null)
			m_ContactRegistry.ContactsChanged -= HandleContactsChanged;
	}
	#endregion

	#region Public Methods
	/// <summary>Run selection from perceived contacts using ObservationSource origin.</summary>
	public void SelectFromPerception()
	{
		Vector3 origin = m_ObservationSource != null
			? m_ObservationSource.GetOriginWorld()
			: transform.position + Vector3.up * 1.6f;
		SelectFromContacts(origin);
	}

	/// <summary>Run selection against observer-local perceived contacts.</summary>
	public void SelectFromPerception(Vector3 _visionOrigin)
	{
		SelectFromContacts(_visionOrigin);
	}

	public void SelectFromContacts()
	{
		Vector3 origin = m_ObservationSource != null
			? m_ObservationSource.GetOriginWorld()
			: transform.position + Vector3.up * 1.6f;
		SelectFromContacts(origin);
	}

	public void SelectFromContacts(Vector3 _visionOrigin)
	{
		using (InfantryProfilerMarkers.TargetSelect.Auto())
		{
			SelectFromContactsUnguarded(_visionOrigin);
		}
	}

	private void EnsureBindings()
	{
		DetectionProcessor processor = m_ContactRegistry;
		if (processor == null || processor.gameObject != gameObject)
			TryGetComponent(out processor);

		if (m_ContactRegistry != processor)
		{
			if (m_ContactRegistry != null)
				m_ContactRegistry.ContactsChanged -= HandleContactsChanged;
			m_ContactRegistry = processor;
		}

		if (m_ContactRegistry != null)
		{
			m_ContactRegistry.ContactsChanged -= HandleContactsChanged;
			m_ContactRegistry.ContactsChanged += HandleContactsChanged;
		}

		if (m_ObservationSource == null)
			m_ObservationSource = GetComponent<UnitObservationSource>() ??
			                      gameObject.AddComponent<UnitObservationSource>();
		if (m_Perception == null)
			TryGetComponent(out m_Perception);
		if (m_Hits == null)
			m_Hits = new RaycastHit[c_RaycastHitBuffer];
		if (m_VisibilityChecker == null)
			m_VisibilityChecker = new VisibilityChecker(transform, m_Hits, m_AimCandidateScratch, m_DebugRays);
	}

	private void SelectFromContactsUnguarded(Vector3 _visionOrigin)
	{
		EnsureBindings();
		CleanupExpiredSuppressedTargets();
		RefreshVisibilityCheckerConfig();
		ContactSelectionPolicy policy = BuildPolicy();
		ResolveWeaponSuitability(out WeaponClassType weaponClass, out float effectiveRange);
		Transform missionTarget = ResolveMissionTarget();

		bool lostUnengageable = false;
		if (m_SelectedTarget != null && !TargetEngageability.IsEngageable(m_SelectedTarget))
		{
			m_SelectedTarget = null;
			m_HasSelectedAimPoint = false;
			m_SelectedAimPointWorld = Vector3.zero;
			lostUnengageable = true;
		}

		Transform previousSelected = m_SelectedTarget;
		m_SelectLogScratch.Length = 0;

		m_ScoredScratch.Clear();
		IPerceivedContactRegistry registry = m_ContactRegistry;
		if (registry != null)
		{
			foreach (KeyValuePair<Transform, PerceivedContact> pair in registry.Contacts)
			{
				PerceivedContact contact = pair.Value;
				if (contact == null || contact.Target == null)
					continue;
				if (IsLineOfFireSuppressed(contact.Target))
				{
					AppendReject(contact.Target, "LoFSuppressed");
					continue;
				}
				if (!TryRevalidateSuppressedTarget(contact.Target, _visionOrigin))
				{
					AppendReject(contact.Target, "LoFRevalidate");
					continue;
				}

				bool worldOk = TargetEngageability.IsEngageable(contact.Target);
				if (IsWorldNonHostile(contact.Target))
				{
					AppendReject(contact.Target, "WorldNonHostile");
					continue;
				}
				if (!ContactSelectionEligibility.Evaluate(contact, worldOk, policy, out ContactSelectionRejectReason reject))
				{
					AppendReject(contact.Target, reject.ToString());
					continue;
				}

				bool hasObservedAim = TargetSelectionMath.TryGetObservedAimPoint(contact, out Vector3 observedAim);
				m_ScoredScratch.Add(new ScoredCandidate
				{
					Target = contact.Target,
					Score = TargetSelectionMath.ScoreWithModifiers(
						contact,
						_visionOrigin,
						policy,
						weaponClass,
						effectiveRange,
						missionTarget),
					HasAim = hasObservedAim,
					AimPoint = observedAim
				});
			}
		}

		int bestIndex = -1;
		int currentIndex = -1;
		float bestScore = float.MinValue;
		float runnerUpScore = float.MinValue;
		Transform runnerUp = null;
		for (int i = 0; i < m_ScoredScratch.Count; i++)
		{
			ScoredCandidate scored = m_ScoredScratch[i];
			if (scored.Target == previousSelected)
				currentIndex = i;

			if (scored.Score > bestScore)
			{
				if (bestIndex >= 0)
				{
					runnerUp = m_ScoredScratch[bestIndex].Target;
					runnerUpScore = bestScore;
				}

				bestScore = scored.Score;
				bestIndex = i;
				continue;
			}

			if (scored.Score > runnerUpScore)
			{
				runnerUpScore = scored.Score;
				runnerUp = scored.Target;
			}
		}

		bool currentEligible = currentIndex >= 0;
		float currentScore = currentEligible ? m_ScoredScratch[currentIndex].Score : float.MinValue;
		Transform best = bestIndex >= 0 ? m_ScoredScratch[bestIndex].Target : null;
		float candidateScore = bestIndex >= 0 ? bestScore : float.MinValue;

		bool shouldSwitch = TargetSwitchMath.ShouldSwitch(
			previousSelected,
			currentEligible,
			currentScore,
			best,
			candidateScore,
			policy.SwitchThreshold,
			out TargetSwitchReason switchReason);
		if (lostUnengageable && best != null)
		{
			shouldSwitch = true;
			switchReason = TargetSwitchReason.LostCurrent;
		}

		int chosenIndex = shouldSwitch ? bestIndex : currentIndex;
		Transform newTarget = chosenIndex >= 0 ? m_ScoredScratch[chosenIndex].Target : null;
		bool hasAim = chosenIndex >= 0 && m_ScoredScratch[chosenIndex].HasAim;
		Vector3 aimPoint = hasAim ? m_ScoredScratch[chosenIndex].AimPoint : Vector3.zero;
		float selectedScore = chosenIndex >= 0 ? m_ScoredScratch[chosenIndex].Score : float.MinValue;

		if (shouldSwitch && currentEligible && previousSelected != newTarget)
		{
			runnerUp = previousSelected;
			runnerUpScore = currentScore;
		}
		else if (!shouldSwitch && switchReason == TargetSwitchReason.Hysteresis && best != newTarget)
		{
			runnerUp = best;
			runnerUpScore = bestScore;
		}

		if (newTarget != null && hasAim)
		{
			Vector3 fireOrigin = GetFireOriginForLofCheck(_visionOrigin);
			if (CheckAndSuppressBlockedTarget(ref newTarget, ref aimPoint, ref hasAim, fireOrigin))
			{
				newTarget = null;
				hasAim = false;
				aimPoint = Vector3.zero;
				selectedScore = float.MinValue;
			}
		}

		if (TryRetainEngageTargetDuringWeaponMaintenance(_visionOrigin, ref newTarget, ref aimPoint, ref hasAim))
			switchReason = TargetSwitchReason.WeaponMaintenanceRetain;

		if (TryApplyForcedPriority(_visionOrigin, ref newTarget, ref aimPoint, ref hasAim) &&
		    newTarget != previousSelected)
			switchReason = TargetSwitchReason.ForcedPriority;

		bool changed = newTarget != previousSelected;
		m_SelectedTarget = newTarget;
		m_HasSelectedAimPoint = newTarget != null && hasAim;
		m_SelectedAimPointWorld = m_HasSelectedAimPoint ? aimPoint : Vector3.zero;

		m_LastSelection = new TargetSelectionSnapshot
		{
			Selected = newTarget,
			RunnerUp = runnerUp,
			SelectedScore = newTarget != null ? selectedScore : 0f,
			RunnerUpScore = runnerUp != null ? runnerUpScore : 0f,
			CurrentScore = previousSelected != null && currentEligible ? currentScore : 0f,
			CandidateScore = best != null ? candidateScore : 0f,
			SwitchThreshold = policy.SwitchThreshold,
			Switched = changed,
			SwitchReason = switchReason,
			Engageable = newTarget != null && hasAim && TargetEngageability.IsEngageable(newTarget),
			HasAimPoint = hasAim,
			ScoredCount = m_ScoredScratch.Count,
			RegistryCount = registry != null ? registry.Contacts.Count : 0,
			RejectSummary = m_SelectLogScratch.ToString()
		};

		if (changed)
			SelectedTargetChanged?.Invoke(m_SelectedTarget);

		if (UnitActionLog.Enabled)
		{
			LogSelectionIfNeeded(
				changed,
				newTarget,
				selectedScore,
				hasAim,
				aimPoint,
				runnerUp,
				runnerUpScore,
				changed,
				switchReason,
				currentEligible ? currentScore : 0f,
				best != null ? candidateScore : 0f,
				policy.SwitchThreshold);
		}

		UpdateTargetVelocityEstimate(newTarget, aimPoint, hasAim);
	}

	public void ClearSelection(bool _invokeEvent)
	{
		bool had = m_SelectedTarget != null;
		m_SelectedTarget = null;
		m_HasSelectedAimPoint = false;
		m_SelectedAimPointWorld = Vector3.zero;
		m_VelocityTrackedTarget = null;
		m_TargetVelocityEstimate = Vector3.zero;
		m_LastVelocityRaw = Vector3.zero;
		m_PreviousAimPointForVelocity = Vector3.zero;

		if (_invokeEvent && had)
			SelectedTargetChanged?.Invoke(null);
	}

	public void SuppressCurrentTargetForLineOfFire(float _seconds)
	{
		Transform currentTarget = m_SelectedTarget;
		if (currentTarget == null)
			return;

		float expireTime = Time.time + Mathf.Max(0f, _seconds);
		m_LineOfFireSuppressedTargets[currentTarget] = expireTime;

		if (m_LogLineOfFireSuppression)
			Debug.Log($"[LoFSup] {name}: SUPPRESS '{currentTarget.name}' for {_seconds:F2}s (expire={expireTime:F2})", this);
		if (UnitActionLog.Enabled)
			UnitActionLog.Write(this, UnitActionLog.Select, "lofSuppress tgt=" + UnitActionLog.Slot(currentTarget) + " sec=" + UnitActionLog.F2(_seconds));

		ClearSelection(true);
	}

	public bool IsLineOfFireSuppressed(Transform _candidate)
	{
		if (_candidate == null)
			return false;

		if (m_LineOfFireSuppressedTargets.TryGetValue(_candidate, out float expireTime) && Time.time < expireTime)
			return true;

		return false;
	}

	/// <summary>Harness / interrupt reset. Production combat uses timed retry, not this.</summary>
	public void ClearLineOfFireSuppression()
	{
		m_LineOfFireSuppressedTargets.Clear();
	}
	#endregion

	#region Private Methods
	private void AppendReject(Transform _target, string _reason)
	{
		if (m_SelectLogScratch.Length > 180)
			return;
		if (m_SelectLogScratch.Length > 0)
			m_SelectLogScratch.Append(',');
		m_SelectLogScratch.Append(UnitActionLog.Slot(_target)).Append(':').Append(_reason);
	}

	private bool IsWorldNonHostile(Transform _target)
	{
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		UnitTeam other = UnitTeam.Resolve(_target);
		if (m_Team == null || other == null)
			return false;
		return !UnitTeam.AreHostile(m_Team.Team, other.Team);
	}

	private void LogSelectionIfNeeded(
		bool _changed,
		Transform _newTarget,
		float _selectedScore,
		bool _hasAim,
		Vector3 _aimPoint,
		Transform _runnerUp,
		float _runnerUpScore,
		bool _switched,
		TargetSwitchReason _switchReason,
		float _currentScore,
		float _candidateScore,
		float _switchThreshold)
	{
		bool hysteresisHold = _switchReason == TargetSwitchReason.Hysteresis && _runnerUp != null;
		bool hysteresisLog = hysteresisHold &&
		                     (_switchReason != m_LastLoggedSwitchReason || _runnerUp != m_LastLoggedRunnerUp);
		bool scoreShift = _newTarget != null &&
		                  _newTarget == m_LastLoggedSelected &&
		                  m_LastLoggedScore > float.MinValue / 4f &&
		                  Mathf.Abs(_selectedScore - m_LastLoggedScore) >= 1f;
		if (!_changed && !scoreShift && !hysteresisLog)
			return;

		m_LastLoggedSelected = _newTarget;
		m_LastLoggedScore = _selectedScore;
		m_LastLoggedSwitchReason = _switchReason;
		m_LastLoggedRunnerUp = _runnerUp;
		bool engageable = _newTarget != null && _hasAim && TargetEngageability.IsEngageable(_newTarget);
		string payload = "selected=" + (_newTarget != null ? UnitActionLog.Slot(_newTarget) : "none") +
		                 " score=" + (_newTarget != null ? UnitActionLog.F2(_selectedScore) : "-") +
		                 " engageable=" + (engageable ? "1" : "0") +
		                 " aim=" + (_hasAim ? "1" : "0") +
		                 " switch=" + (_switched ? "1" : "0") +
		                 " switchReason=" + _switchReason +
		                 " currentScore=" + UnitActionLog.F2(_currentScore) +
		                 " candidateScore=" + UnitActionLog.F2(_candidateScore) +
		                 " switchThreshold=" + UnitActionLog.F2(_switchThreshold);
		if (_changed)
			payload += " retainRange=" + UnitActionLog.F1(ResolveRetainRangeMeters());
		if (_hasAim)
			payload += " aimPt=" + UnitActionLog.Vec(_aimPoint);
		if (_runnerUp != null)
			payload += " runnerUp=" + UnitActionLog.Slot(_runnerUp) + ":" + UnitActionLog.F2(_runnerUpScore);
		if (m_SelectLogScratch.Length > 0)
			payload += " rejected=" + m_SelectLogScratch;
		UnitActionLog.Write(this, UnitActionLog.Select, payload);
		if (_changed || hysteresisLog)
			UnitActionLog.Timeline(UnitActionLog.Select, "actor=" + UnitActionLog.Slot(this) + " " + payload);
	}

	private void HandleContactsChanged()
	{
		SelectFromContacts();
	}

	private ContactSelectionPolicy BuildPolicy()
	{
		return new ContactSelectionPolicy
		{
			ExcludeFriendly = m_ExcludeFriendly,
			ExcludeNeutralIdentity = m_ExcludeNeutralIdentity,
			AllowUnknown = m_AllowUnknownIdentity,
			StaleEligible = m_StaleEligible,
			StaleThreshold = m_MemoryStaleThreshold,
			ObservedBonus = m_ObservedBonus,
			ConfidenceWeight = m_ConfidenceWeight,
			ThreatWeight = m_ThreatWeight,
			DistanceWeight = m_DistanceWeight,
			StalePenalty = m_StalePenalty,
			HostileBonus = m_HostileBonus,
			SwitchThreshold = m_SwitchThreshold,
			WeaponSuitabilityWeight = m_WeaponSuitabilityWeight,
			MissionBonus = m_MissionBonus
		};
	}

	private void RefreshVisibilityCheckerConfig()
	{
		if (m_VisibilityChecker == null)
			return;
		m_VisibilityChecker.Configure(m_LayerMask, m_QueryTriggerInteraction, ResolveRetainRangeMeters(), false);
	}

	private float ResolveRetainRangeMeters()
	{
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		float resolved = m_Vision != null ? m_Vision.ResolvedMaxRange : UnitVisionProfile.BaseRangeMeters;
		return CombatRetainMath.ResolveRetainRangeMeters(resolved);
	}

	private Vector3 GetFireOriginForLofCheck(Vector3 _fallbackOrigin)
	{
		if (m_Equipment != null)
		{
			EquippedWeapon weapon = m_Equipment.EquippedWeapon;
			if (weapon != null && weapon.FireOriginTransform != null)
				return weapon.FireOriginTransform.position;
		}

		return _fallbackOrigin;
	}

	private bool CheckAndSuppressBlockedTarget(
		ref Transform _target,
		ref Vector3 _aimPoint,
		ref bool _hasAimPoint,
		Vector3 _origin)
	{
		if (!_hasAimPoint)
			return false;
		if (m_Hits == null || m_LayerMask == 0)
			return false;

		Vector3 dir = _aimPoint - _origin;
		float dist = dir.magnitude;
		if (dist < 0.05f)
			return false;

		dir /= dist;

		int hitCount = Physics.SphereCastNonAlloc(
			_origin,
			m_LineOfFireSafetyRadius,
			dir,
			m_Hits,
			dist,
			m_LayerMask,
			m_QueryTriggerInteraction);
		SortRaycastHitsByDistance(m_Hits, hitCount);

		UnitTeamId myTeam = m_Team != null ? m_Team.Team : UnitTeamId.Player;
		m_LineOfFireSeenRoots.Clear();

		for (int h = 0; h < hitCount; h++)
		{
			Collider hc = m_Hits[h].collider;
			if (hc == null)
				continue;
			if (hc.transform == transform || hc.transform.IsChildOf(transform))
				continue;
			if (hc.transform == _target || hc.transform.IsChildOf(_target))
				return false;

			if (hc.GetComponent<UnitBodyHitZone>() == null && hc.GetComponentInParent<UnitBodyHitZone>() == null)
				continue;

			UnitTeam hitTeam = hc.GetComponentInParent<UnitTeam>();
			if (hitTeam == null)
				continue;
			if (!m_LineOfFireSeenRoots.Add(hitTeam.transform))
				continue;
			if (hitTeam.Team != myTeam && hitTeam.Team != UnitTeamId.Neutral)
				continue;

			m_LineOfFireSuppressedTargets[_target] = Time.time + m_LineOfFireBlockedRetrySeconds;
			return true;
		}

		return false;
	}

	private static void SortRaycastHitsByDistance(RaycastHit[] _hits, int _count)
	{
		if (_hits == null || _count <= 1)
			return;

		for (int i = 1; i < _count; i++)
		{
			RaycastHit key = _hits[i];
			int j = i - 1;
			while (j >= 0 && _hits[j].distance > key.distance)
			{
				_hits[j + 1] = _hits[j];
				j--;
			}

			_hits[j + 1] = key;
		}
	}

	private void CleanupExpiredSuppressedTargets()
	{
		if (m_LineOfFireSuppressedTargets.Count == 0)
			return;

		float now = Time.time;
		m_ExpiredSuppressedKeys.Clear();
		foreach (var kvp in m_LineOfFireSuppressedTargets)
		{
			if (kvp.Key == null || kvp.Value <= now)
				m_ExpiredSuppressedKeys.Add(kvp.Key);
		}

		for (int i = 0; i < m_ExpiredSuppressedKeys.Count; i++)
			m_LineOfFireSuppressedTargets.Remove(m_ExpiredSuppressedKeys[i]);
	}

	private bool TryRevalidateSuppressedTarget(Transform _candidate, Vector3 _origin)
	{
		if (_candidate == null || !m_LineOfFireSuppressedTargets.TryGetValue(_candidate, out float expireTime))
			return true;

		if (Time.time < expireTime)
			return true;

		m_LineOfFireSuppressedTargets.Remove(_candidate);

		Vector3 targetCenter = GetCandidateRoughCenter(_candidate);
		Vector3 dir = targetCenter - _origin;
		float dist = dir.magnitude;
		if (dist < 0.05f)
			return true;

		dir /= dist;

		int hitCount = Physics.SphereCastNonAlloc(
			_origin,
			m_LineOfFireSafetyRadius,
			dir,
			m_Hits,
			dist,
			m_LayerMask,
			m_QueryTriggerInteraction);

		UnitTeamId myTeam = m_Team != null ? m_Team.Team : UnitTeamId.Player;
		float closestDist = float.MaxValue;
		Collider closestCollider = null;
		m_LineOfFireSeenRoots.Clear();

		for (int h = 0; h < hitCount; h++)
		{
			Collider hc = m_Hits[h].collider;
			if (hc == null || hc.transform == transform || hc.transform.IsChildOf(transform))
				continue;
			if (hc.GetComponent<UnitBodyHitZone>() == null && hc.GetComponentInParent<UnitBodyHitZone>() == null)
				continue;

			UnitTeam hitTeamRoot = hc.GetComponentInParent<UnitTeam>();
			if (hitTeamRoot != null && !m_LineOfFireSeenRoots.Add(hitTeamRoot.transform))
				continue;

			if (m_Hits[h].distance < closestDist)
			{
				closestDist = m_Hits[h].distance;
				closestCollider = hc;
			}
		}

		if (closestCollider != null)
		{
			if (closestCollider.transform == _candidate || closestCollider.transform.IsChildOf(_candidate))
				return true;

			UnitTeam hitTeam = closestCollider.GetComponentInParent<UnitTeam>();
			if (hitTeam != null && (hitTeam.Team == myTeam || hitTeam.Team == UnitTeamId.Neutral))
			{
				m_LineOfFireSuppressedTargets[_candidate] = Time.time + m_LineOfFireBlockedRetrySeconds;
				return false;
			}
		}

		return true;
	}

	private static Vector3 GetCandidateRoughCenter(Transform _candidate)
	{
		UnitBodyHitZone[] zones = _candidate.GetComponentsInChildren<UnitBodyHitZone>(true);
		if (zones != null && zones.Length > 0 &&
		    UnitBodyHitZoneVisionUtility.TryGetCombinedBounds(zones, out Bounds combined))
			return combined.center;

		Collider body = UnitBodyHitZoneVisionUtility.TryGetPreferredCollider(zones, BodyPartType.Chest)
			?? UnitBodyHitZoneVisionUtility.TryGetFirstCollider(zones)
			?? _candidate.GetComponentInChildren<Collider>();
		if (body != null)
			return body.bounds.center;

		if (_candidate.TryGetComponent(out ShootingRangeTarget rangeTarget) && rangeTarget.TargetCollider != null)
			return rangeTarget.TargetCollider.bounds.center;

		return _candidate.position;
	}

	private bool IsWeaponMaintenanceActive()
	{
		if (m_ReloadController != null && m_ReloadController.IsReloadBusy)
			return true;
		return m_WeaponRuntime != null && m_WeaponRuntime.TransientState.HasActiveMalfunction;
	}

	private bool TryRetainEngageTargetDuringWeaponMaintenance(
		Vector3 _origin,
		ref Transform _newTarget,
		ref Vector3 _aimPoint,
		ref bool _hasAimPoint)
	{
		if (!m_RetainTargetDuringReloadOrMalfunction || !IsWeaponMaintenanceActive() || m_SelectedTarget == null)
			return false;

		if (!IsContactEligibleForSelection(m_SelectedTarget))
			return false;

		if (!TryRevalidateRetainedEngageTarget(m_SelectedTarget, _origin, out Vector3 retainedAim, out bool retainedHasAim))
			return false;

		_newTarget = m_SelectedTarget;
		_aimPoint = retainedAim;
		_hasAimPoint = retainedHasAim;
		return true;
	}

	private bool TryRevalidateRetainedEngageTarget(
		Transform _targetRoot,
		Vector3 _origin,
		out Vector3 _aimPoint,
		out bool _hasAimPoint)
	{
		_aimPoint = Vector3.zero;
		_hasAimPoint = false;

		if (!TargetEngageability.IsEngageable(_targetRoot))
			return false;
		if (IsLineOfFireSuppressed(_targetRoot))
			return false;
		if (!TryRevalidateSuppressedTarget(_targetRoot, _origin))
			return false;

		RefreshVisibilityCheckerConfig();
		float retainRange = ResolveRetainRangeMeters();

		if (m_Perception != null &&
		    m_Perception.TryGetObservation(_targetRoot, out VisionObservation observed) &&
		    observed.IsVisible)
		{
			float distance = Mathf.Sqrt(Mathf.Max(0f, observed.DistanceSq));
			if (!CombatRetainMath.CanRetainAtDistance(distance, retainRange))
				return false;
			if (!observed.HasAimPoint)
				return false;
			_aimPoint = observed.AimPoint;
			_hasAimPoint = true;
			return true;
		}

		if (m_VisibilityChecker == null)
			return false;

		UnitBodyHitZone[] zones = _targetRoot.GetComponentsInChildren<UnitBodyHitZone>(true);
		if (zones != null && zones.Length > 0)
		{
			if (!m_VisibilityChecker.TryFindBestVisibleAimPointFromHitZones(
				    _origin, zones, _targetRoot, out Vector3 aimPoint, out _))
				return false;

			if (!CombatRetainMath.CanRetainAtDistance(Vector3.Distance(_origin, aimPoint), retainRange))
				return false;

			_aimPoint = aimPoint;
			_hasAimPoint = true;
			return true;
		}

		Collider legacyTargetCol =
			UnitBodyHitZoneVisionUtility.TryGetPreferredCollider(zones, BodyPartType.Chest)
			?? UnitBodyHitZoneVisionUtility.TryGetFirstCollider(zones)
			?? _targetRoot.GetComponentInChildren<Collider>();
		if (legacyTargetCol == null)
			return false;

		Vector3 targetCenter = legacyTargetCol.bounds.center;
		if (!CombatRetainMath.CanRetainAtDistance(Vector3.Distance(_origin, targetCenter), retainRange))
			return false;

		if (!m_VisibilityChecker.TryFindBestVisibleAimPointFromCollider(
			    _origin, legacyTargetCol, _targetRoot, out Vector3 legacyAimPoint, out _))
			return false;

		_aimPoint = legacyAimPoint;
		_hasAimPoint = true;
		return true;
	}

	private bool TryApplyForcedPriority(
		Vector3 _origin,
		ref Transform _newTarget,
		ref Vector3 _aimPoint,
		ref bool _hasAimPoint)
	{
		if (m_ForcedPriorityTarget == null || m_ForcedPriorityTarget == _newTarget)
			return false;

		Transform forcedRoot = m_ForcedPriorityTarget;
		if (!IsContactEligibleForSelection(forcedRoot))
			return false;
		if (IsLineOfFireSuppressed(forcedRoot))
			return false;
		if (!TryRevalidateSuppressedTarget(forcedRoot, _origin))
			return false;

		_newTarget = forcedRoot;
		if (m_ContactRegistry != null &&
		    m_ContactRegistry.TryGetContact(forcedRoot, out PerceivedContact forcedContact) &&
		    TargetSelectionMath.TryGetObservedAimPoint(forcedContact, out Vector3 observedAim))
		{
			_aimPoint = observedAim;
			_hasAimPoint = true;
		}
		else
		{
			_hasAimPoint = false;
			_aimPoint = Vector3.zero;
		}

		return true;
	}

	private Transform ResolveMissionTarget()
	{
		if (m_MissionTarget != null)
			return m_MissionTarget;
		if (!TryGetComponent(out UnitAIController ai) || ai == null)
			return null;
		if (ai.CurrentState != UnitAIState.Attack && ai.CurrentState != UnitAIState.Defense)
			return null;
		return ai.CurrentContext.TargetEntity;
	}

	private void ResolveWeaponSuitability(out WeaponClassType _weaponClass, out float _effectiveRangeMeters)
	{
		if (m_WeaponClassOverride != WeaponClassType.Unknown)
		{
			_weaponClass = m_WeaponClassOverride;
			_effectiveRangeMeters = m_EffectiveRangeOverride > 0.01f ? m_EffectiveRangeOverride : 100f;
			return;
		}

		_weaponClass = WeaponClassType.Unknown;
		_effectiveRangeMeters = 100f;
		if (m_Equipment == null)
			TryGetComponent(out m_Equipment);
		ItemDefinition item = m_Equipment != null ? m_Equipment.EquippedDefinition : null;
		WeaponDefinition definition = item != null ? item.WeaponDefinition : null;
		if (definition == null)
			return;

		_weaponClass = definition.WeaponClass;
		_effectiveRangeMeters = definition.EffectiveRangeMeters;
	}

	private bool IsContactEligibleForSelection(Transform _target)
	{
		if (_target == null || m_ContactRegistry == null)
			return false;
		if (!m_ContactRegistry.TryGetContact(_target, out PerceivedContact contact) || contact == null)
			return false;
		return ContactSelectionEligibility.Evaluate(
			contact,
			TargetEngageability.IsEngageable(_target),
			BuildPolicy(),
			out _);
	}

	private void UpdateTargetVelocityEstimate(Transform _newTarget, Vector3 _newAimPoint, bool _hasValidAimPoint)
	{
		float now = Time.time;

		if (_newTarget != m_VelocityTrackedTarget)
		{
			m_VelocityTrackedTarget = _newTarget;
			m_PreviousAimPointForVelocity = _hasValidAimPoint ? _newAimPoint : Vector3.zero;
			m_TargetVelocityEstimate = Vector3.zero;
			m_LastVelocityRaw = Vector3.zero;
			m_LastAimPointUpdateTime = now;
			return;
		}

		if (!_hasValidAimPoint || _newAimPoint == Vector3.zero)
			return;

		float dt = now - m_LastAimPointUpdateTime;
		m_LastAimPointUpdateTime = now;

		if (dt > 0.001f && m_PreviousAimPointForVelocity != Vector3.zero)
		{
			Vector3 rawVelocity = (_newAimPoint - m_PreviousAimPointForVelocity) / dt;
			if (m_AimPointVelocitySmoothTime <= 0.0001f)
				m_TargetVelocityEstimate = rawVelocity;
			else
			{
				float t = 1f - Mathf.Exp(-dt / m_AimPointVelocitySmoothTime);
				m_LastVelocityRaw = rawVelocity;
				m_TargetVelocityEstimate = Vector3.Lerp(m_TargetVelocityEstimate, rawVelocity, t);
			}
		}

		m_PreviousAimPointForVelocity = _newAimPoint;
	}
	#endregion
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Chooses an engage target from <see cref="UnitPerception"/> observations.
/// Owns: nearest selection, ForcedPriority, LoF suppress, reload/malfunction retain, selected velocity.
/// Runs automatically on <see cref="UnitPerception.PerceptionFrameApplied"/> — does not need UnitVision.
/// Allowed deps: UnitPerception, TargetEngageability, own VisibilityChecker, UnitObservationSource, UnitTeam/Equipment/reload.
/// Forbidden deps: UnitVision as orchestrator / FOV / detect scan ownership.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitPerception))]
[RequireComponent(typeof(UnitObservationSource))]
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

	[Header("Physics / retain LOS")]
	[SerializeField] private LayerMask m_LayerMask = ~0;
	[SerializeField] private QueryTriggerInteraction m_QueryTriggerInteraction = QueryTriggerInteraction.Ignore;
	[SerializeField, Min(0.5f)] private float m_MaxEngageRange = 18f;

	[Tooltip("While reload / bolt / malfunction — keep current engage target without FOV (range + LOS still required).")]
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
	#endregion

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

	/// <summary>Selected target if it is currently engageable (alive / available).</summary>
	public Transform GetEngageableSelectedTarget()
	{
		return TargetEngageability.IsEngageable(m_SelectedTarget) ? m_SelectedTarget : null;
	}

	/// <summary>World aim point for the engageable selected target (with velocity extrapolation).</summary>
	public Vector3 GetEngageableAimPointWorld()
	{
		Transform selected = GetEngageableSelectedTarget();
		if (selected == null)
			return Vector3.zero;

		Vector3 basePoint;
		if (m_HasSelectedAimPoint)
			basePoint = m_SelectedAimPointWorld;
		else if (selected.TryGetComponent(out ShootingRangeTarget rangeTarget))
			basePoint = rangeTarget.GetAimPointWorld();
		else
		{
			UnitBodyHitZone[] zones = selected.GetComponentsInChildren<UnitBodyHitZone>(true);
			if (zones != null && zones.Length > 0 &&
			    UnitBodyHitZoneVisionUtility.TryGetCombinedBounds(zones, out Bounds combined))
				basePoint = combined.center;
			else
			{
				Collider body = UnitBodyHitZoneVisionUtility.TryGetPreferredCollider(zones, BodyPartType.Chest)
					?? UnitBodyHitZoneVisionUtility.TryGetFirstCollider(zones)
					?? selected.GetComponentInChildren<Collider>();
				basePoint = body != null ? body.bounds.center : selected.position;
			}
		}

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

		m_VisibilityChecker = new VisibilityChecker(transform, m_Hits, m_AimCandidateScratch, m_DebugRays);
		RefreshVisibilityCheckerConfig();
	}

	private void OnEnable()
	{
		if (m_Perception == null)
			m_Perception = GetComponent<UnitPerception>();
		if (m_Perception != null)
			m_Perception.PerceptionFrameApplied += HandlePerceptionFrameApplied;
	}

	private void OnDisable()
	{
		if (m_Perception != null)
			m_Perception.PerceptionFrameApplied -= HandlePerceptionFrameApplied;
	}
	#endregion

	#region Public Methods
	/// <summary>Run selection against the current perception frame using ObservationSource origin.</summary>
	public void SelectFromPerception()
	{
		Vector3 origin = m_ObservationSource != null
			? m_ObservationSource.GetOriginWorld()
			: transform.position + Vector3.up * 1.6f;
		SelectFromPerception(origin);
	}

	/// <summary>Run selection against the current perception frame.</summary>
	public void SelectFromPerception(Vector3 _visionOrigin)
	{
		CleanupExpiredSuppressedTargets();
		RefreshVisibilityCheckerConfig();

		if (!TargetEngageability.IsEngageable(m_SelectedTarget))
		{
			m_SelectedTarget = null;
			m_HasSelectedAimPoint = false;
			m_SelectedAimPointWorld = Vector3.zero;
		}

		Transform newTarget = null;
		bool hasAim = false;
		Vector3 aimPoint = Vector3.zero;
		float bestDistSq = float.MaxValue;

		IReadOnlyList<VisionObservation> observations = m_Perception != null
			? m_Perception.Observations
			: Array.Empty<VisionObservation>();

		for (int i = 0; i < observations.Count; i++)
		{
			VisionObservation obs = observations[i];
			if (obs.Target == null || !obs.IsVisible)
				continue;
			if (IsLineOfFireSuppressed(obs.Target))
				continue;
			if (!TryRevalidateSuppressedTarget(obs.Target, _visionOrigin))
				continue;
			if (obs.DistanceSq >= bestDistSq)
				continue;

			bestDistSq = obs.DistanceSq;
			newTarget = obs.Target;
			hasAim = obs.HasAimPoint;
			aimPoint = obs.AimPoint;
		}

		if (newTarget != null)
		{
			Vector3 fireOrigin = GetFireOriginForLofCheck(_visionOrigin);
			if (CheckAndSuppressBlockedTarget(ref newTarget, ref aimPoint, ref hasAim, fireOrigin))
			{
				newTarget = null;
				hasAim = false;
				aimPoint = Vector3.zero;
			}
		}

		TryRetainEngageTargetDuringWeaponMaintenance(_visionOrigin, ref newTarget, ref aimPoint, ref hasAim);
		TryApplyForcedPriority(_visionOrigin, ref newTarget, ref aimPoint, ref hasAim);

		bool changed = newTarget != m_SelectedTarget;
		m_SelectedTarget = newTarget;
		m_HasSelectedAimPoint = newTarget != null && hasAim;
		m_SelectedAimPointWorld = m_HasSelectedAimPoint ? aimPoint : Vector3.zero;

		if (changed)
			SelectedTargetChanged?.Invoke(m_SelectedTarget);

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
	#endregion

	#region Private Methods
	private void HandlePerceptionFrameApplied()
	{
		SelectFromPerception();
	}

	private void RefreshVisibilityCheckerConfig()
	{
		if (m_VisibilityChecker == null)
			return;
		m_VisibilityChecker.Configure(m_LayerMask, m_QueryTriggerInteraction, m_MaxEngageRange, false);
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

		UnitTeamId myTeam = m_Team != null ? m_Team.Team : UnitTeamId.Player;
		var seenRoots = new HashSet<Transform>();

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
			if (!seenRoots.Add(hitTeam.transform))
				continue;
			if (hitTeam.Team != myTeam && hitTeam.Team != UnitTeamId.Neutral)
				continue;

			m_LineOfFireSuppressedTargets[_target] = Time.time + m_LineOfFireBlockedRetrySeconds;
			return true;
		}

		return false;
	}

	private void CleanupExpiredSuppressedTargets()
	{
		if (m_LineOfFireSuppressedTargets.Count == 0)
			return;

		float now = Time.time;
		var expiredKeys = new List<Transform>();
		foreach (var kvp in m_LineOfFireSuppressedTargets)
		{
			if (kvp.Key == null || kvp.Value <= now)
				expiredKeys.Add(kvp.Key);
		}

		foreach (var key in expiredKeys)
			m_LineOfFireSuppressedTargets.Remove(key);
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
		var seenUnitRoots = new HashSet<Transform>();

		for (int h = 0; h < hitCount; h++)
		{
			Collider hc = m_Hits[h].collider;
			if (hc == null || hc.transform == transform || hc.transform.IsChildOf(transform))
				continue;
			if (hc.GetComponent<UnitBodyHitZone>() == null && hc.GetComponentInParent<UnitBodyHitZone>() == null)
				continue;

			UnitTeam hitTeamRoot = hc.GetComponentInParent<UnitTeam>();
			if (hitTeamRoot != null && !seenUnitRoots.Add(hitTeamRoot.transform))
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

		float rangeSq = m_MaxEngageRange * m_MaxEngageRange;

		if (m_Perception != null &&
		    m_Perception.TryGetObservation(_targetRoot, out VisionObservation observed) &&
		    observed.IsVisible)
		{
			if (observed.DistanceSq > rangeSq || observed.DistanceSq < 0.0001f)
				return false;
			_aimPoint = observed.HasAimPoint ? observed.AimPoint : GetCandidateRoughCenter(_targetRoot);
			_hasAimPoint = true;
			return true;
		}

		if (m_VisibilityChecker == null)
			return false;

		UnitBodyHitZone[] zones = _targetRoot.GetComponentsInChildren<UnitBodyHitZone>(true);
		if (zones != null && zones.Length > 0)
		{
			if (!m_VisibilityChecker.TryFindBestVisibleAimPointFromHitZones(
				    _origin, zones, _targetRoot, out Vector3 aimPoint))
				return false;

			Vector3 toAim = aimPoint - _origin;
			toAim.y = 0f;
			if (toAim.sqrMagnitude > rangeSq || toAim.sqrMagnitude < 0.0001f)
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
		Vector3 toTarget = targetCenter - _origin;
		toTarget.y = 0f;
		if (toTarget.sqrMagnitude > rangeSq || toTarget.sqrMagnitude < 0.0001f)
			return false;

		if (!m_VisibilityChecker.TryFindBestVisibleAimPointFromCollider(
			    _origin, legacyTargetCol, _targetRoot, out Vector3 legacyAimPoint))
			return false;

		_aimPoint = legacyAimPoint;
		_hasAimPoint = true;
		return true;
	}

	private void TryApplyForcedPriority(
		Vector3 _origin,
		ref Transform _newTarget,
		ref Vector3 _aimPoint,
		ref bool _hasAimPoint)
	{
		if (m_ForcedPriorityTarget == null || m_ForcedPriorityTarget == _newTarget)
			return;

		Transform forcedRoot = m_ForcedPriorityTarget;
		bool forcedValid = false;
		Vector3 forcedAimPoint = Vector3.zero;

		bool isLiveUnitCandidate =
			forcedRoot.gameObject.activeInHierarchy &&
			forcedRoot.TryGetComponent(out UnitTeam _) &&
			UnitConsciousness.IsTargetableTarget(forcedRoot) &&
			!(forcedRoot.TryGetComponent(out DamageableTarget forcedDmg) && !forcedDmg.IsAlive);

		if (isLiveUnitCandidate &&
		    !IsLineOfFireSuppressed(forcedRoot) &&
		    TryRevalidateSuppressedTarget(forcedRoot, _origin))
		{
			forcedAimPoint = GetCandidateRoughCenter(forcedRoot);
			forcedValid = true;
		}
		else if (forcedRoot.TryGetComponent(out ShootingRangeTarget rangeTarget) &&
		         rangeTarget.IsAvailableForTargeting &&
		         !IsLineOfFireSuppressed(forcedRoot) &&
		         TryRevalidateSuppressedTarget(forcedRoot, _origin))
		{
			forcedAimPoint = rangeTarget.GetAimPointWorld();
			forcedValid = true;
		}

		if (!forcedValid)
			return;

		Vector3 eyePos = m_ObservationSource != null
			? m_ObservationSource.GetEyeWorldPosition()
			: transform.position + Vector3.up * 1.6f;
		Vector3 rayDir = (forcedAimPoint - eyePos).normalized;
		float rayDist = Vector3.Distance(eyePos, forcedAimPoint);
		if (!Physics.Raycast(eyePos, rayDir, rayDist, m_LayerMask, m_QueryTriggerInteraction))
		{
			_newTarget = forcedRoot;
			_aimPoint = forcedAimPoint;
			_hasAimPoint = true;
		}
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

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Raycast / hit-zone LOS checks for vision detection.
/// Answers physical visibility and visible aim point — not combat target selection.
/// </summary>
public sealed class VisibilityChecker
{
	#region Private Fields
	private readonly Transform m_SelfRoot;
	private readonly RaycastHit[] m_Hits;
	private readonly List<UnitBodyHitZoneVisionUtility.VisionAimCandidate> m_AimCandidateScratch;
	private readonly List<(Vector3 from, Vector3 to, bool hitTarget)> m_DebugRays;
	private LayerMask m_LayerMask;
	private QueryTriggerInteraction m_QueryTriggerInteraction;
	private float m_VisionRange;
	private bool m_DrawVisionGizmos;
	private VisionScanStats m_Stats;
	public bool LastLosWasBlocked { get; private set; }
	public string LastLosBlocker { get; private set; }
	#endregion

	#region Construction
	public VisibilityChecker(
		Transform _selfRoot,
		RaycastHit[] _hits,
		List<UnitBodyHitZoneVisionUtility.VisionAimCandidate> _aimCandidateScratch,
		List<(Vector3 from, Vector3 to, bool hitTarget)> _debugRays)
	{
		m_SelfRoot = _selfRoot;
		m_Hits = _hits;
		m_AimCandidateScratch = _aimCandidateScratch;
		m_DebugRays = _debugRays;
	}
	#endregion

	#region Public Methods
	public void Configure(LayerMask _layerMask, QueryTriggerInteraction _queryTriggerInteraction, float _visionRange, bool _drawGizmos)
	{
		m_LayerMask = _layerMask;
		m_QueryTriggerInteraction = _queryTriggerInteraction;
		m_VisionRange = _visionRange;
		m_DrawVisionGizmos = _drawGizmos;
	}

	public void BindStats(VisionScanStats _stats)
	{
		m_Stats = _stats;
	}

	public void ClearDebugRays()
	{
		m_DebugRays.Clear();
	}

	public bool TryFindBestVisibleAimPointFromHitZones(
		Vector3 _eye,
		UnitBodyHitZone[] _hitZones,
		Transform _opponentRoot,
		out Vector3 _aimPoint,
		out float _exposure01)
	{
		_aimPoint = Vector3.zero;
		_exposure01 = 0f;
		bool found = false;
		float bestWeight = float.MinValue;
		float totalWeight = 0f;
		float visibleWeight = 0f;

		for (int z = 0; z < _hitZones.Length; z++)
		{
			UnitBodyHitZone zone = _hitZones[z];
			if (!UnitBodyHitZoneVisionUtility.IsUsableVisionZone(zone, out Collider zoneCol))
				continue;

			UnitBodyHitZoneVisionUtility.BuildAimCandidates(zone.BodyPart, zoneCol, m_AimCandidateScratch);
			for (int i = 0; i < m_AimCandidateScratch.Count; i++)
			{
				m_Stats?.AddHitZoneCheck();
				UnitBodyHitZoneVisionUtility.VisionAimCandidate candidate = m_AimCandidateScratch[i];
				float weight = Mathf.Max(0.0001f, candidate.Weight);
				totalWeight += weight;

				bool ok = HasLineOfSightToPoint(_eye, candidate.Point, _opponentRoot, zoneCol, out Vector3 rayEnd, out bool hitTarget);
				if (m_DrawVisionGizmos)
					m_DebugRays.Add((_eye, rayEnd, hitTarget && ok));

				if (!ok)
					continue;

				visibleWeight += weight;
				if (candidate.Weight <= bestWeight)
					continue;

				bestWeight = candidate.Weight;
				_aimPoint = candidate.Point;
				found = true;
			}
		}

		_exposure01 = totalWeight > 0.0001f ? Mathf.Clamp01(visibleWeight / totalWeight) : 0f;
		return found;
	}

	/// <summary>
	/// Scope / far path: chest → head → pelvis, stop on the first LOS. Not the G1 exposure grid.
	/// </summary>
	public bool TryFindFirstVisibleAimPointCheap(
		Vector3 _eye,
		UnitBodyHitZone[] _hitZones,
		Transform _opponentRoot,
		out Vector3 _aimPoint,
		out float _exposure01)
	{
		_aimPoint = Vector3.zero;
		_exposure01 = 0f;
		if (_hitZones == null || _hitZones.Length == 0)
			return false;

		if (TryCheapZone(_eye, _hitZones, _opponentRoot, BodyPartType.Chest, out _aimPoint) ||
		    TryCheapZone(_eye, _hitZones, _opponentRoot, BodyPartType.Head, out _aimPoint) ||
		    TryCheapZone(_eye, _hitZones, _opponentRoot, BodyPartType.Abdomen, out _aimPoint))
		{
			_exposure01 = 1f;
			return true;
		}

		return false;
	}

	private bool TryCheapZone(
		Vector3 _eye,
		UnitBodyHitZone[] _hitZones,
		Transform _opponentRoot,
		BodyPartType _part,
		out Vector3 _aimPoint)
	{
		_aimPoint = Vector3.zero;
		Collider zoneCol = UnitBodyHitZoneVisionUtility.TryGetPreferredCollider(_hitZones, _part);
		if (zoneCol == null)
			return false;

		UnitBodyHitZoneVisionUtility.BuildAimCandidates(_part, zoneCol, m_AimCandidateScratch);
		if (m_AimCandidateScratch.Count == 0)
			return false;

		m_Stats?.AddHitZoneCheck();
		UnitBodyHitZoneVisionUtility.VisionAimCandidate candidate = m_AimCandidateScratch[0];
		bool ok = HasLineOfSightToPoint(_eye, candidate.Point, _opponentRoot, zoneCol, out Vector3 rayEnd, out bool hitTarget);
		if (m_DrawVisionGizmos)
			m_DebugRays.Add((_eye, rayEnd, hitTarget && ok));
		if (!ok)
			return false;

		_aimPoint = candidate.Point;
		return true;
	}

	public bool TryFindBestVisibleAimPointFromCollider(
		Vector3 _eye,
		Collider _targetCol,
		Transform _opponentRoot,
		out Vector3 _aimPoint,
		out float _exposure01)
	{
		_aimPoint = Vector3.zero;
		_exposure01 = 0f;
		bool found = false;
		float bestWeight = float.MinValue;

		UnitBodyHitZoneVisionUtility.BuildAimCandidates(BodyPartType.Chest, _targetCol, m_AimCandidateScratch);
		for (int i = 0; i < m_AimCandidateScratch.Count; i++)
		{
			UnitBodyHitZoneVisionUtility.VisionAimCandidate candidate = m_AimCandidateScratch[i];
			bool ok = HasLineOfSightToPoint(_eye, candidate.Point, _opponentRoot, _targetCol, out Vector3 rayEnd, out bool hitTarget);
			if (m_DrawVisionGizmos)
				m_DebugRays.Add((_eye, rayEnd, hitTarget && ok));
			if (!ok || candidate.Weight <= bestWeight)
				continue;

			bestWeight = candidate.Weight;
			_aimPoint = candidate.Point;
			found = true;
		}

		// Legacy collider path: full exposure if any LOS sample succeeds.
		_exposure01 = found ? 1f : 0f;
		return found;
	}

	public bool HasLineOfSightToPoint(
		Vector3 _eye,
		Vector3 _worldPoint,
		Transform _opponentRoot,
		Collider _primaryTargetCollider,
		out Vector3 _rayEndDebug,
		out bool _hitTargetCollider)
	{
		LastLosWasBlocked = false;
		LastLosBlocker = null;
		_hitTargetCollider = false;
		m_Stats?.AddLosCheck();
		Vector3 dir = (_worldPoint - _eye);
		float dist = dir.magnitude;
		if (dist < 0.02f)
		{
			_rayEndDebug = _worldPoint;
			return true;
		}

		dir /= dist;
		float castMax = Mathf.Min(dist + 0.1f, m_VisionRange);
		Vector3 origin = _eye + dir * 0.08f;

		int hitCount = Physics.RaycastNonAlloc(
			origin,
			dir,
			m_Hits,
			castMax - 0.08f,
			m_LayerMask,
			QueryTriggerInteraction.Collide);

		_rayEndDebug = origin + dir * (castMax - 0.08f);
		if (hitCount <= 0)
			return false;

		for (int i = 1; i < hitCount; i++)
		{
			RaycastHit key = m_Hits[i];
			int j = i - 1;
			while (j >= 0 && m_Hits[j].distance > key.distance)
			{
				m_Hits[j + 1] = m_Hits[j];
				j--;
			}
			m_Hits[j + 1] = key;
		}

		for (int h = 0; h < hitCount; h++)
		{
			RaycastHit hit = m_Hits[h];
			Collider hc = hit.collider;
			if (hc == null)
				continue;
			if (hc.transform.IsChildOf(m_SelfRoot))
				continue;
			if (hc.isTrigger && !hc.transform.IsChildOf(_opponentRoot) && hc != _primaryTargetCollider)
				continue;

			if (hc == _primaryTargetCollider || hc.transform.IsChildOf(_opponentRoot))
			{
				_hitTargetCollider = true;
				_rayEndDebug = hit.point;
				return true;
			}

			LastLosWasBlocked = true;
			LastLosBlocker = hc.name;
			_rayEndDebug = hit.point;
			return false;
		}

		return false;
	}

	public bool TryCoarseLineOfSightToBounds(
		Vector3 _eye,
		Bounds _bounds,
		Transform _opponentRoot,
		Collider _primaryTargetCollider,
		out Vector3 _samplePoint)
	{
		_samplePoint = _bounds.ClosestPoint(_eye);
		if ((_samplePoint - _eye).sqrMagnitude < 0.0001f)
			_samplePoint = _bounds.center;

		return HasLineOfSightToPoint(
			_eye,
			_samplePoint,
			_opponentRoot,
			_primaryTargetCollider,
			out _,
			out _);
	}

	public bool TryGetLosBlocker(
		Vector3 _origin,
		Transform _visibleTarget,
		Vector3 _aimPoint,
		out string _blockerName)
	{
		_blockerName = null;

		if (_visibleTarget == null)
		{
			_blockerName = "no target or aim point";
			return true;
		}

		Vector3 dir = (_aimPoint - _origin);
		float dist = dir.magnitude;
		if (dist < 0.02f)
			return false;

		dir /= dist;
		float castDist = Mathf.Min(dist + 0.1f, m_VisionRange);
		Vector3 rayOrigin = _origin + dir * 0.08f;

		int hitCount = Physics.RaycastNonAlloc(
			rayOrigin,
			dir,
			m_Hits,
			castDist - 0.08f,
			m_LayerMask,
			m_QueryTriggerInteraction);

		for (int h = 0; h < hitCount; h++)
		{
			RaycastHit hit = m_Hits[h];
			Collider hc = hit.collider;
			if (hc == null)
				continue;
			if (hc.transform.IsChildOf(m_SelfRoot))
				continue;
			if (hc.transform == _visibleTarget || hc.transform.IsChildOf(_visibleTarget))
				return false;

			_blockerName = hc.name;
			return true;
		}

		_blockerName = "nothing hit";
		return true;
	}
	#endregion
}

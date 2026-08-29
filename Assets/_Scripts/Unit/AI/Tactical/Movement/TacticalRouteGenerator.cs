using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14.1 candidate factory. Direct is always first. Not cover-to-cover. 14.3 appends corridor hops separately.
/// </summary>
public static class TacticalRouteGenerator
{
	#region Constants
	public const int DefaultMaxRouteCandidates = 4;
	public const float DefaultDiversityMeters = 3f;
	public const float DefaultOffsetMeters = 6f;
	#endregion

	#region Public Methods
	public static int Generate(
		in TacticalRouteSituation _situation,
		List<TacticalRouteCandidate> _destination,
		int _max,
		float _diversityMeters,
		float _offsetMeters)
	{
		_destination.Clear();
		if (!_situation.HasDestination)
			return 0;
		int cap = Mathf.Max(1, _max);
		var direct = new TacticalRouteCandidate();
		direct.SetDirect(1, _situation.Origin, _situation.Destination);
		_destination.Add(direct);
		if (cap <= 1)
			return _destination.Count;

		Vector3 forward = _situation.Destination - _situation.Origin;
		forward.y = 0f;
		if (forward.sqrMagnitude < 0.01f)
			return _destination.Count;
		forward.Normalize();
		Vector3 right = Vector3.Cross(Vector3.up, forward);
		if (right.sqrMagnitude < 0.01f)
			right = Vector3.right;
		right.Normalize();
		float offset = Mathf.Max(0.5f, _offsetMeters);
		float diversity = Mathf.Max(0.25f, _diversityMeters);
		Vector3 mid = Vector3.Lerp(_situation.Origin, _situation.Destination, 0.5f);
		TryAddOffset(_situation, mid + right * offset, 2, diversity, cap, _destination);
		TryAddOffset(_situation, mid - right * offset, 3, diversity, cap, _destination);
		TryAddOffset(_situation, mid + right * (offset * 1.7f), 4, diversity, cap, _destination);
		return _destination.Count;
	}

	public static int CapAndDedup(
		List<TacticalRouteCandidate> _candidates,
		int _max,
		float _diversityMeters)
	{
		if (_candidates == null)
			return 0;
		int cap = Mathf.Max(1, _max);
		float diversity = Mathf.Max(0.25f, _diversityMeters);
		for (int i = _candidates.Count - 1; i >= 0; i--)
		{
			TacticalRouteCandidate candidate = _candidates[i];
			if (candidate == null)
			{
				_candidates.RemoveAt(i);
				continue;
			}

			if (IsDuplicate(candidate, _candidates, i, diversity))
			{
				candidate.RejectReason = TacticalRouteRejectReason.Duplicate;
				_candidates.RemoveAt(i);
			}
		}

		while (_candidates.Count > cap)
		{
			TacticalRouteCandidate extra = _candidates[_candidates.Count - 1];
			if (extra != null)
				extra.RejectReason = TacticalRouteRejectReason.Capped;
			_candidates.RemoveAt(_candidates.Count - 1);
		}

		return _candidates.Count;
	}

	public static bool IsDiverseFrom(
		TacticalRouteCandidate _candidate,
		TacticalRouteCandidate _other,
		float _diversityMeters)
	{
		if (_candidate == null || _other == null)
			return true;
		Vector3 a = SignaturePoint(_candidate);
		Vector3 b = SignaturePoint(_other);
		float thresh = Mathf.Max(0.25f, _diversityMeters);
		return CoverSpatialMath.PlanarDistanceSqr(a, b) >= thresh * thresh;
	}
	#endregion

	#region Private Methods
	private static void TryAddOffset(
		in TacticalRouteSituation _situation,
		Vector3 _hop,
		int _id,
		float _diversityMeters,
		int _cap,
		List<TacticalRouteCandidate> _destination)
	{
		if (_destination.Count >= _cap)
			return;
		var candidate = new TacticalRouteCandidate();
		candidate.SetWaypoint(_id, _situation.Origin, _situation.Destination, _hop);
		for (int i = 0; i < _destination.Count; i++)
		{
			if (!IsDiverseFrom(candidate, _destination[i], _diversityMeters))
				return;
		}

		_destination.Add(candidate);
	}

	private static bool IsDuplicate(
		TacticalRouteCandidate _candidate,
		List<TacticalRouteCandidate> _all,
		int _index,
		float _diversityMeters)
	{
		for (int i = 0; i < _index; i++)
		{
			if (!IsDiverseFrom(_candidate, _all[i], _diversityMeters))
				return true;
		}

		return false;
	}

	private static Vector3 SignaturePoint(TacticalRouteCandidate _candidate)
	{
		if (_candidate.Intermediates != null && _candidate.Intermediates.Count > 0)
			return _candidate.Intermediates[0].Position;
		return Vector3.Lerp(_candidate.Origin, _candidate.Destination, 0.5f);
	}
	#endregion
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Search 2.0 candidate generate / filter / score. Pure. Does not write Memory or call Fire.
/// Built once per Search Enter and cached.
/// </summary>
public static class UnitAISearchPlanner
{
	#region Constants
	public const int MaxSearchCandidates = 6;
	public const float DuplicateRadius = 1.5f;
	public const float RingRadiusFraction = 0.6f;
	public const float AxisOffsetFraction = 0.5f;
	public const float HorizonSeconds = 30f;
	private const int c_RingCount = 4;
	#endregion

	#region Private Fields
	private static readonly List<Vector3> s_Seeds = new List<Vector3>(8);
	private static readonly List<UnitAISearchCandidate> s_Filtered = new List<UnitAISearchCandidate>(8);
	private static Vector3 s_SortOrigin;
	#endregion

	#region Public Methods
	public static void Build(
		in UnitAISearchArea _area,
		Vector3 _origin,
		float _now,
		ISearchReachability _reachability,
		List<UnitAISearchCandidate> _results)
	{
		if (_results == null)
			return;

		_results.Clear();
		s_Seeds.Clear();
		s_Filtered.Clear();

		ISearchReachability reach = _reachability ?? UnitAISearchAlwaysReachable.Instance;
		float radius = Mathf.Max(0.01f, _area.Radius);
		Vector3 center = _area.Center;
		Vector3 axis = Planar(center - _origin);
		if (axis.sqrMagnitude < 0.0001f)
			axis = Vector3.forward;
		axis.Normalize();

		s_Seeds.Add(center);
		s_Seeds.Add(center - axis * (radius * AxisOffsetFraction));
		s_Seeds.Add(center + axis * (radius * AxisOffsetFraction));
		float ring = radius * RingRadiusFraction;
		float startAngle = Mathf.Atan2(axis.x, axis.z);
		for (int i = 0; i < c_RingCount; i++)
		{
			float angle = startAngle + i * (Mathf.PI * 0.5f);
			s_Seeds.Add(center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * ring);
		}

		s_SortOrigin = _origin;
		float freshness = Freshness(_area.Timestamp, _now);
		for (int i = 0; i < s_Seeds.Count; i++)
		{
			Vector3 seed = s_Seeds[i];
			if (!UnitSearchNavigationMath.IsInsideSearchArea(seed, center, radius))
				continue;
			if (!reach.TryAccept(_origin, seed, out Vector3 sampled))
				continue;
			if (!UnitSearchNavigationMath.IsInsideSearchArea(sampled, center, radius))
				continue;
			if (IsDuplicate(s_Filtered, sampled))
				continue;

			float score = Score(_area, sampled, _origin, radius, freshness);
			s_Filtered.Add(new UnitAISearchCandidate(sampled, score));
		}

		s_Filtered.Sort(CompareDeterministic);
		int count = Mathf.Min(s_Filtered.Count, MaxSearchCandidates);
		for (int i = 0; i < count; i++)
			_results.Add(s_Filtered[i]);
	}

	public static float Freshness(float _timestamp, float _now)
	{
		float age = _now >= _timestamp ? _now - _timestamp : 0f;
		return 1f - Mathf.Clamp01(age / HorizonSeconds);
	}

	public static float Score(
		in UnitAISearchArea _area,
		Vector3 _point,
		Vector3 _origin,
		float _radius,
		float _freshness)
	{
		float radius = Mathf.Max(0.01f, _radius);
		float evidence = 1f - Mathf.Clamp01(UnitSearchNavigationMath.PlanarDistance(_point, _area.Center) / radius);
		float proximity = 1f - Mathf.Clamp01(UnitSearchNavigationMath.PlanarDistance(_point, _origin) / (radius * 2f));
		return evidence * 0.40f + _area.Confidence * 0.25f + _freshness * 0.20f + proximity * 0.15f;
	}
	#endregion

	#region Private Methods
	private static Vector3 Planar(Vector3 _value)
	{
		_value.y = 0f;
		return _value;
	}

	private static bool IsDuplicate(List<UnitAISearchCandidate> _existing, Vector3 _point)
	{
		for (int i = 0; i < _existing.Count; i++)
		{
			if (UnitSearchNavigationMath.PlanarDistance(_existing[i].Position, _point) <= DuplicateRadius)
				return true;
		}

		return false;
	}

	private static int CompareDeterministic(UnitAISearchCandidate _a, UnitAISearchCandidate _b)
	{
		int score = _b.Score.CompareTo(_a.Score);
		if (score != 0)
			return score;

		float da = UnitSearchNavigationMath.PlanarDistance(_a.Position, s_SortOrigin);
		float db = UnitSearchNavigationMath.PlanarDistance(_b.Position, s_SortOrigin);
		int origin = da.CompareTo(db);
		if (origin != 0)
			return origin;

		int x = _a.Position.x.CompareTo(_b.Position.x);
		if (x != 0)
			return x;
		return _a.Position.z.CompareTo(_b.Position.z);
	}
	#endregion
}

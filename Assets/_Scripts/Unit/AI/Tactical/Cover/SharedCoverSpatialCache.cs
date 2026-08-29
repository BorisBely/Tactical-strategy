using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #13.0 shared spatial cover cache. Geometry only. Not “best cover”. Not Fire.
/// Lazy per-region generation, in-flight dedup, GeometryVersion invalidation.
/// 13.1 fills ICoverCandidateSource; cache still owns when generate runs.
/// </summary>
public sealed class SharedCoverSpatialCache
{
	#region Nested
	private sealed class RegionSlot
	{
		public readonly List<CoverCandidate> Candidates = new List<CoverCandidate>(16);
		public int GeometryVersion;
	}
	#endregion

	#region Private Fields
	private readonly ICoverCandidateSource m_Source;
	private readonly Dictionary<CoverRegionId, RegionSlot> m_Regions =
		new Dictionary<CoverRegionId, RegionSlot>(32);
	private readonly Dictionary<CoverRegionId, RegionSlot> m_InFlight =
		new Dictionary<CoverRegionId, RegionSlot>(4);
	private readonly List<CoverCandidate> m_GenerateScratch = new List<CoverCandidate>(16);
	private float m_RegionSizeMeters = CoverSpatialMath.DefaultRegionSizeMeters;
	private int m_MaxCoverCandidates = CoverSpatialMath.DefaultMaxCoverCandidates;
	private int m_GeometryVersion = 1;
	private int m_GenerationCount;
	private int m_CacheHitCount;
	private int m_CacheMissCount;
	#endregion

	#region Public Properties
	public int GeometryVersion => m_GeometryVersion;
	public int GenerationCount => m_GenerationCount;
	public int CacheHitCount => m_CacheHitCount;
	public int CacheMissCount => m_CacheMissCount;
	public int CachedRegionCount => m_Regions.Count;
	public float RegionSizeMeters => m_RegionSizeMeters;
	public int MaxCoverCandidates => m_MaxCoverCandidates;
	#endregion

	#region Public Constructors
	public SharedCoverSpatialCache(ICoverCandidateSource _source)
	{
		m_Source = _source;
	}
	#endregion

	#region Public Methods
	public void SetRegionSizeMeters(float _meters)
	{
		m_RegionSizeMeters = Mathf.Max(1f, _meters);
	}

	public void SetMaxCoverCandidates(int _max)
	{
		m_MaxCoverCandidates = Mathf.Max(1, _max);
	}

	public CoverRegionId RegionAt(Vector3 _world)
	{
		return CoverSpatialMath.WorldToRegion(_world, m_RegionSizeMeters);
	}

	public IReadOnlyList<CoverCandidate> GetCandidates(Vector3 _world)
	{
		return GetCandidates(RegionAt(_world));
	}

	public IReadOnlyList<CoverCandidate> GetCandidates(CoverRegionId _region)
	{
		if (m_InFlight.TryGetValue(_region, out RegionSlot inFlight))
			return inFlight.Candidates;

		if (m_Regions.TryGetValue(_region, out RegionSlot cached) &&
		    cached.GeometryVersion == m_GeometryVersion)
		{
			m_CacheHitCount++;
			LogCache(_region, false, cached.Candidates.Count);
			return cached.Candidates;
		}

		return Generate(_region);
	}

	public void InvalidateRegion(CoverRegionId _region)
	{
		m_Regions.Remove(_region);
	}

	public void BumpGeometryVersion()
	{
		m_GeometryVersion++;
		m_Regions.Clear();
	}
	#endregion

	#region Private Methods
	private IReadOnlyList<CoverCandidate> Generate(CoverRegionId _region)
	{
		var slot = new RegionSlot { GeometryVersion = m_GeometryVersion };
		m_InFlight[_region] = slot;

		m_GenerateScratch.Clear();
		if (m_Source != null)
		{
			Bounds bounds = CoverSpatialMath.RegionBounds(_region, m_RegionSizeMeters);
			m_Source.Generate(_region, bounds, m_GeometryVersion, m_GenerateScratch);
		}

		int cap = m_MaxCoverCandidates;
		int copyCount = Mathf.Min(cap, m_GenerateScratch.Count);
		for (int i = 0; i < copyCount; i++)
		{
			CoverCandidate candidate = m_GenerateScratch[i];
			if (candidate == null)
				continue;
			candidate.RegionId = _region;
			candidate.GeometryVersion = m_GeometryVersion;
			if (candidate.CandidateId == 0)
				candidate.CandidateId = slot.Candidates.Count + 1;
			slot.Candidates.Add(candidate);
		}

		m_InFlight.Remove(_region);
		m_Regions[_region] = slot;
		m_GenerationCount++;
		m_CacheMissCount++;
		LogCache(_region, true, slot.Candidates.Count);
		return slot.Candidates;
	}

	private static void LogCache(CoverRegionId _region, bool _generated, int _candidates)
	{
		if (!UnitActionLog.Enabled)
			return;
		string payload = _generated
			? "region=" + _region.LogLabel + " generated=1 candidates=" + _candidates
			: "region=" + _region.LogLabel + " reuse=1";
		UnitActionLog.Timeline(UnitActionLog.CoverCache, payload);
	}
	#endregion
}

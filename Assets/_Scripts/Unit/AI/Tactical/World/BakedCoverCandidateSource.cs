using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Play-time #13 source: copy editor-baked candidates. No physics query.
/// </summary>
public sealed class BakedCoverCandidateSource : ICoverCandidateSource
{
	#region Private Fields
	private readonly IReadOnlyList<BakedCoverCandidateRecord> m_Baked;
	#endregion

	#region Public Constructors
	public BakedCoverCandidateSource(IReadOnlyList<BakedCoverCandidateRecord> _baked)
	{
		m_Baked = _baked;
	}
	#endregion

	#region Public Methods
	public void Generate(
		CoverRegionId _region,
		Bounds _bounds,
		int _geometryVersion,
		List<CoverCandidate> _destination)
	{
		if (_destination == null || m_Baked == null)
			return;
		for (int i = 0; i < m_Baked.Count; i++)
		{
			BakedCoverCandidateRecord record = m_Baked[i];
			if (record.RegionX != _region.X || record.RegionZ != _region.Z)
				continue;
			CoverCandidate candidate = record.ToCandidate();
			candidate.GeometryVersion = _geometryVersion;
			_destination.Add(candidate);
		}
	}
	#endregion
}

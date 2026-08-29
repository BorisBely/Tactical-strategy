using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Produces geometric cover candidates for one spatial region. Shared cache owns when this runs.
/// Production path: CoverCandidateGenerator. Tests may inject a stub.
/// </summary>
public interface ICoverCandidateSource
{
	void Generate(CoverRegionId _region, Bounds _bounds, int _geometryVersion, List<CoverCandidate> _destination);
}

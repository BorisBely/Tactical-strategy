using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local scene geometry for one cover region. Cache never talks to colliders directly.
/// </summary>
public interface ICoverGeometrySource
{
	void Collect(CoverRegionId _region, Bounds _queryBounds, List<CoverGeometrySurface> _destination);
}

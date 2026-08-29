using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local scene geometry for one cover region. Cache never talks to colliders directly.
/// </summary>
public interface ICoverGeometrySource
{
	void Collect(CoverRegionId _region, Bounds _queryBounds, List<CoverGeometrySurface> _destination);
}

/// <summary>
/// Oriented box of a closed prop. Not a wall face. #13.2C.10
/// </summary>
public struct CoverObstacleSilhouette
{
	public Vector3 Center;
	public Vector3 Axis;
	public Vector3 Extents;
}

/// <summary>
/// Optional collider-silhouette pass for small closed props. Point-bake sources omit this.
/// </summary>
public interface ICoverObstacleSilhouetteSource
{
	void BeginObstacleCollect(CoverGenerationSettings _settings);
	IReadOnlyList<CoverObstacleSilhouette> LastObstacles { get; }
}

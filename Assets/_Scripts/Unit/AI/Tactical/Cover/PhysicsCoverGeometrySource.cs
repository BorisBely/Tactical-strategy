using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static world colliders in the queried region. Skips characters, vehicles, triggers,
/// and <see cref="TacticalTransparent"/> panes (glass is not a cover wall).
/// Box and mesh faces follow the object's transform. Collect tiles so large arenas are not truncated.
/// Small closed props can be taken as silhouettes instead of wall faces (#13.2C.10).
/// </summary>
public sealed class PhysicsCoverGeometrySource : ICoverGeometrySource, ICoverObstacleSilhouetteSource
{
	#region Constants
	private const int c_HitCapacity = 256;
	private const float c_MinFaceLength = 0.8f;
	private const float c_MinFaceHeight = 0.8f;
	private const int c_DetailedMeshTriangleThreshold = 24;
	private const float c_MaxWallUpDot = 0.35f;
	private const float c_ExposureProbeOffset = 0.12f;
	private const float c_ExposureProbeRadius = 0.07f;
	private const float c_CollectTileMeters = 16f;
	private const float c_MinSplitExtents = 2f;
	#endregion

	#region Private Fields
	private readonly Collider[] m_Hits = new Collider[c_HitCapacity];
	private readonly Collider[] m_ExposureHits = new Collider[16];
	private readonly List<CoverObstacleSilhouette> m_Obstacles = new List<CoverObstacleSilhouette>(32);
	private LayerMask m_Mask = ~0;
	private bool m_ObstacleCollectArmed;
	private CoverGenerationSettings m_ObstacleSettings;
	#endregion

	#region Public Properties
	public int QueryCount { get; private set; }
	public CoverRegionId LastRegion { get; private set; }
	public Bounds LastBounds { get; private set; }
	public IReadOnlyList<CoverObstacleSilhouette> LastObstacles => m_Obstacles;
	public LayerMask Mask
	{
		get => m_Mask;
		set => m_Mask = value;
	}
	#endregion

	#region Public Methods
	public void ResetQueryCount()
	{
		QueryCount = 0;
	}

	public void BeginObstacleCollect(CoverGenerationSettings _settings)
	{
		m_ObstacleCollectArmed = true;
		m_ObstacleSettings = _settings ?? new CoverGenerationSettings();
		m_Obstacles.Clear();
	}

	public void Collect(CoverRegionId _region, Bounds _queryBounds, List<CoverGeometrySurface> _destination)
	{
		QueryCount++;
		LastRegion = _region;
		LastBounds = _queryBounds;
		if (!m_ObstacleCollectArmed)
			m_Obstacles.Clear();
		if (_destination == null)
		{
			m_ObstacleCollectArmed = false;
			return;
		}

		Physics.SyncTransforms();
		var seen = new HashSet<Collider>();
		float tile = Mathf.Max(4f, c_CollectTileMeters);
		int x0 = Mathf.FloorToInt(_queryBounds.min.x / tile);
		int x1 = Mathf.FloorToInt(_queryBounds.max.x / tile);
		int z0 = Mathf.FloorToInt(_queryBounds.min.z / tile);
		int z1 = Mathf.FloorToInt(_queryBounds.max.z / tile);
		Vector3 tileExtents = new Vector3(tile * 0.5f + 0.05f, Mathf.Max(1f, _queryBounds.extents.y), tile * 0.5f + 0.05f);
		for (int x = x0; x <= x1; x++)
		{
			for (int z = z0; z <= z1; z++)
			{
				Vector3 center = new Vector3((x + 0.5f) * tile, _queryBounds.center.y, (z + 0.5f) * tile);
				CollectTile(center, tileExtents, seen, _destination);
			}
		}

		m_ObstacleCollectArmed = false;
	}

	public static bool IsCharacterOrVehicle(Collider _collider)
	{
		if (_collider == null)
			return false;

		Transform t = _collider.transform;
		return t.GetComponentInParent<UnitConsciousness>() != null ||
		       t.GetComponentInParent<RtsUnitMember>() != null ||
		       t.GetComponentInParent<VehicleController>() != null;
	}
	#endregion

	#region Private Methods
	private void CollectTile(
		Vector3 _center,
		Vector3 _extents,
		HashSet<Collider> _seen,
		List<CoverGeometrySurface> _destination)
	{
		int hitCount = Physics.OverlapBoxNonAlloc(
			_center,
			_extents,
			m_Hits,
			Quaternion.identity,
			m_Mask,
			QueryTriggerInteraction.Ignore);

		if (hitCount >= m_Hits.Length && _extents.x > c_MinSplitExtents && _extents.z > c_MinSplitExtents)
		{
			Vector3 half = new Vector3(_extents.x * 0.5f, _extents.y, _extents.z * 0.5f);
			CollectTile(_center + new Vector3(-half.x, 0f, -half.z), half, _seen, _destination);
			CollectTile(_center + new Vector3(half.x, 0f, -half.z), half, _seen, _destination);
			CollectTile(_center + new Vector3(-half.x, 0f, half.z), half, _seen, _destination);
			CollectTile(_center + new Vector3(half.x, 0f, half.z), half, _seen, _destination);
			return;
		}

		int count = Mathf.Min(hitCount, m_Hits.Length);
		for (int i = 0; i < count; i++)
		{
			Collider collider = m_Hits[i];
			if (!IsWorldGeometry(collider) || !_seen.Add(collider))
				continue;
			if (m_ObstacleCollectArmed && TryTakeAsSmallObstacle(collider))
				continue;
			AppendColliderFaces(collider, _destination);
		}
	}

	private static bool IsWorldGeometry(Collider _collider)
	{
		if (_collider == null || !_collider.enabled || _collider.isTrigger)
			return false;
		if (TacticalTransparency.IsMarked(_collider))
			return false;
		return !IsCharacterOrVehicle(_collider);
	}

	private bool TryTakeAsSmallObstacle(Collider _collider)
	{
		if (!ProtectionZoneObstacle.TryFromCollider(_collider, m_ObstacleSettings, out CoverObstacleSilhouette silhouette))
			return false;
		m_Obstacles.Add(silhouette);
		return true;
	}

	private void AppendColliderFaces(Collider _collider, List<CoverGeometrySurface> _destination)
	{
		int firstAdded = _destination.Count;
		if (_collider is BoxCollider box)
		{
			AppendBoxFaces(box, _destination);
		}
		else if (_collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
		{
			if (!m_ObstacleCollectArmed || !TryAppendDetailedMeshFaces(meshCollider, _destination))
				AppendOrientedBoundsFaces(_collider.transform, meshCollider.sharedMesh.bounds, _destination);
		}
		else
			AppendOrientedBoundsFaces(_collider.transform, LocalColliderBounds(_collider), _destination);

		if (m_ObstacleCollectArmed)
			RemoveInternallyOccludedFaces(_collider, firstAdded, _destination);
	}

	private void RemoveInternallyOccludedFaces(
		Collider _source,
		int _firstAdded,
		List<CoverGeometrySurface> _destination)
	{
		for (int i = _destination.Count - 1; i >= _firstAdded; i--)
		{
			if (HasAnyExposedSample(_source, _destination[i]))
				continue;
			_destination.RemoveAt(i);
		}
	}

	private bool HasAnyExposedSample(Collider _source, CoverGeometrySurface _surface)
	{
		Vector3 normal = Flatten(_surface.Normal);
		Vector3 tangent = Flatten(_surface.Tangent);
		if (normal.sqrMagnitude < 0.01f || tangent.sqrMagnitude < 0.01f)
			return true;
		normal.Normalize();
		tangent.Normalize();
		float sampleY = Mathf.Clamp(
			_source.bounds.min.y + 0.9f,
			_source.bounds.min.y + 0.1f,
			_source.bounds.max.y - 0.1f);
		float along = _surface.Length * 0.32f;
		for (int i = -1; i <= 1; i++)
		{
			Vector3 point = _surface.Origin + tangent * (along * i) +
			                normal * c_ExposureProbeOffset;
			point.y = sampleY;
			if (!IsOccupiedByOtherGeometry(point, _source))
				return true;
		}

		return false;
	}

	private bool IsOccupiedByOtherGeometry(Vector3 _point, Collider _source)
	{
		int count = Physics.OverlapSphereNonAlloc(
			_point,
			c_ExposureProbeRadius,
			m_ExposureHits,
			m_Mask,
			QueryTriggerInteraction.Ignore);
		for (int i = 0; i < count; i++)
		{
			Collider hit = m_ExposureHits[i];
			if (hit == null || hit == _source || !IsWorldGeometry(hit))
				continue;
			return true;
		}

		return false;
	}

	private static bool TryAppendDetailedMeshFaces(
		MeshCollider _collider,
		List<CoverGeometrySurface> _destination)
	{
		Mesh mesh = _collider.sharedMesh;
		if (_collider.convex || mesh == null || !mesh.isReadable)
			return false;

		Vector3[] vertices;
		int[] triangles;
		try
		{
			vertices = mesh.vertices;
			triangles = mesh.triangles;
		}
		catch (UnityException)
		{
			return false;
		}

		if (triangles == null || triangles.Length / 3 < c_DetailedMeshTriangleThreshold)
			return false;

		int before = _destination.Count;
		Transform transform = _collider.transform;
		for (int i = 0; i + 2 < triangles.Length; i += 3)
		{
			int indexA = triangles[i];
			int indexB = triangles[i + 1];
			int indexC = triangles[i + 2];
			if (indexA < 0 || indexA >= vertices.Length ||
			    indexB < 0 || indexB >= vertices.Length ||
			    indexC < 0 || indexC >= vertices.Length)
				continue;

			Vector3 a = transform.TransformPoint(vertices[indexA]);
			Vector3 b = transform.TransformPoint(vertices[indexB]);
			Vector3 c = transform.TransformPoint(vertices[indexC]);
			Vector3 triangleNormal = Vector3.Cross(b - a, c - a);
			if (triangleNormal.sqrMagnitude < 0.0001f)
				continue;
			triangleNormal.Normalize();
			if (Mathf.Abs(triangleNormal.y) > c_MaxWallUpDot)
				continue;

			Vector3 planarA = Flatten(a);
			Vector3 planarB = Flatten(b);
			Vector3 planarC = Flatten(c);
			Vector3 start = planarA;
			Vector3 end = planarB;
			float longestSqr = (planarB - planarA).sqrMagnitude;
			float acSqr = (planarC - planarA).sqrMagnitude;
			if (acSqr > longestSqr)
			{
				longestSqr = acSqr;
				end = planarC;
			}

			float bcSqr = (planarC - planarB).sqrMagnitude;
			if (bcSqr > longestSqr)
			{
				longestSqr = bcSqr;
				start = planarB;
				end = planarC;
			}

			float height = Mathf.Max(a.y, b.y, c.y) - Mathf.Min(a.y, b.y, c.y);
			float length = Mathf.Sqrt(longestSqr);
			if (length < c_MinFaceLength || height < c_MinFaceHeight)
				continue;

			Vector3 planarNormal = Flatten(triangleNormal);
			Vector3 tangent = end - start;
			if (planarNormal.sqrMagnitude < 0.01f || tangent.sqrMagnitude < 0.01f)
				continue;
			planarNormal.Normalize();
			tangent.Normalize();
			if (Mathf.Abs(Vector3.Dot(planarNormal, tangent)) > 0.15f)
				continue;

			TryAppendWorldFace(
				(start + end) * 0.5f,
				planarNormal,
				tangent,
				length,
				height,
				_destination);
		}

		return _destination.Count > before;
	}

	private static Bounds LocalColliderBounds(Collider _collider)
	{
		if (_collider is SphereCollider sphere)
			return new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);
		if (_collider is CapsuleCollider capsule)
		{
			float radius = capsule.radius;
			float height = Mathf.Max(capsule.height, radius * 2f);
			Vector3 size = Vector3.one * radius * 2f;
			if (capsule.direction == 0)
				size.x = height;
			else if (capsule.direction == 1)
				size.y = height;
			else
				size.z = height;
			return new Bounds(capsule.center, size);
		}

		Transform t = _collider.transform;
		Vector3 localCenter = t.InverseTransformPoint(_collider.bounds.center);
		Vector3 localSize = t.InverseTransformVector(_collider.bounds.size);
		localSize.x = Mathf.Abs(localSize.x);
		localSize.y = Mathf.Abs(localSize.y);
		localSize.z = Mathf.Abs(localSize.z);
		return new Bounds(localCenter, localSize);
	}

	private static void AppendBoxFaces(BoxCollider _box, List<CoverGeometrySurface> _destination)
	{
		AppendOrientedBoundsFaces(_box.transform, new Bounds(_box.center, _box.size), _destination);
	}

	private static void AppendOrientedBoundsFaces(
		Transform _transform,
		Bounds _localBounds,
		List<CoverGeometrySurface> _destination)
	{
		Vector3 center = _localBounds.center;
		Vector3 size = _localBounds.size;
		TryAppendLocalFace(_transform, center, size, Vector3.right, Vector3.forward, _destination);
		TryAppendLocalFace(_transform, center, size, Vector3.left, Vector3.forward, _destination);
		TryAppendLocalFace(_transform, center, size, Vector3.forward, Vector3.right, _destination);
		TryAppendLocalFace(_transform, center, size, Vector3.back, Vector3.right, _destination);
	}

	private static void TryAppendLocalFace(
		Transform _transform,
		Vector3 _boxCenter,
		Vector3 _boxSize,
		Vector3 _localNormal,
		Vector3 _localTangent,
		List<CoverGeometrySurface> _destination)
	{
		Vector3 localOrigin = _boxCenter + Vector3.Scale(_localNormal, _boxSize) * 0.5f;
		Vector3 worldOrigin = _transform.TransformPoint(localOrigin);
		Vector3 worldNormal = _transform.TransformDirection(_localNormal).normalized;
		Vector3 worldTangent = _transform.TransformDirection(_localTangent);
		float length = _transform.TransformVector(Vector3.Scale(_localTangent, _boxSize)).magnitude;
		float height = _transform.TransformVector(Vector3.Scale(Vector3.up, _boxSize)).magnitude;
		TryAppendWorldFace(worldOrigin, worldNormal, worldTangent, length, height, _destination);
	}

	private static void TryAppendWorldFace(
		Vector3 _origin,
		Vector3 _normal,
		Vector3 _tangent,
		float _length,
		float _height,
		List<CoverGeometrySurface> _destination)
	{
		if (_length < c_MinFaceLength || _height < c_MinFaceHeight)
			return;
		if (_normal.sqrMagnitude < 0.01f)
			return;
		Vector3 normal = _normal.normalized;
		if (Mathf.Abs(normal.y) > 0.7f)
			return;

		Vector3 tangent = Vector3.ProjectOnPlane(_tangent, normal);
		if (tangent.sqrMagnitude < 0.01f)
			tangent = Vector3.Cross(Vector3.up, normal);
		if (tangent.sqrMagnitude < 0.01f)
			return;

		_destination.Add(new CoverGeometrySurface
		{
			Origin = _origin,
			Normal = normal,
			Tangent = tangent.normalized,
			Length = _length,
			Height = _height
		});
	}

	private static Vector3 Flatten(Vector3 _value)
	{
		_value.y = 0f;
		return _value;
	}
	#endregion
}

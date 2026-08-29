using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static world colliders in the queried region. Skips characters, vehicles, triggers.
/// Prototype extractor: box faces + AABB sides for other colliders. Not one collider → one candidate.
/// </summary>
public sealed class PhysicsCoverGeometrySource : ICoverGeometrySource
{
	#region Constants
	private const int c_HitCapacity = 128;
	private const float c_MinFaceLength = 0.8f;
	private const float c_MinFaceHeight = 0.8f;
	#endregion

	#region Private Fields
	private readonly Collider[] m_Hits = new Collider[c_HitCapacity];
	private LayerMask m_Mask = ~0;
	#endregion

	#region Public Properties
	public int QueryCount { get; private set; }
	public CoverRegionId LastRegion { get; private set; }
	public Bounds LastBounds { get; private set; }
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

	public void Collect(CoverRegionId _region, Bounds _queryBounds, List<CoverGeometrySurface> _destination)
	{
		QueryCount++;
		LastRegion = _region;
		LastBounds = _queryBounds;
		if (_destination == null)
			return;

		Physics.SyncTransforms();
		int hitCount = Physics.OverlapBoxNonAlloc(
			_queryBounds.center,
			_queryBounds.extents,
			m_Hits,
			Quaternion.identity,
			m_Mask,
			QueryTriggerInteraction.Ignore);

		for (int i = 0; i < hitCount; i++)
		{
			Collider collider = m_Hits[i];
			if (!IsWorldGeometry(collider))
				continue;

			if (collider is BoxCollider box)
				AppendBoxFaces(box, _destination);
			else
				AppendBoundsFaces(collider.bounds, _destination);
		}
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
	private static bool IsWorldGeometry(Collider _collider)
	{
		if (_collider == null || !_collider.enabled || _collider.isTrigger)
			return false;
		return !IsCharacterOrVehicle(_collider);
	}

	private static void AppendBoxFaces(BoxCollider _box, List<CoverGeometrySurface> _destination)
	{
		Transform t = _box.transform;
		Vector3 center = _box.center;
		Vector3 size = _box.size;
		TryAppendLocalFace(t, center, size, Vector3.right, Vector3.forward, _destination);
		TryAppendLocalFace(t, center, size, Vector3.left, Vector3.forward, _destination);
		TryAppendLocalFace(t, center, size, Vector3.forward, Vector3.right, _destination);
		TryAppendLocalFace(t, center, size, Vector3.back, Vector3.right, _destination);
	}

	private static void AppendBoundsFaces(Bounds _bounds, List<CoverGeometrySurface> _destination)
	{
		if (_bounds.size.y < c_MinFaceHeight)
			return;

		Vector3 c = _bounds.center;
		Vector3 e = _bounds.extents;
		TryAppendWorldFace(
			new Vector3(c.x + e.x, c.y, c.z),
			Vector3.right,
			Vector3.forward,
			_bounds.size.z,
			_bounds.size.y,
			_destination);
		TryAppendWorldFace(
			new Vector3(c.x - e.x, c.y, c.z),
			Vector3.left,
			Vector3.forward,
			_bounds.size.z,
			_bounds.size.y,
			_destination);
		TryAppendWorldFace(
			new Vector3(c.x, c.y, c.z + e.z),
			Vector3.forward,
			Vector3.right,
			_bounds.size.x,
			_bounds.size.y,
			_destination);
		TryAppendWorldFace(
			new Vector3(c.x, c.y, c.z - e.z),
			Vector3.back,
			Vector3.right,
			_bounds.size.x,
			_bounds.size.y,
			_destination);
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
			Length = _length
		});
	}
	#endregion
}

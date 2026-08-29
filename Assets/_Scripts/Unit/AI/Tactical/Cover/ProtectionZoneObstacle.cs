using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #13.2C.5 / #13.2C.10: closed ~1–2.5 m props become one Obstacle zone.
/// Collider silhouette first (narrow jersey). Face-cluster remains for injected surfaces.
/// </summary>
public static class ProtectionZoneObstacle
{
	#region Constants
	private const float c_MinThicknessMeters = 0.35f;
	#endregion

	#region Public Methods
	public static bool TryFromCollider(
		Collider _collider,
		CoverGenerationSettings _settings,
		out CoverObstacleSilhouette _silhouette)
	{
		_silhouette = default;
		if (_collider == null)
			return false;
		if (!TryLocalBounds(_collider, out Bounds local))
			return false;
		return TryFromLocalBox(_collider.transform, local, _settings, out _silhouette);
	}

	public static void EmitSilhouettes(
		IReadOnlyList<CoverObstacleSilhouette> _silhouettes,
		CoverGenerationSettings _settings,
		List<ProtectionZone> _destination)
	{
		if (_silhouettes == null || _destination == null)
			return;
		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		for (int i = 0; i < _silhouettes.Count; i++)
			_destination.Add(ToZone(_silhouettes[i], settings));
	}

	public static void Extract(
		IReadOnlyList<CoverGeometrySurface> _surfaces,
		CoverGenerationSettings _settings,
		List<ProtectionZone> _destination,
		bool[] _consumed)
	{
		if (_surfaces == null || _destination == null || _consumed == null)
			return;

		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		float maxSize = Mathf.Max(0.5f, settings.MaxSmallObstacleMeters);
		float minSize = Mathf.Max(0.2f, settings.MinSmallObstacleMeters);

		for (int i = 0; i < _surfaces.Count; i++)
		{
			if (_consumed[i] || _surfaces[i].Length > maxSize)
				continue;

			var group = new List<int>(4) { i };
			Vector3 seed = Flatten(_surfaces[i].Origin);
			for (int j = 0; j < _surfaces.Count; j++)
			{
				if (j == i || _consumed[j] || _surfaces[j].Length > maxSize)
					continue;
				if (CoverSpatialMath.PlanarDistanceSqr(seed, Flatten(_surfaces[j].Origin)) > maxSize * maxSize)
					continue;
				group.Add(j);
			}

			if (group.Count < 3)
				continue;
			if (!TryOrientedBounds(
				    group,
				    _surfaces,
				    out Vector3 center,
				    out Vector3 axis,
				    out Vector3 extents))
				continue;
			float footprint = Mathf.Max(extents.x, extents.z);
			if (footprint > maxSize || footprint < minSize * 0.5f)
				continue;
			if (CountDistinctNormals(group, _surfaces) < 3)
				continue;

			for (int g = 0; g < group.Count; g++)
				_consumed[group[g]] = true;

			_destination.Add(ToZone(new CoverObstacleSilhouette
			{
				Center = center,
				Axis = axis,
				Extents = extents
			}, settings));
		}
	}
	#endregion

	#region Private Methods
	private static bool TryFromLocalBox(
		Transform _transform,
		Bounds _localBounds,
		CoverGenerationSettings _settings,
		out CoverObstacleSilhouette _silhouette)
	{
		_silhouette = default;
		if (_transform == null)
			return false;
		CoverGenerationSettings settings = _settings ?? new CoverGenerationSettings();
		Vector3 size = _localBounds.size;
		Vector3 worldX = _transform.TransformVector(new Vector3(size.x, 0f, 0f));
		Vector3 worldZ = _transform.TransformVector(new Vector3(0f, 0f, size.z));
		worldX.y = 0f;
		worldZ.y = 0f;
		float lengthX = worldX.magnitude;
		float lengthZ = worldZ.magnitude;
		float height = _transform.TransformVector(new Vector3(0f, size.y, 0f)).magnitude;
		Vector3 axis;
		float length;
		float thickness;
		if (lengthZ >= lengthX)
		{
			axis = lengthZ > 0.01f ? worldZ / lengthZ : Vector3.forward;
			length = lengthZ;
			thickness = lengthX;
		}
		else
		{
			axis = lengthX > 0.01f ? worldX / lengthX : Vector3.right;
			length = lengthX;
			thickness = lengthZ;
		}

		if (axis.sqrMagnitude < 0.5f)
			return false;
		if (!FitsSmallObstacle(thickness, height, length, settings))
			return false;

		Vector3 center = _transform.TransformPoint(_localBounds.center);
		center.y = 0f;
		_silhouette = new CoverObstacleSilhouette
		{
			Center = center,
			Axis = axis,
			Extents = new Vector3(thickness, height, length)
		};
		return true;
	}

	private static bool FitsSmallObstacle(
		float _thickness,
		float _height,
		float _length,
		CoverGenerationSettings _settings)
	{
		float maxSize = Mathf.Max(0.5f, _settings.MaxSmallObstacleMeters);
		float minSize = Mathf.Max(0.2f, _settings.MinSmallObstacleMeters);
		float minHeight = Mathf.Max(0.2f, _settings.MinSmallObstacleHeightMeters);
		float maxHeight = Mathf.Max(minHeight, _settings.MaxSmallObstacleHeightMeters);
		float planarMax = Mathf.Max(_thickness, _length);
		float planarMin = Mathf.Min(_thickness, _length);
		if (planarMax > maxSize || planarMax < minSize * 0.5f)
			return false;
		if (planarMin < c_MinThicknessMeters)
			return false;
		if (_height < minHeight || _height > maxHeight)
			return false;
		return true;
	}

	private static bool TryLocalBounds(Collider _collider, out Bounds _local)
	{
		_local = default;
		if (_collider is BoxCollider box)
		{
			_local = new Bounds(box.center, box.size);
			return true;
		}

		if (_collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
		{
			_local = meshCollider.sharedMesh.bounds;
			return true;
		}

		if (_collider is SphereCollider sphere)
		{
			_local = new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);
			return true;
		}

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
			_local = new Bounds(capsule.center, size);
			return true;
		}

		return false;
	}

	private static ProtectionZone ToZone(CoverObstacleSilhouette _silhouette, CoverGenerationSettings _settings)
	{
		Vector3 extents = _silhouette.Extents;
		float height = Mathf.Max(_settings.MinSmallObstacleHeightMeters, extents.y);
		return new ProtectionZone
		{
			GeometryType = ProtectionZoneType.Obstacle,
			Center = Flatten(_silhouette.Center),
			Axis = PlanarUnit(_silhouette.Axis).sqrMagnitude > 0.5f
				? PlanarUnit(_silhouette.Axis)
				: Vector3.forward,
			Width = extents.z,
			Depth = Mathf.Max(_settings.ZoneDepthMeters, extents.x * 0.5f),
			ProtectionHeight = height,
			SurfaceNormal = Vector3.zero,
			Capabilities = ProtectionCapabilities.CanPeek,
			ObstacleExtents = extents,
			OpeningCenter = Flatten(_silhouette.Center)
		};
	}

	private static bool TryOrientedBounds(
		List<int> _group,
		IReadOnlyList<CoverGeometrySurface> _surfaces,
		out Vector3 _center,
		out Vector3 _axis,
		out Vector3 _extents)
	{
		_center = Vector3.zero;
		_axis = Vector3.right;
		_extents = Vector3.zero;
		float bestLength = -1f;
		Vector3 axis = Vector3.right;
		float height = 0f;
		for (int g = 0; g < _group.Count; g++)
		{
			CoverGeometrySurface surface = _surfaces[_group[g]];
			height = Mathf.Max(height, surface.Height);
			Vector3 tangent = PlanarUnit(surface.Tangent);
			if (tangent.sqrMagnitude < 0.5f)
				continue;
			if (surface.Length <= bestLength)
				continue;
			bestLength = surface.Length;
			axis = tangent;
		}

		if (bestLength < 0.2f)
			return false;

		Vector3 side = Vector3.Cross(Vector3.up, axis);
		if (side.sqrMagnitude < 0.01f)
			return false;
		side.Normalize();

		float minA = float.MaxValue;
		float maxA = float.MinValue;
		float minS = float.MaxValue;
		float maxS = float.MinValue;
		for (int g = 0; g < _group.Count; g++)
		{
			CoverGeometrySurface surface = _surfaces[_group[g]];
			if (!surface.TryGetPlanarEnds(out Vector3 start, out Vector3 end))
				return false;
			minA = Mathf.Min(minA, Vector3.Dot(start, axis), Vector3.Dot(end, axis));
			maxA = Mathf.Max(maxA, Vector3.Dot(start, axis), Vector3.Dot(end, axis));
			minS = Mathf.Min(minS, Vector3.Dot(start, side), Vector3.Dot(end, side));
			maxS = Mathf.Max(maxS, Vector3.Dot(start, side), Vector3.Dot(end, side));
		}

		float length = maxA - minA;
		float thickness = maxS - minS;
		if (length < 0.2f || thickness < 0.2f)
			return false;

		_axis = axis;
		_center = axis * ((minA + maxA) * 0.5f) + side * ((minS + maxS) * 0.5f);
		_center.y = 0f;
		_extents = new Vector3(thickness, Mathf.Max(0.8f, height), length);
		return true;
	}

	private static int CountDistinctNormals(
		List<int> _group,
		IReadOnlyList<CoverGeometrySurface> _surfaces)
	{
		int count = 0;
		var normals = new Vector3[8];
		for (int g = 0; g < _group.Count; g++)
		{
			Vector3 n = PlanarUnit(_surfaces[_group[g]].Normal);
			if (n.sqrMagnitude < 0.5f)
				continue;
			bool unique = true;
			for (int k = 0; k < count; k++)
			{
				if (Vector3.Dot(n, normals[k]) > 0.85f)
				{
					unique = false;
					break;
				}
			}

			if (!unique)
				continue;
			if (count >= normals.Length)
				break;
			normals[count] = n;
			count++;
		}

		return count;
	}

	private static Vector3 Flatten(Vector3 _value)
	{
		_value.y = 0f;
		return _value;
	}

	private static Vector3 PlanarUnit(Vector3 _value)
	{
		Vector3 v = _value;
		v.y = 0f;
		return v.sqrMagnitude < 0.01f ? Vector3.zero : v.normalized;
	}
	#endregion
}

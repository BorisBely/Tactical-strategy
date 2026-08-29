using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

/// <summary>
/// #13.2C: logical surfaces → compact protection zones. Not stance points. Not unit occupy.
/// </summary>
public sealed class ProtectionZoneGenerator : IProtectionZoneSource
{
	#region Nested
	private struct Span
	{
		public float Min;
		public float Max;
	}
	#endregion

	#region Private Fields
	private readonly ICoverGeometrySource m_Geometry;
	private readonly ICoverNavMeshProbe m_NavMesh;
	private readonly ICoverClearanceProbe m_Clearance;
	private readonly ICoverOcclusionProbe m_Occlusion;
	private readonly CoverGenerationSettings m_Settings;
	private readonly CoverClassificationSettings m_ClassSettings;
	private readonly ICoverWindowProbe m_Window;
	private readonly List<CoverGeometrySurface> m_Surfaces = new List<CoverGeometrySurface>(64);
	private readonly List<CoverOpeningSeed> m_Openings = new List<CoverOpeningSeed>(16);
	private readonly List<CoverOpeningSeed> m_PhysicalOpenings = new List<CoverOpeningSeed>(16);
	private readonly List<CoverCornerSeed> m_Corners = new List<CoverCornerSeed>(16);
	private readonly List<CoverBoundarySide> m_BoundarySides = new List<CoverBoundarySide>(32);
	private readonly List<CoverBoundarySeed> m_PhysicalBoundaries = new List<CoverBoundarySeed>(16);
	private readonly List<CoverObstacleSilhouette> m_Silhouettes = new List<CoverObstacleSilhouette>(32);
	private PhysicsCoverSeamProbe m_PhysicsSeam;
	#endregion

	#region Public Properties
	public double LastGenerationMilliseconds { get; private set; }
	public int LastZoneCount { get; private set; }
	#endregion

	#region Public Constructors
	public ProtectionZoneGenerator(
		ICoverGeometrySource _geometry,
		ICoverNavMeshProbe _navMesh,
		ICoverClearanceProbe _clearance,
		CoverGenerationSettings _settings = null,
		ICoverOcclusionProbe _occlusion = null,
		CoverClassificationSettings _classSettings = null,
		ICoverWindowProbe _window = null)
	{
		m_Geometry = _geometry;
		m_NavMesh = _navMesh;
		m_Clearance = _clearance;
		m_Occlusion = _occlusion;
		m_Settings = _settings ?? new CoverGenerationSettings();
		m_ClassSettings = _classSettings ?? new CoverClassificationSettings();
		m_Window = _window;
	}
	#endregion

	#region Public Methods
	public void Generate(Bounds _worldBounds, int _geometryVersion, List<ProtectionZone> _destination)
	{
		Stopwatch watch = Stopwatch.StartNew();
		m_Surfaces.Clear();
		m_Openings.Clear();
		m_PhysicalOpenings.Clear();
		m_Corners.Clear();
		m_BoundarySides.Clear();
		m_PhysicalBoundaries.Clear();
		m_Silhouettes.Clear();

		Bounds query = CoverSpatialMath.ExpandHorizontally(_worldBounds, m_Settings.GeometryMarginMeters);
		var silhouetteSource = m_Geometry as ICoverObstacleSilhouetteSource;
		if (silhouetteSource != null)
			silhouetteSource.BeginObstacleCollect(m_Settings);
		if (m_Geometry != null)
			m_Geometry.Collect(CoverSpatialMath.WorldToRegion(_worldBounds.center, CoverSpatialMath.DefaultRegionSizeMeters), query, m_Surfaces);
		if (silhouetteSource != null)
		{
			IReadOnlyList<CoverObstacleSilhouette> collected = silhouetteSource.LastObstacles;
			if (collected != null)
			{
				for (int i = 0; i < collected.Count; i++)
					m_Silhouettes.Add(collected[i]);
			}
		}

		CoverSurfaceMerge.Rebuild(m_Surfaces, m_Settings, ResolveSeamProbe());
		BuildZones(_worldBounds, _geometryVersion, _destination);

		watch.Stop();
		LastGenerationMilliseconds = watch.Elapsed.TotalMilliseconds;
		LastZoneCount = _destination != null ? _destination.Count : 0;
	}
	#endregion

	#region Private Methods
	private void BuildZones(Bounds _worldBounds, int _geometryVersion, List<ProtectionZone> _destination)
	{
		if (_destination == null)
			return;

		var consumed = new bool[m_Surfaces.Count];
		ProtectionZoneObstacle.EmitSilhouettes(m_Silhouettes, m_Settings, _destination);
		if (!(m_Geometry is ICoverObstacleSilhouetteSource))
			ProtectionZoneObstacle.Extract(m_Surfaces, m_Settings, _destination, consumed);

		var remaining = new List<CoverGeometrySurface>(m_Surfaces.Count);
		for (int i = 0; i < m_Surfaces.Count; i++)
		{
			if (consumed[i])
				continue;
			remaining.Add(m_Surfaces[i]);
		}

		CoverOpeningGeometry.Collect(remaining, m_Settings, m_Openings);
		CoverOpeningGeometry.CollapsePhysical(m_Openings, m_Settings, m_PhysicalOpenings);
		CoverCornerGeometry.CollectProtected(remaining, m_Settings, m_Corners);
		ConfirmProtectedCorners();
		EmitWallsAndEdges(remaining, _destination);
		EmitOpeningJambs(_destination);
		EmitOpenings(_destination);
		EmitCorners(_destination);

		for (int i = _destination.Count - 1; i >= 0; i--)
		{
			ProtectionZone zone = _destination[i];
			if (!CoverSpatialMath.ContainsPlanar(_worldBounds, zone.Center))
			{
				_destination.RemoveAt(i);
				continue;
			}

			bool navMeshValid = IsWalkable(zone);
			FillProtection(zone);
			zone.NavMeshValid = navMeshValid;
			zone.GeometryVersion = _geometryVersion;
			zone.RegionId = CoverSpatialMath.WorldToRegion(zone.Center, CoverSpatialMath.DefaultRegionSizeMeters);
		}

		ProtectionZoneDedup.Apply(_destination, m_Settings);
		_destination.Sort(ProtectionZoneDedup.Compare);
		for (int i = 0; i < _destination.Count; i++)
			_destination[i].ZoneId = i + 1;
	}

	private void EmitWallsAndEdges(
		List<CoverGeometrySurface> _surfaces,
		List<ProtectionZone> _destination)
	{
		m_BoundarySides.Clear();
		m_PhysicalBoundaries.Clear();
		float minWidth = Mathf.Max(0.05f, m_Settings.MinZoneWidthMeters);
		float depth = Mathf.Max(0.05f, m_Settings.ZoneDepthMeters);
		var spans = new List<Span>(4);
		for (int i = 0; i < _surfaces.Count; i++)
		{
			CoverGeometrySurface surface = _surfaces[i];
			if (!surface.TryGetPlanarEnds(out Vector3 start, out Vector3 end))
				continue;
			Vector3 normal = PlanarUnit(surface.Normal);
			Vector3 axis = PlanarUnit(surface.Tangent);
			if (normal.sqrMagnitude < 0.5f)
				continue;
			if (axis.sqrMagnitude < 0.5f)
				axis = Vector3.Cross(Vector3.up, normal);
			if (axis.sqrMagnitude < 0.5f)
				continue;
			axis.Normalize();

			float sMin = Vector3.Dot(start, axis);
			float sMax = Vector3.Dot(end, axis);
			if (sMin > sMax)
			{
				float swap = sMin;
				sMin = sMax;
				sMax = swap;
			}

			spans.Clear();
			spans.Add(new Span { Min = sMin, Max = sMax });
			SubtractOpenings(spans, surface, axis, normal);

			for (int s = 0; s < spans.Count; s++)
			{
				float width = spans[s].Max - spans[s].Min;
				if (width < minWidth)
					continue;
				float mid = 0.5f * (spans[s].Min + spans[s].Max);
				float plane = 0.5f * (Vector3.Dot(start, normal) + Vector3.Dot(end, normal));
				Vector3 origin = axis * mid + normal * plane;
				origin.y = 0f;
				_destination.Add(new ProtectionZone
				{
					GeometryType = ProtectionZoneType.Wall,
					Center = origin,
					Axis = axis,
					Width = width,
					Depth = depth,
					ProtectionHeight = surface.Height > 0.05f ? surface.Height : 2f,
					SurfaceNormal = normal
				});

				if (spans[s].Min <= sMin + 0.05f)
				{
					Vector3 boundary = origin - axis * (width * 0.5f);
					if (!IsInternalCollinearBoundary(boundary, i, _surfaces) &&
					    !IsCornerJunction(boundary))
					{
						AddBoundarySide(
							boundary,
							axis,
							normal,
							-axis,
							Mathf.Min(m_Settings.EdgeInsetMeters * 2f, width),
							surface.Height);
					}
				}

				if (spans[s].Max >= sMax - 0.05f)
				{
					Vector3 boundary = origin + axis * (width * 0.5f);
					if (!IsInternalCollinearBoundary(boundary, i, _surfaces) &&
					    !IsCornerJunction(boundary))
					{
						AddBoundarySide(
							boundary,
							axis,
							normal,
							axis,
							Mathf.Min(m_Settings.EdgeInsetMeters * 2f, width),
							surface.Height);
					}
				}
			}
		}

		CoverBoundaryGeometry.CollapseWallEnds(m_BoundarySides, m_Settings, m_PhysicalBoundaries);
		for (int i = 0; i < m_PhysicalBoundaries.Count; i++)
			EmitWallEnd(m_PhysicalBoundaries[i], _destination);
	}

	private bool IsCornerJunction(Vector3 _point)
	{
		float radius = Mathf.Max(0.1f, m_Settings.MaxCornerVertexSeparationMeters);
		float radiusSqr = radius * radius;
		for (int i = 0; i < m_Corners.Count; i++)
		{
			if (CoverSpatialMath.PlanarDistanceSqr(_point, m_Corners[i].Vertex) <= radiusSqr)
				return true;
		}

		return false;
	}

	private bool IsInternalCollinearBoundary(
		Vector3 _point,
		int _surfaceIndex,
		IReadOnlyList<CoverGeometrySurface> _surfaces)
	{
		CoverGeometrySurface source = _surfaces[_surfaceIndex];
		Vector3 sourceNormal = PlanarUnit(source.Normal);
		if (!source.TryGetPlanarEnds(out Vector3 sourceStart, out Vector3 sourceEnd))
			return false;
		Vector3 sourceFar = FarDirection(_point, sourceStart, sourceEnd);
		float align = Mathf.Clamp(m_Settings.MergeNormalAlignDot, 0.5f, 0.99f);
		float maxPlane = Mathf.Max(0.05f, m_Settings.MergePlaneOffsetMeters);
		float endpointSlack = Mathf.Max(0.05f, m_Settings.MergeSeamGapMeters);
		float junctionSlack = Mathf.Min(0.25f, endpointSlack);
		float interiorSlack = 0.05f;
		for (int i = 0; i < _surfaces.Count; i++)
		{
			if (i == _surfaceIndex)
				continue;
			CoverGeometrySurface other = _surfaces[i];
			Vector3 otherNormal = PlanarUnit(other.Normal);
			if (sourceNormal.sqrMagnitude < 0.5f || otherNormal.sqrMagnitude < 0.5f)
				continue;
			if (!other.TryGetPlanarEnds(out Vector3 start, out Vector3 end))
				continue;

			Vector3 axis = PlanarUnit(end - start);
			if (axis.sqrMagnitude < 0.5f)
				continue;
			float length = Mathf.Sqrt(CoverSpatialMath.PlanarDistanceSqr(start, end));
			float along = Vector3.Dot(_point - start, axis);
			if (along < -endpointSlack || along > length + endpointSlack)
				continue;
			Vector3 nearest = start + axis * Mathf.Clamp(along, 0f, length);
			float distanceSqr = CoverSpatialMath.PlanarDistanceSqr(_point, nearest);
			float normalDot = Vector3.Dot(sourceNormal, otherNormal);
			if (normalDot < align)
			{
				if (Mathf.Abs(normalDot) < align &&
				    along > interiorSlack && along < length - interiorSlack &&
				    distanceSqr <= junctionSlack * junctionSlack)
					return true;
				continue;
			}

			float planeDistance = Mathf.Abs(Vector3.Dot(_point - other.Origin, otherNormal));
			if (planeDistance > maxPlane || distanceSqr > endpointSlack * endpointSlack)
				continue;
			if (along > interiorSlack && along < length - interiorSlack)
				return true;

			Vector3 otherFar = FarDirection(_point, start, end);
			float directionDot = Vector3.Dot(sourceFar, otherFar);
			if (directionDot < -align)
				return true;
			if (directionDot > align && i < _surfaceIndex)
				return true;
		}

		return false;
	}

	private static Vector3 FarDirection(Vector3 _point, Vector3 _start, Vector3 _end)
	{
		Vector3 far = CoverSpatialMath.PlanarDistanceSqr(_point, _start) <=
		              CoverSpatialMath.PlanarDistanceSqr(_point, _end)
			? _end - _point
			: _start - _point;
		return PlanarUnit(far);
	}

	private void AddBoundarySide(
		Vector3 _center,
		Vector3 _surfaceAxis,
		Vector3 _surfaceNormal,
		Vector3 _outward,
		float _range,
		float _height)
	{
		Vector3 center = _center;
		center.y = 0f;
		m_BoundarySides.Add(new CoverBoundarySide
		{
			Center = center,
			SurfaceAxis = PlanarUnit(_surfaceAxis),
			SurfaceNormal = PlanarUnit(_surfaceNormal),
			Outward = PlanarUnit(_outward),
			Range = Mathf.Max(0.35f, _range),
			Height = _height > 0.05f ? _height : 2f
		});
	}

	private static void EmitWallEnd(
		in CoverBoundarySeed _seed,
		List<ProtectionZone> _destination)
	{
		_destination.Add(new ProtectionZone
		{
			GeometryType = ProtectionZoneType.Edge,
			Center = _seed.Center,
			Axis = PlanarUnit(_seed.Axis),
			Width = Mathf.Max(0.1f, _seed.Width),
			Depth = Mathf.Max(0.1f, _seed.Depth),
			ProtectionHeight = _seed.Height > 0.05f ? _seed.Height : 2f,
			SurfaceNormal = PlanarUnit(_seed.Outward),
			Capabilities = ProtectionCapabilities.CanPeek,
			EdgeDirection = PlanarUnit(_seed.Outward),
			EdgeKind = _seed.Kind
		});
	}

	private static void EmitBoundary(
		Vector3 _center,
		Vector3 _axis,
		Vector3 _normal,
		Vector3 _direction,
		float _range,
		float _depth,
		float _height,
		ProtectionEdgeKind _kind,
		List<ProtectionZone> _destination)
	{
		Vector3 center = _center;
		center.y = 0f;
		_destination.Add(new ProtectionZone
		{
			GeometryType = ProtectionZoneType.Edge,
			Center = center,
			Axis = _axis,
			Width = Mathf.Max(0.35f, _range),
			Depth = _depth,
			ProtectionHeight = _height > 0.05f ? _height : 2f,
			SurfaceNormal = _normal,
			Capabilities = ProtectionCapabilities.CanPeek,
			EdgeDirection = _direction,
			EdgeKind = _kind
		});
	}

	private void EmitOpeningJambs(List<ProtectionZone> _destination)
	{
		float depth = Mathf.Max(0.05f, m_Settings.ZoneDepthMeters);
		for (int i = 0; i < m_PhysicalOpenings.Count; i++)
		{
			CoverOpeningSeed seed = m_PhysicalOpenings[i];
			Vector3 axis = PlanarUnit(seed.Axis);
			if (axis.sqrMagnitude < 0.5f)
				continue;
			Vector3 normal = PlanarUnit(seed.Normal);
			float range = Mathf.Min(m_Settings.EdgeInsetMeters * 2f, seed.Width);
			float half = seed.Width * 0.5f;
			EmitBoundary(
				seed.Center - axis * half,
				axis,
				normal,
				-axis,
				range,
				depth,
				2f,
				ProtectionEdgeKind.OpeningJamb,
				_destination);
			EmitBoundary(
				seed.Center + axis * half,
				axis,
				normal,
				axis,
				range,
				depth,
				2f,
				ProtectionEdgeKind.OpeningJamb,
				_destination);
		}
	}

	private void SubtractOpenings(
		List<Span> _spans,
		CoverGeometrySurface _surface,
		Vector3 _axis,
		Vector3 _normal)
	{
		float align = m_Settings.OpeningNormalAlignDot;
		float maxPlane = m_Settings.MaxOpeningPlaneOffsetMeters;
		float pad = 0.05f;
		if (!_surface.TryGetPlanarEnds(out Vector3 start, out Vector3 end))
			return;
		float plane = 0.5f * (Vector3.Dot(start, _normal) + Vector3.Dot(end, _normal));
		for (int i = 0; i < m_Openings.Count; i++)
		{
			CoverOpeningSeed seed = m_Openings[i];
			if (Vector3.Dot(PlanarUnit(seed.Normal), _normal) < align)
				continue;
			float seedPlane = Vector3.Dot(seed.Center, _normal);
			if (Mathf.Abs(seedPlane - plane) > maxPlane)
				continue;
			float mid = Vector3.Dot(seed.Center, _axis);
			float half = seed.Width * 0.5f + pad;
			Cut(_spans, mid - half, mid + half);
		}
	}

	private static void Cut(List<Span> _spans, float _cutMin, float _cutMax)
	{
		var next = new List<Span>(_spans.Count + 1);
		for (int i = 0; i < _spans.Count; i++)
		{
			Span span = _spans[i];
			if (_cutMax <= span.Min || _cutMin >= span.Max)
			{
				next.Add(span);
				continue;
			}

			if (_cutMin > span.Min + 0.05f)
				next.Add(new Span { Min = span.Min, Max = _cutMin });
			if (_cutMax < span.Max - 0.05f)
				next.Add(new Span { Max = span.Max, Min = _cutMax });
		}

		_spans.Clear();
		_spans.AddRange(next);
	}

	private void EmitOpenings(List<ProtectionZone> _destination)
	{
		float depth = Mathf.Max(0.05f, m_Settings.ZoneDepthMeters);
		ICoverWindowProbe window = ResolveWindowProbe();
		for (int i = 0; i < m_PhysicalOpenings.Count; i++)
		{
			CoverOpeningSeed seed = m_PhysicalOpenings[i];
			var zone = new ProtectionZone
			{
				GeometryType = ProtectionZoneType.Opening,
				Center = seed.Center,
				Axis = PlanarUnit(seed.Axis),
				Width = seed.Width,
				Depth = depth,
				ProtectionHeight = 2f,
				SurfaceNormal = PlanarUnit(seed.Normal),
				Capabilities = ProtectionCapabilities.CanStepLeft |
				               ProtectionCapabilities.CanStepRight |
				               ProtectionCapabilities.CanPeek,
				OpeningCenter = seed.Center,
				OpeningAxis = PlanarUnit(seed.Axis),
				OpeningWidth = seed.Width,
				LeftOffset = seed.LeftOffset,
				RightOffset = seed.RightOffset
			};

			if (window != null && TryInspectWindow(zone, window, out CoverWindowHit hit))
			{
				zone.GeometryType = ProtectionZoneType.Window;
				zone.HasTransparentPane = hit.HasTransparentPane;
				zone.HasFrame = hit.HasFrame;
				zone.WindowCenter = hit.Center;
				zone.WindowAxis = hit.Axis.sqrMagnitude > 0.01f ? hit.Axis : zone.OpeningAxis;
				zone.WindowWidth = hit.Width > 0.05f ? hit.Width : zone.OpeningWidth;
				zone.Capabilities |= ProtectionCapabilities.CanFireThrough | ProtectionCapabilities.CanObserveThrough;
			}

			_destination.Add(zone);
		}
	}

	private void EmitCorners(List<ProtectionZone> _destination)
	{
		float minRadius = Mathf.Max(0.05f, m_Settings.CornerPocketMinRadiusMeters);
		float maxRadius = Mathf.Max(minRadius, m_Settings.CornerPocketMaxRadiusMeters);
		for (int i = 0; i < m_Corners.Count; i++)
		{
			CoverCornerSeed seed = m_Corners[i];
			float halfAngle = Mathf.Clamp(
				Vector3.Angle(seed.NormalA, seed.NormalB) * 0.5f,
				10f,
				80f);
			_destination.Add(new ProtectionZone
			{
				GeometryType = ProtectionZoneType.Corner,
				Center = seed.Vertex,
				Axis = PlanarUnit(seed.Facing),
				Width = maxRadius * 2f,
				Depth = maxRadius - minRadius,
				ProtectionHeight = seed.Height > 0.05f ? seed.Height : 2f,
				SurfaceNormal = PlanarUnit(seed.Facing),
				Capabilities = ProtectionCapabilities.CanPeek,
				CornerFacing = seed.Facing,
				CornerNormalA = seed.NormalA,
				CornerNormalB = seed.NormalB,
				CornerDirectionA = seed.DirectionA,
				CornerDirectionB = seed.DirectionB,
				CornerVertex = seed.Vertex,
				CornerMinRadius = minRadius,
				CornerMaxRadius = maxRadius,
				CornerHalfAngleDegrees = halfAngle,
				CornerOrientation = seed.Orientation
			});
		}
	}

	private void ConfirmProtectedCorners()
	{
		if (m_Occlusion == null)
			return;

		for (int i = m_Corners.Count - 1; i >= 0; i--)
		{
			if (!HasProtectionInBothDirections(m_Corners[i]))
				m_Corners.RemoveAt(i);
		}
	}

	private bool HasProtectionInBothDirections(in CoverCornerSeed _seed)
	{
		Vector3 point = _seed.Position + Vector3.up;
		float distance = Mathf.Max(
			m_Settings.ProtectedCornerProbeDistanceMeters,
			m_Settings.StandOffMeters * 1.41421356f + 0.2f);
		Vector3 normalA = PlanarUnit(_seed.NormalA);
		Vector3 normalB = PlanarUnit(_seed.NormalB);
		if (normalA.sqrMagnitude < 0.5f || normalB.sqrMagnitude < 0.5f)
			return false;
		return m_Occlusion.IsBlocked(point, point - normalA * distance) &&
		       m_Occlusion.IsBlocked(point, point - normalB * distance);
	}

	private bool IsWalkable(ProtectionZone _zone)
	{
		if (_zone.GeometryType == ProtectionZoneType.Obstacle)
			return IsObstacleWalkable(_zone);
		if (_zone.GeometryType == ProtectionZoneType.Corner)
			return TryAcceptStand(ProbePoint(_zone), _zone.CornerFacing);

		int samples = Mathf.Max(1, m_Settings.ZoneWalkSamples);
		Vector3 axis = PlanarUnit(_zone.Axis);
		if (axis.sqrMagnitude < 0.5f)
			axis = Vector3.right;
		Vector3 probe = ProbePoint(_zone);
		for (int i = 0; i < samples; i++)
		{
			float t = samples == 1 ? 0.5f : (i + 0.5f) / samples;
			Vector3 pos = probe + axis * ((t - 0.5f) * _zone.Width);
			pos.y = 0f;
			if (!TryAcceptStand(pos, _zone.SurfaceNormal))
				continue;
			return true;
		}

		return TryAcceptStand(probe, _zone.SurfaceNormal);
	}

	private bool IsObstacleWalkable(ProtectionZone _zone)
	{
		float standoff = Mathf.Max(0.05f, m_Settings.StandOffMeters);
		Vector3 axis = PlanarUnit(_zone.Axis);
		if (axis.sqrMagnitude < 0.5f)
			axis = Vector3.forward;
		Vector3 side = Vector3.Cross(Vector3.up, axis);
		if (side.sqrMagnitude < 0.01f)
			side = Vector3.right;
		side.Normalize();
		Vector3 e = _zone.ObstacleExtents;
		float along = Mathf.Max(0.2f, e.z * 0.5f) + standoff;
		float across = Mathf.Max(0.2f, e.x * 0.5f) + standoff;
		Vector3 c = _zone.Center;
		c.y = 0f;
		Vector3[] samples =
		{
			c + axis * along,
			c - axis * along,
			c + side * across,
			c - side * across
		};
		for (int i = 0; i < samples.Length; i++)
		{
			if (TryAcceptStand(samples[i], PlanarUnit(samples[i] - c)))
				return true;
		}

		return false;
	}

	private Vector3 ProbePoint(ProtectionZone _zone)
	{
		Vector3 point = _zone.Center;
		point.y = 0f;
		if (_zone.GeometryType == ProtectionZoneType.Corner)
		{
			Vector3 facing = PlanarUnit(_zone.CornerFacing);
			if (facing.sqrMagnitude > 0.5f)
				return point + facing * (Mathf.Max(0.05f, m_Settings.StandOffMeters) * 1.41421356f);
			return point;
		}
		if (_zone.GeometryType != ProtectionZoneType.Wall &&
		    _zone.GeometryType != ProtectionZoneType.Edge)
			return point;
		Vector3 n = PlanarUnit(_zone.SurfaceNormal);
		if (n.sqrMagnitude < 0.5f)
			return point;
		return point + n * Mathf.Max(0.05f, m_Settings.StandOffMeters);
	}

	private bool TryAcceptStand(Vector3 _position, Vector3 _normal)
	{
		if (m_NavMesh != null && !m_NavMesh.TrySample(_position, out _))
			return false;
		return m_Clearance == null || m_Clearance.HasBodyClearance(_position, _normal);
	}

	private void FillProtection(ProtectionZone _zone)
	{
		CoverClassifier.SampleProtection(
			ProbePoint(_zone),
			_zone.SurfaceNormal.sqrMagnitude > 0.01f ? _zone.SurfaceNormal : Vector3.forward,
			m_Occlusion,
			m_ClassSettings,
			out CoverProtectionProfile standing,
			out CoverProtectionProfile crouch);
		float rear = 0f;
		float side = 0f;
		if (m_Occlusion != null)
		{
			Vector3 hip = ProbePoint(_zone) + Vector3.up * 1f;
			Vector3 n = PlanarUnit(_zone.SurfaceNormal);
			if (n.sqrMagnitude > 0.5f)
			{
				rear = m_Occlusion.IsBlocked(hip, hip - n * 0.85f) ? 1f : 0f;
				Vector3 tangent = Vector3.Cross(Vector3.up, n);
				if (tangent.sqrMagnitude > 0.01f)
				{
					tangent.Normalize();
					float a = m_Occlusion.IsBlocked(hip, hip + tangent * 0.85f) ? 1f : 0f;
					float b = m_Occlusion.IsBlocked(hip, hip - tangent * 0.85f) ? 1f : 0f;
					side = Mathf.Max(a, b);
				}
			}
		}

		if (_zone.ProtectionHeight < 0.05f)
			_zone.ProtectionHeight = standing.Average >= 0.5f ? 2f : (crouch.Average >= 0.5f ? 1.2f : 0.5f);
		_zone.Protection = new ProtectionHeightProfile
		{
			HeightMeters = _zone.ProtectionHeight,
			Standing = standing,
			Crouch = crouch,
			RearProtection = rear,
			SideProtection = side
		};
	}

	private bool TryInspectWindow(ProtectionZone _zone, ICoverWindowProbe _probe, out CoverWindowHit _hit)
	{
		var stub = new CoverCandidate
		{
			OpeningValid = true,
			OpeningCenter = _zone.OpeningCenter,
			OpeningAxis = _zone.OpeningAxis,
			OpeningWidth = _zone.OpeningWidth,
			Normal = _zone.SurfaceNormal,
			Position = _zone.Center
		};
		return _probe.TryInspect(stub, out _hit);
	}

	private ICoverSeamProbe ResolveSeamProbe()
	{
		if (!m_Settings.ConfirmSurfaceWithPhysics)
			return null;
		if (m_PhysicsSeam == null)
			m_PhysicsSeam = new PhysicsCoverSeamProbe(m_Settings.PhysicsMask);
		return m_PhysicsSeam;
	}

	private ICoverWindowProbe ResolveWindowProbe()
	{
		return m_Window;
	}

	private static Vector3 PlanarUnit(Vector3 _value)
	{
		Vector3 v = _value;
		v.y = 0f;
		return v.sqrMagnitude < 0.01f ? Vector3.zero : v.normalized;
	}
	#endregion
}

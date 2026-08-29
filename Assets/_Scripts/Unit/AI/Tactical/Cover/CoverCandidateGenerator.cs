using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

/// <summary>
/// #13.1 local geometry → positional CoverCandidate. #13.2 classifies kept candidates in the same generate.
/// </summary>
public sealed class CoverCandidateGenerator : ICoverCandidateSource
{
	#region Private Fields
	private readonly ICoverGeometrySource m_Geometry;
	private readonly ICoverNavMeshProbe m_NavMesh;
	private readonly ICoverClearanceProbe m_Clearance;
	private readonly ICoverOcclusionProbe m_Occlusion;
	private readonly CoverGenerationSettings m_Settings;
	private readonly CoverClassificationSettings m_ClassSettings;
	private readonly CoverClassifier m_Classifier = new CoverClassifier();
	private readonly ICoverWindowProbe m_Window;
	private PhysicsCoverWindowProbe m_PhysicsWindow;
	private PhysicsCoverSeamProbe m_PhysicsSeam;
	private readonly List<CoverGeometrySurface> m_Surfaces = new List<CoverGeometrySurface>(32);
	private readonly List<CoverOpeningSeed> m_Openings = new List<CoverOpeningSeed>(8);
	private readonly List<CoverCornerSeed> m_Corners = new List<CoverCornerSeed>(8);
	private readonly List<CoverCandidate> m_Scratch = new List<CoverCandidate>(32);
	private readonly List<CoverRejectedSample> m_Rejected = new List<CoverRejectedSample>(32);
	private readonly RaycastHit[] m_RayHits = new RaycastHit[8];
	#endregion

	#region Public Properties
	public CoverGenerationSettings Settings => m_Settings;
	public int LastSampleCount { get; private set; }
	public int LastAcceptedBeforeCap { get; private set; }
	public int LastRejectedNavMeshCount { get; private set; }
	public int LastRejectedClearanceCount { get; private set; }
	public int LastRejectedOutsideCount { get; private set; }
	public int LastRejectedUnanchoredCount { get; private set; }
	public double LastGenerationMilliseconds { get; private set; }
	public int LastClassificationCount { get; private set; }
	public IReadOnlyList<CoverRejectedSample> LastRejected => m_Rejected;
	#endregion

	#region Public Constructors
	public CoverCandidateGenerator(
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
	public void Generate(
		CoverRegionId _region,
		Bounds _bounds,
		int _geometryVersion,
		List<CoverCandidate> _destination)
	{
		Stopwatch watch = Stopwatch.StartNew();
		LastSampleCount = 0;
		LastAcceptedBeforeCap = 0;
		LastRejectedNavMeshCount = 0;
		LastRejectedClearanceCount = 0;
		LastRejectedOutsideCount = 0;
		LastRejectedUnanchoredCount = 0;
		LastClassificationCount = 0;
		m_Surfaces.Clear();
		m_Scratch.Clear();
		m_Rejected.Clear();
		m_Openings.Clear();
		m_Corners.Clear();

		Bounds queryBounds = CoverSpatialMath.ExpandHorizontally(_bounds, m_Settings.GeometryMarginMeters);
		if (m_Geometry != null)
			m_Geometry.Collect(_region, queryBounds, m_Surfaces);

		CoverSurfaceMerge.Rebuild(m_Surfaces, m_Settings, ResolveSeamProbe());
		SortSurfaces();
		SampleSurfaces(_bounds);
		SampleEdges(_bounds);
		SampleOpenings(_bounds);
		SampleCorners(_bounds);

		CoverSpatialReduce.Deduplicate(m_Scratch, m_Settings.DedupRadiusMeters);
		LastAcceptedBeforeCap = m_Scratch.Count;
		CoverSpatialReduce.ReduceToSpatiallyDiverse(
			m_Scratch,
			Mathf.Max(1, m_Settings.MaxCoverCandidates));

		if (_destination != null)
		{
			for (int i = 0; i < m_Scratch.Count; i++)
			{
				CoverCandidate candidate = m_Scratch[i];
				m_Classifier.Classify(candidate, m_Occlusion, m_ClassSettings, CoverThreatFrame.CoverBacked);
				LastClassificationCount++;
				candidate.CandidateId = i + 1;
				candidate.RegionId = _region;
				candidate.GeometryVersion = _geometryVersion;
				candidate.NavMeshValid = true;
				candidate.Occupancy = CoverOccupancy.Available;
				_destination.Add(candidate);
			}

			CoverEdgeGeometry.TagEdges(_destination, m_Surfaces, m_Settings);
			CoverOpeningGeometry.TagOpenings(_destination, m_Settings);
			CoverOpeningGeometry.AbsorbPassageEdges(_destination, m_Settings);
			CoverWindowGeometry.TagWindows(_destination, ResolveWindowProbe(), m_Settings);
			CoverCornerGeometry.TagCorners(_destination, m_Corners, m_Settings);
			CoverCornerGeometry.AbsorbCornerEdges(_destination, m_Settings);
			for (int i = 0; i < _destination.Count; i++)
			{
				CoverCandidate candidate = _destination[i];
				if (candidate == null)
					continue;
				CoverClassifier.FinalizeBake(candidate);
				candidate.CandidateId = i + 1;
			}

			LastClassificationCount = _destination.Count;
		}

		watch.Stop();
		LastGenerationMilliseconds = watch.Elapsed.TotalMilliseconds;
		LogGenerate(_region, _destination != null ? _destination.Count : m_Scratch.Count);
	}
	#endregion

	#region Private Methods
	private void SampleSurfaces(Bounds _regionBounds)
	{
		float spacing = Mathf.Max(0.25f, m_Settings.SampleSpacingMeters);
		float standoff = Mathf.Max(0.05f, m_Settings.StandOffMeters);
		int minSamples = Mathf.Max(1, m_Settings.MinSamplesPerSurface);

		for (int s = 0; s < m_Surfaces.Count; s++)
		{
			CoverGeometrySurface surface = m_Surfaces[s];
			if (surface.Length < 0.05f || surface.Normal.sqrMagnitude < 0.01f)
				continue;

			Vector3 normal = surface.Normal.normalized;
			Vector3 tangent = surface.Tangent;
			if (tangent.sqrMagnitude < 0.01f)
				tangent = Vector3.Cross(Vector3.up, normal);
			tangent = Vector3.ProjectOnPlane(tangent, normal);
			if (tangent.sqrMagnitude < 0.01f)
				continue;
			tangent.Normalize();

			int sampleCount = Mathf.Max(minSamples, Mathf.RoundToInt(surface.Length / spacing));
			for (int i = 0; i < sampleCount; i++)
			{
				LastSampleCount++;
				float t = sampleCount == 1 ? 0.5f : (i + 0.5f) / sampleCount;
				Vector3 onSurface = surface.Origin + tangent * ((t - 0.5f) * surface.Length);
				Vector3 pos = onSurface + normal * standoff;
				TryAccept(pos, normal, _regionBounds, false);
			}
		}
	}

	private void SampleEdges(Bounds _regionBounds)
	{
		float standoff = Mathf.Max(0.05f, m_Settings.StandOffMeters);
		float inset = Mathf.Max(0.05f, m_Settings.EdgeInsetMeters);
		for (int s = 0; s < m_Surfaces.Count; s++)
		{
			CoverGeometrySurface surface = m_Surfaces[s];
			if (!CoverEdgeGeometry.SurfaceSupportsEdge(surface, m_Settings.MinEdgeSurfaceLengthMeters))
				continue;
			Vector3 normal = surface.Normal.sqrMagnitude > 0.01f ? surface.Normal.normalized : surface.Normal;
			LastSampleCount++;
			TryAccept(
				CoverEdgeGeometry.EndSamplePosition(surface, standoff, inset, true),
				normal,
				_regionBounds,
				true);
			LastSampleCount++;
			TryAccept(
				CoverEdgeGeometry.EndSamplePosition(surface, standoff, inset, false),
				normal,
				_regionBounds,
				true);
		}
	}

	private void SampleOpenings(Bounds _regionBounds)
	{
		CoverOpeningGeometry.Collect(m_Surfaces, m_Settings, m_Openings);
		float standoff = Mathf.Max(0.05f, m_Settings.StandOffMeters);
		for (int i = 0; i < m_Openings.Count; i++)
		{
			CoverOpeningSeed seed = m_Openings[i];
			LastSampleCount++;
			TryAccept(
				CoverOpeningGeometry.StandPosition(in seed, standoff),
				seed.Normal,
				_regionBounds,
				false,
				in seed,
				true);
		}
	}

	private void SampleCorners(Bounds _regionBounds)
	{
		CoverCornerGeometry.Collect(m_Surfaces, m_Settings, m_Corners);
		for (int i = 0; i < m_Corners.Count; i++)
		{
			CoverCornerSeed seed = m_Corners[i];
			LastSampleCount++;
			CoverOpeningSeed none = default;
			TryAccept(
				seed.Position,
				seed.Facing,
				_regionBounds,
				false,
				in none,
				false,
				in seed,
				true);
		}
	}

	private void TryAccept(Vector3 _position, Vector3 _normal, Bounds _regionBounds, bool _edgeSeed)
	{
		CoverOpeningSeed none = default;
		TryAccept(_position, _normal, _regionBounds, _edgeSeed, in none, false);
	}

	private void TryAccept(
		Vector3 _position,
		Vector3 _normal,
		Bounds _regionBounds,
		bool _edgeSeed,
		in CoverOpeningSeed _opening,
		bool _openingSeed)
	{
		CoverCornerSeed none = default;
		TryAccept(_position, _normal, _regionBounds, _edgeSeed, in _opening, _openingSeed, in none, false);
	}

	private void TryAccept(
		Vector3 _position,
		Vector3 _normal,
		Bounds _regionBounds,
		bool _edgeSeed,
		in CoverOpeningSeed _opening,
		bool _openingSeed,
		in CoverCornerSeed _corner,
		bool _cornerSeed)
	{
		if (!CoverSpatialMath.ContainsPlanar(_regionBounds, _position))
		{
			Reject(_position, _normal, CoverRejectReason.OutsideRegion);
			LastRejectedOutsideCount++;
			return;
		}

		Vector3 sampled = _position;
		if (m_NavMesh != null)
		{
			if (!m_NavMesh.TrySample(_position, out sampled))
			{
				Reject(_position, _normal, CoverRejectReason.OffNavMesh);
				LastRejectedNavMeshCount++;
				return;
			}

			if (!CoverSpatialMath.ContainsPlanar(_regionBounds, sampled))
			{
				Reject(sampled, _normal, CoverRejectReason.OutsideRegion);
				LastRejectedOutsideCount++;
				return;
			}
		}

		if (!_openingSeed && !_cornerSeed &&
		    m_Settings.ConfirmSurfaceWithPhysics &&
		    !IsAnchoredToGeometry(sampled, _normal))
		{
			Reject(sampled, _normal, CoverRejectReason.Unanchored);
			LastRejectedUnanchoredCount++;
			return;
		}

		if (m_Clearance != null && !m_Clearance.HasBodyClearance(sampled, _normal))
		{
			Reject(sampled, _normal, CoverRejectReason.NoClearance);
			LastRejectedClearanceCount++;
			return;
		}

		var candidate = new CoverCandidate
		{
			Position = sampled,
			Normal = _normal,
			CoverType = CoverType.None,
			NavMeshValid = true,
			Occupancy = CoverOccupancy.Available,
			EdgeSeed = _edgeSeed
		};
		if (_openingSeed)
			CoverOpeningGeometry.ApplySeed(candidate, in _opening);
		if (_cornerSeed)
			CoverCornerGeometry.ApplySeed(candidate, in _corner);
		m_Scratch.Add(candidate);
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
		if (m_Window != null)
			return m_Window;
		if (!m_Settings.ConfirmSurfaceWithPhysics)
			return null;
		if (m_PhysicsWindow == null)
			m_PhysicsWindow = new PhysicsCoverWindowProbe();
		return m_PhysicsWindow;
	}

	private bool IsAnchoredToGeometry(Vector3 _position, Vector3 _normal)
	{
		Vector3 n = _normal;
		n.y = 0f;
		if (n.sqrMagnitude < 0.01f)
			return false;
		n.Normalize();

		Vector3 origin = _position + Vector3.up * 0.9f;
		float maxDist = m_Settings.StandOffMeters + 1.2f;
		int hits = Physics.RaycastNonAlloc(
			origin,
			-n,
			m_RayHits,
			maxDist,
			m_Settings.PhysicsMask,
			QueryTriggerInteraction.Ignore);
		for (int i = 0; i < hits; i++)
		{
			Collider collider = m_RayHits[i].collider;
			if (collider == null || collider.isTrigger)
				continue;
			if (PhysicsCoverGeometrySource.IsCharacterOrVehicle(collider))
				continue;
			if (TacticalTransparency.IsMarked(collider))
				continue;
			return true;
		}

		return false;
	}

	private void Reject(Vector3 _position, Vector3 _normal, CoverRejectReason _reason)
	{
		m_Rejected.Add(new CoverRejectedSample
		{
			Position = _position,
			Normal = _normal,
			Reason = _reason
		});
	}

	private void SortSurfaces()
	{
		m_Surfaces.Sort(CompareSurfaces);
	}

	private static int CompareSurfaces(CoverGeometrySurface _a, CoverGeometrySurface _b)
	{
		int x = _a.Origin.x.CompareTo(_b.Origin.x);
		if (x != 0)
			return x;
		int z = _a.Origin.z.CompareTo(_b.Origin.z);
		if (z != 0)
			return z;
		int nx = _a.Normal.x.CompareTo(_b.Normal.x);
		if (nx != 0)
			return nx;
		return _a.Normal.z.CompareTo(_b.Normal.z);
	}

	private static void LogGenerate(CoverRegionId _region, int _kept)
	{
		if (!UnitActionLog.Enabled)
			return;
		UnitActionLog.Timeline(
			UnitActionLog.CoverCandidate,
			"region=" + _region.LogLabel + " kept=" + _kept);
	}
	#endregion
}

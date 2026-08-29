using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #13.1 Candidate Generation. Geometry → CoverCandidate. Not score. Not classification. Not Fire.
	/// </summary>
	public sealed class DynamicCoverGenerationTests
	{
		#region Nested
		private sealed class RecordingGeometrySource : ICoverGeometrySource
		{
			public int QueryCount;
			public CoverRegionId LastRegion;
			public Bounds LastBounds;
			public SharedCoverSpatialCache Cache;
			public bool ReenterSameRegion;
			public readonly List<CoverGeometrySurface> Surfaces = new List<CoverGeometrySurface>();

			public void Collect(
				CoverRegionId _region,
				Bounds _queryBounds,
				List<CoverGeometrySurface> _destination)
			{
				QueryCount++;
				LastRegion = _region;
				LastBounds = _queryBounds;
				if (ReenterSameRegion && Cache != null)
					Cache.GetCandidates(_region);
				for (int i = 0; i < Surfaces.Count; i++)
					_destination.Add(Surfaces[i]);
			}
		}

		private sealed class ScriptedNavMesh : ICoverNavMeshProbe
		{
			public bool AcceptAll = true;
			public readonly List<Vector3> Blocked = new List<Vector3>();
			public float BlockRadius = 0.55f;

			public bool TrySample(Vector3 _world, out Vector3 _onMesh)
			{
				_onMesh = _world;
				if (!AcceptAll)
					return false;
				for (int i = 0; i < Blocked.Count; i++)
				{
					if (CoverSpatialMath.PlanarDistanceSqr(_world, Blocked[i]) <= BlockRadius * BlockRadius)
						return false;
				}

				return true;
			}
		}

		private sealed class ScriptedClearance : ICoverClearanceProbe
		{
			public bool AcceptAll = true;
			public readonly List<Vector3> Blocked = new List<Vector3>();
			public float BlockRadius = 0.55f;

			public bool HasBodyClearance(Vector3 _position, Vector3 _normal)
			{
				if (!AcceptAll)
					return false;
				for (int i = 0; i < Blocked.Count; i++)
				{
					if (CoverSpatialMath.PlanarDistanceSqr(_position, Blocked[i]) <= BlockRadius * BlockRadius)
						return false;
				}

				return true;
			}
		}
		#endregion

		#region Private Fields
		private GameObject m_PhysicsRoot;
		#endregion

		#region Setup
		[TearDown]
		public void TearDown()
		{
			if (m_PhysicsRoot == null)
				return;
			Object.DestroyImmediate(m_PhysicsRoot);
			m_PhysicsRoot = null;
		}
		#endregion

		#region A Geometry source
		[Test]
		public void A1_SourceReceivesCorrectRegion()
		{
			RecordingGeometrySource geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(8f, 0f, 2f), Vector3.forward, 4f));
			CoverCandidateGenerator gen = MakeGenerator(geo);
			CoverRegionId region = CoverSpatialMath.WorldToRegion(Vector3.zero, CoverSpatialMath.DefaultRegionSizeMeters);
			Bounds bounds = CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters);
			var dest = new List<CoverCandidate>();
			gen.Generate(region, bounds, 1, dest);
			Assert.AreEqual(1, geo.QueryCount);
			Assert.AreEqual(region, geo.LastRegion);
		}

		[Test]
		public void A2_OnlyRequestedRegionIsQueried()
		{
			RecordingGeometrySource geo = new RecordingGeometrySource();
			CoverCandidateGenerator gen = MakeGenerator(geo);
			CoverRegionId region = CoverSpatialMath.WorldToRegion(Vector3.zero, CoverSpatialMath.DefaultRegionSizeMeters);
			Bounds bounds = CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters);
			gen.Generate(region, bounds, 1, new List<CoverCandidate>());
			Assert.AreEqual(1, geo.QueryCount);
			Assert.AreEqual(region, geo.LastRegion);
			Assert.Less(Mathf.Abs(geo.LastBounds.center.x - bounds.center.x), 0.01f);
			Assert.Less(Mathf.Abs(geo.LastBounds.center.z - bounds.center.z), 0.01f);
			float expected = CoverSpatialMath.DefaultRegionSizeMeters + gen.Settings.GeometryMarginMeters * 2f;
			Assert.Less(Mathf.Abs(geo.LastBounds.size.x - expected), 0.01f);
			Assert.Less(geo.LastBounds.size.x, 24f);
		}

		[Test]
		public void A3_SourceCanReturnNoGeometry()
		{
			RecordingGeometrySource geo = new RecordingGeometrySource();
			List<CoverCandidate> list = Generate(geo);
			Assert.AreEqual(0, list.Count);
			Assert.AreEqual(1, geo.QueryCount);
		}

		[Test]
		public void A4_SourceReturnsMultipleSurfaces()
		{
			RecordingGeometrySource geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(4f, 0f, 2f), Vector3.forward, 3f));
			geo.Surfaces.Add(Wall(new Vector3(12f, 0f, 14f), Vector3.back, 3f));
			List<CoverCandidate> list = Generate(geo);
			Assert.GreaterOrEqual(geo.Surfaces.Count, 2);
			Assert.GreaterOrEqual(list.Count, 2);
		}

		[Test]
		public void A2b_Physics_FarColliderIsNotCollected()
		{
			Vector3 origin = new Vector3(8000f, 0f, 8000f);
			CoverRegionId region = CoverSpatialMath.WorldToRegion(origin, CoverSpatialMath.DefaultRegionSizeMeters);
			Bounds bounds = CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters);
			m_PhysicsRoot = new GameObject("CoverGenPhysicsA");
			CreateBox("LocalWall", bounds.center + new Vector3(0f, 1f, 4f), new Vector3(8f, 2f, 0.4f));
			CreateBox("FarWall", bounds.center + new Vector3(50f, 1f, 0f), new Vector3(8f, 2f, 0.4f));
			Physics.SyncTransforms();

			var source = new PhysicsCoverGeometrySource();
			var surfaces = new List<CoverGeometrySurface>();
			Bounds query = CoverSpatialMath.ExpandHorizontally(bounds, 1.5f);
			source.Collect(region, query, surfaces);
			Assert.AreEqual(region, source.LastRegion);
			Assert.AreEqual(1, source.QueryCount);
			Assert.Greater(surfaces.Count, 0);
			for (int i = 0; i < surfaces.Count; i++)
				Assert.IsTrue(CoverSpatialMath.ContainsPlanar(query, surfaces[i].Origin), surfaces[i].Origin.ToString());
		}

		[Test]
		public void A3b_Physics_EmptyRegion()
		{
			Vector3 origin = new Vector3(12000f, 0f, 12000f);
			CoverRegionId region = CoverSpatialMath.WorldToRegion(origin, CoverSpatialMath.DefaultRegionSizeMeters);
			Bounds bounds = CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters);
			var source = new PhysicsCoverGeometrySource();
			var surfaces = new List<CoverGeometrySurface>();
			source.Collect(region, CoverSpatialMath.ExpandHorizontally(bounds, 1.5f), surfaces);
			Assert.AreEqual(0, surfaces.Count);
		}

		[Test]
		public void A4b_Physics_MultipleSurfaces()
		{
			Vector3 origin = new Vector3(8240f, 0f, 8240f);
			CoverRegionId region = CoverSpatialMath.WorldToRegion(origin, CoverSpatialMath.DefaultRegionSizeMeters);
			Bounds bounds = CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters);
			m_PhysicsRoot = new GameObject("CoverGenPhysicsA4");
			CreateBox("WallA", bounds.center + new Vector3(0f, 1f, 5f), new Vector3(8f, 2f, 0.4f));
			CreateBox("WallB", bounds.center + new Vector3(-5f, 1f, 0f), new Vector3(0.4f, 2f, 8f));
			Physics.SyncTransforms();
			var source = new PhysicsCoverGeometrySource();
			var surfaces = new List<CoverGeometrySurface>();
			source.Collect(region, CoverSpatialMath.ExpandHorizontally(bounds, 1.5f), surfaces);
			Assert.GreaterOrEqual(surfaces.Count, 4);
		}
		#endregion

		#region B Candidate generation
		[Test]
		public void B1_ValidSurface_ProducesCandidates()
		{
			RecordingGeometrySource geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(8f, 0f, 2f), Vector3.forward, 10f));
			List<CoverCandidate> list = Generate(geo);
			Assert.Greater(list.Count, 0);
			Assert.Greater(list.Count, 1);
		}

		[Test]
		public void B2_OpenRegion_EmptyResult()
		{
			List<CoverCandidate> list = Generate(new RecordingGeometrySource());
			Assert.AreEqual(0, list.Count);
		}

		[Test]
		public void B3_CandidateHasPosition()
		{
			CoverCandidate candidate = FirstCandidate();
			Assert.Greater(candidate.Position.sqrMagnitude, 0.01f);
		}

		[Test]
		public void B4_CandidateHasNormal()
		{
			CoverCandidate candidate = FirstCandidate();
			Assert.Greater(candidate.Normal.sqrMagnitude, 0.5f);
			Assert.Greater(Vector3.Dot(candidate.Normal.normalized, Vector3.forward), 0.9f);
		}

		[Test]
		public void B5_CandidateHasCorrectRegion()
		{
			CoverCandidate candidate = FirstCandidate();
			CoverRegionId expected = CoverSpatialMath.WorldToRegion(Vector3.zero, CoverSpatialMath.DefaultRegionSizeMeters);
			Assert.AreEqual(expected, candidate.RegionId);
		}

		[Test]
		public void B6_CandidateHasCurrentGeometryVersion()
		{
			RecordingGeometrySource geo = WallSource();
			CoverCandidateGenerator gen = MakeGenerator(geo);
			CoverRegionId region = CoverSpatialMath.WorldToRegion(Vector3.zero, CoverSpatialMath.DefaultRegionSizeMeters);
			Bounds bounds = CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters);
			var dest = new List<CoverCandidate>();
			gen.Generate(region, bounds, 7, dest);
			Assert.Greater(dest.Count, 0);
			Assert.AreEqual(7, dest[0].GeometryVersion);
			for (int i = 0; i < dest.Count; i++)
			{
				Assert.AreEqual(7, dest[i].GeometryVersion);
				Assert.AreNotEqual(CoverType.Standing, dest[i].CoverType);
			}
		}
		#endregion

		#region C NavMesh
		[Test]
		public void C1_ValidNavMeshPoint_Accept()
		{
			RecordingGeometrySource geo = WallSource();
			var nav = new ScriptedNavMesh { AcceptAll = true };
			List<CoverCandidate> list = Generate(geo, nav, new ScriptedClearance());
			Assert.Greater(list.Count, 0);
			Assert.IsTrue(list[0].NavMeshValid);
		}

		[Test]
		public void C2_OffNavMesh_Reject()
		{
			RecordingGeometrySource geo = WallSource();
			var nav = new ScriptedNavMesh { AcceptAll = false };
			CoverCandidateGenerator gen = MakeGenerator(geo, nav, new ScriptedClearance());
			CoverRegionId region = CoverSpatialMath.WorldToRegion(Vector3.zero, CoverSpatialMath.DefaultRegionSizeMeters);
			var dest = new List<CoverCandidate>();
			gen.Generate(
				region,
				CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters),
				1,
				dest);
			Assert.AreEqual(0, dest.Count);
			Assert.Greater(gen.LastRejectedNavMeshCount, 0);
		}

		[Test]
		public void C3_BlockedPosition_Reject()
		{
			RecordingGeometrySource geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(4f, 0f, 2f), Vector3.forward, 1f));
			geo.Surfaces.Add(Wall(new Vector3(12f, 0f, 2f), Vector3.forward, 1f));
			var nav = new ScriptedNavMesh();
			Vector3 blocked = new Vector3(4f, 0f, 2f) + Vector3.forward * 0.45f;
			nav.Blocked.Add(blocked);
			List<CoverCandidate> list = Generate(geo, nav, new ScriptedClearance());
			Assert.AreEqual(1, list.Count);
			Assert.Greater(CoverSpatialMath.PlanarDistanceSqr(list[0].Position, blocked), 1f);
		}
		#endregion

		#region D Clearance
		[Test]
		public void D1_ValidClearance_Accept()
		{
			List<CoverCandidate> list = Generate(WallSource(), new ScriptedNavMesh(), new ScriptedClearance());
			Assert.Greater(list.Count, 0);
		}

		[Test]
		public void D2_WallIntersection_Reject()
		{
			RecordingGeometrySource geo = WallSource();
			CoverCandidateGenerator probe = MakeGenerator(geo);
			var dest = new List<CoverCandidate>();
			CoverRegionId region = CoverSpatialMath.WorldToRegion(Vector3.zero, CoverSpatialMath.DefaultRegionSizeMeters);
			probe.Generate(
				region,
				CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters),
				1,
				dest);
			Assert.Greater(dest.Count, 0);
			var clearance = new ScriptedClearance();
			clearance.Blocked.Add(dest[0].Position);
			List<CoverCandidate> filtered = Generate(geo, new ScriptedNavMesh(), clearance);
			Assert.Less(filtered.Count, dest.Count);
		}

		[Test]
		public void D3_BlockedCorridor_Reject()
		{
			RecordingGeometrySource geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(8f, 0f, 8f), Vector3.forward, 1f));
			var clearance = new ScriptedClearance { AcceptAll = false };
			List<CoverCandidate> list = Generate(geo, new ScriptedNavMesh(), clearance);
			Assert.AreEqual(0, list.Count);
			CoverCandidateGenerator gen = MakeGenerator(geo, new ScriptedNavMesh(), clearance);
			CoverRegionId region = CoverSpatialMath.WorldToRegion(Vector3.zero, CoverSpatialMath.DefaultRegionSizeMeters);
			gen.Generate(
				region,
				CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters),
				1,
				new List<CoverCandidate>());
			Assert.Greater(gen.LastRejectedClearanceCount, 0);
		}

		[Test]
		public void D1b_Physics_OpenSpace_Accept()
		{
			var probe = new PhysicsCoverClearanceProbe();
			Assert.IsTrue(probe.HasBodyClearance(new Vector3(9000f, 20f, 9000f), Vector3.forward));
		}

		[Test]
		public void D2b_Physics_InsideBox_Reject()
		{
			m_PhysicsRoot = new GameObject("CoverClearanceD2");
			CreateBox("Solid", new Vector3(9008f, 1f, 9008f), new Vector3(2f, 2f, 2f));
			Physics.SyncTransforms();
			var probe = new PhysicsCoverClearanceProbe();
			Assert.IsFalse(probe.HasBodyClearance(new Vector3(9008f, 0f, 9008f), Vector3.forward));
		}

		[Test]
		public void D3b_Physics_NarrowCorridor_Reject()
		{
			m_PhysicsRoot = new GameObject("CoverClearanceD3");
			CreateBox("Left", new Vector3(9100f - 0.35f, 1f, 9100f), new Vector3(0.4f, 2f, 2f));
			CreateBox("Right", new Vector3(9100f + 0.35f, 1f, 9100f), new Vector3(0.4f, 2f, 2f));
			Physics.SyncTransforms();
			var probe = new PhysicsCoverClearanceProbe();
			Assert.IsFalse(probe.HasBodyClearance(new Vector3(9100f, 0f, 9100f), Vector3.forward));
		}
		#endregion

		#region E Deduplication
		[Test]
		public void E1_IdenticalPoints_Collapse()
		{
			var list = new List<CoverCandidate>
			{
				Cand(new Vector3(1f, 0f, 1f), Vector3.forward),
				Cand(new Vector3(1f, 0f, 1f), Vector3.forward)
			};
			CoverSpatialReduce.Deduplicate(list, 0.75f);
			Assert.AreEqual(1, list.Count);
		}

		[Test]
		public void E2_NearIdentical_Collapse()
		{
			var list = new List<CoverCandidate>
			{
				Cand(new Vector3(1f, 0f, 1f), Vector3.forward),
				Cand(new Vector3(1.2f, 0f, 1.1f), Vector3.forward)
			};
			CoverSpatialReduce.Deduplicate(list, 0.75f);
			Assert.AreEqual(1, list.Count);
		}

		[Test]
		public void E3_DistinctPoints_Remain()
		{
			var list = new List<CoverCandidate>
			{
				Cand(new Vector3(1f, 0f, 1f), Vector3.forward),
				Cand(new Vector3(4f, 0f, 1f), Vector3.forward)
			};
			CoverSpatialReduce.Deduplicate(list, 0.75f);
			Assert.AreEqual(2, list.Count);
		}
		#endregion

		#region F Cap
		[Test]
		public void F1_AtMostSixteen_Remains()
		{
			List<CoverCandidate> list = GenerateGrid(8);
			Assert.AreEqual(8, list.Count);
		}

		[Test]
		public void F2_OverSixteen_ReducedToCap()
		{
			List<CoverCandidate> list = GenerateGrid(30);
			Assert.LessOrEqual(list.Count, CoverSpatialMath.DefaultMaxCoverCandidates);
			Assert.AreEqual(CoverSpatialMath.DefaultMaxCoverCandidates, list.Count);
		}

		[Test]
		public void F3_RepeatedGeneration_IsDeterministic()
		{
			RecordingGeometrySource geo = GridSource(20);
			List<CoverCandidate> a = Generate(geo);
			List<CoverCandidate> b = Generate(geo);
			Assert.AreEqual(a.Count, b.Count);
			for (int i = 0; i < a.Count; i++)
			{
				Assert.Less(Mathf.Abs(a[i].Position.x - b[i].Position.x), 0.001f);
				Assert.Less(Mathf.Abs(a[i].Position.z - b[i].Position.z), 0.001f);
			}
		}
		#endregion

		#region G Spatial diversity
		[Test]
		public void G1_CapPreservesSpatialSpread()
		{
			List<CoverCandidate> list = GenerateGrid(30);
			Assert.LessOrEqual(list.Count, 16);
			float minX = float.MaxValue;
			float maxX = float.MinValue;
			float minZ = float.MaxValue;
			float maxZ = float.MinValue;
			for (int i = 0; i < list.Count; i++)
			{
				minX = Mathf.Min(minX, list[i].Position.x);
				maxX = Mathf.Max(maxX, list[i].Position.x);
				minZ = Mathf.Min(minZ, list[i].Position.z);
				maxZ = Mathf.Max(maxZ, list[i].Position.z);
			}

			Assert.Greater(maxX - minX, 8f);
			Assert.Greater(maxZ - minZ, 8f);
		}
		#endregion

		#region Cache integration
		[Test]
		public void Cache_FirstRequest_MissesAndGenerates()
		{
			RecordingGeometrySource geo = WallSource();
			CoverCandidateGenerator gen = MakeGenerator(geo);
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(gen);
			IReadOnlyList<CoverCandidate> list = cache.GetCandidates(Vector3.zero);
			Assert.AreEqual(1, geo.QueryCount);
			Assert.AreEqual(1, cache.GenerationCount);
			Assert.AreEqual(1, cache.CacheMissCount);
			Assert.AreEqual(0, cache.CacheHitCount);
			Assert.Greater(list.Count, 0);
		}

		[Test]
		public void Cache_SecondRequest_HitNoGeometryQuery()
		{
			RecordingGeometrySource geo = WallSource();
			CoverCandidateGenerator gen = MakeGenerator(geo);
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(gen);
			IReadOnlyList<CoverCandidate> first = cache.GetCandidates(Vector3.zero);
			IReadOnlyList<CoverCandidate> second = cache.GetCandidates(new Vector3(1f, 0f, 1f));
			Assert.AreEqual(1, geo.QueryCount);
			Assert.AreEqual(1, cache.GenerationCount);
			Assert.AreEqual(1, cache.CacheHitCount);
			Assert.AreSame(first, second);
		}

		[Test]
		public void Cache_ParallelRequests_OneInFlightGeneration()
		{
			RecordingGeometrySource geo = WallSource();
			geo.ReenterSameRegion = true;
			CoverCandidateGenerator gen = MakeGenerator(geo);
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(gen);
			geo.Cache = cache;
			IReadOnlyList<CoverCandidate> a = cache.GetCandidates(Vector3.zero);
			IReadOnlyList<CoverCandidate> b = cache.GetCandidates(Vector3.zero);
			Assert.AreEqual(1, geo.QueryCount);
			Assert.AreEqual(1, cache.GenerationCount);
			Assert.AreSame(a, b);
		}

		[Test]
		public void Cache_Invalidate_RegeneratesWithNewVersion()
		{
			RecordingGeometrySource geo = WallSource();
			CoverCandidateGenerator gen = MakeGenerator(geo);
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(gen);
			cache.GetCandidates(Vector3.zero);
			cache.BumpGeometryVersion();
			IReadOnlyList<CoverCandidate> next = cache.GetCandidates(Vector3.zero);
			Assert.AreEqual(2, geo.QueryCount);
			Assert.AreEqual(2, cache.GeometryVersion);
			Assert.Greater(next.Count, 0);
			Assert.AreEqual(2, next[0].GeometryVersion);
		}
		#endregion

		#region Multi-unit / performance
		[Test]
		public void MultiUnit_TwentyUnits_ThreeRegions_ThreeGenerations()
		{
			RecordingGeometrySource geo = WallSource();
			CoverCandidateGenerator gen = MakeGenerator(geo);
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(gen);
			Vector3 r1 = Vector3.zero;
			Vector3 r2 = new Vector3(CoverSpatialMath.DefaultRegionSizeMeters + 1f, 0f, 0f);
			Vector3 r3 = new Vector3(0f, 0f, CoverSpatialMath.DefaultRegionSizeMeters + 1f);
			QueryMany(cache, r1, 8);
			QueryMany(cache, r2, 7);
			QueryMany(cache, r3, 5);
			Assert.AreEqual(3, cache.GenerationCount);
			Assert.AreEqual(3, geo.QueryCount);
			Assert.AreEqual(17, cache.CacheHitCount);
		}

		[Test]
		public void Perf_GenerationCount_FollowsRegionsNotUnits()
		{
			RecordingGeometrySource geo = WallSource();
			CoverCandidateGenerator gen = MakeGenerator(geo);
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(gen);
			cache.GetCandidates(Vector3.zero);
			Assert.AreEqual(1, cache.GenerationCount);
			QueryMany(cache, Vector3.zero, 20);
			Assert.AreEqual(1, cache.GenerationCount);
			Assert.AreEqual(1, geo.QueryCount);

			for (int r = 0; r < 10; r++)
			{
				Vector3 p = new Vector3(r * CoverSpatialMath.DefaultRegionSizeMeters + 1f, 0f, 32f);
				QueryMany(cache, p, 10);
			}

			Assert.AreEqual(11, cache.GenerationCount);
			Assert.AreEqual(11, geo.QueryCount);
		}
		#endregion

		#region Helpers
		private static CoverCandidateGenerator MakeGenerator(
			ICoverGeometrySource _geo,
			ICoverNavMeshProbe _nav = null,
			ICoverClearanceProbe _clearance = null)
		{
			return new CoverCandidateGenerator(
				_geo,
				_nav ?? new ScriptedNavMesh(),
				_clearance ?? new ScriptedClearance());
		}

		private static List<CoverCandidate> Generate(
			RecordingGeometrySource _geo,
			ICoverNavMeshProbe _nav = null,
			ICoverClearanceProbe _clearance = null)
		{
			CoverCandidateGenerator gen = MakeGenerator(_geo, _nav, _clearance);
			CoverRegionId region = CoverSpatialMath.WorldToRegion(Vector3.zero, CoverSpatialMath.DefaultRegionSizeMeters);
			Bounds bounds = CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters);
			var dest = new List<CoverCandidate>();
			gen.Generate(region, bounds, 1, dest);
			return dest;
		}

		private static CoverCandidate FirstCandidate()
		{
			List<CoverCandidate> list = Generate(WallSource());
			Assert.Greater(list.Count, 0);
			return list[0];
		}

		private static RecordingGeometrySource WallSource()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(8f, 0f, 2f), Vector3.forward, 10f));
			return geo;
		}

		private static RecordingGeometrySource GridSource(int _count)
		{
			var geo = new RecordingGeometrySource();
			int n = 0;
			for (int ix = 0; ix < 6 && n < _count; ix++)
			{
				for (int iz = 0; iz < 5 && n < _count; iz++)
				{
					float x = 1.5f + ix * 2.4f;
					float z = 1.5f + iz * 3f;
					geo.Surfaces.Add(Wall(new Vector3(x, 0f, z), Vector3.forward, 1f));
					n++;
				}
			}

			return geo;
		}

		private static List<CoverCandidate> GenerateGrid(int _count)
		{
			return Generate(GridSource(_count));
		}

		private static CoverGeometrySurface Wall(Vector3 _origin, Vector3 _normal, float _length)
		{
			Vector3 n = _normal.normalized;
			Vector3 tan = Vector3.Cross(Vector3.up, n);
			if (tan.sqrMagnitude < 0.01f)
				tan = Vector3.right;
			return new CoverGeometrySurface
			{
				Origin = _origin,
				Normal = n,
				Tangent = tan.normalized,
				Length = _length
			};
		}

		private static CoverCandidate Cand(Vector3 _pos, Vector3 _normal)
		{
			return new CoverCandidate
			{
				Position = _pos,
				Normal = _normal
			};
		}

		private static void QueryMany(SharedCoverSpatialCache _cache, Vector3 _anchor, int _count)
		{
			for (int i = 0; i < _count; i++)
				_cache.GetCandidates(_anchor + Vector3.right * (i * 0.15f));
		}

		private GameObject CreateBox(string _name, Vector3 _world, Vector3 _size)
		{
			if (m_PhysicsRoot == null)
				m_PhysicsRoot = new GameObject("CoverGenPhysicsRoot");
			var go = new GameObject(_name);
			go.transform.SetParent(m_PhysicsRoot.transform, false);
			go.transform.position = _world;
			BoxCollider box = go.AddComponent<BoxCollider>();
			box.size = _size;
			return go;
		}
		#endregion
	}
}

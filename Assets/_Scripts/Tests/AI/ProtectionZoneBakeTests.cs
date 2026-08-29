using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #13.2C Protection Zone Bake. Geometry zones only. No unit occupy / peek / fire.
	/// </summary>
	public sealed class ProtectionZoneBakeTests
	{
		#region Nested
		[Serializable]
		private sealed class RecordWrap
		{
			public BakedProtectionZoneRecord Record;
		}

		private sealed class RecordingGeometrySource : ICoverGeometrySource
		{
			public int QueryCount;
			public readonly List<CoverGeometrySurface> Surfaces = new List<CoverGeometrySurface>();

			public void Collect(
				CoverRegionId _region,
				Bounds _queryBounds,
				List<CoverGeometrySurface> _destination)
			{
				QueryCount++;
				for (int i = 0; i < Surfaces.Count; i++)
					_destination.Add(Surfaces[i]);
			}
		}

		private sealed class AcceptNavMesh : ICoverNavMeshProbe
		{
			public bool TrySample(Vector3 _world, out Vector3 _onMesh)
			{
				_onMesh = _world;
				return true;
			}
		}

		private sealed class RejectNavMesh : ICoverNavMeshProbe
		{
			public bool TrySample(Vector3 _world, out Vector3 _onMesh)
			{
				_onMesh = default;
				return false;
			}
		}

		private sealed class AcceptClearance : ICoverClearanceProbe
		{
			public bool HasBodyClearance(Vector3 _position, Vector3 _normal)
			{
				return true;
			}
		}

		private sealed class BoundsOcclusionProbe : ICoverOcclusionProbe
		{
			private readonly Bounds[] m_Bounds;

			public BoundsOcclusionProbe(params Bounds[] _bounds)
			{
				m_Bounds = _bounds ?? Array.Empty<Bounds>();
			}

			public bool IsBlocked(Vector3 _from, Vector3 _to)
			{
				for (int i = 0; i < m_Bounds.Length; i++)
				{
					if (CoverOcclusionMath.SegmentHitsAabb(_from, _to, m_Bounds[i]))
						return true;
				}

				return false;
			}
		}

		private sealed class ScriptedWindowProbe : ICoverWindowProbe
		{
			public bool PaneForAnyOpening;

			public bool TryInspect(CoverCandidate _opening, out CoverWindowHit _hit)
			{
				_hit = default;
				if (_opening == null || !_opening.OpeningValid || !PaneForAnyOpening)
					return false;
				_hit = new CoverWindowHit
				{
					HasTransparentPane = true,
					HasFrame = true,
					Center = _opening.OpeningCenter,
					Axis = _opening.OpeningAxis,
					Width = _opening.OpeningWidth
				};
				return true;
			}
		}
		#endregion

		#region 13.2C.0 Data model
		[Test]
		public void ZoneTypeInts_AreStable()
		{
			Assert.AreEqual(0, (int)ProtectionZoneType.Wall);
			Assert.AreEqual(1, (int)ProtectionZoneType.Edge);
			Assert.AreEqual(2, (int)ProtectionZoneType.Opening);
			Assert.AreEqual(3, (int)ProtectionZoneType.Window);
			Assert.AreEqual(4, (int)ProtectionZoneType.Corner);
			Assert.AreEqual(5, (int)ProtectionZoneType.Obstacle);
			Assert.AreEqual(1, (int)ProtectionEdgeKind.WallEnd);
			Assert.AreEqual(4, (int)ProtectionEdgeKind.OpeningJamb);
		}

		[Test]
		public void BakeRecord_FromToZone_Roundtrip()
		{
			ProtectionZone source = SampleWall();
			BakedProtectionZoneRecord record = BakedProtectionZoneRecord.FromZone(source);
			string json = JsonUtility.ToJson(new RecordWrap { Record = record });
			var wrap = JsonUtility.FromJson<RecordWrap>(json);
			ProtectionZone copy = wrap.Record.ToZone();
			Assert.AreEqual(source.ZoneId, copy.ZoneId);
			Assert.AreEqual(source.GeometryType, copy.GeometryType);
			Assert.AreEqual(source.Width, copy.Width);
			Assert.AreEqual(source.Depth, copy.Depth);
			Assert.AreEqual(source.ProtectionHeight, copy.ProtectionHeight);
			Assert.AreEqual(source.RegionId, copy.RegionId);
			Assert.AreEqual(source.GeometryVersion, copy.GeometryVersion);
			Assert.AreEqual(source.Capabilities, copy.Capabilities);
			Assert.Less(Vector3.Distance(source.Center, copy.Center), 0.001f);
		}

		[Test]
		public void BakeRecord_CornerPocketRange_Roundtrip()
		{
			var source = new ProtectionZone
			{
				GeometryType = ProtectionZoneType.Corner,
				Center = Vector3.zero,
				CornerVertex = Vector3.zero,
				CornerFacing = (Vector3.forward + Vector3.right).normalized,
				CornerMinRadius = 0.3f,
				CornerMaxRadius = 0.9f,
				CornerHalfAngleDegrees = 45f,
				CornerOrientation = CoverCornerOrientation.Inner
			};

			ProtectionZone copy = BakedProtectionZoneRecord.FromZone(source).ToZone();
			Assert.AreEqual(source.CornerMinRadius, copy.CornerMinRadius);
			Assert.AreEqual(source.CornerMaxRadius, copy.CornerMaxRadius);
			Assert.AreEqual(source.CornerHalfAngleDegrees, copy.CornerHalfAngleDegrees);
			Assert.AreEqual(CoverCornerOrientation.Inner, copy.CornerOrientation);
		}

		[Test]
		public void PlaySource_DoesNotRescanGeometry()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(10f, 0f, 0f), Vector3.forward, 20f, 2f));
			ProtectionZone zone = Generate(geo)[0];
			int queries = geo.QueryCount;
			Assert.Greater(queries, 0);
			zone.GeometryVersion = 1;
			var play = new BakedProtectionZoneSource(
				new List<BakedProtectionZoneRecord> { BakedProtectionZoneRecord.FromZone(zone) });
			var dest = new List<ProtectionZone>();
			play.Generate(new Bounds(Vector3.zero, Vector3.one * 40f), 9, dest);
			Assert.AreEqual(queries, geo.QueryCount);
			Assert.AreEqual(1, dest.Count);
			Assert.AreEqual(9, dest[0].GeometryVersion);
			Assert.AreEqual(zone.GeometryType, dest[0].GeometryType);
		}
		#endregion

		#region 13.2C.1–.2 Surfaces and segmentation
		[Test]
		public void TenOverlappingPrefabs_OneWallZone()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.AddRange(OverlapChain(10, 2f, 0.28f, 2f));
			List<ProtectionZone> dest = Generate(geo);
			Assert.AreEqual(1, Count(dest, ProtectionZoneType.Wall));
			Assert.AreEqual(0, Count(dest, ProtectionZoneType.Opening));
			Assert.AreEqual(2, Count(dest, ProtectionZoneType.Edge), "merged pieces expose only outer boundaries");
			Assert.Greater(First(dest, ProtectionZoneType.Wall).Width, 15f);
		}

		[Test]
		public void TwentyMeterWall_OneZoneNoMidPoints()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(10f, 0f, 0f), Vector3.forward, 20f, 2f));
			List<ProtectionZone> dest = Generate(geo);
			Assert.AreEqual(1, Count(dest, ProtectionZoneType.Wall));
			Assert.AreEqual(0, Count(dest, ProtectionZoneType.Obstacle));
			Assert.Greater(First(dest, ProtectionZoneType.Wall).Width, 19f);
		}

		[Test]
		public void LowerOverlappingPiece_DoesNotCreateBoundariesInsideContinuousWall()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(10f, 0f, 0f), Vector3.forward, 20f, 2.5f));
			geo.Surfaces.Add(Wall(new Vector3(6f, 0f, 0f), Vector3.forward, 2f, 1f));
			List<ProtectionZone> dest = Generate(geo);

			Assert.AreEqual(2, Count(dest, ProtectionZoneType.Wall), "height layers remain explicit surfaces");
			Assert.AreEqual(2, Count(dest, ProtectionZoneType.Edge), "contained piece endpoints are not topology boundaries");
		}

		[Test]
		public void TJoinedSurface_DoesNotExposeBoundaryInsideHostWall()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(Vector3.zero, Vector3.forward, 8f, 2f));
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, -2f), Vector3.right, 4f, 2f));
			List<ProtectionZone> dest = Generate(geo);

			Assert.AreEqual(3, Count(dest, ProtectionZoneType.Edge),
				"host ends and free branch end remain; joined T endpoint is not a Boundary");
		}

		[Test]
		public void WallWithRealDoor_WallPlusOpening()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.AddRange(DoorChain(4, 2.4f, 4, 2f, 0.28f, 2f));
			List<ProtectionZone> dest = Generate(geo);
			Assert.AreEqual(2, Count(dest, ProtectionZoneType.Wall));
			Assert.AreEqual(1, Count(dest, ProtectionZoneType.Opening));
			Assert.Greater(First(dest, ProtectionZoneType.Opening).OpeningWidth, 2f);
			Assert.Less(First(dest, ProtectionZoneType.Opening).OpeningWidth, 2.8f);
		}
		#endregion

		#region 13.2C.3 Height
		[Test]
		public void LowObject_StoresCrouchHeightOnWallZone()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(10f, 0f, 0f), Vector3.forward, 20f, 1.1f));
			ProtectionZone wall = First(Generate(geo, LowSlab()), ProtectionZoneType.Wall);
			Assert.AreEqual(ProtectionZoneType.Wall, wall.GeometryType);
			Assert.Less(Mathf.Abs(wall.ProtectionHeight - 1.1f), 0.05f);
		}

		[Test]
		public void HighWall_IsWallZoneNotStandingPoint()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(10f, 0f, 0f), Vector3.forward, 20f, 2f));
			List<ProtectionZone> dest = Generate(geo, HighSlab());
			Assert.AreEqual(1, Count(dest, ProtectionZoneType.Wall));
			Assert.Greater(First(dest, ProtectionZoneType.Wall).ProtectionHeight, 1.5f);
		}

		[Test]
		public void SurfaceWithoutCurrentStandPoint_IsRetainedAsGeometry()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(10f, 0f, 0f), Vector3.forward, 20f, 2f));
			var generator = new ProtectionZoneGenerator(
				geo,
				new RejectNavMesh(),
				new AcceptClearance(),
				new CoverGenerationSettings());
			var dest = new List<ProtectionZone>();
			generator.Generate(new Bounds(new Vector3(10f, 0f, 0f), new Vector3(48f, 8f, 24f)), 1, dest);

			Assert.AreEqual(1, Count(dest, ProtectionZoneType.Wall));
			Assert.IsFalse(First(dest, ProtectionZoneType.Wall).NavMeshValid);
		}
		#endregion

		#region 13.2C.4 Edge Opening Window Corner
		[Test]
		public void LongWall_HasExactlyTwoLogicalEnds()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(10f, 0f, 0f), Vector3.forward, 20f, 2f));
			List<ProtectionZone> dest = Generate(geo);
			Assert.AreEqual(2, Count(dest, ProtectionZoneType.Edge));
			Assert.AreEqual(ProtectionEdgeKind.WallEnd, First(dest, ProtectionZoneType.Edge).EdgeKind);
		}

		[Test]
		public void TwoSidedWall_EmitsOnePhysicalBandPerEndCap()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(4f, 0f, 0.4f), Vector3.forward, 8f, 2f));
			geo.Surfaces.Add(Wall(new Vector3(4f, 0f, -0.4f), Vector3.back, 8f, 2f));

			List<ProtectionZone> dest = Generate(geo);
			Assert.AreEqual(2, Count(dest, ProtectionZoneType.Edge),
				"two side faces must collapse from four endpoint records to two physical end-caps");
			for (int i = 0; i < dest.Count; i++)
			{
				ProtectionZone zone = dest[i];
				if (zone.GeometryType != ProtectionZoneType.Edge)
					continue;
				Assert.AreEqual(ProtectionEdgeKind.WallEnd, zone.EdgeKind);
				Assert.Less(Mathf.Abs(zone.Width - 0.8f), 0.05f);
				Assert.Greater(Mathf.Abs(Vector3.Dot(zone.Axis.normalized, Vector3.forward)), 0.9f);
				Assert.Greater(Mathf.Abs(Vector3.Dot(zone.EdgeDirection.normalized, Vector3.right)), 0.9f);
			}
		}

		[Test]
		public void ShortWall_HasTwoBoundariesWithoutThreeMeterCap()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(1f, 0f, 0f), Vector3.forward, 2f, 2f));
			List<ProtectionZone> dest = Generate(geo);
			Assert.AreEqual(1, Count(dest, ProtectionZoneType.Wall));
			Assert.AreEqual(2, Count(dest, ProtectionZoneType.Edge));
		}

		[Test]
		public void WallCenter_LiesOnSurfaceNotStandOff()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(10f, 0f, 0f), Vector3.forward, 20f, 2f));
			ProtectionZone wall = First(Generate(geo), ProtectionZoneType.Wall);
			Assert.Less(Mathf.Abs(wall.Center.z), 0.08f);
		}

		[Test]
		public void DoorCuts_MarkOpeningJambBoundaries()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.AddRange(DoorChain(4, 2.4f, 4, 2f, 0.28f, 2f));
			List<ProtectionZone> dest = Generate(geo);
			int jambs = 0;
			int wallEnds = 0;
			for (int i = 0; i < dest.Count; i++)
			{
				if (dest[i].GeometryType != ProtectionZoneType.Edge)
					continue;
				if (dest[i].EdgeKind == ProtectionEdgeKind.OpeningJamb)
					jambs++;
				if (dest[i].EdgeKind == ProtectionEdgeKind.WallEnd)
					wallEnds++;
			}

			Assert.Greater(jambs, 0);
			Assert.Greater(wallEnds, 0);
		}

		[Test]
		public void TwoSidedWall_EmitsOnePhysicalOpeningAndTwoJambs()
		{
			var geo = new RecordingGeometrySource();
			List<CoverGeometrySurface> front = DoorChain(4, 2.4f, 4, 2f, 0.28f, 2f);
			for (int i = 0; i < front.Count; i++)
			{
				CoverGeometrySurface surface = front[i];
				surface.Origin.z = 0.4f;
				geo.Surfaces.Add(surface);
				surface.Origin.z = -0.4f;
				surface.Normal = Vector3.back;
				surface.Tangent = Vector3.left;
				geo.Surfaces.Add(surface);
			}

			List<ProtectionZone> dest = Generate(geo);
			Assert.AreEqual(1, Count(dest, ProtectionZoneType.Opening));
			Assert.Less(Mathf.Abs(First(dest, ProtectionZoneType.Opening).Center.z), 0.05f);
			int jambs = 0;
			for (int i = 0; i < dest.Count; i++)
			{
				if (dest[i].GeometryType == ProtectionZoneType.Edge &&
				    dest[i].EdgeKind == ProtectionEdgeKind.OpeningJamb)
					jambs++;
			}

			Assert.AreEqual(2, jambs);
			AssertOpeningHasNoWallBridge(dest, First(dest, ProtectionZoneType.Opening));
			Assert.IsFalse(
				(First(dest, ProtectionZoneType.Opening).Capabilities & ProtectionCapabilities.CanOpen) != 0,
				"an unmarked gap is not an operable door");
		}

		[Test]
		public void WindowOpening_BecomesWindowZone()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.AddRange(GapSurfaces(2.4f, 3f, 2f));
			var probe = new ScriptedWindowProbe { PaneForAnyOpening = true };
			List<ProtectionZone> dest = Generate(geo, null, probe);
			Assert.AreEqual(1, Count(dest, ProtectionZoneType.Window));
			Assert.AreEqual(0, Count(dest, ProtectionZoneType.Opening));
			Assert.IsTrue(First(dest, ProtectionZoneType.Window).HasTransparentPane);
		}

		[Test]
		public void LRoom_OneCornerZone()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(4f, 0f, 0f), Vector3.forward, 8f, 2f));
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, 4f), Vector3.right, 8f, 2f));
			List<ProtectionZone> dest = Generate(geo, InnerCornerSlabs());
			Assert.AreEqual(1, Count(dest, ProtectionZoneType.Corner));
			ProtectionZone corner = First(dest, ProtectionZoneType.Corner);
			Assert.AreEqual(CoverCornerOrientation.Inner, corner.CornerOrientation);
			Assert.Greater(corner.CornerFacing.sqrMagnitude, 0.01f);
			Assert.Less(Vector3.Distance(corner.Center, corner.CornerVertex), 0.01f);
			Assert.Greater(corner.CornerDirectionA.sqrMagnitude, 0.5f);
			Assert.Greater(corner.CornerDirectionB.sqrMagnitude, 0.5f);
			Assert.Greater(corner.CornerMaxRadius, corner.CornerMinRadius);
			Assert.Greater(corner.CornerHalfAngleDegrees, 10f);
			Assert.AreEqual(2, Count(dest, ProtectionZoneType.Edge),
				"joined corner replaces the two internal wall-end boundaries");
		}

		[Test]
		public void OuterL_DoesNotCreateProtectedCornerPocket()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(-4f, 0f, 0f), Vector3.forward, 8f, 2f));
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, -4f), Vector3.right, 8f, 2f));

			Assert.AreEqual(0, Count(Generate(geo), ProtectionZoneType.Corner));
		}

		[Test]
		public void ShortProtrusion_DoesNotCreateProtectedCornerPocket()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(0.55f, 0f, 0f), Vector3.forward, 1.1f, 2f));
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, 4f), Vector3.right, 8f, 2f));

			Assert.AreEqual(0, Count(Generate(geo), ProtectionZoneType.Corner));
		}

		[Test]
		public void TJoinedWalls_CreateOneProtectedCornerPocket()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(Vector3.zero, Vector3.forward, 8f, 2f));
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, 2f), Vector3.right, 4f, 2f));
			var occlusion = new BoundsOcclusionProbe(
				new Bounds(new Vector3(0f, 1.1f, 0f), new Vector3(8f, 2.2f, 0.4f)),
				new Bounds(new Vector3(0f, 1.1f, 2f), new Vector3(0.4f, 2.2f, 4f)));

			Assert.AreEqual(1, Count(Generate(geo, occlusion), ProtectionZoneType.Corner));
		}

		[Test]
		public void CornerMissingSideProtection_IsRejected()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(4f, 0f, 0f), Vector3.forward, 8f, 2f));
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, 4f), Vector3.right, 8f, 2f));

			Assert.AreEqual(0, Count(Generate(geo, HighSlab()), ProtectionZoneType.Corner));
		}
		#endregion

		#region 13.2C.5 / 13.2C.10 Small object
		[Test]
		public void SmallSquare_IsOneObstacleNotEightPoints()
		{
			List<ProtectionZone> dest = Generate(SquareBox(1.2f, 1.1f));
			Assert.AreEqual(1, Count(dest, ProtectionZoneType.Obstacle));
			Assert.AreEqual(0, Count(dest, ProtectionZoneType.Wall));
			Assert.AreEqual(0, Count(dest, ProtectionZoneType.Corner));
			Assert.AreEqual(0, Count(dest, ProtectionZoneType.Edge));
			Assert.Less(First(dest, ProtectionZoneType.Obstacle).Width, 2.6f);
		}

		[Test]
		public void RotatedSquare_ObstacleAxisFollowsYaw()
		{
			List<ProtectionZone> dest = Generate(RotatedSquareBox(1.2f, 1.1f, 45f));
			Assert.AreEqual(1, Count(dest, ProtectionZoneType.Obstacle));
			Vector3 axis = First(dest, ProtectionZoneType.Obstacle).Axis;
			axis.y = 0f;
			axis.Normalize();
			Assert.Greater(Mathf.Abs(axis.x), 0.3f, "rotated obstacle must not snap Axis to world X/Z");
			Assert.Greater(Mathf.Abs(axis.z), 0.3f);
		}

		[Test]
		public void NarrowJerseyCollider_IsOneObstacleNotTwoWalls()
		{
			GameObject go = CreateBox(
				new Vector3(8300f, 0.47f, 8300f),
				Quaternion.identity,
				new Vector3(0.73f, 0.94f, 2.05f));
			try
			{
				List<ProtectionZone> dest = GeneratePhysics(
					new Bounds(new Vector3(8300f, 1f, 8300f), new Vector3(12f, 8f, 12f)));
				Assert.AreEqual(1, Count(dest, ProtectionZoneType.Obstacle), "Barrier_01-class prop is one silhouette");
				Assert.AreEqual(0, Count(dest, ProtectionZoneType.Wall));
				Assert.AreEqual(0, Count(dest, ProtectionZoneType.Edge));
				Assert.AreEqual(0, Count(dest, ProtectionZoneType.Corner));
				ProtectionZone obstacle = First(dest, ProtectionZoneType.Obstacle);
				Assert.Less(obstacle.ObstacleExtents.x, 1f);
				Assert.Greater(obstacle.ObstacleExtents.z, 1.8f);
				Assert.Less(Mathf.Abs(obstacle.ProtectionHeight - 0.94f), 0.05f);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void NarrowJerseyMeshCollider_IsOneObstacle()
		{
			GameObject go = CreateBox(
				new Vector3(8320f, 0.47f, 8320f),
				Quaternion.identity,
				new Vector3(0.73f, 0.94f, 2.05f));
			try
			{
				Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
				UnityEngine.Object.DestroyImmediate(go.GetComponent<BoxCollider>());
				MeshCollider meshCollider = go.AddComponent<MeshCollider>();
				meshCollider.sharedMesh = mesh;
				Physics.SyncTransforms();
				List<ProtectionZone> dest = GeneratePhysics(
					new Bounds(new Vector3(8320f, 1f, 8320f), new Vector3(12f, 8f, 12f)));
				Assert.AreEqual(1, Count(dest, ProtectionZoneType.Obstacle));
				Assert.AreEqual(0, Count(dest, ProtectionZoneType.Wall));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void NarrowJerseyCollider_AxisFollowsYaw()
		{
			GameObject go = CreateBox(
				new Vector3(8340f, 0.47f, 8340f),
				Quaternion.Euler(0f, 25f, 0f),
				new Vector3(0.73f, 0.94f, 2.05f));
			try
			{
				List<ProtectionZone> dest = GeneratePhysics(
					new Bounds(new Vector3(8340f, 1f, 8340f), new Vector3(12f, 8f, 12f)));
				Assert.AreEqual(1, Count(dest, ProtectionZoneType.Obstacle));
				Vector3 axis = First(dest, ProtectionZoneType.Obstacle).Axis;
				axis.y = 0f;
				axis.Normalize();
				float yaw = Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg;
				Assert.Less(Mathf.Abs(Mathf.DeltaAngle(yaw, 25f)), 2f);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void TallNarrowJersey_StaysWallFacesForMerge()
		{
			var root = new GameObject("TallJerseyLine");
			try
			{
				for (int i = 0; i < 3; i++)
				{
					GameObject go = CreateBox(
						new Vector3(8360f, 1.655f, 8360f + i * 1.5f),
						Quaternion.identity,
						new Vector3(0.78f, 3.31f, 1.76f));
					go.transform.SetParent(root.transform, true);
				}

				List<ProtectionZone> dest = GeneratePhysics(
					new Bounds(new Vector3(8360f, 2f, 8362f), new Vector3(16f, 10f, 16f)));
				Assert.AreEqual(0, Count(dest, ProtectionZoneType.Obstacle), "tall jersey must not become Obstacle");
				Assert.AreEqual(2, Count(dest, ProtectionZoneType.Wall), "collinear tall faces still merge");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void CompositeWall_InternalColliderEndCapsAreCulled()
		{
			var root = new GameObject("CompositeWall");
			try
			{
				for (int i = 0; i < 3; i++)
				{
					GameObject piece = CreateBox(
						new Vector3(8420f, 1f, 8422f + i * 4f),
						Quaternion.identity,
						new Vector3(1f, 2f, 4f));
					piece.transform.SetParent(root.transform, true);
				}

				var source = new PhysicsCoverGeometrySource();
				source.BeginObstacleCollect(new CoverGenerationSettings());
				var surfaces = new List<CoverGeometrySurface>();
				source.Collect(
					new CoverRegionId(526, 526),
					new Bounds(new Vector3(8420f, 1f, 8426f), new Vector3(12f, 8f, 20f)),
					surfaces);
				int endCaps = 0;
				for (int i = 0; i < surfaces.Count; i++)
				{
					if (Mathf.Abs(surfaces[i].Normal.z) > 0.9f)
						endCaps++;
				}

				Assert.AreEqual(2, endCaps, "only the two exposed wall ends may survive");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void NarrowJersey_WithoutSilhouettePass_StillDropsShortFaces()
		{
			GameObject go = CreateBox(
				new Vector3(8380f, 0.47f, 8380f),
				Quaternion.identity,
				new Vector3(0.73f, 0.94f, 2.05f));
			try
			{
				var dest = new List<CoverGeometrySurface>();
				new PhysicsCoverGeometrySource().Collect(
					new CoverRegionId(520, 520),
					new Bounds(new Vector3(8380f, 1f, 8380f), new Vector3(12f, 8f, 12f)),
					dest);
				Assert.AreEqual(2, dest.Count, "point-bake collect must keep 0.8 m face filter");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void RotatedBoxCollider_FacesFollowYaw()
		{
			GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
			try
			{
				go.transform.position = new Vector3(8100f, 1f, 8100f);
				go.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
				go.transform.localScale = new Vector3(8f, 2f, 0.4f);
				Physics.SyncTransforms();
				var dest = new List<CoverGeometrySurface>();
				new PhysicsCoverGeometrySource().Collect(
					new CoverRegionId(500, 500),
					new Bounds(new Vector3(8100f, 1f, 8100f), new Vector3(16f, 8f, 16f)),
					dest);
				Assert.IsTrue(HasYawAlignedFace(dest), "box faces must follow transform yaw, not world AABB");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void RotatedMeshCollider_FacesFollowYaw()
		{
			GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
			try
			{
				go.transform.position = new Vector3(8200f, 1f, 8200f);
				go.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
				go.transform.localScale = new Vector3(6f, 2f, 0.5f);
				Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
				UnityEngine.Object.DestroyImmediate(go.GetComponent<BoxCollider>());
				MeshCollider meshCollider = go.AddComponent<MeshCollider>();
				meshCollider.sharedMesh = mesh;
				Physics.SyncTransforms();
				var dest = new List<CoverGeometrySurface>();
				new PhysicsCoverGeometrySource().Collect(
					new CoverRegionId(512, 512),
					new Bounds(new Vector3(8200f, 1f, 8200f), new Vector3(16f, 8f, 16f)),
					dest);
				Assert.IsTrue(HasYawAlignedFace(dest), "mesh collider must use oriented local bounds, not world AABB");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void ConcaveMeshCollider_UsesCompositeContourNotOneAabb()
		{
			GameObject go = CreateConcavePrism(new Vector3(8400f, 0f, 8400f));
			Mesh mesh = go.GetComponent<MeshCollider>().sharedMesh;
			try
			{
				var source = new PhysicsCoverGeometrySource();
				source.BeginObstacleCollect(new CoverGenerationSettings());
				var dest = new List<CoverGeometrySurface>();
				source.Collect(
					new CoverRegionId(525, 525),
					new Bounds(new Vector3(8402f, 1f, 8402f), new Vector3(12f, 8f, 12f)),
					dest);

				bool hasInnerNotch = false;
				bool hasFakeAabbTop = false;
				for (int i = 0; i < dest.Count; i++)
				{
					CoverGeometrySurface surface = dest[i];
					if (Mathf.Abs(surface.Origin.z - 8401f) < 0.1f &&
					    surface.Length > 1.8f && surface.Length < 2.2f)
						hasInnerNotch = true;
					if (Mathf.Abs(surface.Origin.z - 8404f) < 0.1f && surface.Length > 3.5f)
						hasFakeAabbTop = true;
				}

				Assert.IsTrue(hasInnerNotch, "concave notch must be present in extracted contour");
				Assert.IsFalse(hasFakeAabbTop, "AABB must not bridge the open concave notch");

				List<ProtectionZone> zones = GeneratePhysics(
					new Bounds(new Vector3(8402f, 1f, 8402f), new Vector3(12f, 8f, 12f)));
				Assert.AreEqual(0, Count(zones, ProtectionZoneType.Obstacle),
					"a fragment of one complex collider must not be reclassified as a small obstacle");
				Assert.GreaterOrEqual(Count(zones, ProtectionZoneType.Wall), 8);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
				UnityEngine.Object.DestroyImmediate(mesh);
			}
		}

		[Test]
		public void PhysicsCollect_KeepsBothEndsOfLongSpread()
		{
			var root = new GameObject("ZoneCollectTiles");
			try
			{
				for (int i = 0; i < 140; i++)
				{
					GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
					cube.transform.SetParent(root.transform, false);
					cube.transform.position = new Vector3(8000f, 1f, 8000f + i);
					cube.transform.localScale = new Vector3(2f, 2f, 2f);
				}

				Physics.SyncTransforms();
				var dest = new List<CoverGeometrySurface>();
				new PhysicsCoverGeometrySource().Collect(
					new CoverRegionId(500, 500),
					new Bounds(new Vector3(8000f, 1f, 8070f), new Vector3(12f, 8f, 160f)),
					dest);
				bool south = false;
				bool north = false;
				for (int i = 0; i < dest.Count; i++)
				{
					if (dest[i].Origin.z < 8010f)
						south = true;
					if (dest[i].Origin.z > 8125f)
						north = true;
				}

				Assert.IsTrue(south, "collect must not drop the first tiles");
				Assert.IsTrue(north, "collect must not drop the last tiles (OverlapBox cap)");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}
		#endregion

		#region 13.2C.6 Region boundary
		[Test]
		public void GeometryOnRegionBoundary_IsOneZone()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(2f, 0f, 0f), Vector3.forward, 20f, 2f));
			List<ProtectionZone> dest = Generate(geo);
			Assert.AreEqual(1, Count(dest, ProtectionZoneType.Wall));
			CoverRegionId region = First(dest, ProtectionZoneType.Wall).RegionId;
			Assert.AreEqual(region, CoverSpatialMath.WorldToRegion(
				First(dest, ProtectionZoneType.Wall).Center,
				CoverSpatialMath.DefaultRegionSizeMeters));
		}
		#endregion

		#region Determinism
		[Test]
		public void ZoneBake_IsDeterministic()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.AddRange(DoorChain(4, 2.4f, 4, 2f, 0.28f, 2f));
			List<ProtectionZone> a = Generate(geo);
			List<ProtectionZone> b = Generate(geo);
			Assert.AreEqual(a.Count, b.Count);
			for (int i = 0; i < a.Count; i++)
			{
				Assert.AreEqual(a[i].GeometryType, b[i].GeometryType);
				Assert.AreEqual(a[i].ZoneId, b[i].ZoneId);
				Assert.Less(Vector3.Distance(a[i].Center, b[i].Center), 0.001f);
				Assert.Less(Mathf.Abs(a[i].Width - b[i].Width), 0.001f);
			}
		}
		#endregion

		#region Helpers
		private static ProtectionZone SampleWall()
		{
			return new ProtectionZone
			{
				ZoneId = 3,
				GeometryType = ProtectionZoneType.Wall,
				Center = new Vector3(4f, 0f, 0.45f),
				Axis = Vector3.right,
				Width = 12f,
				Depth = 0.65f,
				ProtectionHeight = 2f,
				SurfaceNormal = Vector3.forward,
				Capabilities = ProtectionCapabilities.CanPeek,
				RegionId = new CoverRegionId(-1, 2),
				GeometryVersion = 7,
				NavMeshValid = true
			};
		}

		private static List<ProtectionZone> Generate(
			RecordingGeometrySource _geo,
			ICoverOcclusionProbe _occlusion = null,
			ICoverWindowProbe _window = null)
		{
			return GenerateFromSource(
				_geo,
				new Bounds(new Vector3(10f, 0f, 0f), new Vector3(48f, 8f, 24f)),
				_occlusion,
				_window);
		}

		private static List<ProtectionZone> GeneratePhysics(Bounds _worldBounds)
		{
			return GenerateFromSource(new PhysicsCoverGeometrySource(), _worldBounds, null, null);
		}

		private static List<ProtectionZone> GenerateFromSource(
			ICoverGeometrySource _geo,
			Bounds _worldBounds,
			ICoverOcclusionProbe _occlusion,
			ICoverWindowProbe _window)
		{
			var gen = new ProtectionZoneGenerator(
				_geo,
				new AcceptNavMesh(),
				new AcceptClearance(),
				new CoverGenerationSettings(),
				_occlusion,
				new CoverClassificationSettings(),
				_window);
			var dest = new List<ProtectionZone>();
			gen.Generate(_worldBounds, 1, dest);
			return dest;
		}

		private static GameObject CreateBox(Vector3 _position, Quaternion _rotation, Vector3 _scale)
		{
			GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
			go.transform.position = _position;
			go.transform.rotation = _rotation;
			go.transform.localScale = _scale;
			Physics.SyncTransforms();
			return go;
		}

		private static GameObject CreateConcavePrism(Vector3 _position)
		{
			Vector2[] polygon =
			{
				new Vector2(0f, 0f),
				new Vector2(4f, 0f),
				new Vector2(4f, 4f),
				new Vector2(3f, 4f),
				new Vector2(3f, 1f),
				new Vector2(1f, 1f),
				new Vector2(1f, 4f),
				new Vector2(0f, 4f)
			};
			var vertices = new Vector3[polygon.Length * 2];
			for (int i = 0; i < polygon.Length; i++)
			{
				vertices[i] = new Vector3(polygon[i].x, 0f, polygon[i].y);
				vertices[i + polygon.Length] = new Vector3(polygon[i].x, 2f, polygon[i].y);
			}

			var triangles = new List<int>(84);
			for (int i = 0; i < polygon.Length; i++)
			{
				int next = (i + 1) % polygon.Length;
				triangles.Add(i);
				triangles.Add(next);
				triangles.Add(next + polygon.Length);
				triangles.Add(i);
				triangles.Add(next + polygon.Length);
				triangles.Add(i + polygon.Length);
			}

			int[] top =
			{
				0, 4, 1,
				0, 5, 4,
				1, 3, 2,
				1, 4, 3,
				0, 6, 5,
				0, 7, 6
			};
			for (int i = 0; i < top.Length; i += 3)
			{
				triangles.Add(top[i] + polygon.Length);
				triangles.Add(top[i + 1] + polygon.Length);
				triangles.Add(top[i + 2] + polygon.Length);
				triangles.Add(top[i + 2]);
				triangles.Add(top[i + 1]);
				triangles.Add(top[i]);
			}

			var mesh = new Mesh { name = "ConcaveProtectionTestMesh" };
			mesh.vertices = vertices;
			mesh.triangles = triangles.ToArray();
			mesh.RecalculateBounds();
			var go = new GameObject("ConcaveProtectionTest");
			go.transform.position = _position;
			MeshCollider collider = go.AddComponent<MeshCollider>();
			collider.sharedMesh = mesh;
			Physics.SyncTransforms();
			return go;
		}

		private static int Count(List<ProtectionZone> _list, ProtectionZoneType _type)
		{
			int n = 0;
			for (int i = 0; i < _list.Count; i++)
			{
				if (_list[i] != null && _list[i].GeometryType == _type)
					n++;
			}

			return n;
		}

		private static void AssertOpeningHasNoWallBridge(
			IReadOnlyList<ProtectionZone> _zones,
			ProtectionZone _opening)
		{
			Assert.IsNotNull(_opening);
			Vector3 axis = _opening.OpeningAxis.normalized;
			Vector3 normal = _opening.SurfaceNormal.normalized;
			float halfOpening = _opening.OpeningWidth * 0.5f;
			for (int i = 0; i < _zones.Count; i++)
			{
				ProtectionZone wall = _zones[i];
				if (wall == null || wall.GeometryType != ProtectionZoneType.Wall)
					continue;
				if (Mathf.Abs(Vector3.Dot(wall.SurfaceNormal.normalized, normal)) < 0.85f)
					continue;
				if (Mathf.Abs(Vector3.Dot(wall.Center - _opening.OpeningCenter, normal)) > 0.55f)
					continue;
				if (Mathf.Abs(Vector3.Dot(wall.Axis.normalized, axis)) < 0.85f)
					continue;

				float center = Vector3.Dot(wall.Center - _opening.OpeningCenter, axis);
				float overlap = Mathf.Min(center + wall.Width * 0.5f, halfOpening) -
				                Mathf.Max(center - wall.Width * 0.5f, -halfOpening);
				Assert.LessOrEqual(overlap, 0.08f, "Opening must remain an empty Surface gap");
			}
		}

		private static ProtectionZone First(List<ProtectionZone> _list, ProtectionZoneType _type)
		{
			for (int i = 0; i < _list.Count; i++)
			{
				if (_list[i] != null && _list[i].GeometryType == _type)
					return _list[i];
			}

			return null;
		}

		private static RecordingGeometrySource SquareBox(float _size, float _height)
		{
			float half = _size * 0.5f;
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, half), Vector3.forward, _size, _height));
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, -half), Vector3.back, _size, _height));
			geo.Surfaces.Add(Wall(new Vector3(half, 0f, 0f), Vector3.right, _size, _height));
			geo.Surfaces.Add(Wall(new Vector3(-half, 0f, 0f), Vector3.left, _size, _height));
			return geo;
		}

		private static List<CoverGeometrySurface> OverlapChain(
			int _pieces,
			float _pieceLength,
			float _overlap,
			float _height)
		{
			var list = new List<CoverGeometrySurface>(_pieces);
			float step = Mathf.Max(0.05f, _pieceLength - _overlap);
			for (int i = 0; i < _pieces; i++)
			{
				float x = _pieceLength * 0.5f + i * step;
				list.Add(Wall(new Vector3(x, 0f, 0f), Vector3.forward, _pieceLength, _height));
			}

			return list;
		}

		private static List<CoverGeometrySurface> DoorChain(
			int _leftPieces,
			float _gapMeters,
			int _rightPieces,
			float _pieceLength,
			float _overlap,
			float _height)
		{
			List<CoverGeometrySurface> list = OverlapChain(_leftPieces, _pieceLength, _overlap, _height);
			float step = Mathf.Max(0.05f, _pieceLength - _overlap);
			float leftEnd = _pieceLength + (_leftPieces - 1) * step;
			float rightStart = leftEnd + _gapMeters;
			for (int i = 0; i < _rightPieces; i++)
			{
				float x = rightStart + _pieceLength * 0.5f + i * step;
				list.Add(Wall(new Vector3(x, 0f, 0f), Vector3.forward, _pieceLength, _height));
			}

			return list;
		}

		private static List<CoverGeometrySurface> GapSurfaces(float _gapMeters, float _length, float _height)
		{
			float leftOrigin = _length * 0.5f;
			float rightOrigin = _length + _gapMeters + _length * 0.5f;
			return new List<CoverGeometrySurface>
			{
				Wall(new Vector3(leftOrigin, 0f, 0f), Vector3.forward, _length, _height),
				Wall(new Vector3(rightOrigin, 0f, 0f), Vector3.forward, _length, _height)
			};
		}

		private static RecordingGeometrySource RotatedSquareBox(float _size, float _height, float _yawDegrees)
		{
			Quaternion rot = Quaternion.Euler(0f, _yawDegrees, 0f);
			float half = _size * 0.5f;
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(rot * new Vector3(0f, 0f, half), rot * Vector3.forward, _size, _height));
			geo.Surfaces.Add(Wall(rot * new Vector3(0f, 0f, -half), rot * Vector3.back, _size, _height));
			geo.Surfaces.Add(Wall(rot * new Vector3(half, 0f, 0f), rot * Vector3.right, _size, _height));
			geo.Surfaces.Add(Wall(rot * new Vector3(-half, 0f, 0f), rot * Vector3.left, _size, _height));
			return geo;
		}

		private static bool HasYawAlignedFace(List<CoverGeometrySurface> _surfaces)
		{
			for (int i = 0; i < _surfaces.Count; i++)
			{
				Vector3 n = _surfaces[i].Normal;
				n.y = 0f;
				if (n.sqrMagnitude < 0.5f)
					continue;
				n.Normalize();
				if (Mathf.Abs(n.x) > 0.3f && Mathf.Abs(n.z) > 0.3f)
					return true;
			}

			return false;
		}

		private static CoverGeometrySurface Wall(Vector3 _origin, Vector3 _normal, float _length, float _height)
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
				Length = _length,
				Height = _height
			};
		}

		private static SlabCoverOcclusionProbe HighSlab()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(10f, 1.1f, 0f), new Vector3(32f, 2.2f, 0.4f)));
		}

		private static BoundsOcclusionProbe InnerCornerSlabs()
		{
			return new BoundsOcclusionProbe(
				new Bounds(new Vector3(4f, 1.1f, 0f), new Vector3(8f, 2.2f, 0.4f)),
				new Bounds(new Vector3(0f, 1.1f, 4f), new Vector3(0.4f, 2.2f, 8f)));
		}

		private static SlabCoverOcclusionProbe LowSlab()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(10f, 0.575f, 0f), new Vector3(32f, 1.15f, 0.4f)));
		}
		#endregion
	}
}

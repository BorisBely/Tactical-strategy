using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #13.2B.0–13.2B.5A Extended Cover Position Bake. Geometry + type only. No unit behavior.
	/// </summary>
	public sealed class ExtendedCoverBakeTests
	{
		#region Nested
		[Serializable]
		private sealed class RecordWrap
		{
			public BakedCoverCandidateRecord Record;
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

		private sealed class AcceptNavMesh : ICoverNavMeshProbe
		{
			public bool TrySample(Vector3 _world, out Vector3 _onMesh)
			{
				_onMesh = _world;
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

		private sealed class ScriptedWindowProbe : ICoverWindowProbe
		{
			public bool PaneForAnyOpening;
			public Vector3 PaneWorld;
			public float PaneRadius;
			public bool HasFrame = true;

			public bool TryInspect(CoverCandidate _opening, out CoverWindowHit _hit)
			{
				_hit = default;
				if (_opening == null || !_opening.OpeningValid)
					return false;

				bool pane = PaneForAnyOpening;
				if (!pane && PaneRadius > 0.01f)
				{
					pane = CoverSpatialMath.PlanarDistanceSqr(_opening.OpeningCenter, PaneWorld) <=
					       PaneRadius * PaneRadius;
				}

				if (!pane)
					return false;

				Vector3 center = PaneForAnyOpening ? _opening.OpeningCenter : PaneWorld;
				center.y = 0f;
				_hit = new CoverWindowHit
				{
					HasTransparentPane = true,
					HasFrame = HasFrame,
					Center = center,
					Axis = _opening.OpeningAxis,
					Width = _opening.OpeningWidth
				};
				return true;
			}
		}

		private sealed class ScriptedSeamProbe : ICoverSeamProbe
		{
			public bool Solid;

			public bool HasSolidInGap(Vector3 _alongStart, Vector3 _alongEnd, Vector3 _normal)
			{
				return Solid;
			}
		}
		#endregion

		#region 13.2B.0 Data model
		[Test]
		public void EnumInts_DoNotShiftFrozenSlots()
		{
			Assert.AreEqual(0, (int)CoverType.None);
			Assert.AreEqual(1, (int)CoverType.Crouch);
			Assert.AreEqual(2, (int)CoverType.Standing);
			Assert.AreEqual(3, (int)CoverType.Partial);
			Assert.AreEqual(4, (int)CoverType.Corner);
			Assert.AreEqual(5, (int)CoverType.Edge);
			Assert.AreEqual(6, (int)CoverType.Opening);
			Assert.AreEqual(7, (int)CoverType.Window);
			Assert.AreEqual(1 << 8, (int)CoverCapabilities.CanObserveThrough);
		}

		[Test]
		public void BakeRecord_FromToCandidate_Roundtrip()
		{
			CoverCandidate source = MakeEdgeCandidate();
			BakedCoverCandidateRecord record = BakedCoverCandidateRecord.FromCandidate(source);
			Assert.AreEqual(CoverType.Edge, record.CoverType);
			Assert.IsTrue(record.EdgeValid);
			Assert.AreEqual(source.Capabilities, record.Capabilities);
			CoverCandidate copy = record.ToCandidate();
			Assert.AreEqual(source.CoverType, copy.CoverType);
			Assert.IsTrue(copy.EdgeValid);
			Assert.IsFalse(copy.EdgeSeed);
			Assert.AreEqual(source.Capabilities, copy.Capabilities);
			Assert.Less(Vector3.Distance(source.Position, copy.Position), 0.001f);
			Assert.Less(Vector3.Distance(source.EdgeDirection, copy.EdgeDirection), 0.001f);
			Assert.AreEqual(source.StandingProfile.Torso, copy.StandingProfile.Torso, 0.001f);
			Assert.AreEqual(CoverOccupancy.Available, copy.Occupancy);
		}

		[Test]
		public void BakeRecord_JsonUtility_PreservesTypes()
		{
			var wrap = new RecordWrap { Record = BakedCoverCandidateRecord.FromCandidate(MakeEdgeCandidate()) };
			string json = JsonUtility.ToJson(wrap);
			RecordWrap loaded = JsonUtility.FromJson<RecordWrap>(json);
			Assert.IsNotNull(loaded);
			Assert.AreEqual(CoverType.Edge, loaded.Record.CoverType);
			Assert.IsTrue(loaded.Record.EdgeValid);
			Assert.AreEqual(CoverCapabilities.CanPeek | CoverCapabilities.CanStand, loaded.Record.Capabilities);
			Assert.AreEqual(3, loaded.Record.CandidateId);
			Assert.AreEqual(CoverType.Window, JsonRoundtripType(CoverType.Window));
			Assert.AreEqual(CoverType.Opening, JsonRoundtripType(CoverType.Opening));
			Assert.AreEqual(CoverType.Corner, JsonRoundtripType(CoverType.Corner));
		}

		[Test]
		public void PlayBakeSource_DoesNotQueryGeometry()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(LongWall());
			CoverCandidate source = MakeEdgeCandidate();
			source.RegionId = new CoverRegionId(0, 0);
			var baked = new List<BakedCoverCandidateRecord>
			{
				BakedCoverCandidateRecord.FromCandidate(source)
			};
			var playSource = new BakedCoverCandidateSource(baked);
			var dest = new List<CoverCandidate>();
			playSource.Generate(new CoverRegionId(0, 0), new Bounds(Vector3.zero, Vector3.one * 20f), 9, dest);
			Assert.AreEqual(0, geo.QueryCount);
			Assert.AreEqual(1, dest.Count);
			Assert.AreEqual(CoverType.Edge, dest[0].CoverType);
			Assert.AreEqual(9, dest[0].GeometryVersion);
		}

		[Test]
		public void PeekGeometry_DoesNotTreatEdgeAsRuntimePeek()
		{
			Assert.IsFalse(CoverPeekGeometry.CanPeek(CoverType.Edge));
			Assert.IsFalse(CoverPeekGeometry.CanPeek(CoverType.Opening));
			Assert.IsFalse(CoverPeekGeometry.CanPeek(CoverType.Window));
			Assert.IsTrue(CoverPeekGeometry.CanPeek(CoverType.Corner));
		}
		#endregion

		#region 13.2B.1 Edge
		[Test]
		public void LongWall_TwoHiddenEdges()
		{
			List<CoverCandidate> dest = Generate(LongWallSource(), HighWall());
			List<CoverCandidate> edges = EdgesOf(dest);
			Assert.AreEqual(2, edges.Count, "long wall must bake two hidden Edge bases, not peek pairs");
			Assert.Greater(CoverSpatialMath.PlanarDistanceSqr(edges[0].Position, edges[1].Position), 9f);
			for (int i = 0; i < edges.Count; i++)
			{
				Assert.IsTrue(edges[i].EdgeValid);
				Assert.IsTrue(edges[i].StandingValid || edges[i].CrouchValid);
				Assert.Greater(edges[i].EdgeDirection.sqrMagnitude, 0.01f);
				Assert.AreEqual(CoverType.Edge, edges[i].CoverType);
				Assert.AreEqual(CoverCapabilities.CanPeek, edges[i].Capabilities & CoverCapabilities.CanPeek);
			}
		}

		[Test]
		public void ShortWall_BelowMinLength_NoEdge()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(5f, 0f, 0f), Vector3.forward, 2f));
			List<CoverCandidate> dest = Generate(geo, HighWall());
			Assert.Greater(dest.Count, 0);
			Assert.AreEqual(0, EdgesOf(dest).Count);
		}

		[Test]
		public void HighMidWall_IsNotEdge()
		{
			List<CoverCandidate> dest = Generate(LongWallSource(), HighWall());
			CoverCandidate mid = Closest(dest, new Vector3(5f, 0f, 0.45f));
			Assert.IsNotNull(mid);
			Assert.IsFalse(mid.EdgeValid);
			Assert.AreEqual(CoverType.None, mid.CoverType);
			Assert.IsTrue(mid.StandingValid);
			Assert.IsFalse(mid.IsTacticalSelectable);
		}

		[Test]
		public void EdgeInsideOtherCandidate_DedupOneSlot()
		{
			List<CoverCandidate> dest = Generate(LongWallSource(), HighWall());
			List<CoverCandidate> edges = EdgesOf(dest);
			Assert.AreEqual(2, edges.Count);
			float radiusSqr = 0.75f * 0.75f;
			for (int i = 0; i < dest.Count; i++)
			{
				for (int j = i + 1; j < dest.Count; j++)
				{
					if (!dest[i].EdgeValid && !dest[j].EdgeValid)
						continue;
					if (Vector3.Dot(dest[i].Normal, dest[j].Normal) <= 0.5f)
						continue;
					Assert.Greater(
						CoverSpatialMath.PlanarDistanceSqr(dest[i].Position, dest[j].Position),
						radiusSqr);
				}
			}
		}

		[Test]
		public void EdgeWithoutClearance_Rejected()
		{
			CoverGeometrySurface wall = LongWall();
			var settings = new CoverGenerationSettings();
			var clearance = new ScriptedClearance { BlockRadius = 1.3f };
			clearance.Blocked.Add(CoverEdgeGeometry.EndSamplePosition(
				wall, settings.StandOffMeters, settings.EdgeInsetMeters, true));
			clearance.Blocked.Add(CoverEdgeGeometry.EndSamplePosition(
				wall, settings.StandOffMeters, settings.EdgeInsetMeters, false));
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(wall);
			CoverCandidateGenerator gen = MakeGenerator(geo, HighWall(), clearance);
			List<CoverCandidate> dest = Generate(gen, geo);
			Assert.Greater(gen.LastRejectedClearanceCount, 0);
			Assert.AreEqual(0, EdgesOf(dest).Count);
			Assert.Greater(dest.Count, 0);
		}

		[Test]
		public void EdgeBake_IsDeterministic()
		{
			RecordingGeometrySource geo = LongWallSource();
			List<CoverCandidate> a = Generate(geo, HighWall());
			List<CoverCandidate> b = Generate(geo, HighWall());
			Assert.AreEqual(a.Count, b.Count);
			for (int i = 0; i < a.Count; i++)
			{
				Assert.AreEqual(a[i].CoverType, b[i].CoverType);
				Assert.AreEqual(a[i].EdgeValid, b[i].EdgeValid);
				Assert.Less(Mathf.Abs(a[i].Position.x - b[i].Position.x), 0.001f);
				Assert.Less(Mathf.Abs(a[i].Position.z - b[i].Position.z), 0.001f);
			}
		}

		[Test]
		public void ClassifyWithoutOcclusion_DoesNotInventEdge()
		{
			List<CoverCandidate> dest = Generate(LongWallSource(), null);
			Assert.Greater(dest.Count, 0);
			Assert.AreEqual(0, EdgesOf(dest).Count);
			Assert.AreEqual(CoverType.None, dest[0].CoverType);
		}
		#endregion

		#region 13.2B.2 Opening
		[Test]
		public void DoorOpening_OneBaseCandidate()
		{
			List<CoverCandidate> dest = Generate(GapSource(1f), null);
			List<CoverCandidate> openings = OpeningsOf(dest);
			Assert.AreEqual(1, openings.Count, "one doorway must bake one Opening, not left/right/stand variants");
			CoverCandidate opening = openings[0];
			Assert.AreEqual(CoverType.Opening, opening.CoverType);
			Assert.IsTrue(opening.OpeningValid);
			Assert.Greater(opening.OpeningWidth, 0.8f);
			Assert.Less(opening.OpeningWidth, 1.3f);
			Assert.Greater(opening.OpeningAxis.sqrMagnitude, 0.01f);
			Assert.Less(Mathf.Abs(opening.OpeningCenter.x - 3.5f), 0.2f);
			Assert.AreEqual(
				CoverCapabilities.CanStepLeft | CoverCapabilities.CanStepRight,
				opening.Capabilities & (CoverCapabilities.CanStepLeft | CoverCapabilities.CanStepRight));
		}

		[Test]
		public void WideOpening_Found()
		{
			List<CoverCandidate> dest = Generate(GapSource(2.4f), null);
			Assert.AreEqual(1, OpeningsOf(dest).Count);
			Assert.Greater(OpeningsOf(dest)[0].OpeningWidth, 2f);
		}

		[Test]
		public void BreachOpening_Found()
		{
			List<CoverCandidate> dest = Generate(GapSource(1.8f), null);
			Assert.AreEqual(1, OpeningsOf(dest).Count);
		}

		[Test]
		public void TooNarrowGap_Rejected()
		{
			List<CoverCandidate> dest = Generate(GapSource(0.3f), null);
			Assert.AreEqual(0, OpeningsOf(dest).Count);
		}

		[Test]
		public void TooWideCorridor_NotOpening()
		{
			List<CoverCandidate> dest = Generate(GapSource(8f), null);
			Assert.AreEqual(0, OpeningsOf(dest).Count);
		}

		[Test]
		public void FarIndependentWalls_NoOpening()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(2f, 0f, 0f), Vector3.forward, 4f));
			geo.Surfaces.Add(Wall(new Vector3(2f, 0f, 8f), Vector3.forward, 4f));
			Assert.AreEqual(0, OpeningsOf(Generate(geo, null)).Count);
		}

		[Test]
		public void IndependentShortProps_NoOpening()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(1.5f, 0f, 1.5f), Vector3.forward, 1f));
			geo.Surfaces.Add(Wall(new Vector3(3.9f, 0f, 1.5f), Vector3.forward, 1f));
			Assert.AreEqual(0, OpeningsOf(Generate(geo, null)).Count);
		}

		[Test]
		public void LWall_IsNotOpening()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(4f, 0f, 0f), Vector3.forward, 8f));
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, 4f), Vector3.right, 8f));
			Assert.AreEqual(0, OpeningsOf(Generate(geo, null)).Count);
		}

		[Test]
		public void OpeningWithoutNavMesh_Rejected()
		{
			RecordingGeometrySource geo = GapSource(1f);
			var nav = new ScriptedNavMesh();
			CoverOpeningSeed seed = FirstSeed(geo);
			nav.Blocked.Add(CoverOpeningGeometry.StandPosition(in seed, 0.45f));
			nav.BlockRadius = 0.4f;
			CoverCandidateGenerator gen = MakeGenerator(geo, null, null, nav);
			List<CoverCandidate> dest = Generate(gen, geo);
			Assert.Greater(gen.LastRejectedNavMeshCount, 0);
			Assert.AreEqual(0, OpeningsOf(dest).Count);
		}

		[Test]
		public void DuplicateColliders_OneOpening()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(1.4f, 0f, 0f), Vector3.forward, 3f));
			geo.Surfaces.Add(Wall(new Vector3(1.5f, 0f, 0f), Vector3.forward, 3f));
			geo.Surfaces.Add(Wall(new Vector3(1.6f, 0f, 0f), Vector3.forward, 3f));
			geo.Surfaces.Add(Wall(new Vector3(5.5f, 0f, 0f), Vector3.forward, 3f));
			Assert.AreEqual(1, OpeningsOf(Generate(geo, null)).Count);
		}

		[Test]
		public void DoorPassage_NotThreeIndependentSlots()
		{
			List<CoverCandidate> dest = Generate(GapSource(1f), DoorWall());
			List<CoverCandidate> openings = OpeningsOf(dest);
			Assert.AreEqual(1, openings.Count);
			int passageSlots = 0;
			for (int i = 0; i < dest.Count; i++)
			{
				if (CoverSpatialMath.PlanarDistanceSqr(dest[i].Position, openings[0].OpeningCenter) <= 1.1f * 1.1f)
					passageSlots++;
			}

			Assert.AreEqual(1, passageSlots);
		}

		[Test]
		public void PlayBakeSource_ReadsOpeningWithoutGeometry()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.AddRange(GapSource(1f).Surfaces);
			CoverCandidate source = MakeOpeningCandidate();
			source.RegionId = new CoverRegionId(0, 0);
			var playSource = new BakedCoverCandidateSource(
				new List<BakedCoverCandidateRecord> { BakedCoverCandidateRecord.FromCandidate(source) });
			var dest = new List<CoverCandidate>();
			playSource.Generate(new CoverRegionId(0, 0), new Bounds(Vector3.zero, Vector3.one * 20f), 11, dest);
			Assert.AreEqual(0, geo.QueryCount);
			Assert.AreEqual(1, dest.Count);
			Assert.AreEqual(CoverType.Opening, dest[0].CoverType);
			Assert.IsTrue(dest[0].OpeningValid);
			Assert.AreEqual(1.1f, dest[0].OpeningWidth, 0.001f);
			Assert.AreEqual(11, dest[0].GeometryVersion);
		}

		[Test]
		public void OpeningBake_IsDeterministic()
		{
			RecordingGeometrySource geo = GapSource(1f);
			List<CoverCandidate> a = Generate(geo, null);
			List<CoverCandidate> b = Generate(geo, null);
			Assert.AreEqual(a.Count, b.Count);
			for (int i = 0; i < a.Count; i++)
			{
				Assert.AreEqual(a[i].CoverType, b[i].CoverType);
				Assert.AreEqual(a[i].OpeningValid, b[i].OpeningValid);
				Assert.Less(Mathf.Abs(a[i].OpeningWidth - b[i].OpeningWidth), 0.001f);
			}
		}
		#endregion

		#region 13.2B.3 Window
		[Test]
		public void TacticalTransparent_IsSemanticMarker()
		{
			var go = new GameObject("TacticalTransparentMarker");
			try
			{
				BoxCollider collider = go.AddComponent<BoxCollider>();
				Assert.IsFalse(TacticalTransparency.IsMarked(collider));
				go.AddComponent<TacticalTransparent>();
				Assert.IsTrue(TacticalTransparency.IsMarked(collider));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void OpeningWithoutGlass_StaysOpening()
		{
			RecordingGeometrySource geo = GapSource(1f);
			CoverCandidateGenerator gen = MakeGenerator(geo, null, null, null, new ScriptedWindowProbe());
			List<CoverCandidate> dest = Generate(gen, geo);
			List<CoverCandidate> openings = OpeningsOf(dest);
			Assert.AreEqual(1, openings.Count);
			Assert.AreEqual(CoverType.Opening, openings[0].CoverType);
			Assert.IsTrue(openings[0].OpeningValid);
			Assert.IsFalse(openings[0].WindowValid);
			Assert.IsFalse(openings[0].HasTransparentPane);
			Assert.AreEqual(0, WindowsOf(dest).Count);
		}

		[Test]
		public void OpeningWithGlassPane_IsWindow()
		{
			RecordingGeometrySource geo = GapSource(1f);
			var probe = new ScriptedWindowProbe { PaneForAnyOpening = true, HasFrame = true };
			List<CoverCandidate> dest = Generate(MakeGenerator(geo, null, null, null, probe), geo);
			List<CoverCandidate> windows = WindowsOf(dest);
			Assert.AreEqual(1, windows.Count, "Opening + pane must become one Window");
			CoverCandidate window = windows[0];
			Assert.AreEqual(CoverType.Window, window.CoverType);
			Assert.IsTrue(window.OpeningValid);
			Assert.IsTrue(window.WindowValid);
			Assert.IsTrue(window.HasTransparentPane);
			Assert.IsTrue(window.HasFrame);
			Assert.Greater(window.WindowWidth, 0.8f);
			Assert.Greater(window.WindowAxis.sqrMagnitude, 0.01f);
			Assert.AreEqual(
				CoverCapabilities.CanFireThrough | CoverCapabilities.CanObserveThrough,
				window.Capabilities & (CoverCapabilities.CanFireThrough | CoverCapabilities.CanObserveThrough));
		}

		[Test]
		public void GlassWallWithoutOpening_IsNotWindow()
		{
			RecordingGeometrySource geo = LongWallSource();
			var probe = new ScriptedWindowProbe { PaneForAnyOpening = true };
			List<CoverCandidate> dest = Generate(MakeGenerator(geo, HighWall(), null, null, probe), geo);
			Assert.Greater(dest.Count, 0);
			Assert.AreEqual(0, OpeningsOf(dest).Count);
			Assert.AreEqual(0, WindowsOf(dest).Count);
		}

		[Test]
		public void DecorativeTransparent_AwayFromOpening_IsNotWindow()
		{
			RecordingGeometrySource geo = GapSource(1f);
			var probe = new ScriptedWindowProbe
			{
				PaneWorld = new Vector3(80f, 0f, 80f),
				PaneRadius = 1.5f,
				HasFrame = true
			};
			List<CoverCandidate> dest = Generate(MakeGenerator(geo, null, null, null, probe), geo);
			Assert.AreEqual(1, OpeningsOf(dest).Count);
			Assert.AreEqual(CoverType.Opening, OpeningsOf(dest)[0].CoverType);
			Assert.AreEqual(0, WindowsOf(dest).Count);
		}

		[Test]
		public void FrameGlassSill_OneWindowCandidate()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(1.4f, 0f, 0f), Vector3.forward, 3f));
			geo.Surfaces.Add(Wall(new Vector3(1.5f, 0f, 0f), Vector3.forward, 3f));
			geo.Surfaces.Add(Wall(new Vector3(1.6f, 0f, 0f), Vector3.forward, 3f));
			geo.Surfaces.Add(Wall(new Vector3(5.5f, 0f, 0f), Vector3.forward, 3f));
			var probe = new ScriptedWindowProbe { PaneForAnyOpening = true, HasFrame = true };
			List<CoverCandidate> dest = Generate(MakeGenerator(geo, null, null, null, probe), geo);
			Assert.AreEqual(1, WindowsOf(dest).Count, "frame + glass + sill must stay one Window");
			Assert.AreEqual(CoverType.Window, WindowsOf(dest)[0].CoverType);
		}

		[Test]
		public void WindowWithoutNavMesh_Rejected()
		{
			RecordingGeometrySource geo = GapSource(1f);
			var nav = new ScriptedNavMesh();
			CoverOpeningSeed seed = FirstSeed(geo);
			nav.Blocked.Add(CoverOpeningGeometry.StandPosition(in seed, 0.45f));
			nav.BlockRadius = 0.4f;
			var probe = new ScriptedWindowProbe { PaneForAnyOpening = true };
			CoverCandidateGenerator gen = MakeGenerator(geo, null, null, nav, probe);
			List<CoverCandidate> dest = Generate(gen, geo);
			Assert.Greater(gen.LastRejectedNavMeshCount, 0);
			Assert.AreEqual(0, OpeningsOf(dest).Count);
			Assert.AreEqual(0, WindowsOf(dest).Count);
		}

		[Test]
		public void LWall_IsNotWindow()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(4f, 0f, 0f), Vector3.forward, 8f));
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, 4f), Vector3.right, 8f));
			var probe = new ScriptedWindowProbe { PaneForAnyOpening = true };
			List<CoverCandidate> dest = Generate(MakeGenerator(geo, HighWall(), null, null, probe), geo);
			Assert.AreEqual(0, WindowsOf(dest).Count);
			Assert.AreEqual(0, OpeningsOf(dest).Count);
		}

		[Test]
		public void MidWallAndEdges_UnchangedWithWindowProbe()
		{
			RecordingGeometrySource geo = LongWallSource();
			var probe = new ScriptedWindowProbe { PaneForAnyOpening = true };
			List<CoverCandidate> dest = Generate(MakeGenerator(geo, HighWall(), null, null, probe), geo);
			CoverCandidate mid = Closest(dest, new Vector3(5f, 0f, 0.45f));
			Assert.IsNotNull(mid);
			Assert.IsFalse(mid.EdgeValid);
			Assert.AreEqual(CoverType.None, mid.CoverType);
			Assert.IsTrue(mid.StandingValid);
			Assert.IsFalse(mid.IsTacticalSelectable);
			Assert.AreEqual(2, EdgesOf(dest).Count);
			Assert.AreEqual(0, WindowsOf(dest).Count);
		}

		[Test]
		public void BakeRecord_WindowFields_Roundtrip()
		{
			CoverCandidate source = MakeWindowCandidate();
			BakedCoverCandidateRecord record = BakedCoverCandidateRecord.FromCandidate(source);
			Assert.AreEqual(CoverType.Window, record.CoverType);
			Assert.IsTrue(record.WindowValid);
			Assert.IsTrue(record.OpeningValid);
			Assert.IsTrue(record.HasFrame);
			Assert.IsTrue(record.HasTransparentPane);
			Assert.AreEqual(1.1f, record.WindowWidth, 0.001f);
			CoverCandidate copy = record.ToCandidate();
			Assert.AreEqual(CoverType.Window, copy.CoverType);
			Assert.IsTrue(copy.WindowValid);
			Assert.IsTrue(copy.OpeningValid);
			Assert.IsTrue(copy.HasTransparentPane);
			Assert.AreEqual(CoverOccupancy.Available, copy.Occupancy);
			Assert.Less(Vector3.Distance(source.WindowCenter, copy.WindowCenter), 0.001f);
			Assert.Less(Vector3.Distance(source.WindowAxis, copy.WindowAxis), 0.001f);
		}

		[Test]
		public void PlayBakeSource_ReadsWindowWithoutGeometry()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.AddRange(GapSource(1f).Surfaces);
			CoverCandidate source = MakeWindowCandidate();
			source.RegionId = new CoverRegionId(0, 0);
			var playSource = new BakedCoverCandidateSource(
				new List<BakedCoverCandidateRecord> { BakedCoverCandidateRecord.FromCandidate(source) });
			var dest = new List<CoverCandidate>();
			playSource.Generate(new CoverRegionId(0, 0), new Bounds(Vector3.zero, Vector3.one * 20f), 12, dest);
			Assert.AreEqual(0, geo.QueryCount);
			Assert.AreEqual(1, dest.Count);
			Assert.AreEqual(CoverType.Window, dest[0].CoverType);
			Assert.IsTrue(dest[0].WindowValid);
			Assert.IsTrue(dest[0].OpeningValid);
			Assert.IsTrue(dest[0].HasTransparentPane);
			Assert.AreEqual(1.1f, dest[0].WindowWidth, 0.001f);
			Assert.AreEqual(12, dest[0].GeometryVersion);
		}

		[Test]
		public void WindowBake_IsDeterministic()
		{
			RecordingGeometrySource geo = GapSource(1f);
			var probe = new ScriptedWindowProbe { PaneForAnyOpening = true, HasFrame = true };
			List<CoverCandidate> a = Generate(MakeGenerator(geo, null, null, null, probe), geo);
			List<CoverCandidate> b = Generate(MakeGenerator(geo, null, null, null, probe), geo);
			Assert.AreEqual(a.Count, b.Count);
			for (int i = 0; i < a.Count; i++)
			{
				Assert.AreEqual(a[i].CoverType, b[i].CoverType);
				Assert.AreEqual(a[i].WindowValid, b[i].WindowValid);
				Assert.AreEqual(a[i].HasTransparentPane, b[i].HasTransparentPane);
				Assert.Less(Mathf.Abs(a[i].WindowWidth - b[i].WindowWidth), 0.001f);
			}
		}
		#endregion

		#region 13.2B.4 Corner
		[Test]
		public void LWall_IsGeometricCorner()
		{
			List<CoverCandidate> dest = Generate(LWallSource(), HighWall());
			List<CoverCandidate> corners = GeometricCornersOf(dest);
			Assert.AreEqual(1, corners.Count, "L-walls must bake one Corner, not CornerLeft/Center/Right");
			CoverCandidate corner = corners[0];
			Assert.AreEqual(CoverType.Corner, corner.CoverType);
			Assert.IsTrue(corner.CornerValid);
			Assert.AreEqual(CoverCornerOrientation.Inner, corner.CornerOrientation);
			Assert.Greater(corner.CornerFacing.sqrMagnitude, 0.01f);
			Assert.Greater(corner.CornerNormalA.sqrMagnitude, 0.01f);
			Assert.Greater(corner.CornerNormalB.sqrMagnitude, 0.01f);
		}

		[Test]
		public void InnerCorner_HasBackSideAndOpenFront()
		{
			List<CoverCandidate> dest = Generate(LWallSource(), HighWall());
			CoverCandidate corner = GeometricCornersOf(dest)[0];
			Vector3 facing = corner.CornerFacing.normalized;
			Assert.Greater(Vector3.Dot(facing, corner.CornerNormalA.normalized), 0.2f);
			Assert.Greater(Vector3.Dot(facing, corner.CornerNormalB.normalized), 0.2f);
			Assert.Greater(Vector3.Dot(facing, Vector3.forward), 0.2f);
			Assert.Greater(Vector3.Dot(facing, Vector3.right), 0.2f);
			Assert.Less(Vector3.Dot(facing, Vector3.back), 0f);
			Assert.Less(Vector3.Dot(facing, Vector3.left), 0f);
		}

		[Test]
		public void LongWall_IsNotGeometricCorner()
		{
			List<CoverCandidate> dest = Generate(LongWallSource(), HighWall());
			Assert.AreEqual(0, GeometricCornersOf(dest).Count);
			Assert.AreEqual(CoverType.None, Closest(dest, new Vector3(5f, 0f, 0.45f)).CoverType);
		}

		[Test]
		public void WallEnd_StaysEdge()
		{
			List<CoverCandidate> dest = Generate(LongWallSource(), HighWall());
			Assert.AreEqual(2, EdgesOf(dest).Count);
			Assert.AreEqual(0, GeometricCornersOf(dest).Count);
			for (int i = 0; i < EdgesOf(dest).Count; i++)
				Assert.AreEqual(CoverType.Edge, EdgesOf(dest)[i].CoverType);
		}

		[Test]
		public void FarWalls_NoCorner()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(2f, 0f, 0f), Vector3.forward, 4f));
			geo.Surfaces.Add(Wall(new Vector3(2f, 0f, 8f), Vector3.forward, 4f));
			Assert.AreEqual(0, GeometricCornersOf(Generate(geo, HighWall())).Count);
		}

		[Test]
		public void CornerFacing_PointsIntoOpenSector()
		{
			CoverCandidate corner = GeometricCornersOf(Generate(LWallSource(), HighWall()))[0];
			Vector3 facing = corner.CornerFacing.normalized;
			Assert.Greater(Vector3.Dot(facing, Vector3.forward + Vector3.right), 0.5f);
			Assert.Less(Mathf.Abs(Vector3.Dot(facing, Vector3.forward) - Vector3.Dot(facing, Vector3.right)), 0.2f);
		}

		[Test]
		public void CornerWithoutNavMesh_Rejected()
		{
			RecordingGeometrySource geo = LWallSource();
			var nav = new ScriptedNavMesh();
			CoverCornerSeed seed = FirstCornerSeed(geo);
			nav.Blocked.Add(seed.Position);
			nav.BlockRadius = 0.4f;
			CoverCandidateGenerator gen = MakeGenerator(geo, HighWall(), null, nav);
			List<CoverCandidate> dest = Generate(gen, geo);
			Assert.Greater(gen.LastRejectedNavMeshCount, 0);
			Assert.AreEqual(0, GeometricCornersOf(dest).Count);
		}

		[Test]
		public void CornerWithoutClearance_Rejected()
		{
			RecordingGeometrySource geo = LWallSource();
			CoverCornerSeed seed = FirstCornerSeed(geo);
			var clearance = new ScriptedClearance { BlockRadius = 0.4f };
			clearance.Blocked.Add(seed.Position);
			CoverCandidateGenerator gen = MakeGenerator(geo, HighWall(), clearance);
			List<CoverCandidate> dest = Generate(gen, geo);
			Assert.Greater(gen.LastRejectedClearanceCount, 0);
			Assert.AreEqual(0, GeometricCornersOf(dest).Count);
		}

		[Test]
		public void OnePhysicalCorner_OneCandidate()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(4f, 0f, 0f), Vector3.forward, 8f));
			geo.Surfaces.Add(Wall(new Vector3(4.05f, 0f, 0f), Vector3.forward, 8f));
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, 4f), Vector3.right, 8f));
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, 4.05f), Vector3.right, 8f));
			Assert.AreEqual(1, GeometricCornersOf(Generate(geo, HighWall())).Count);
		}

		[Test]
		public void BoxedInL_IsNotCorner()
		{
			RecordingGeometrySource geo = LWallSource();
			Vector3 n = new Vector3(-1f, 0f, -1f).normalized;
			geo.Surfaces.Add(Wall(new Vector3(2f, 0f, 2f), n, 6f));
			Assert.AreEqual(0, GeometricCornersOf(Generate(geo, HighWall())).Count);
		}

		[Test]
		public void DoorAndWindow_UnchangedWithCorners()
		{
			Assert.AreEqual(1, OpeningsOf(Generate(GapSource(1f), null)).Count);
			Assert.AreEqual(CoverType.Opening, OpeningsOf(Generate(GapSource(1f), null))[0].CoverType);
			var probe = new ScriptedWindowProbe { PaneForAnyOpening = true };
			RecordingGeometrySource geo = GapSource(1f);
			List<CoverCandidate> windows = WindowsOf(Generate(MakeGenerator(geo, null, null, null, probe), geo));
			Assert.AreEqual(1, windows.Count);
			Assert.AreEqual(CoverType.Window, windows[0].CoverType);
		}

		[Test]
		public void BakeRecord_CornerFields_Roundtrip()
		{
			CoverCandidate source = MakeCornerCandidate();
			BakedCoverCandidateRecord record = BakedCoverCandidateRecord.FromCandidate(source);
			Assert.AreEqual(CoverType.Corner, record.CoverType);
			Assert.IsTrue(record.CornerValid);
			Assert.AreEqual(CoverCornerOrientation.Inner, record.CornerOrientation);
			CoverCandidate copy = record.ToCandidate();
			Assert.AreEqual(CoverType.Corner, copy.CoverType);
			Assert.IsTrue(copy.CornerValid);
			Assert.AreEqual(CoverOccupancy.Available, copy.Occupancy);
			Assert.Less(Vector3.Distance(source.CornerFacing, copy.CornerFacing), 0.001f);
			Assert.Less(Vector3.Distance(source.CornerNormalA, copy.CornerNormalA), 0.001f);
			Assert.Less(Vector3.Distance(source.CornerNormalB, copy.CornerNormalB), 0.001f);
		}

		[Test]
		public void PlayBakeSource_ReadsCornerWithoutGeometry()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.AddRange(LWallSource().Surfaces);
			CoverCandidate source = MakeCornerCandidate();
			source.RegionId = new CoverRegionId(0, 0);
			var playSource = new BakedCoverCandidateSource(
				new List<BakedCoverCandidateRecord> { BakedCoverCandidateRecord.FromCandidate(source) });
			var dest = new List<CoverCandidate>();
			playSource.Generate(new CoverRegionId(0, 0), new Bounds(Vector3.zero, Vector3.one * 20f), 13, dest);
			Assert.AreEqual(0, geo.QueryCount);
			Assert.AreEqual(1, dest.Count);
			Assert.AreEqual(CoverType.Corner, dest[0].CoverType);
			Assert.IsTrue(dest[0].CornerValid);
			Assert.Greater(dest[0].CornerFacing.sqrMagnitude, 0.01f);
			Assert.AreEqual(13, dest[0].GeometryVersion);
		}

		[Test]
		public void CornerBake_IsDeterministic()
		{
			RecordingGeometrySource geo = LWallSource();
			List<CoverCandidate> a = Generate(geo, HighWall());
			List<CoverCandidate> b = Generate(geo, HighWall());
			Assert.AreEqual(a.Count, b.Count);
			for (int i = 0; i < a.Count; i++)
			{
				Assert.AreEqual(a[i].CoverType, b[i].CoverType);
				Assert.AreEqual(a[i].CornerValid, b[i].CornerValid);
				Assert.Less(Mathf.Abs(a[i].CornerFacing.x - b[i].CornerFacing.x), 0.001f);
				Assert.Less(Mathf.Abs(a[i].CornerFacing.z - b[i].CornerFacing.z), 0.001f);
			}
		}

		[Test]
		public void OuterL_IsOuterCorner()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(-4f, 0f, 0f), Vector3.forward, 8f));
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, -4f), Vector3.right, 8f));
			List<CoverCandidate> corners = GeometricCornersOf(Generate(geo, HighWall()));
			Assert.AreEqual(1, corners.Count);
			Assert.AreEqual(CoverCornerOrientation.Outer, corners[0].CornerOrientation);
			Assert.AreEqual(CoverType.Corner, corners[0].CoverType);
		}
		#endregion

		#region 13.2B.5 Final classification
		[Test]
		public void ResolveType_WindowBeatsOpening()
		{
			var candidate = new CoverCandidate { OpeningValid = true, WindowValid = true, EdgeValid = true };
			Assert.AreEqual(CoverType.Window, CoverClassifier.ResolveType(candidate));
		}

		[Test]
		public void ResolveType_OpeningBeatsEdge()
		{
			var candidate = new CoverCandidate { OpeningValid = true, EdgeValid = true, CornerValid = true };
			Assert.AreEqual(CoverType.Opening, CoverClassifier.ResolveType(candidate));
		}

		[Test]
		public void ResolveType_GeometricCornerBeatsEdge()
		{
			var candidate = new CoverCandidate
			{
				EdgeValid = true,
				CornerValid = true,
				CornerFacing = Vector3.forward
			};
			Assert.AreEqual(CoverType.Corner, CoverClassifier.ResolveType(candidate));
		}

		[Test]
		public void ResolveType_EdgeBeatsLegacyCorner()
		{
			var candidate = new CoverCandidate { EdgeValid = true, CornerValid = true };
			Assert.AreEqual(CoverType.Edge, CoverClassifier.ResolveType(candidate));
		}

		[Test]
		public void ResolveType_HighWallIsNoneNotStanding()
		{
			var candidate = new CoverCandidate { StandingValid = true, CrouchValid = true };
			Assert.AreEqual(CoverType.None, CoverClassifier.ResolveType(candidate));
			Assert.AreNotEqual(CoverType.Standing, CoverClassifier.ResolveType(candidate));
		}

		[Test]
		public void ResolveType_CrouchOnlyWhenStandingInvalid()
		{
			var crouch = new CoverCandidate { CrouchValid = true, StandingValid = false };
			Assert.AreEqual(CoverType.Crouch, CoverClassifier.ResolveType(crouch));
			var both = new CoverCandidate { CrouchValid = true, StandingValid = true };
			Assert.AreEqual(CoverType.None, CoverClassifier.ResolveType(both));
		}

		[Test]
		public void FinalizeBake_DoesNotDropOverlappingFields()
		{
			var candidate = new CoverCandidate
			{
				OpeningValid = true,
				WindowValid = true,
				EdgeValid = true,
				OpeningWidth = 1.25f,
				OpeningAxis = Vector3.right,
				OpeningCenter = new Vector3(3.5f, 0f, 0f),
				EdgeDirection = Vector3.left,
				StandingValid = true,
				CrouchValid = true,
				StandingProfile = new CoverProtectionProfile { Torso = 0.8f },
				CrouchProfile = new CoverProtectionProfile { Torso = 1f },
				Capabilities = CoverCapabilities.CanPeek |
				               CoverCapabilities.CanStepLeft |
				               CoverCapabilities.CanStepRight |
				               CoverCapabilities.CanFireThrough |
				               CoverCapabilities.CanObserveThrough
			};
			CoverClassifier.FinalizeBake(candidate);
			Assert.AreEqual(CoverType.Window, candidate.CoverType);
			Assert.IsTrue(candidate.OpeningValid);
			Assert.IsTrue(candidate.WindowValid);
			Assert.IsTrue(candidate.EdgeValid);
			Assert.AreEqual(1.25f, candidate.OpeningWidth, 0.001f);
			Assert.AreEqual(Vector3.right, candidate.OpeningAxis);
			Assert.AreEqual(Vector3.left, candidate.EdgeDirection);
			Assert.AreEqual(0.8f, candidate.StandingProfile.Torso, 0.001f);
			Assert.AreEqual(1f, candidate.CrouchProfile.Torso, 0.001f);
			Assert.AreEqual(
				CoverCapabilities.CanPeek |
				CoverCapabilities.CanStepLeft |
				CoverCapabilities.CanStepRight |
				CoverCapabilities.CanFireThrough |
				CoverCapabilities.CanObserveThrough |
				CoverCapabilities.CanStand |
				CoverCapabilities.CanCrouch,
				candidate.Capabilities);
		}

		[Test]
		public void MidWall_IsNotSelectableStandingType()
		{
			CoverCandidate mid = Closest(Generate(LongWallSource(), HighWall()), new Vector3(5f, 0f, 0.45f));
			Assert.IsTrue(mid.StandingValid);
			Assert.IsTrue(mid.CrouchValid);
			Assert.Greater(mid.StandingProfile.Torso, 0.5f);
			Assert.AreEqual(CoverType.None, mid.CoverType);
			Assert.IsFalse(mid.IsTacticalSelectable);
			Assert.IsFalse(CoverScoreMath.IsSelectable(mid));
		}

		[Test]
		public void WallEdge_IsEdge()
		{
			Assert.AreEqual(2, EdgesOf(Generate(LongWallSource(), HighWall())).Count);
			Assert.AreEqual(CoverType.Edge, EdgesOf(Generate(LongWallSource(), HighWall()))[0].CoverType);
			Assert.AreEqual(
				CoverCapabilities.CanPeek,
				EdgesOf(Generate(LongWallSource(), HighWall()))[0].Capabilities & CoverCapabilities.CanPeek);
		}

		[Test]
		public void LowWall_IsCrouch()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(5f, 0f, 0f), Vector3.forward, 2.4f));
			CoverCandidate mid = Closest(Generate(geo, LowWall()), new Vector3(5f, 0f, 0.45f));
			Assert.IsFalse(mid.StandingValid);
			Assert.IsTrue(mid.CrouchValid);
			Assert.AreEqual(CoverType.Crouch, mid.CoverType);
			Assert.IsTrue(mid.IsTacticalSelectable);
		}

		[Test]
		public void Door_IsOpening()
		{
			CoverCandidate opening = OpeningsOf(Generate(GapSource(1f), null))[0];
			Assert.AreEqual(CoverType.Opening, opening.CoverType);
			Assert.AreEqual(
				CoverCapabilities.CanStepLeft | CoverCapabilities.CanStepRight |
				CoverCapabilities.CanOpen | CoverCapabilities.CanClose,
				opening.Capabilities & (CoverCapabilities.CanStepLeft | CoverCapabilities.CanStepRight |
				                        CoverCapabilities.CanOpen | CoverCapabilities.CanClose));
		}

		[Test]
		public void Window_BeatsOpening()
		{
			RecordingGeometrySource geo = GapSource(1f);
			var probe = new ScriptedWindowProbe { PaneForAnyOpening = true, HasFrame = true };
			CoverCandidate window = WindowsOf(Generate(MakeGenerator(geo, null, null, null, probe), geo))[0];
			Assert.AreEqual(CoverType.Window, window.CoverType);
			Assert.IsTrue(window.OpeningValid);
			Assert.Greater(window.OpeningWidth, 0.8f);
			Assert.Greater(window.OpeningAxis.sqrMagnitude, 0.01f);
			Assert.AreEqual(
				CoverCapabilities.CanObserveThrough | CoverCapabilities.CanFireThrough,
				window.Capabilities & (CoverCapabilities.CanObserveThrough | CoverCapabilities.CanFireThrough));
			Assert.AreEqual(
				CoverCapabilities.CanStepLeft | CoverCapabilities.CanStepRight |
				CoverCapabilities.CanOpen | CoverCapabilities.CanClose,
				window.Capabilities & (CoverCapabilities.CanStepLeft | CoverCapabilities.CanStepRight |
				                        CoverCapabilities.CanOpen | CoverCapabilities.CanClose));
		}

		[Test]
		public void GeometricCorner_BeatsEdgeFlag()
		{
			CoverCandidate corner = GeometricCornersOf(Generate(LWallSource(), HighWall()))[0];
			Assert.AreEqual(CoverType.Corner, corner.CoverType);
			Assert.IsTrue(corner.IsTacticalSelectable);
		}

		[Test]
		public void PartialWall_IsPartial()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(5f, 0f, 0f), Vector3.forward, 2.4f));
			CoverCandidate mid = Closest(Generate(geo, PartialWall()), new Vector3(5f, 0f, 0.45f));
			Assert.IsTrue(mid.PartialValid);
			Assert.AreEqual(CoverType.Partial, mid.CoverType);
			Assert.IsTrue(mid.IsTacticalSelectable);
		}

		[Test]
		public void OpeningAbsorbsEdge_PrimaryIsOpening()
		{
			List<CoverCandidate> dest = Generate(GapSource(1f), DoorWall());
			Assert.AreEqual(1, OpeningsOf(dest).Count);
			Assert.AreEqual(CoverType.Opening, OpeningsOf(dest)[0].CoverType);
		}

		[Test]
		public void HighWall_KeepsProfilesWhenTypeIsNone()
		{
			CoverCandidate mid = Closest(Generate(LongWallSource(), HighWall()), new Vector3(5f, 0f, 0.45f));
			Assert.AreEqual(CoverType.None, mid.CoverType);
			Assert.Greater(mid.StandingProfile.Torso, 0.5f);
			Assert.Greater(mid.CrouchProfile.Torso, 0.5f);
			Assert.AreEqual(
				CoverCapabilities.CanStand | CoverCapabilities.CanCrouch,
				mid.Capabilities & (CoverCapabilities.CanStand | CoverCapabilities.CanCrouch));
		}

		[Test]
		public void LegacyStanding_RemainsSelectableForFrozenScore()
		{
			var injected = new CoverCandidate
			{
				CoverType = CoverType.Standing,
				NavMeshValid = true,
				StandingValid = true
			};
			Assert.IsTrue(CoverScoreMath.IsSelectable(injected));
			Assert.IsFalse(CoverClassifier.IsTacticalType(CoverType.Standing));
		}

		[Test]
		public void PlayBakeSource_ReadsCrouchWithoutGeometry()
		{
			AssertPlayRecord(MakeTypedCandidate(CoverType.Crouch), CoverType.Crouch, 22);
		}

		[Test]
		public void PlayBakeSource_ReadsPartialWithoutGeometry()
		{
			AssertPlayRecord(MakeTypedCandidate(CoverType.Partial), CoverType.Partial, 23);
		}

		[Test]
		public void PlayBakeSource_ReadsNoneWithoutGeometry()
		{
			CoverCandidate source = MakeTypedCandidate(CoverType.None);
			source.StandingValid = true;
			source.StandingProfile = new CoverProtectionProfile { Torso = 0.9f };
			List<CoverCandidate> dest = PlayGenerate(source, 24);
			Assert.AreEqual(CoverType.None, dest[0].CoverType);
			Assert.IsFalse(dest[0].IsTacticalSelectable);
			Assert.IsTrue(dest[0].StandingValid);
			Assert.AreEqual(0.9f, dest[0].StandingProfile.Torso, 0.001f);
		}

		[Test]
		public void PlayBakeSource_PreservesOpeningFieldsOnWindow()
		{
			CoverCandidate source = MakeWindowCandidate();
			source.RegionId = new CoverRegionId(0, 0);
			List<CoverCandidate> dest = PlayGenerate(source, 25);
			Assert.AreEqual(CoverType.Window, dest[0].CoverType);
			Assert.IsTrue(dest[0].OpeningValid);
			Assert.AreEqual(1.1f, dest[0].OpeningWidth, 0.001f);
			Assert.AreEqual(Vector3.right, dest[0].OpeningAxis);
			Assert.AreEqual(
				CoverCapabilities.CanFireThrough | CoverCapabilities.CanObserveThrough,
				dest[0].Capabilities & (CoverCapabilities.CanFireThrough | CoverCapabilities.CanObserveThrough));
		}

		[Test]
		public void PlayBakeSource_PreservesEdgeDirection()
		{
			CoverCandidate source = MakeEdgeCandidate();
			source.RegionId = new CoverRegionId(0, 0);
			List<CoverCandidate> dest = PlayGenerate(source, 26);
			Assert.AreEqual(CoverType.Edge, dest[0].CoverType);
			Assert.AreEqual(Vector3.right, dest[0].EdgeDirection);
			Assert.AreEqual(CoverCapabilities.CanPeek, dest[0].Capabilities & CoverCapabilities.CanPeek);
		}

		[Test]
		public void PlayBakeSource_ReadsFinalTypesWithoutGeometry()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(LongWall());
			CoverType[] types =
			{
				CoverType.Edge, CoverType.Crouch, CoverType.Opening, CoverType.Window,
				CoverType.Corner, CoverType.Partial, CoverType.None
			};
			var baked = new List<BakedCoverCandidateRecord>(types.Length);
			for (int i = 0; i < types.Length; i++)
			{
				CoverCandidate source = MakeEdgeCandidate();
				source.CandidateId = i + 1;
				source.RegionId = new CoverRegionId(0, 0);
				source.CoverType = types[i];
				source.EdgeValid = types[i] == CoverType.Edge;
				source.OpeningValid = types[i] == CoverType.Opening || types[i] == CoverType.Window;
				source.WindowValid = types[i] == CoverType.Window;
				source.CornerValid = types[i] == CoverType.Corner;
				source.PartialValid = types[i] == CoverType.Partial;
				source.CrouchValid = types[i] == CoverType.Crouch;
				baked.Add(BakedCoverCandidateRecord.FromCandidate(source));
			}

			var dest = new List<CoverCandidate>();
			new BakedCoverCandidateSource(baked).Generate(
				new CoverRegionId(0, 0), new Bounds(Vector3.zero, Vector3.one * 20f), 21, dest);
			Assert.AreEqual(0, geo.QueryCount);
			Assert.AreEqual(types.Length, dest.Count);
			for (int i = 0; i < types.Length; i++)
			{
				Assert.AreEqual(types[i], dest[i].CoverType);
				Assert.AreEqual(21, dest[i].GeometryVersion);
			}
		}

		[Test]
		public void FinalTypeBake_IsDeterministic()
		{
			RecordingGeometrySource geo = LongWallSource();
			List<CoverCandidate> a = Generate(geo, HighWall());
			List<CoverCandidate> b = Generate(geo, HighWall());
			Assert.AreEqual(a.Count, b.Count);
			for (int i = 0; i < a.Count; i++)
			{
				Assert.AreEqual(a[i].CoverType, b[i].CoverType);
				Assert.AreEqual(a[i].IsTacticalSelectable, b[i].IsTacticalSelectable);
				Assert.AreEqual(a[i].Capabilities, b[i].Capabilities);
			}
		}

		[Test]
		public void FormatTypeLabel_OmitsStanding()
		{
			Assert.AreEqual("C123 Edge", CoverClassifier.FormatTypeLabel(123, CoverType.Edge));
			Assert.AreEqual("C124 Crouch", CoverClassifier.FormatTypeLabel(124, CoverType.Crouch));
			Assert.AreEqual("C125 Opening", CoverClassifier.FormatTypeLabel(125, CoverType.Opening));
			Assert.AreEqual("C126 Window", CoverClassifier.FormatTypeLabel(126, CoverType.Window));
			Assert.AreEqual("C127 Corner", CoverClassifier.FormatTypeLabel(127, CoverType.Corner));
			Assert.AreEqual("C128 Partial", CoverClassifier.FormatTypeLabel(128, CoverType.Partial));
			Assert.AreEqual("C1", CoverClassifier.FormatTypeLabel(1, CoverType.Standing));
			Assert.AreEqual("C2", CoverClassifier.FormatTypeLabel(2, CoverType.None));
		}
		#endregion

		#region 13.2B.5A Logical surfaces
		[Test]
		public void OverlappingPrefabs_MergeToOneSurface()
		{
			List<CoverGeometrySurface> surfaces = OverlapChain(3, 2f, 0.28f);
			CoverSurfaceMerge.Rebuild(surfaces, new CoverGenerationSettings(), null);
			Assert.AreEqual(1, surfaces.Count);
			Assert.Greater(surfaces[0].Length, 5f);
		}

		[Test]
		public void TenOverlappingPrefabs_TwoEdgesNoOpening()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.AddRange(OverlapChain(10, 2f, 0.28f));
			List<CoverCandidate> dest = GenerateWide(geo, WideSlab());
			Assert.AreEqual(2, TypedCount(dest, CoverType.Edge));
			Assert.AreEqual(0, TypedCount(dest, CoverType.Opening));
		}

		[Test]
		public void BlockedColliderSeam_IsNotOpening()
		{
			List<CoverGeometrySurface> surfaces = GapSurfaces(1f);
			Assert.AreEqual(2, surfaces.Count);
			CoverSurfaceMerge.Rebuild(surfaces, new CoverGenerationSettings(), new ScriptedSeamProbe { Solid = true });
			Assert.AreEqual(1, surfaces.Count);
			Assert.AreEqual(0, OpeningsOf(GenerateFromSurfaces(surfaces, WideSlab())).Count);
		}

		[Test]
		public void OpenDoorGap_DoesNotMergeWithoutSeam()
		{
			List<CoverGeometrySurface> surfaces = GapSurfaces(2.4f);
			CoverSurfaceMerge.Rebuild(surfaces, new CoverGenerationSettings(), null);
			Assert.AreEqual(2, surfaces.Count);
		}

		[Test]
		public void DoorAfterMerge_OneOpeningTwoEdges()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.AddRange(DoorChain(4, 2.4f, 4, 2f, 0.28f));
			List<CoverCandidate> dest = GenerateWide(geo, WideSlab());
			Assert.AreEqual(1, TypedCount(dest, CoverType.Opening));
			Assert.Greater(OpeningsOf(dest)[0].OpeningWidth, 2f);
			Assert.Less(OpeningsOf(dest)[0].OpeningWidth, 2.8f);
			Assert.AreEqual(2, TypedCount(dest, CoverType.Edge));
		}

		[Test]
		public void SeparateWalls_DoNotMerge()
		{
			var surfaces = new List<CoverGeometrySurface>
			{
				Wall(new Vector3(2f, 0f, 0f), Vector3.forward, 4f),
				Wall(new Vector3(2f, 0f, 8f), Vector3.forward, 4f)
			};
			CoverSurfaceMerge.Rebuild(surfaces, new CoverGenerationSettings(), null);
			Assert.AreEqual(2, surfaces.Count);
			Assert.AreEqual(0, OpeningsOf(GenerateFromSurfaces(surfaces, null)).Count);
		}

		[Test]
		public void ShortPropsWithSpace_NotOpening()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(1.5f, 0f, 1.5f), Vector3.forward, 1f));
			geo.Surfaces.Add(Wall(new Vector3(3.9f, 0f, 1.5f), Vector3.forward, 1f));
			Assert.AreEqual(0, OpeningsOf(Generate(geo, null)).Count);
		}

		[Test]
		public void LogicalL_StillGeometricCorner()
		{
			List<CoverCandidate> corners = GeometricCornersOf(Generate(LWallSource(), HighWall()));
			Assert.AreEqual(1, corners.Count);
			Assert.AreEqual(CoverType.Corner, corners[0].CoverType);
		}

		[Test]
		public void SurfaceMerge_IsDeterministic()
		{
			List<CoverGeometrySurface> a = OverlapChain(10, 2f, 0.28f);
			List<CoverGeometrySurface> b = OverlapChain(10, 2f, 0.28f);
			b.Reverse();
			var settings = new CoverGenerationSettings();
			CoverSurfaceMerge.Rebuild(a, settings, null);
			CoverSurfaceMerge.Rebuild(b, settings, null);
			Assert.AreEqual(1, a.Count);
			Assert.AreEqual(1, b.Count);
			Assert.Less(Mathf.Abs(a[0].Length - b[0].Length), 0.001f);
			Assert.Less(Vector3.Distance(a[0].Origin, b[0].Origin), 0.001f);
		}
		#endregion

		#region Helpers
		private static CoverCandidate MakeEdgeCandidate()
		{
			return new CoverCandidate
			{
				CandidateId = 3,
				Position = new Vector3(1.2f, 0f, 4.5f),
				Normal = Vector3.forward,
				CoverType = CoverType.Edge,
				StandingValid = true,
				CrouchValid = true,
				EdgeValid = true,
				EdgeSeed = true,
				EdgeDirection = Vector3.right,
				LeftOffset = 0.4f,
				RightOffset = 0.6f,
				OpeningWidth = 0f,
				Capabilities = CoverCapabilities.CanPeek | CoverCapabilities.CanStand,
				StandingProfile = new CoverProtectionProfile { Head = 0.9f, Torso = 1f, Pelvis = 1f, Legs = 1f },
				CrouchProfile = new CoverProtectionProfile { Head = 1f, Torso = 1f, Pelvis = 1f, Legs = 1f },
				NavMeshValid = true,
				RegionId = new CoverRegionId(2, 3),
				GeometryVersion = 4,
				Occupancy = CoverOccupancy.Occupied
			};
		}

		private static CoverType JsonRoundtripType(CoverType _type)
		{
			CoverCandidate candidate = MakeEdgeCandidate();
			candidate.CoverType = _type;
			candidate.EdgeValid = _type == CoverType.Edge;
			candidate.OpeningValid = _type == CoverType.Opening;
			candidate.WindowValid = _type == CoverType.Window;
			candidate.CornerValid = _type == CoverType.Corner;
			var wrap = new RecordWrap { Record = BakedCoverCandidateRecord.FromCandidate(candidate) };
			return JsonUtility.FromJson<RecordWrap>(JsonUtility.ToJson(wrap)).Record.CoverType;
		}

		private static CoverCandidate MakeOpeningCandidate()
		{
			return new CoverCandidate
			{
				CandidateId = 8,
				Position = new Vector3(3.5f, 0f, 0.45f),
				Normal = Vector3.forward,
				CoverType = CoverType.Opening,
				OpeningValid = true,
				OpeningSeed = true,
				OpeningAxis = Vector3.right,
				OpeningCenter = new Vector3(3.5f, 0f, 0f),
				OpeningWidth = 1.1f,
				LeftOffset = 0.55f,
				RightOffset = 0.55f,
				Capabilities = CoverCapabilities.CanStepLeft | CoverCapabilities.CanStepRight,
				NavMeshValid = true,
				RegionId = new CoverRegionId(0, 0),
				GeometryVersion = 1,
				Occupancy = CoverOccupancy.Occupied
			};
		}

		private static CoverCandidate MakeWindowCandidate()
		{
			CoverCandidate candidate = MakeOpeningCandidate();
			candidate.CandidateId = 12;
			candidate.CoverType = CoverType.Window;
			candidate.WindowValid = true;
			candidate.HasFrame = true;
			candidate.HasTransparentPane = true;
			candidate.WindowAxis = Vector3.right;
			candidate.WindowCenter = new Vector3(3.5f, 0f, 0f);
			candidate.WindowWidth = 1.1f;
			candidate.Capabilities |= CoverCapabilities.CanFireThrough | CoverCapabilities.CanObserveThrough;
			return candidate;
		}

		private static CoverCandidate MakeTypedCandidate(CoverType _type)
		{
			CoverCandidate candidate = MakeEdgeCandidate();
			candidate.CandidateId = 40 + (int)_type;
			candidate.RegionId = new CoverRegionId(0, 0);
			candidate.CoverType = _type;
			candidate.EdgeValid = _type == CoverType.Edge;
			candidate.OpeningValid = _type == CoverType.Opening || _type == CoverType.Window;
			candidate.WindowValid = _type == CoverType.Window;
			candidate.CornerValid = _type == CoverType.Corner;
			candidate.PartialValid = _type == CoverType.Partial;
			candidate.CrouchValid = _type == CoverType.Crouch;
			candidate.StandingValid = false;
			if (_type != CoverType.Edge)
				candidate.EdgeDirection = Vector3.zero;
			return candidate;
		}

		private static List<CoverCandidate> PlayGenerate(CoverCandidate _source, int _geometryVersion)
		{
			var geo = new RecordingGeometrySource();
			var dest = new List<CoverCandidate>();
			new BakedCoverCandidateSource(
				new List<BakedCoverCandidateRecord> { BakedCoverCandidateRecord.FromCandidate(_source) }).Generate(
				new CoverRegionId(0, 0),
				new Bounds(Vector3.zero, Vector3.one * 20f),
				_geometryVersion,
				dest);
			Assert.AreEqual(0, geo.QueryCount);
			Assert.AreEqual(1, dest.Count);
			Assert.AreEqual(_geometryVersion, dest[0].GeometryVersion);
			return dest;
		}

		private static void AssertPlayRecord(CoverCandidate _source, CoverType _type, int _geometryVersion)
		{
			List<CoverCandidate> dest = PlayGenerate(_source, _geometryVersion);
			Assert.AreEqual(_type, dest[0].CoverType);
		}

		private static CoverCandidate MakeCornerCandidate()
		{
			return new CoverCandidate
			{
				CandidateId = 13,
				Position = new Vector3(0.45f, 0f, 0.45f),
				Normal = new Vector3(0.707f, 0f, 0.707f),
				CoverType = CoverType.Corner,
				CornerValid = true,
				CornerFacing = new Vector3(0.707f, 0f, 0.707f),
				CornerNormalA = Vector3.forward,
				CornerNormalB = Vector3.right,
				CornerVertex = Vector3.zero,
				CornerOrientation = CoverCornerOrientation.Inner,
				StandingValid = true,
				CrouchValid = true,
				StandingProfile = new CoverProtectionProfile { Head = 0.4f, Torso = 0.6f, Pelvis = 0.6f, Legs = 0.5f },
				CrouchProfile = new CoverProtectionProfile { Head = 0.8f, Torso = 0.9f, Pelvis = 0.9f, Legs = 0.9f },
				NavMeshValid = true,
				RegionId = new CoverRegionId(0, 0),
				GeometryVersion = 1,
				Occupancy = CoverOccupancy.Occupied
			};
		}

		private static CoverCandidateGenerator MakeGenerator(
			ICoverGeometrySource _geo,
			ICoverOcclusionProbe _occlusion,
			ICoverClearanceProbe _clearance = null,
			ICoverNavMeshProbe _nav = null,
			ICoverWindowProbe _window = null)
		{
			return new CoverCandidateGenerator(
				_geo,
				_nav ?? new AcceptNavMesh(),
				_clearance ?? new ScriptedClearance(),
				null,
				_occlusion,
				null,
				_window);
		}

		private static List<CoverCandidate> Generate(
			RecordingGeometrySource _geo,
			ICoverOcclusionProbe _occlusion)
		{
			return Generate(MakeGenerator(_geo, _occlusion), _geo);
		}

		private static List<CoverCandidate> Generate(
			CoverCandidateGenerator _gen,
			RecordingGeometrySource _geo)
		{
			CoverRegionId region = CoverSpatialMath.WorldToRegion(Vector3.zero, CoverSpatialMath.DefaultRegionSizeMeters);
			var dest = new List<CoverCandidate>();
			_gen.Generate(
				region,
				CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters),
				1,
				dest);
			return dest;
		}

		private static List<CoverCandidate> EdgesOf(List<CoverCandidate> _list)
		{
			var edges = new List<CoverCandidate>();
			for (int i = 0; i < _list.Count; i++)
			{
				if (_list[i] != null && (_list[i].EdgeValid || _list[i].CoverType == CoverType.Edge))
					edges.Add(_list[i]);
			}

			return edges;
		}

		private static CoverCandidate Closest(List<CoverCandidate> _list, Vector3 _point)
		{
			CoverCandidate best = null;
			float bestDist = float.MaxValue;
			for (int i = 0; i < _list.Count; i++)
			{
				float d = CoverSpatialMath.PlanarDistanceSqr(_list[i].Position, _point);
				if (d >= bestDist)
					continue;
				bestDist = d;
				best = _list[i];
			}

			return best;
		}

		private static List<CoverCandidate> OpeningsOf(List<CoverCandidate> _list)
		{
			var openings = new List<CoverCandidate>();
			for (int i = 0; i < _list.Count; i++)
			{
				if (_list[i] != null && (_list[i].OpeningValid || _list[i].CoverType == CoverType.Opening))
					openings.Add(_list[i]);
			}

			return openings;
		}

		private static List<CoverCandidate> WindowsOf(List<CoverCandidate> _list)
		{
			var windows = new List<CoverCandidate>();
			for (int i = 0; i < _list.Count; i++)
			{
				if (_list[i] != null && (_list[i].WindowValid || _list[i].CoverType == CoverType.Window))
					windows.Add(_list[i]);
			}

			return windows;
		}

		private static List<CoverCandidate> GeometricCornersOf(List<CoverCandidate> _list)
		{
			var corners = new List<CoverCandidate>();
			for (int i = 0; i < _list.Count; i++)
			{
				if (_list[i] != null && CoverClassifier.HasGeometricCorner(_list[i]))
					corners.Add(_list[i]);
			}

			return corners;
		}

		private static RecordingGeometrySource GapSource(float _gapMeters)
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.AddRange(GapSurfaces(_gapMeters));
			return geo;
		}

		private static List<CoverGeometrySurface> GapSurfaces(float _gapMeters)
		{
			float length = 3f;
			float leftOrigin = length * 0.5f;
			float rightOrigin = length + _gapMeters + length * 0.5f;
			return new List<CoverGeometrySurface>
			{
				Wall(new Vector3(leftOrigin, 0f, 0f), Vector3.forward, length),
				Wall(new Vector3(rightOrigin, 0f, 0f), Vector3.forward, length)
			};
		}

		private static List<CoverGeometrySurface> OverlapChain(int _pieces, float _pieceLength, float _overlap)
		{
			var list = new List<CoverGeometrySurface>(_pieces);
			float step = Mathf.Max(0.05f, _pieceLength - _overlap);
			for (int i = 0; i < _pieces; i++)
			{
				float x = _pieceLength * 0.5f + i * step;
				list.Add(Wall(new Vector3(x, 0f, 0f), Vector3.forward, _pieceLength));
			}

			return list;
		}

		private static List<CoverGeometrySurface> DoorChain(
			int _leftPieces,
			float _gapMeters,
			int _rightPieces,
			float _pieceLength,
			float _overlap)
		{
			List<CoverGeometrySurface> list = OverlapChain(_leftPieces, _pieceLength, _overlap);
			float step = Mathf.Max(0.05f, _pieceLength - _overlap);
			float leftEnd = _pieceLength + (_leftPieces - 1) * step;
			float rightStart = leftEnd + _gapMeters;
			for (int i = 0; i < _rightPieces; i++)
			{
				float x = rightStart + _pieceLength * 0.5f + i * step;
				list.Add(Wall(new Vector3(x, 0f, 0f), Vector3.forward, _pieceLength));
			}

			return list;
		}

		private static List<CoverCandidate> GenerateWide(
			RecordingGeometrySource _geo,
			ICoverOcclusionProbe _occlusion)
		{
			CoverCandidateGenerator gen = MakeGenerator(_geo, _occlusion);
			var dest = new List<CoverCandidate>();
			gen.Generate(
				new CoverRegionId(0, 0),
				new Bounds(new Vector3(12f, 0f, 0f), new Vector3(40f, 8f, 24f)),
				1,
				dest);
			return dest;
		}

		private static List<CoverCandidate> GenerateFromSurfaces(
			List<CoverGeometrySurface> _surfaces,
			ICoverOcclusionProbe _occlusion)
		{
			var geo = new RecordingGeometrySource();
			for (int i = 0; i < _surfaces.Count; i++)
				geo.Surfaces.Add(_surfaces[i]);
			return Generate(geo, _occlusion);
		}

		private static int TypedCount(List<CoverCandidate> _list, CoverType _type)
		{
			int n = 0;
			for (int i = 0; i < _list.Count; i++)
			{
				if (_list[i] != null && _list[i].CoverType == _type)
					n++;
			}

			return n;
		}

		private static SlabCoverOcclusionProbe WideSlab()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(12f, 1.1f, 0f), new Vector3(32f, 2.2f, 0.4f)));
		}

		private static CoverOpeningSeed FirstSeed(RecordingGeometrySource _geo)
		{
			var seeds = new List<CoverOpeningSeed>(4);
			CoverOpeningGeometry.Collect(_geo.Surfaces, new CoverGenerationSettings(), seeds);
			Assert.Greater(seeds.Count, 0);
			return seeds[0];
		}

		private static CoverCornerSeed FirstCornerSeed(RecordingGeometrySource _geo)
		{
			var seeds = new List<CoverCornerSeed>(4);
			CoverCornerGeometry.Collect(_geo.Surfaces, new CoverGenerationSettings(), seeds);
			Assert.Greater(seeds.Count, 0);
			return seeds[0];
		}

		private static SlabCoverOcclusionProbe DoorWall()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(3.5f, 1.1f, 0f), new Vector3(14f, 2.2f, 0.4f)));
		}

		private static RecordingGeometrySource LongWallSource()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(LongWall());
			return geo;
		}

		private static RecordingGeometrySource LWallSource()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(Wall(new Vector3(4f, 0f, 0f), Vector3.forward, 8f));
			geo.Surfaces.Add(Wall(new Vector3(0f, 0f, 4f), Vector3.right, 8f));
			return geo;
		}

		private static CoverGeometrySurface LongWall()
		{
			return Wall(new Vector3(5f, 0f, 0f), Vector3.forward, 10f);
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

		private static SlabCoverOcclusionProbe HighWall()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(5f, 1.1f, 0f), new Vector3(14f, 2.2f, 0.4f)));
		}

		private static SlabCoverOcclusionProbe LowWall()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(5f, 0.575f, 0f), new Vector3(12f, 1.15f, 0.4f)));
		}

		private static SlabCoverOcclusionProbe PartialWall()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(5f, 0.3f, 0f), new Vector3(12f, 0.6f, 0.4f)));
		}
		#endregion
	}
}

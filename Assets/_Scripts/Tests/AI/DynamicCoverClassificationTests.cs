using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #13.2 Cover Classification. Geometric type + protection profile. Not score. Not Fire. Not lean.
	/// </summary>
	public sealed class DynamicCoverClassificationTests
	{
		#region Nested
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

		private sealed class AcceptClearance : ICoverClearanceProbe
		{
			public bool HasBodyClearance(Vector3 _position, Vector3 _normal)
			{
				return true;
			}
		}
		#endregion

		#region Private Fields
		private readonly CoverClassifier m_Classifier = new CoverClassifier();
		private readonly CoverClassificationSettings m_Settings = new CoverClassificationSettings();
		#endregion

		#region A Basic classification
		[Test]
		public void A1_StandingCover()
		{
			CoverCandidate candidate = Classify(HighWall(), CandidateOnWall());
			Assert.IsTrue(candidate.StandingValid);
			Assert.IsTrue(candidate.CrouchValid);
			Assert.AreEqual(CoverType.Standing, candidate.CoverType);
		}

		[Test]
		public void A2_CrouchCover()
		{
			CoverCandidate candidate = Classify(LowWall(), CandidateOnWall());
			Assert.IsFalse(candidate.StandingValid);
			Assert.IsTrue(candidate.CrouchValid);
			Assert.AreEqual(CoverType.Crouch, candidate.CoverType);
		}

		[Test]
		public void A3_BothStancesPossible()
		{
			CoverCandidate candidate = Classify(HighWall(), CandidateOnWall());
			Assert.IsTrue(candidate.StandingValid);
			Assert.IsTrue(candidate.CrouchValid);
			Assert.AreEqual(CoverType.Standing, candidate.CoverType);
		}

		[Test]
		public void A4_PartialCover()
		{
			CoverCandidate candidate = Classify(PartialWall(), CandidateOnWall());
			Assert.IsFalse(candidate.StandingValid);
			Assert.IsFalse(candidate.CrouchValid);
			Assert.IsTrue(candidate.PartialValid);
			Assert.AreEqual(CoverType.Partial, candidate.CoverType);
		}

		[Test]
		public void A5_CornerCover()
		{
			CoverCandidate candidate = Classify(CornerWall(), CandidateOnWall());
			Assert.IsTrue(candidate.CornerValid);
			Assert.AreEqual(CoverType.Corner, candidate.CoverType);
		}

		[Test]
		public void A6_None()
		{
			CoverCandidate candidate = Classify(EmptyWall(), CandidateOnWall());
			Assert.IsFalse(candidate.StandingValid);
			Assert.IsFalse(candidate.CrouchValid);
			Assert.IsFalse(candidate.PartialValid);
			Assert.AreEqual(CoverType.None, candidate.CoverType);
		}
		#endregion

		#region B Pose sensitivity
		[Test]
		public void B1_StandingExposed()
		{
			CoverCandidate candidate = Classify(LowWall(), CandidateOnWall());
			Assert.Less(candidate.StandingProfile.Head, m_Settings.SegmentThreshold);
			Assert.Less(candidate.StandingProfile.Torso, m_Settings.SegmentThreshold);
			Assert.IsFalse(candidate.StandingValid);
		}

		[Test]
		public void B2_CrouchProtected()
		{
			CoverCandidate candidate = Classify(LowWall(), CandidateOnWall());
			Assert.GreaterOrEqual(candidate.CrouchProfile.Head, m_Settings.SegmentThreshold);
			Assert.GreaterOrEqual(candidate.CrouchProfile.Torso, m_Settings.SegmentThreshold);
			Assert.IsTrue(candidate.CrouchValid);
		}

		[Test]
		public void B3_SameCandidate_DifferentByStance()
		{
			CoverCandidate candidate = Classify(LowWall(), CandidateOnWall());
			Assert.AreNotEqual(candidate.StandingValid, candidate.CrouchValid);
			Assert.AreEqual(CoverType.Crouch, candidate.CoverType);
		}
		#endregion

		#region C Protection profile
		[Test]
		public void C1_ProtectedTorso()
		{
			CoverCandidate candidate = Classify(HighWall(), CandidateOnWall());
			Assert.GreaterOrEqual(candidate.StandingProfile.Torso, m_Settings.SegmentThreshold);
		}

		[Test]
		public void C2_ExposedHead()
		{
			CoverCandidate candidate = Classify(LowWall(), CandidateOnWall());
			Assert.Less(candidate.StandingProfile.Head, m_Settings.SegmentThreshold);
		}

		[Test]
		public void C3_PartialTorso()
		{
			CoverCandidate candidate = Classify(PartialWall(), CandidateOnWall());
			Assert.Less(candidate.StandingProfile.Torso, m_Settings.SegmentThreshold);
			Assert.GreaterOrEqual(candidate.StandingProfile.Legs, m_Settings.SegmentThreshold);
		}

		[Test]
		public void C4_DifferentGeometryProfile()
		{
			CoverCandidate high = Classify(HighWall(), CandidateOnWall());
			CoverCandidate low = Classify(LowWall(), CandidateOnWall());
			Assert.AreNotEqual(high.StandingProfile.Head, low.StandingProfile.Head);
			Assert.Greater(high.StandingProfile.Average, low.StandingProfile.Average);
		}
		#endregion

		#region D Orientation
		[Test]
		public void D1_CorrectSide_ClassifiesCover()
		{
			CoverCandidate candidate = CandidateOnWall();
			m_Classifier.Classify(candidate, LowWall(), m_Settings, CoverThreatFrame.CoverBacked);
			Assert.AreEqual(CoverType.Crouch, candidate.CoverType);
		}

		[Test]
		public void D2_OppositeSide_DifferentClassification()
		{
			CoverCandidate backed = CandidateOnWall();
			CoverCandidate open = CandidateOnWall();
			m_Classifier.Classify(backed, LowWall(), m_Settings, CoverThreatFrame.CoverBacked);
			m_Classifier.Classify(open, LowWall(), m_Settings, CoverThreatFrame.OpenSide);
			Assert.AreEqual(CoverType.Crouch, backed.CoverType);
			Assert.AreEqual(CoverType.None, open.CoverType);
		}
		#endregion

		#region E Shared cache
		[Test]
		public void E1_SameGeometry_SameClassification()
		{
			RecordingGeometrySource geo = WallSource();
			CoverCandidateGenerator gen = new CoverCandidateGenerator(
				geo,
				new AcceptNavMesh(),
				new AcceptClearance(),
				null,
				HighWall());
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(gen);
			IReadOnlyList<CoverCandidate> first = cache.GetCandidates(new Vector3(5f, 0f, 0.5f));
			IReadOnlyList<CoverCandidate> second = cache.GetCandidates(new Vector3(5.2f, 0f, 0.4f));
			Assert.Greater(first.Count, 0);
			Assert.AreSame(first, second);
			Assert.AreEqual(first[0].CoverType, second[0].CoverType);
			Assert.AreEqual(1, cache.GenerationCount);
			Assert.AreEqual(first.Count, gen.LastClassificationCount);
		}

		[Test]
		public void E2_TwentyUnits_ThreeRegions_ThreeClassifications()
		{
			RecordingGeometrySource geo = WallSource();
			CoverCandidateGenerator gen = new CoverCandidateGenerator(
				geo,
				new AcceptNavMesh(),
				new AcceptClearance(),
				null,
				HighWall());
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(gen);
			QueryMany(cache, new Vector3(5f, 0f, 0.5f), 8);
			QueryMany(cache, new Vector3(CoverSpatialMath.DefaultRegionSizeMeters + 1f, 0f, 0f), 7);
			QueryMany(cache, new Vector3(0f, 0f, CoverSpatialMath.DefaultRegionSizeMeters + 1f), 5);
			Assert.AreEqual(3, cache.GenerationCount);
			Assert.AreEqual(3, geo.QueryCount);
			Assert.Less(cache.GenerationCount, 20);
		}
		#endregion

		#region Helpers
		private CoverCandidate Classify(ICoverOcclusionProbe _probe, CoverCandidate _candidate)
		{
			m_Classifier.Classify(_candidate, _probe, m_Settings, CoverThreatFrame.CoverBacked);
			return _candidate;
		}

		private static CoverCandidate CandidateOnWall()
		{
			return new CoverCandidate
			{
				Position = new Vector3(5f, 0f, 0.5f),
				Normal = Vector3.forward
			};
		}

		private static SlabCoverOcclusionProbe HighWall()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(5f, 1.1f, 0f), new Vector3(12f, 2.2f, 0.4f)));
		}

		private static SlabCoverOcclusionProbe LowWall()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(5f, 0.575f, 0f), new Vector3(12f, 1.15f, 0.4f)));
		}

		private static SlabCoverOcclusionProbe PartialWall()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(5f, 0.3f, 0f), new Vector3(12f, 0.6f, 0.4f)));
		}

		private static SlabCoverOcclusionProbe CornerWall()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(2.75f, 1.1f, 0f), new Vector3(5.5f, 2.2f, 0.4f)));
		}

		private static SlabCoverOcclusionProbe EmptyWall()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(80f, 0f, 80f), Vector3.one * 0.2f));
		}

		private static RecordingGeometrySource WallSource()
		{
			var geo = new RecordingGeometrySource();
			geo.Surfaces.Add(new CoverGeometrySurface
			{
				Origin = new Vector3(5f, 0f, 0f),
				Normal = Vector3.forward,
				Tangent = Vector3.right,
				Length = 8f
			});
			return geo;
		}

		private static void QueryMany(SharedCoverSpatialCache _cache, Vector3 _anchor, int _count)
		{
			for (int i = 0; i < _count; i++)
				_cache.GetCandidates(_anchor + Vector3.right * (i * 0.15f));
		}
		#endregion
	}
}

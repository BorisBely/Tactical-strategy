using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #13.0 Shared Spatial Cover Cache. Geometry reuse, not individual score. Not Fire.
	/// </summary>
	public sealed class DynamicCoverCacheTests
	{
		#region Nested
		private sealed class RecordingSource : ICoverCandidateSource
		{
			public int GenerateCount;
			public SharedCoverSpatialCache Cache;
			public bool ReenterSameRegion;
			public int CandidatesPerGenerate = 3;

			public void Generate(
				CoverRegionId _region,
				Bounds _bounds,
				int _geometryVersion,
				List<CoverCandidate> _destination)
			{
				GenerateCount++;
				if (ReenterSameRegion && Cache != null)
					Cache.GetCandidates(_region);

				for (int i = 0; i < CandidatesPerGenerate; i++)
				{
					_destination.Add(new CoverCandidate
					{
						CandidateId = i + 1,
						Position = _bounds.center + Vector3.right * i,
						Normal = Vector3.forward,
						CoverType = CoverType.Standing,
						StandingValid = true,
						NavMeshValid = true
					});
				}
			}
		}
		#endregion

		#region A Shared cache
		[Test]
		public void A1_FirstRequest_GeneratesCache()
		{
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			IReadOnlyList<CoverCandidate> list = cache.GetCandidates(Vector3.zero);
			Assert.AreEqual(1, source.GenerateCount);
			Assert.AreEqual(1, cache.GenerationCount);
			Assert.AreEqual(3, list.Count);
			Assert.AreEqual(1, cache.CachedRegionCount);
		}

		[Test]
		public void A2_SecondRequest_ReusesCache()
		{
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			IReadOnlyList<CoverCandidate> first = cache.GetCandidates(Vector3.zero);
			IReadOnlyList<CoverCandidate> second = cache.GetCandidates(new Vector3(1f, 0f, 1f));
			Assert.AreEqual(1, source.GenerateCount);
			Assert.AreSame(first, second);
		}

		[Test]
		public void A3_SimultaneousRequests_Deduplicate()
		{
			RecordingSource source = new RecordingSource { ReenterSameRegion = true };
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			source.Cache = cache;
			IReadOnlyList<CoverCandidate> a = cache.GetCandidates(Vector3.zero);
			IReadOnlyList<CoverCandidate> b = cache.GetCandidates(Vector3.zero);
			IReadOnlyList<CoverCandidate> c = cache.GetCandidates(Vector3.zero);
			Assert.AreEqual(1, source.GenerateCount);
			Assert.AreSame(a, b);
			Assert.AreSame(a, c);
		}

		[Test]
		public void A4_DifferentRegions_GenerateIndependently()
		{
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			Vector3 inR0 = Vector3.zero;
			Vector3 inOther = new Vector3(CoverSpatialMath.DefaultRegionSizeMeters + 1f, 0f, 0f);
			Assert.IsFalse(CoverSpatialMath.SameRegion(inR0, inOther, CoverSpatialMath.DefaultRegionSizeMeters));
			cache.GetCandidates(inR0);
			cache.GetCandidates(inOther);
			Assert.AreEqual(2, source.GenerateCount);
			Assert.AreEqual(2, cache.CachedRegionCount);
		}

		[Test]
		public void A5_InvalidatedRegion_Regenerates()
		{
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			CoverRegionId region = cache.RegionAt(Vector3.zero);
			cache.GetCandidates(region);
			cache.InvalidateRegion(region);
			cache.GetCandidates(region);
			Assert.AreEqual(2, source.GenerateCount);
		}

		[Test]
		public void A6_ValidRegion_DoesNotRegenerate()
		{
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			CoverRegionId keep = cache.RegionAt(Vector3.zero);
			CoverRegionId other = cache.RegionAt(new Vector3(CoverSpatialMath.DefaultRegionSizeMeters + 1f, 0f, 0f));
			cache.GetCandidates(keep);
			cache.GetCandidates(other);
			cache.InvalidateRegion(other);
			cache.GetCandidates(keep);
			Assert.AreEqual(2, source.GenerateCount);
			Assert.AreEqual(1, cache.CachedRegionCount);
		}

		[Test]
		public void A5b_GeometryVersionBump_Regenerates()
		{
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			cache.GetCandidates(Vector3.zero);
			cache.BumpGeometryVersion();
			cache.GetCandidates(Vector3.zero);
			Assert.AreEqual(2, source.GenerateCount);
			Assert.AreEqual(2, cache.GeometryVersion);
		}

		[Test]
		public void Cap_TruncatesToMaxCoverCandidates()
		{
			RecordingSource source = new RecordingSource { CandidatesPerGenerate = 40 };
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			cache.SetMaxCoverCandidates(5);
			IReadOnlyList<CoverCandidate> list = cache.GetCandidates(Vector3.zero);
			Assert.AreEqual(5, list.Count);
		}
		#endregion
	}
}

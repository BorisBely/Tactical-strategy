using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class VisionLodPolicyTests
	{
		[SetUp]
		public void SetUp()
		{
			VisionScanScheduler.ResetForTests();
		}

		[Test]
		public void ImmediateScan_IsDetail()
		{
			VisionLodObserverContext ctx = IdleContext();
			ctx.ImmediateScan = true;
			Assert.AreEqual(VisionScanTier.Detail, VisionLodMath.ResolveObserverTier(ctx));
			Assert.IsTrue(VisionLodMath.MaySpendLos(VisionScanTier.Detail));
			Assert.IsTrue(VisionLodMath.MayApplyVisionFrame(VisionScanTier.Detail));
		}

		[Test]
		public void SelectedOrRecentlyLost_IsDetail()
		{
			VisionLodObserverContext selected = IdleContext();
			selected.HasSelectedTarget = true;
			Assert.AreEqual(VisionScanTier.Detail, VisionLodMath.ResolveObserverTier(selected));

			VisionLodObserverContext lost = IdleContext();
			lost.HasRecentlyLostContact = true;
			Assert.AreEqual(VisionScanTier.Detail, VisionLodMath.ResolveObserverTier(lost));
		}

		[Test]
		public void QueuedDetailDue_IsDetail()
		{
			VisionLodObserverContext ctx = IdleContext();
			ctx.HasQueuedDetailDue = true;
			Assert.AreEqual(VisionScanTier.Detail, VisionLodMath.ResolveObserverTier(ctx));
		}

		[Test]
		public void StaleDetail_IsRangeFov_ThenIdle()
		{
			VisionLodObserverContext discover = IdleContext();
			discover.SecondsSinceLastDetailScan = 0.6f;
			discover.DiscoverIntervalSeconds = 0.5f;
			Assert.AreEqual(VisionScanTier.RangeFov, VisionLodMath.ResolveObserverTier(discover));

			VisionLodObserverContext idle = IdleContext();
			idle.SecondsSinceLastDetailScan = 0.1f;
			idle.SecondsSinceLastMembershipScan = 0.1f;
			Assert.AreEqual(VisionScanTier.Idle, VisionLodMath.ResolveObserverTier(idle));
			Assert.IsFalse(VisionLodMath.MaySpendLos(VisionScanTier.Idle));
			Assert.IsFalse(VisionLodMath.MayApplyVisionFrame(VisionScanTier.RangeFov));
		}

		[Test]
		public void MembershipStale_IsCheap()
		{
			VisionLodObserverContext ctx = IdleContext();
			ctx.SecondsSinceLastDetailScan = 0.1f;
			ctx.SecondsSinceLastMembershipScan = 2f;
			Assert.AreEqual(VisionScanTier.Cheap, VisionLodMath.ResolveObserverTier(ctx));
		}

		[Test]
		public void IntervalScale_IdleLongerThanDetail()
		{
			Assert.Greater(
				VisionLodMath.IntervalScale(VisionScanTier.Idle),
				VisionLodMath.IntervalScale(VisionScanTier.Detail));
		}

		[Test]
		public void DistanceBuckets()
		{
			Assert.AreEqual(VisionDistanceBucket.Near20, VisionLodMath.Bucket(10f));
			Assert.AreEqual(VisionDistanceBucket.Mid100, VisionLodMath.Bucket(50f));
			Assert.AreEqual(VisionDistanceBucket.Far500, VisionLodMath.Bucket(200f));
			Assert.AreEqual(VisionDistanceBucket.Beyond500, VisionLodMath.Bucket(600f));
		}

		[Test]
		public void CacheExpiry_ByTimeAndMovement()
		{
			Vector3 origin = Vector3.zero;
			Vector3 fwd = Vector3.forward;
			Vector3 target = new Vector3(0f, 0f, 10f);
			Assert.IsTrue(VisionLodMath.CacheIsValid(
				0.1f, 0f, 0.3f, origin, origin, target, target, fwd, fwd, 0.35f, 2.5f));
			Assert.IsFalse(VisionLodMath.CacheIsValid(
				0.5f, 0f, 0.3f, origin, origin, target, target, fwd, fwd, 0.35f, 2.5f));
			Assert.IsFalse(VisionLodMath.CacheIsValid(
				0.1f, 0f, 0.3f, origin, origin + Vector3.right, target, target, fwd, fwd, 0.35f, 2.5f));
			Assert.IsFalse(VisionLodMath.CacheIsValid(
				0.1f, 0f, 0.3f, origin, origin, target, target + Vector3.forward, fwd, fwd, 0.35f, 2.5f));
		}

		[Test]
		public void Scheduler_CapsDetailSlots_ImmediateBypasses()
		{
			VisionScanScheduler.ResetForTests();
			VisionScanScheduler.DetailSlotsPerFrame = 2;
			Assert.IsTrue(VisionScanScheduler.TryAcquireDetailSlot(false));
			Assert.IsTrue(VisionScanScheduler.TryAcquireDetailSlot(false));
			Assert.IsFalse(VisionScanScheduler.TryAcquireDetailSlot(false));
			Assert.IsTrue(VisionScanScheduler.TryAcquireDetailSlot(true));
		}

		[Test]
		public void CoarseFov_BehindObserver_FailsWithoutNeedingLos()
		{
			Vector3 origin = Vector3.zero;
			Vector3 forward = Vector3.forward;
			Vector3 behind = new Vector3(0f, 0f, -12f);
			bool inside = VisionGeometry.IsWithinCoarseRangeAndFov(
				origin,
				forward,
				behind,
				false,
				default,
				50f * 50f,
				60f,
				out _,
				out bool rangePass,
				out bool fovPass);
			Assert.IsTrue(rangePass);
			Assert.IsFalse(fovPass);
			Assert.IsFalse(inside);
		}

		[Test]
		public void CoarseFov_Ahead_Passes()
		{
			bool inside = VisionGeometry.IsWithinCoarseRangeAndFov(
				Vector3.zero,
				Vector3.forward,
				new Vector3(0f, 0f, 12f),
				false,
				default,
				50f * 50f,
				60f,
				out _,
				out bool rangePass,
				out bool fovPass);
			Assert.IsTrue(rangePass);
			Assert.IsTrue(fovPass);
			Assert.IsTrue(inside);
		}

		[Test]
		public void SameInputs_DifferentBudget_SameTierWhenImmediate()
		{
			VisionLodObserverContext ctx = IdleContext();
			ctx.ImmediateScan = true;
			VisionScanTier a = VisionLodMath.ResolveObserverTier(ctx);
			VisionScanScheduler.DetailSlotsPerFrame = 1;
			VisionScanTier b = VisionLodMath.ResolveObserverTier(ctx);
			Assert.AreEqual(a, b);
			Assert.AreEqual(VisionScanTier.Detail, a);
		}

		[Test]
		public void OutOfFov_ImmediateScan_DoesNotRaycast()
		{
			var registryGo = new GameObject("G8Registry");
			registryGo.AddComponent<UnitVisionRegistry>();
			GameObject observer = CreateStub("G8Obs", UnitTeamId.Player, Vector3.zero);
			GameObject target = CreateStub("G8Behind", UnitTeamId.Enemy, new Vector3(0f, 0f, -12f));
			observer.transform.rotation = Quaternion.identity;
			try
			{
				UnitVision vision = observer.GetComponent<UnitVision>();
				vision.SetVisionRange(40f);
				vision.ScanStats.Reset();
				vision.RequestImmediateScan();
				Assert.GreaterOrEqual(vision.ScanStats.LastScanCandidateCount, 1, "behind target must still be collected");
				Assert.AreEqual(0, vision.ScanStats.LastScanLosCheckCount, "FOV fail must skip LOS");
				Assert.AreEqual(0, vision.GetComponent<UnitPerception>().ObservationCount);
			}
			finally
			{
				Object.DestroyImmediate(observer);
				Object.DestroyImmediate(target);
				Object.DestroyImmediate(registryGo);
			}
		}

		private static VisionLodObserverContext IdleContext()
		{
			return new VisionLodObserverContext
			{
				ImmediateScan = false,
				HasSelectedTarget = false,
				HasRecentlyLostContact = false,
				HasQueuedDetailDue = false,
				SecondsSinceLastDetailScan = 0.1f,
				SecondsSinceLastMembershipScan = 0.1f,
				DiscoverIntervalSeconds = VisionLodMath.DefaultDiscoverIntervalSeconds,
				MembershipIntervalSeconds = VisionLodMath.DefaultMembershipIntervalSeconds
			};
		}

		private static GameObject CreateStub(string _name, UnitTeamId _team, Vector3 _position)
		{
			var go = new GameObject(_name);
			go.transform.position = _position;
			UnitTeam team = go.AddComponent<UnitTeam>();
			team.SetTeam(_team);
			go.AddComponent<UnitObservationSource>();
			go.AddComponent<UnitPerception>();
			CapsuleCollider col = go.AddComponent<CapsuleCollider>();
			col.height = 1.8f;
			col.radius = 0.3f;
			col.center = new Vector3(0f, 0.9f, 0f);
			go.AddComponent<UnitVision>();
			return go;
		}
	}
}

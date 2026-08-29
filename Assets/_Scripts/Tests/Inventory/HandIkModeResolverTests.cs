using NUnit.Framework;

namespace Inventory.Tests
{
	public sealed class HandIkModeResolverTests
	{
		private static UnitHandIkModeResolver.Weights DefaultWeights()
		{
			return new UnitHandIkModeResolver.Weights
			{
				GripLeftDefault = 0.9f,
				GripRightDefault = 0.35f,
				RightNotReadyWeight = 1f,
				ReadyBlend01 = 0f,
				RunLeft = 1f,
				RunRight = 0f
			};
		}

		[Test]
		public void StandingNotReadyIdle_KeepsHoldMix()
		{
			var query = new UnitHandIkModeResolver.Query
			{
				PeacefulCarry = true
			};
			UnitHandIkModeResolver.Result result = UnitHandIkModeResolver.Resolve(query, DefaultWeights());
			Assert.AreEqual(HandIkMode.Hold, result.Mode);
			Assert.AreEqual(0.9f, result.LeftWeightTarget, 0.001f);
			Assert.AreEqual(0.35f, result.RightWeightTarget, 0.001f);
		}

		[Test]
		public void StandingNotReadyWalk_LeftOnGrip_RightFollowsWalkClip()
		{
			var query = new UnitHandIkModeResolver.Query
			{
				Walking = true,
				PeacefulCarry = true
			};
			UnitHandIkModeResolver.Result result = UnitHandIkModeResolver.Resolve(query, DefaultWeights());
			Assert.AreEqual(HandIkMode.SoftHold, result.Mode);
			Assert.AreEqual(HandIkIntent.WeaponHold, result.LeftIntent);
			Assert.AreEqual(HandIkIntent.MovementRelaxation, result.RightIntent);
			Assert.AreEqual(1f, result.LeftWeightTarget, 0.001f);
			Assert.AreEqual(0f, result.RightWeightTarget, 0.001f);
		}

		[Test]
		public void StandingNotReadyRun_KeepsLeftHold()
		{
			var query = new UnitHandIkModeResolver.Query
			{
				Running = true,
				PeacefulCarry = true
			};
			UnitHandIkModeResolver.Result result = UnitHandIkModeResolver.Resolve(query, DefaultWeights());
			Assert.AreEqual(HandIkMode.SoftHold, result.Mode);
			Assert.AreEqual(HandIkIntent.WeaponHold, result.LeftIntent);
			Assert.AreEqual(1f, result.LeftWeightTarget, 0.001f);
			Assert.AreEqual(0f, result.RightWeightTarget, 0.001f);
		}

		[Test]
		public void StandingLowReadyWalk_KeepsHold()
		{
			UnitHandIkModeResolver.Weights weights = DefaultWeights();
			weights.ReadyBlend01 = 1f;
			var query = new UnitHandIkModeResolver.Query
			{
				Walking = true,
				PeacefulCarry = false
			};
			UnitHandIkModeResolver.Result result = UnitHandIkModeResolver.Resolve(query, weights);
			Assert.AreEqual(HandIkMode.Hold, result.Mode);
			Assert.AreEqual(0.9f, result.LeftWeightTarget, 0.001f);
			Assert.AreEqual(0.35f, result.RightWeightTarget, 0.001f);
		}

		[Test]
		public void StandingNotReadyWalk_Reacquiring_LeftOnGrip_RightFollowsWalkClip()
		{
			var query = new UnitHandIkModeResolver.Query
			{
				Walking = true,
				PeacefulCarry = true,
				Reacquiring = true
			};
			UnitHandIkModeResolver.Result result = UnitHandIkModeResolver.Resolve(query, DefaultWeights());
			Assert.AreEqual(HandIkMode.SoftHold, result.Mode);
			Assert.AreEqual(1f, result.LeftWeightTarget, 0.001f);
			Assert.AreEqual(0f, result.RightWeightTarget, 0.001f);
		}

		[Test]
		public void StandingNotReadyWalk_PoseBlend_KeepsTransitionHold()
		{
			var query = new UnitHandIkModeResolver.Query
			{
				Walking = true,
				PeacefulCarry = true,
				PoseBlending = true
			};
			UnitHandIkModeResolver.Result result = UnitHandIkModeResolver.Resolve(query, DefaultWeights());
			Assert.AreEqual(HandIkMode.Transition, result.Mode);
			Assert.AreEqual(0.9f, result.LeftWeightTarget, 0.001f);
			Assert.AreEqual(0.35f, result.RightWeightTarget, 0.001f);
		}
	}
}

using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class PerceptionWorkStaggerTests
	{
		[Test]
		public void Phase01_IsUnitInterval()
		{
			Assert.GreaterOrEqual(PerceptionWorkStagger.Phase01(1), 0f);
			Assert.Less(PerceptionWorkStagger.Phase01(1), 1f);
			Assert.AreNotEqual(PerceptionWorkStagger.Phase01(1), PerceptionWorkStagger.Phase01(2));
		}

		[Test]
		public void NextInterval_StaysInBandPlusJitter()
		{
			Random.InitState(7);
			float delay = PerceptionWorkStagger.NextIntervalSeconds(42, 0.2f, 0.3f);
			Assert.GreaterOrEqual(delay, 0.2f);
			Assert.LessOrEqual(delay, 0.3f + PerceptionWorkStagger.FrameJitterSeconds(42) + 0.0001f);
		}

		[Test]
		public void FrameJitter_DiffersById()
		{
			Assert.AreNotEqual(
				PerceptionWorkStagger.FrameJitterSeconds(10),
				PerceptionWorkStagger.FrameJitterSeconds(11));
		}
	}
}

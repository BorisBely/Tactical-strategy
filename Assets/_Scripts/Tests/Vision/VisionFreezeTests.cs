using NUnit.Framework;

namespace Vision.Tests
{
	public sealed class VisionFreezeTests
	{
		[Test]
		public void FreezeReport_MatchesMathDefaults()
		{
			VisionFreezeBaseline.ReportResult result = VisionFreezeBaseline.BuildReport();
			Assert.AreEqual(0, result.FailCount, result.Body);
		}
	}
}

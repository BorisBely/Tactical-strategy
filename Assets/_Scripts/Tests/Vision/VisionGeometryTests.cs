using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class VisionGeometryTests
	{
		[Test]
		public void OpticFov_399Inside_401Outside_At225m()
		{
			const float halfFov = 4f;
			const float rangeSq = 300f * 300f;
			Vector3 origin = Vector3.zero;
			Vector3 forward = Vector3.forward;
			Vector3 inside = Quaternion.AngleAxis(3.99f, Vector3.up) * Vector3.forward * 225f;
			Vector3 outside = Quaternion.AngleAxis(4.01f, Vector3.up) * Vector3.forward * 225f;

			Assert.IsTrue(
				VisionGeometry.IsWithinRangeAndFov(origin, forward, inside, rangeSq, halfFov, out _),
				"3.99° at 225 m must be inside optic half-FOV 4°");
			Assert.IsFalse(
				VisionGeometry.IsWithinRangeAndFov(origin, forward, outside, rangeSq, halfFov, out _),
				"4.01° at 225 m must be outside optic half-FOV 4°");
		}
	}
}

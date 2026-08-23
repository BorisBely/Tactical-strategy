#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Offline G8 LOD math smoke (no Play Mode). Full runtime suite writes DetectionG8_LAST.txt on Play.
/// </summary>
public static class DetectionG8TestRunner
{
	[MenuItem("Tools/Tests/Archive/G Stages/Run DetectionG8 Lod Smoke (no Play)", false, 136)]
	public static void RunLodSmokeFromMenu()
	{
		string report = BuildLodSmokeReport();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "DetectionG8_Math_LAST.txt");
		File.WriteAllText(latest, report, Encoding.UTF8);
		int resultAt = report.LastIndexOf("RESULT=", StringComparison.Ordinal);
		string resultLine = resultAt >= 0 ? report.Substring(resultAt).Trim() : "RESULT=UNKNOWN";
		Debug.Log($"[DetectionG8TestRunner] wrote {latest} {resultLine}\n{report}");
	}

	public static string BuildLodSmokeReport()
	{
		var sb = new StringBuilder(2048);
		int pass = 0;
		int fail = 0;

		void Check(string name, bool ok, string detail)
		{
			if (ok)
			{
				pass++;
				sb.AppendLine($"PASS {name} | {detail}");
			}
			else
			{
				fail++;
				sb.AppendLine($"FAIL {name} | {detail}");
			}
		}

		sb.AppendLine($"DetectionG8 LodMathSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine("---");

		VisionLodObserverContext idle = new VisionLodObserverContext
		{
			SecondsSinceLastDetailScan = 0.1f,
			SecondsSinceLastMembershipScan = 0.1f,
			DiscoverIntervalSeconds = 0.5f,
			MembershipIntervalSeconds = 1.5f
		};
		Check("Math_Idle",
			VisionLodMath.ResolveObserverTier(idle) == VisionScanTier.Idle,
			"Idle");
		idle.ImmediateScan = true;
		Check("Math_ImmediateDetail",
			VisionLodMath.ResolveObserverTier(idle) == VisionScanTier.Detail,
			"Immediate → T3");
		Check("Math_T2NoLos",
			!VisionLodMath.MaySpendLos(VisionScanTier.RangeFov) &&
			!VisionLodMath.MayApplyVisionFrame(VisionScanTier.RangeFov),
			"T2 no rays / no vision frame");
		Check("Math_IdleLongerThanDetail",
			VisionLodMath.IntervalScale(VisionScanTier.Idle) >
			VisionLodMath.IntervalScale(VisionScanTier.Detail),
			"Idle interval scale");
		Check("Math_BehindFailsFov",
			!VisionGeometry.IsWithinCoarseRangeAndFov(
				Vector3.zero, Vector3.forward, new Vector3(0f, 0f, -10f),
				false, default, 400f, 60f, out _, out _, out _),
			"Behind observer");
		Check("Math_AheadPassesFov",
			VisionGeometry.IsWithinCoarseRangeAndFov(
				Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 10f),
				false, default, 400f, 60f, out _, out _, out _),
			"Ahead");
		Check("Math_CacheExpired",
			!VisionLodMath.CacheIsValid(
				1f, 0f, 0.3f,
				Vector3.zero, Vector3.zero,
				Vector3.forward, Vector3.forward,
				Vector3.forward, Vector3.forward,
				0.35f, 2.5f),
			"TTL");
		Check("Math_500mIsBucketNotEngage",
			VisionLodMath.Bucket(500f) == VisionDistanceBucket.Beyond500 ||
			VisionLodMath.Bucket(499f) == VisionDistanceBucket.Far500,
			"Distance buckets");

		sb.AppendLine("---");
		sb.AppendLine($"RESULT={(fail == 0 ? "PASS" : "FAIL")} pass={pass} fail={fail}");
		return sb.ToString();
	}
}
#endif

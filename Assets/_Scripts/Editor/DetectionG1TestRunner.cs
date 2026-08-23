#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Offline G1 math smoke (no Play Mode). Full runtime suite writes DetectionG1_LAST.txt on Play.
/// </summary>
public static class DetectionG1TestRunner
{
	[MenuItem("Tools/Tests/Archive/G Stages/Run DetectionG1 Math Smoke (no Play)", false, 130)]
	public static void RunMathSmokeFromMenu()
	{
		string report = BuildMathSmokeReport();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "DetectionG1_Math_LAST.txt");
		File.WriteAllText(latest, report, Encoding.UTF8);
		Debug.Log($"[DetectionG1TestRunner] wrote {latest}\n{report}");
	}

	public static string BuildMathSmokeReport()
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

		sb.AppendLine($"DetectionG1 MathSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine("---");

		float d10 = DetectionQualityMath.DistanceFactor(10f);
		float d100 = DetectionQualityMath.DistanceFactor(100f);
		float d400 = DetectionQualityMath.DistanceFactor(400f);
		Check("Math_DistanceMonotone", d10 >= d100 && d100 >= d400, $"d10={d10:F3} d100={d100:F3} d400={d400:F3}");

		float f0 = DetectionQualityMath.FovFactor(0f);
		float f50 = DetectionQualityMath.FovFactor(50f);
		Check("Math_FovMonotone", f0 >= f50, $"f0={f0:F3} f50={f50:F3}");

		float qFull = DetectionQualityMath.VisibilityQuality(d100, f0, 1f, 1f);
		float qLow = DetectionQualityMath.VisibilityQuality(d100, f0, 0.1f, 1f);
		Check("Math_ExposureMonotone", qFull >= qLow, $"qFull={qFull:F3} qLow={qLow:F3}");

		float qIdle = DetectionQualityMath.VisibilityQuality(d400, f50, 0.1f, DetectionQualityMath.MovementFactor(0f));
		float qRun = DetectionQualityMath.VisibilityQuality(d400, f50, 0.1f, DetectionQualityMath.MovementFactor(4.5f));
		Check("Math_MovementHelpsButNotMagic", qRun > qIdle && qRun < 0.5f, $"idle={qIdle:F3} run={qRun:F3}");

		float acquired = DetectionQualityMath.IntegrateProgress(0f, 1f, 0.1f);
		float afterLoss = DetectionQualityMath.IntegrateProgress(1f, 0f, 0.1f);
		Check("Math_AcquireFasterThanLose", (acquired - 0f) > (1f - afterLoss),
			$"acqDelta={acquired:F3} loseDelta={1f - afterLoss:F3}");

		float mid = (DetectionQualityMath.DefaultLoseThreshold + DetectionQualityMath.DefaultAcquireThreshold) * 0.5f;
		float held = DetectionQualityMath.IntegrateProgress(0.55f, mid, 0.5f);
		Check("Math_HysteresisHold", Mathf.Abs(held - 0.55f) < 0.0001f, $"midQ={mid:F3} held={held:F3}");

		float progress = 0f;
		for (int i = 0; i < 20; i++)
			progress = DetectionQualityMath.IntegrateProgress(progress, 1f, 0.05f);
		float soft = progress;
		for (int i = 0; i < 3; i++)
			soft = DetectionQualityMath.IntegrateProgress(soft, 0f, 0.05f);
		Check("Math_SoftLoseKeepsProgress", soft > 0.5f, $"before={progress:F3} afterGap={soft:F3}");

		float qA = DetectionQualityMath.VisibilityQuality(
			DetectionQualityMath.DistanceFactor(10f), DetectionQualityMath.FovFactor(0f), 1f, 1f);
		float qF = DetectionQualityMath.VisibilityQuality(
			DetectionQualityMath.DistanceFactor(400f), DetectionQualityMath.FovFactor(50f), 1f, 1f);
		Check("Math_PresetA_BetterThanF", qA > qF, $"A={qA:F3} F={qF:F3}");

		sb.AppendLine("---");
		sb.AppendLine($"RESULT={(fail == 0 ? "PASS" : "FAIL")} pass={pass} fail={fail}");
		return sb.ToString();
	}
}
#endif

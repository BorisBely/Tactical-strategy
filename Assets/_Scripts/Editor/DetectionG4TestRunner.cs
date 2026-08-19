#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Offline G4 memory math smoke (no Play Mode). Full runtime suite writes DetectionG4_LAST.txt on Play.
/// </summary>
public static class DetectionG4TestRunner
{
	[MenuItem("Tools/Tests/Run DetectionG4 Memory Smoke (no Play)")]
	public static void RunMemorySmokeFromMenu()
	{
		string report = BuildMemorySmokeReport();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "DetectionG4_Math_LAST.txt");
		File.WriteAllText(latest, report, Encoding.UTF8);
		int resultAt = report.LastIndexOf("RESULT=", StringComparison.Ordinal);
		string resultLine = resultAt >= 0 ? report.Substring(resultAt).Trim() : "RESULT=UNKNOWN";
		Debug.Log($"[DetectionG4TestRunner] wrote {latest} {resultLine}\n{report}");
	}

	public static string BuildMemorySmokeReport()
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

		sb.AppendLine($"DetectionG4 MemoryMathSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine("---");

		Check("Math_ZeroTimeInitial",
			Mathf.Abs(MemoryDecayMath.Evaluate(0f, 1f) - 1f) < 0.0001f, "t=0 → 1");
		Check("Math_NegativeElapsedInitial",
			Mathf.Abs(MemoryDecayMath.Evaluate(-2f, 0.4f) - 0.4f) < 0.0001f, "elapsed<0");
		Check("Math_HorizonZero",
			MemoryDecayMath.Evaluate(MemoryDecayMath.DefaultHorizonSeconds, 1f) <= 0.0001f, "horizon");
		Check("Math_PastHorizonZero",
			MemoryDecayMath.Evaluate(99f, 0.8f) <= 0.0001f, "t>>horizon");

		float prev = 1f;
		bool monotone = true;
		for (int i = 1; i <= 20; i++)
		{
			float next = MemoryDecayMath.Evaluate(i * 0.5f, 1f);
			if (next > prev + 0.0001f)
				monotone = false;
			prev = next;
		}
		Check("Math_Monotone", monotone, "t↑ ⇒ conf↓");

		float early = MemoryDecayMath.Evaluate(1f, 1f);
		float late = MemoryDecayMath.Evaluate(7f, 1f);
		Check("Math_EarlyGreaterLate", early > late, $"1s={early:F3} 7s={late:F3}");

		float full = MemoryDecayMath.Evaluate(3f, 1f);
		float half = MemoryDecayMath.Evaluate(3f, 0.5f);
		float low = MemoryDecayMath.Evaluate(3f, 0.2f);
		Check("Math_InitialHalf", Mathf.Abs(half - full * 0.5f) < 0.0001f, $"half={half:F3}");
		Check("Math_InitialLow", Mathf.Abs(low - full * 0.2f) < 0.0001f, $"low={low:F3}");

		Check("Math_ClampInitial", Mathf.Abs(MemoryDecayMath.Evaluate(0f, 1.7f) - 1f) < 0.0001f, "initial>1");
		Check("Math_Stale", MemoryDecayMath.IsStale(0.2f) && !MemoryDecayMath.IsStale(0.9f), "stale band");
		Check("Math_Forgotten", MemoryDecayMath.IsForgotten(0f) && !MemoryDecayMath.HasMemory(0f), "forgotten");

		sb.AppendLine("---");
		sb.AppendLine($"RESULT={(fail == 0 ? "PASS" : "FAIL")} pass={pass} fail={fail}");
		return sb.ToString();
	}
}
#endif

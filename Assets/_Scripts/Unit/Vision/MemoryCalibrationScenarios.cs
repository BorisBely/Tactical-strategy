using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Block B memory gameplay calibration (math, no Play).
/// Does not retune Detection Q. Does not know about orders, reload, Search, or target selection.
/// </summary>
public static class MemoryCalibrationScenarios
{
	#region Constants
	public const float BaselineRecentlyLostSeconds = MemoryDecayMath.DefaultRecentlyLostSeconds;
	public const float BaselineHorizonSeconds = MemoryDecayMath.DefaultHorizonSeconds;
	public const float BaselineShape = MemoryDecayMath.DefaultShapeExponent;
	public const float BaselineStale = MemoryDecayMath.DefaultStaleThreshold;

	public static readonly float[] DecaySampleSeconds =
	{
		0f, 2f, 5f, 8f, 10f, 12f, 15f, 20f, 25f, 30f, 35f
	};

	public static readonly float[] TimelineSampleSeconds =
	{
		0f, 2f, 5f, 8f, 10f, 12f, 15f, 20f, 25f, 30f, 35f, 45f, 60f
	};

	public static readonly float[] RecentlyLostSweepSeconds = { 2f, 3f, 5f, 7f, 10f };
	public static readonly float[] HorizonSweepSeconds = { 20f, 30f, 45f, 60f };
	#endregion

	#region Nested Types
	public sealed class ReportResult
	{
		public string Body;
		public int PassCount;
		public int FailCount;
	}
	#endregion

	#region Public API
	public static string FeelZone(float _elapsedSinceLoss)
	{
		if (_elapsedSinceLoss < BaselineRecentlyLostSeconds)
			return "VERY_FRESH";
		if (_elapsedSinceLoss < 12f)
			return "FRESH_MEMORY";
		if (_elapsedSinceLoss < 20f)
			return "UNCERTAIN_MEMORY";
		if (_elapsedSinceLoss < BaselineHorizonSeconds)
			return "STALE_APPROACH";
		return "FORGOTTEN";
	}

	public static ObservationState ExpectedObservationState(float _elapsedSinceLoss)
	{
		return _elapsedSinceLoss < BaselineRecentlyLostSeconds
			? ObservationState.RecentlyLost
			: ObservationState.Lost;
	}

	public static ReportResult BuildReport()
	{
		var sb = new StringBuilder(12288);
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

		sb.AppendLine("BLOCK B — MEMORY CALIBRATION MATH");
		sb.AppendLine("=================================");
		sb.AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine("B1 CONTRACT");
		sb.AppendLine($"RecentlyLost = {F(BaselineRecentlyLostSeconds, 1)} s");
		sb.AppendLine($"MemoryHorizon = {F(BaselineHorizonSeconds, 1)} s");
		sb.AppendLine($"Shape = {F(BaselineShape, 1)}");
		sb.AppendLine($"Stale = {F(BaselineStale, 2)}");
		sb.AppendLine("World State ≠ Perception State ≠ Decision State");
		sb.AppendLine("LastKnownPosition = LastSeenPosition (frozen, no velocity extrapolation)");
		sb.AppendLine("Forgotten ≠ Deleted");
		sb.AppendLine("Memory ≠ DetectionProgress ≠ IdentityConfidence");
		sb.AppendLine("---");

		Check("B1_RecentlyLost",
			Mathf.Abs(MemoryDecayMath.DefaultRecentlyLostSeconds - 5f) < 0.0001f,
			$"default={MemoryDecayMath.DefaultRecentlyLostSeconds:F1}");
		Check("B1_Horizon",
			Mathf.Abs(MemoryDecayMath.DefaultHorizonSeconds - 30f) < 0.0001f,
			$"default={MemoryDecayMath.DefaultHorizonSeconds:F1}");
		Check("B1_Shape",
			Mathf.Abs(MemoryDecayMath.DefaultShapeExponent - 1.5f) < 0.0001f,
			$"default={MemoryDecayMath.DefaultShapeExponent:F1}");
		Check("B1_Stale",
			Mathf.Abs(MemoryDecayMath.DefaultStaleThreshold - 0.25f) < 0.0001f,
			$"default={MemoryDecayMath.DefaultStaleThreshold:F2}");

		float eval0 = MemoryDecayMath.Evaluate(0f, 1f);
		float evalHorizon = MemoryDecayMath.Evaluate(BaselineHorizonSeconds, 1f);
		float evalPast = MemoryDecayMath.Evaluate(BaselineHorizonSeconds + 5f, 1f);
		Check("B3_Evaluate0", Mathf.Abs(eval0 - 1f) < 0.0001f, $"conf={F(eval0, 3)}");
		Check("B3_EvaluateHorizon", evalHorizon <= 0.0001f, $"conf={F(evalHorizon, 3)}");
		Check("B3_EvaluateHorizonPlus", evalPast <= 0.0001f, $"conf={F(evalPast, 3)}");

		float prev = eval0;
		bool monotone = true;
		bool inRange = eval0 >= 0f && eval0 <= 1f;
		for (int i = 1; i <= 40; i++)
		{
			float t = i * 1f;
			float conf = MemoryDecayMath.Evaluate(t, 1f);
			if (conf > prev + 0.0001f)
				monotone = false;
			if (conf < 0f || conf > 1f)
				inRange = false;
			prev = conf;
		}

		Check("B3_NeverIncreases", monotone, "t↑ ⇒ conf↓");
		Check("B3_Clamp01", inRange, "0 ≤ conf ≤ 1");

		Check("B3_StaleAbove",
			!MemoryDecayMath.IsStale(BaselineStale + 0.01f),
			"conf > 0.25 → not stale");
		Check("B3_StaleAt",
			MemoryDecayMath.IsStale(BaselineStale),
			"conf = 0.25 → stale");
		Check("B3_StaleBelow",
			MemoryDecayMath.IsStale(0.10f),
			"0 < conf ≤ 0.25 → stale");
		Check("B3_ForgottenNotStale",
			!MemoryDecayMath.IsStale(0f) && MemoryDecayMath.IsForgotten(0f),
			"conf = 0 → forgotten, not stale (production)");

		float tStale = MemoryDecayMath.ElapsedSecondsForConfidence(BaselineStale);
		float confAtStaleT = MemoryDecayMath.Evaluate(tStale, 1f);
		Check("B3_StaleCrossingInverts",
			Mathf.Abs(confAtStaleT - BaselineStale) < 0.001f,
			$"t={F(tStale, 2)} conf={F(confAtStaleT, 3)}");

		sb.AppendLine("---");
		sb.AppendLine("MEMORY_DECAY  H=30  shape=1.5");
		sb.AppendLine("stale column = production IsStale (forgotten at conf=0 is stale=false)");
		for (int i = 0; i < DecaySampleSeconds.Length; i++)
		{
			float t = DecaySampleSeconds[i];
			float conf = MemoryDecayMath.Evaluate(t, 1f);
			bool stale = MemoryDecayMath.IsStale(conf);
			bool forgotten = MemoryDecayMath.IsForgotten(conf);
			sb.AppendLine(
				$"t={F(t, 1)}  conf={F(conf, 3)} stale={stale.ToString().ToLowerInvariant()} forgotten={forgotten.ToString().ToLowerInvariant()} zone={FeelZone(t)}");
		}

		sb.AppendLine("---");
		sb.AppendLine("FEEL ZONES (gameplay labels, not extra production states)");
		sb.AppendLine("0–5s   VERY_FRESH        RecentlyLost  high confidence");
		sb.AppendLine("5–12s  FRESH_MEMORY      Lost          high confidence");
		sb.AppendLine("12–20s UNCERTAIN_MEMORY  Lost          medium confidence");
		sb.AppendLine("20–30s STALE_APPROACH    Lost          low / stale");
		sb.AppendLine("30s+   FORGOTTEN         conf=0        contact remains");
		sb.AppendLine($"stale crossing ≈ {F(tStale, 2)} s (conf={F(BaselineStale, 2)})");

		sb.AppendLine("---");
		sb.AppendLine("B11 RecentlyLost sweep (horizon fixed 30s, diagnostic, not FAIL)");
		for (int i = 0; i < RecentlyLostSweepSeconds.Length; i++)
		{
			float grace = RecentlyLostSweepSeconds[i];
			sb.AppendLine(
				$"RecentlyLost={F(grace, 1)}s  t<{F(grace, 1)}→RecentlyLost  t>={F(grace, 1)}→Lost");
		}

		sb.AppendLine("---");
		sb.AppendLine("B12 Horizon sweep (shape=1.5, diagnostic, not FAIL)");
		for (int h = 0; h < HorizonSweepSeconds.Length; h++)
		{
			float horizon = HorizonSweepSeconds[h];
			sb.AppendLine($"H={F(horizon, 0)}");
			for (int i = 0; i < DecaySampleSeconds.Length; i++)
			{
				float t = DecaySampleSeconds[i];
				float conf = MemoryDecayMath.Evaluate(t, 1f, horizon, BaselineShape);
				bool stale = MemoryDecayMath.IsStale(conf);
				sb.AppendLine($"  t={F(t, 1)}  conf={F(conf, 3)} stale={stale.ToString().ToLowerInvariant()}");
			}
		}

		sb.AppendLine("---");
		sb.AppendLine($"RESULT={(fail == 0 ? "PASS" : "FAIL")} pass={pass} fail={fail}");

		return new ReportResult
		{
			Body = sb.ToString(),
			PassCount = pass,
			FailCount = fail
		};
	}

	public static string F(float _value, int _digits)
	{
		return _value.ToString("F" + _digits, CultureInfo.InvariantCulture);
	}
	#endregion
}

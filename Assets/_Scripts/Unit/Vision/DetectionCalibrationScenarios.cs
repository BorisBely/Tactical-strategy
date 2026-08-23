using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Gameplay calibration scale A–H (not DetectionTestController A–G).
/// Math A–H via <see cref="DetectionQualityMath"/> defaults. V1.8c: AcquireThreshold 0.25, Q→time + gate.
/// </summary>
public static class DetectionCalibrationScenarios
{
	#region Constants
	public const float WalkSpeedMeters = 1.4f;
	public const float RunSpeedMeters = 4.5f;
	public const float SimDtSeconds = 0.02f;
	public const float TimeoutSeconds = 12f;
	public const float UnlogicalNeighborJump = 1.5f;
	private static readonly float[] s_FovCurveOffsets = { 0f, 30f, 50f, 60f };
	private static readonly float[] s_V13FovCurve =
	{
		1.000f, 0.370f, 0.150f, 0.150f
	};
	private static readonly FrozenCal[] s_V13Baseline =
	{
		new FrozenCal("A", 1.0000f, 1.000f, "0.36", "Detected"),
		new FrozenCal("B", 1.0000f, 1.000f, "0.36", "Detected"),
		new FrozenCal("C", 0.4802f, 1.000f, "0.74", "Detected"),
		new FrozenCal("D", 0.2045f, 0.370f, "timeout", "Undetected"),
		new FrozenCal("E", 0.2502f, 1.000f, "timeout", "Undetected"),
		new FrozenCal("F", 0.0256f, 0.150f, "timeout", "Undetected"),
		new FrozenCal("G", 0.0037f, 0.150f, "timeout", "Undetected"),
		new FrozenCal("H", 0.0006f, 0.150f, "timeout", "Undetected")
	};
	private static readonly float[] s_QTimeSweep =
	{
		1.00f, 0.90f, 0.80f, 0.70f, 0.60f, 0.50f, 0.40f, 0.30f, 0.25f
	};
	private static readonly float[] s_GateQ =
	{
		0.251f, 0.250f, 0.249f
	};
	#endregion

	#region Nested Types
	public readonly struct Scenario
	{
		public readonly string Id;
		public readonly float DistanceMeters;
		public readonly float Exposure01;
		public readonly float FovOffsetDegrees;
		public readonly float MoveSpeedMeters;
		public readonly string ExpectedCategory;

		public Scenario(
			string _id,
			float _distanceMeters,
			float _exposure01,
			float _fovOffsetDegrees,
			float _moveSpeedMeters,
			string _expectedCategory)
		{
			Id = _id;
			DistanceMeters = _distanceMeters;
			Exposure01 = _exposure01;
			FovOffsetDegrees = _fovOffsetDegrees;
			MoveSpeedMeters = _moveSpeedMeters;
			ExpectedCategory = _expectedCategory;
		}
	}

	public struct QSnapshot
	{
		public float DistanceFactor;
		public float FovFactor;
		public float ExposureFactor;
		public float MovementFactor;
		public float Q;
	}

	public sealed class ProgressRun
	{
		public float TDetect = -1f;
		public float Progress;
		public float MaxProgress;
		public DetectionState State = DetectionState.Undetected;
		public bool Detected;
		public string ActualCategory;
		public string Trace;
	}

	public sealed class ReportResult
	{
		public string Body;
		public int PassCount;
		public int FailCount;
	}

	private readonly struct FrozenCal
	{
		public readonly string Id;
		public readonly float Q;
		public readonly float FovFactor;
		public readonly string TDetect;
		public readonly string State;

		public FrozenCal(string _id, float _q, float _fovFactor, string _tDetect, string _state)
		{
			Id = _id;
			Q = _q;
			FovFactor = _fovFactor;
			TDetect = _tDetect;
			State = _state;
		}
	}
	#endregion

	#region Public API
	public static Scenario[] All
	{
		get
		{
			return new[]
			{
				new Scenario("A", 10f, 1f, 0f, 0f, "I"),
				new Scenario("B", 30f, 1f, 0f, WalkSpeedMeters, "VF"),
				new Scenario("C", 80f, 0.5f, 0f, 0f, "F"),
				new Scenario("D", 80f, 0.5f, 30f, WalkSpeedMeters, "F-M"),
				new Scenario("E", 150f, 0.3f, 0f, 0f, "M"),
				// F/G/H stay as Q-math samples past the eye cap. Runtime without optic: no observation.
				new Scenario("F", 250f, 0.3f, 50f, 0f, "S"),
				new Scenario("G", 400f, 0.1f, 50f, RunSpeedMeters, "VS"),
				new Scenario("H", 500f, 0.05f, 60f, 0f, "N")
			};
		}
	}

	public static QSnapshot EvaluateQ(in Scenario _scenario)
	{
		QSnapshot snap = default;
		snap.DistanceFactor = DetectionQualityMath.DistanceFactor(_scenario.DistanceMeters);
		snap.FovFactor = DetectionQualityMath.FovFactor(_scenario.FovOffsetDegrees);
		snap.ExposureFactor = Mathf.Clamp01(_scenario.Exposure01);
		snap.MovementFactor = DetectionQualityMath.MovementFactor(_scenario.MoveSpeedMeters);
		snap.Q = DetectionQualityMath.VisibilityQuality(
			snap.DistanceFactor,
			snap.FovFactor,
			snap.ExposureFactor,
			snap.MovementFactor);
		return snap;
	}

	public static ProgressRun SimulateProgress(float _quality)
	{
		return SimulateProgress(_quality, DetectionQualityMath.DefaultAcquireThreshold);
	}

	public static ProgressRun SimulateProgress(in Scenario _scenario)
	{
		return SimulateProgress(
			EvaluateQ(_scenario).Q,
			DetectionQualityMath.DefaultAcquireThreshold,
			AttentionMath.EvaluateMultiplier(_scenario.FovOffsetDegrees));
	}

	/// <summary>
	/// Runtime Detected gate. FrozenCal timeout (D, E–H) stays gated — linear Q sim must not
	/// force Detected when Stage 6 keeps partial Exposure × periphery as a miss.
	/// </summary>
	public static bool ExpectsRuntimeDetected(in Scenario _scenario)
	{
		for (int i = 0; i < s_V13Baseline.Length; i++)
		{
			if (s_V13Baseline[i].Id != _scenario.Id)
				continue;
			if (string.Equals(s_V13Baseline[i].TDetect, "timeout", StringComparison.Ordinal))
				return false;
			break;
		}

		return SimulateProgress(EvaluateQ(_scenario).Q).Detected;
	}

	public static ProgressRun SimulateProgress(
		float _quality,
		float _acquireThreshold,
		float _attentionMultiplier = 1f)
	{
		var run = new ProgressRun();
		var trace = new StringBuilder(512);
		float progress = 0f;
		DetectionState state = DetectionState.Undetected;
		bool loggedDetecting = false;
		bool logged25 = false;
		bool logged50 = false;
		bool logged75 = false;
		float t = 0f;
		float maxProgress = 0f;
		float att = AttentionMath.ClampMultiplier(_attentionMultiplier);

		trace.AppendLine($"START Q={F(_quality, 3)} attMul={F(att, 2)}");
		trace.AppendLine($"Q={F(_quality, 3)} state={state} progress={F(progress, 3)} t=0.00");

		while (t < TimeoutSeconds - 0.0001f)
		{
			progress = DetectionQualityMath.IntegrateProgress(
				progress,
				_quality,
				SimDtSeconds,
				DetectionQualityMath.DefaultAcquireTime,
				DetectionQualityMath.DefaultLossTime,
				_acquireThreshold,
				DetectionQualityMath.DefaultLoseThreshold,
				DetectionQualityMath.DefaultAcquisitionExponent,
				att);
			t += SimDtSeconds;
			if (progress > maxProgress)
				maxProgress = progress;
			DetectionState next = DetectionQualityMath.ResolveState(progress);

			if (!loggedDetecting && next == DetectionState.Detecting && state == DetectionState.Undetected)
			{
				loggedDetecting = true;
				trace.AppendLine($"STATE Undetected -> Detecting t={F(t, 2)}");
			}

			if (!logged25 && progress >= 0.25f)
			{
				logged25 = true;
				trace.AppendLine($"progress={F(progress, 3)} t={F(t, 2)}");
			}

			if (!logged50 && progress >= 0.50f)
			{
				logged50 = true;
				trace.AppendLine($"progress={F(progress, 3)} t={F(t, 2)}");
			}

			if (!logged75 && progress >= 0.75f)
			{
				logged75 = true;
				trace.AppendLine($"progress={F(progress, 3)} t={F(t, 2)}");
			}

			if (next == DetectionState.Detected)
			{
				run.TDetect = t;
				run.Detected = true;
				run.Progress = progress;
				run.MaxProgress = maxProgress;
				run.State = DetectionState.Detected;
				trace.AppendLine($"STATE {state} -> Detected t={F(t, 2)}");
				trace.AppendLine($"DETECTED t={F(t, 2)} progress={F(progress, 3)}");
				trace.AppendLine("END");
				run.ActualCategory = CategoryFromTime(t, true);
				run.Trace = trace.ToString();
				return run;
			}

			state = next;
		}

		run.TDetect = -1f;
		run.Detected = false;
		run.Progress = progress;
		run.MaxProgress = maxProgress;
		run.State = DetectionQualityMath.ResolveState(progress);
		run.ActualCategory = CategoryFromTime(-1f, false);
		trace.AppendLine($"TIMEOUT t={F(TimeoutSeconds, 2)} state={run.State} progress={F(progress, 3)}");
		trace.AppendLine("END");
		run.Trace = trace.ToString();
		return run;
	}

	public static ReportResult BuildReport()
	{
		var sb = new StringBuilder(32768);
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

		sb.AppendLine("VISION DETECTION CALIBRATION");
		sb.AppendLine("============================");
		sb.AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine("pass=V1.8c Q→time (AcquireThreshold=0.25; AcquireTime=0.35)");
		sb.AppendLine("---");
		sb.AppendLine("Defaults:");
		sb.AppendLine($"DistanceCurve=normalized t=d/ResolvedMaxRange edge={F(DetectionQualityMath.DefaultFarFactor, 3)}");
		sb.AppendLine($"DistanceDefaultRange={F(DetectionQualityMath.DefaultFarMeters, 0)}");
		sb.AppendLine($"FovHalfReference={F(DetectionQualityMath.DefaultFovHalfDegrees, 0)}");
		sb.AppendLine($"FovEdgeFactor={F(DetectionQualityMath.DefaultFovEdgeFactor, 3)}");
		sb.AppendLine($"AcquireThreshold={F(DetectionQualityMath.DefaultAcquireThreshold, 2)}");
		sb.AppendLine($"LoseThreshold={F(DetectionQualityMath.DefaultLoseThreshold, 2)}");
		sb.AppendLine($"AcquireTime={F(DetectionQualityMath.DefaultAcquireTime, 2)}");
		sb.AppendLine($"AcquisitionExponent={F(DetectionQualityMath.DefaultAcquisitionExponent, 1)}");
		sb.AppendLine($"LossTime={F(DetectionQualityMath.DefaultLossTime, 2)}");
		sb.AppendLine("---");

		Scenario[] scenarios = All;
		var snaps = new QSnapshot[scenarios.Length];
		var runs = new ProgressRun[scenarios.Length];
		for (int i = 0; i < scenarios.Length; i++)
		{
			snaps[i] = EvaluateQ(scenarios[i]);
			runs[i] = SimulateProgress(scenarios[i]);
		}

		sb.AppendLine("FOV CURVE");
		sb.AppendLine("---------");
		for (int i = 0; i < s_FovCurveOffsets.Length; i++)
		{
			float offset = s_FovCurveOffsets[i];
			float after = DetectionQualityMath.FovFactor(offset);
			sb.AppendLine(
				$"offset={F(offset, 0)} before={F(s_V13FovCurve[i], 3)} after={F(after, 3)}");
		}

		sb.AppendLine("---");
		sb.AppendLine("BEFORE vs AFTER (V1.3 frozen vs current)");
		sb.AppendLine("----------------------------------------");
		for (int i = 0; i < scenarios.Length; i++)
		{
			FrozenCal before = s_V13Baseline[i];
			QSnapshot after = snaps[i];
			ProgressRun run = runs[i];
			string tAfter = run.Detected ? F(run.TDetect, 2) : "timeout";
			sb.AppendLine(
				$"{before.Id}  Q {F(before.Q, 4)} -> {F(after.Q, 4)}  " +
				$"fov {F(before.FovFactor, 3)} -> {F(after.FovFactor, 3)}  " +
				$"tDetect {before.TDetect} -> {tAfter}  " +
				$"state {before.State} -> {run.State}");
		}

		sb.AppendLine("---");
		sb.AppendLine("SCENARIOS");
		sb.AppendLine("---------");

		for (int i = 0; i < scenarios.Length; i++)
		{
			Scenario sc = scenarios[i];
			QSnapshot q = snaps[i];
			ProgressRun run = runs[i];
			string tDetect = run.Detected ? F(run.TDetect, 2) : "timeout";

			sb.AppendLine($"CAL {sc.Id}");
			sb.AppendLine(
				$"dist={F(sc.DistanceMeters, 1)} fov={F(sc.FovOffsetDegrees, 1)} " +
				$"exp={F(sc.Exposure01, 2)} move={F(q.MovementFactor, 2)} q={F(q.Q, 2)}");
			sb.AppendLine(
				$"distanceFactor={F(q.DistanceFactor, 3)} fovFactor={F(q.FovFactor, 3)} " +
				$"exposureFactor={F(q.ExposureFactor, 3)} movementFactor={F(q.MovementFactor, 3)}");
			sb.AppendLine(
				$"state={run.State} progress={F(run.Progress, 3)} tDetect={tDetect} " +
				$"thresholdA={F(DetectionQualityMath.DefaultAcquireThreshold, 2)} " +
				$"thresholdL={F(DetectionQualityMath.DefaultLoseThreshold, 2)}");
			sb.AppendLine($"expected={sc.ExpectedCategory} actual={run.ActualCategory} (category bins diagnostic only)");
			sb.AppendLine($"[{sc.Id}]");
			string[] lines = run.Trace.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
			for (int t = 0; t < lines.Length; t++)
			{
				if (string.IsNullOrEmpty(lines[t]))
					continue;
				sb.AppendLine($"[{sc.Id}] {lines[t]}");
			}

			sb.AppendLine();
		}

		sb.AppendLine("RELATIVE ORDER");
		sb.AppendLine("--------------");
		for (int i = 0; i < scenarios.Length; i++)
			sb.AppendLine($"Q {scenarios[i].Id}={F(snaps[i].Q, 4)}");

		float qA = snaps[0].Q;
		float qB = snaps[1].Q;
		float qC = snaps[2].Q;
		float qD = snaps[3].Q;
		float qE = snaps[4].Q;
		float qF = snaps[5].Q;
		float qG = snaps[6].Q;
		float qH = snaps[7].Q;

		Check("Order_A_ge_B", qA + 0.0001f >= qB, $"A={F(qA, 4)} B={F(qB, 4)}");
		Check("Order_B_gt_C", qB > qC, $"B={F(qB, 4)} C={F(qC, 4)}");
		Check("Order_E_gt_F", qE > qF, $"E={F(qE, 4)} F={F(qF, 4)}");
		Check("Order_F_gt_G", qF > qG, $"F={F(qF, 4)} G={F(qG, 4)}");
		Check("Order_G_ge_H", qG + 0.0001f >= qH, $"G={F(qG, 4)} H={F(qH, 4)}");
		Check(
			"Order_D_not_unlogical_vs_C",
			qD <= qC * UnlogicalNeighborJump + 0.0001f,
			$"C={F(qC, 4)} D={F(qD, 4)} (walk vs 30° FOV may be close; fail only if D > C×{UnlogicalNeighborJump})");

		ProgressRun[] qTimeRuns = AppendQTimeSweep(sb);
		bool monotone = true;
		var monotoneSb = new StringBuilder();
		for (int i = 0; i < s_QTimeSweep.Length - 2; i++)
		{
			ProgressRun a = qTimeRuns[i];
			ProgressRun b = qTimeRuns[i + 1];
			if (!a.Detected || !b.Detected || a.TDetect > b.TDetect + 0.0001f)
			{
				monotone = false;
				monotoneSb.Append($" {F(s_QTimeSweep[i], 2)}->{F(s_QTimeSweep[i + 1], 2)}");
			}
		}

		Check(
			"Time_Monotone_Q1_to_Q30",
			monotone,
			monotone
				? "tDetect non-decreasing as Q 1.00→0.30"
				: "jump" + monotoneSb);

		ProgressRun[] gateRuns = AppendGate(sb);
		Check("Gate_Q251_Detected", gateRuns[0].Detected, $"Q=0.251 tDetect={(gateRuns[0].Detected ? F(gateRuns[0].TDetect, 2) : "timeout")}");
		Check("Gate_Q250_NotDetected", !gateRuns[1].Detected && gateRuns[1].MaxProgress < 0.0001f,
			$"Q=0.250 state={gateRuns[1].State} maxProgress={F(gateRuns[1].MaxProgress, 3)}");
		Check("Gate_Q249_NotDetected", !gateRuns[2].Detected && gateRuns[2].MaxProgress < 0.0001f,
			$"Q=0.249 state={gateRuns[2].State} maxProgress={F(gateRuns[2].MaxProgress, 3)}");

		sb.AppendLine("---");
		sb.AppendLine($"RESULT={(fail == 0 ? "PASS" : "FAIL")} pass={pass} fail={fail}");

		return new ReportResult
		{
			Body = sb.ToString(),
			PassCount = pass,
			FailCount = fail
		};
	}

	public static string CategoryFromTime(float _tDetect, bool _detected)
	{
		if (!_detected || _tDetect < 0f)
			return "N";
		if (_tDetect < 0.2f)
			return "I";
		if (_tDetect < 0.5f)
			return "VF";
		if (_tDetect < 1f)
			return "F";
		if (_tDetect < 2f)
			return "M";
		if (_tDetect < 4f)
			return "S";
		if (_tDetect < 8f)
			return "VS";
		return "N";
	}
	#endregion

	#region Private Methods
	private static ProgressRun[] AppendQTimeSweep(StringBuilder _sb)
	{
		var runs = new ProgressRun[s_QTimeSweep.Length];
		float thr = DetectionQualityMath.DefaultAcquireThreshold;
		float tAcq = DetectionQualityMath.DefaultAcquireTime;

		_sb.AppendLine("---");
		_sb.AppendLine("Q TIME SWEEP V1.8c");
		_sb.AppendLine("------------------");
		_sb.AppendLine($"AcquireThreshold={F(thr, 2)} AcquireTime={F(tAcq, 2)}");
		_sb.AppendLine("branch=grow if Q>thr; hold if lose<Q<=thr; decay if Q<=lose");
		_sb.AppendLine();

		for (int i = 0; i < s_QTimeSweep.Length; i++)
		{
			float q = s_QTimeSweep[i];
			ProgressRun run = SimulateProgress(q, thr);
			runs[i] = run;
			string tDetect = run.Detected ? F(run.TDetect, 2) : "timeout";
			_sb.AppendLine(
				$"Q={F(q, 2)} thr={F(thr, 2)} tAcq={F(tAcq, 2)} " +
				$"tDetect={tDetect} state={run.State} maxProgress={F(run.MaxProgress, 3)} " +
				$"branch={ResolveProgressBranch(q)}");
		}

		return runs;
	}

	private static ProgressRun[] AppendGate(StringBuilder _sb)
	{
		var runs = new ProgressRun[s_GateQ.Length];
		float thr = DetectionQualityMath.DefaultAcquireThreshold;

		_sb.AppendLine();
		_sb.AppendLine("GATE 0.25");
		_sb.AppendLine("---------");
		_sb.AppendLine("expect Q=0.251 grow/Detected; Q=0.250 hold/timeout; Q=0.249 hold/timeout");

		for (int i = 0; i < s_GateQ.Length; i++)
		{
			float q = s_GateQ[i];
			ProgressRun run = SimulateProgress(q, thr);
			runs[i] = run;
			string tDetect = run.Detected ? F(run.TDetect, 2) : "timeout";
			_sb.AppendLine(
				$"Q={F(q, 3)} thr={F(thr, 2)} tDetect={tDetect} state={run.State} " +
				$"maxProgress={F(run.MaxProgress, 3)} branch={ResolveProgressBranch(q)}");
		}

		return runs;
	}

	private static string ResolveProgressBranch(float _quality)
	{
		float acquire = Mathf.Clamp01(DetectionQualityMath.DefaultAcquireThreshold);
		float lose = Mathf.Clamp(DetectionQualityMath.DefaultLoseThreshold, 0f, acquire);
		if (_quality > acquire)
			return "grow";
		if (_quality > lose)
			return "hold";
		return "decay";
	}

	private static string F(float _value, int _digits)
	{
		return _value.ToString("F" + _digits, CultureInfo.InvariantCulture);
	}
	#endregion
}

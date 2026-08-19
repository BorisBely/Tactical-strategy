using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// V1.9.5: run G1–G8 + G8 Stress sequentially in one Play.
/// Each stage still writes its own *_LAST.txt.
/// Summary: Assets/_Docs/Logs/Tests/DetectionG_Regression_LAST.txt
/// </summary>
[DefaultExecutionOrder(56)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class DetectionGRegressionPlaySmoke : MonoBehaviour
{
	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private bool m_Started;
	private int m_PassCount;
	private int m_FailCount;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		if (m_Started)
			return;
		if (DetectionHarnessPlayMode.RunGStage != DetectionHarnessPlayMode.AllGStages)
			return;
		m_Started = true;
		StartCoroutine(RunAll());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunGStage == DetectionHarnessPlayMode.AllGStages)
			DetectionHarnessPlayMode.ResetFlags();
	}
	#endregion

	#region Public Methods
	public void RunFromEditor()
	{
		if (!isActiveAndEnabled)
			return;
		StopAllCoroutines();
		m_Started = true;
		StartCoroutine(RunAll());
	}
	#endregion

	#region Private Methods
	private IEnumerator RunAll()
	{
		yield return null;
		yield return null;

		m_Report.Clear();
		m_PassCount = 0;
		m_FailCount = 0;
		m_Report.AppendLine("DETECTION G1–G8 REGRESSION (one Play)");
		m_Report.AppendLine("====================================");
		m_Report.AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		m_Report.AppendLine("V1.9.5 — sequential AutoSmoke, no Q retune");
		m_Report.AppendLine("---");

		Debug.Log("[DetectionGRegressionPlaySmoke] G1–G8 + Stress starting (one Play).", this);

		DetectionG1AutoSmoke g1 = GetComponent<DetectionG1AutoSmoke>();
		DetectionG2AutoSmoke g2 = GetComponent<DetectionG2AutoSmoke>();
		DetectionG3AutoSmoke g3 = GetComponent<DetectionG3AutoSmoke>();
		DetectionG4AutoSmoke g4 = GetComponent<DetectionG4AutoSmoke>();
		DetectionG5AutoSmoke g5 = GetComponent<DetectionG5AutoSmoke>();
		DetectionG6AutoSmoke g6 = GetComponent<DetectionG6AutoSmoke>();
		DetectionG7AutoSmoke g7 = GetComponent<DetectionG7AutoSmoke>();
		DetectionG8AutoSmoke g8 = GetComponent<DetectionG8AutoSmoke>();
		DetectionG8StressSmoke g8s = GetComponent<DetectionG8StressSmoke>();

		if (g1 != null) yield return g1.RunSuite();
		AppendStage("G1", "DetectionG1_LAST.txt", g1 != null);
		CleanupBetweenStages();
		yield return null;

		if (g2 != null) yield return g2.RunSuite();
		AppendStage("G2", "DetectionG2_LAST.txt", g2 != null);
		CleanupBetweenStages();
		yield return null;

		if (g3 != null) yield return g3.RunSuite();
		AppendStage("G3", "DetectionG3_LAST.txt", g3 != null);
		CleanupBetweenStages();
		yield return null;

		if (g4 != null) yield return g4.RunSuite();
		AppendStage("G4", "DetectionG4_LAST.txt", g4 != null);
		CleanupBetweenStages();
		yield return null;

		if (g5 != null) yield return g5.RunSuite();
		AppendStage("G5", "DetectionG5_LAST.txt", g5 != null);
		CleanupBetweenStages();
		yield return null;

		if (g6 != null) yield return g6.RunSuite();
		AppendStage("G6", "DetectionG6_LAST.txt", g6 != null);
		CleanupBetweenStages();
		yield return null;

		if (g7 != null) yield return g7.RunSuite();
		AppendStage("G7", "DetectionG7_LAST.txt", g7 != null);
		CleanupBetweenStages();
		yield return null;

		if (g8 != null) yield return g8.RunSuite();
		AppendStage("G8", "DetectionG8_LAST.txt", g8 != null);
		CleanupBetweenStages();
		yield return null;

		if (g8s != null) yield return g8s.RunSuite();
		AppendStage("G8 Stress", "DetectionG8_Stress_LAST.txt", g8s != null);

		m_Report.AppendLine("---");
		m_Report.AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");

		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "DetectionG_Regression_LAST.txt");
		File.WriteAllText(latest, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			$"[DetectionGRegressionPlaySmoke] wrote {latest} RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);
	}

	private void AppendStage(string _label, string _fileName, bool _present)
	{
		if (!_present)
		{
			m_FailCount++;
			m_Report.AppendLine($"FAIL {_label} | AutoSmoke missing on harness");
			Debug.LogError($"[DetectionGRegressionPlaySmoke] FAIL {_label} | AutoSmoke missing", this);
			return;
		}

		string path = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests", _fileName);
		if (!File.Exists(path))
		{
			m_FailCount++;
			m_Report.AppendLine($"FAIL {_label} | missing {_fileName}");
			Debug.LogError($"[DetectionGRegressionPlaySmoke] FAIL {_label} | missing {_fileName}", this);
			return;
		}

		string body = File.ReadAllText(path);
		int at = body.LastIndexOf("RESULT=", StringComparison.Ordinal);
		string resultLine = at >= 0 ? body.Substring(at).Trim().Replace("\r", "").Split('\n')[0] : "RESULT=UNKNOWN";
		bool pass = resultLine.IndexOf("RESULT=PASS", StringComparison.Ordinal) >= 0;
		if (pass)
			m_PassCount++;
		else
			m_FailCount++;
		m_Report.AppendLine($"{(pass ? "PASS" : "FAIL")} {_label} | {resultLine}");
	}

	private void CleanupBetweenStages()
	{
		DestroyByName("G2_ObserverB");
		DestroyByName("G2_ObserverB_Minimal");
		DestroyByName("G3_ObserverB");
		DestroyByName("G3_ObserverB_Minimal");
		DestroyByName("G5_ForcedDummy");
		DestroyByName("IdentityCalib_ObserverB");
		DestroyByName("IdentityCalib_ObserverB_Minimal");

		DetectionTestController harness = GetComponent<DetectionTestController>();
		if (harness == null)
			return;

		if (harness.Observer != null &&
		    harness.Observer.TryGetComponent(out UnitVision vision))
			vision.enabled = true;

		harness.ResetPairToIdleCalibrationPad();

		if (harness.DetectionProcessor != null)
		{
			harness.DetectionProcessor.ClearContacts();
			harness.DetectionProcessor.ClearSimulatedTime();
			harness.DetectionProcessor.ClearAffiliationCue(harness.Target);
		}

		if (harness.Observer != null &&
		    harness.Observer.TryGetComponent(out TargetSelector selector))
			selector.ClearLineOfFireSuppression();
	}

	private static void DestroyByName(string _name)
	{
		GameObject go = GameObject.Find(_name);
		if (go != null)
			Destroy(go);
	}
	#endregion
}

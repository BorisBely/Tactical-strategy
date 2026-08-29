using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #7–#11 Play regression in one session. Each layer still writes its own LAST.txt.
/// Summary: Assets/_Docs/Logs/Tests/FrozenLayersPlay_LAST.txt
/// Menu: Tools/Tests/Run Regression (Play)
/// </summary>
[DefaultExecutionOrder(70)]
[DisallowMultipleComponent]
public sealed class FrozenLayersPlayCoordinator : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private int m_PassCount;
	private int m_FailCount;
	private int m_LayerFailCount;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunFrozenLayersPlay;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunFrozenLayersPlay)
			return;
		if (FindAnyObjectByType<FrozenLayersPlayCoordinator>() != null)
			return;
		var go = new GameObject("FrozenLayersPlayCoordinator");
		go.AddComponent<FrozenLayersPlayCoordinator>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunFrozenLayersPlay)
			DetectionHarnessPlayMode.ResetFlags();
	}
	#endregion

	#region Public Methods
	public void RunFromEditor()
	{
		if (!isActiveAndEnabled)
			return;
		StopAllCoroutines();
		StartCoroutine(RunSuite());
	}
	#endregion

	#region Private Methods
	private IEnumerator RunSuite()
	{
		yield return null;

		m_Report.Length = 0;
		m_PassCount = 0;
		m_FailCount = 0;
		m_LayerFailCount = 0;
		AppendLine("REGRESSION #7–#11 PLAY");
		AppendLine("======================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("#7 ImmediateThreat → #8 Combat Events → #9 Sound In AI → #10 Search 2.0 → #11 Command Priority");
		AppendLine("---");

		yield return RunLayer<ImmediateThreatLiveRuntimeSmoke>(
			"#7 Immediate Threat Live",
			"ImmediateThreatLiveRuntimeSmoke");
		yield return RunLayer<CombatEventWorldRuntimeSmoke>(
			"#8 Combat Event World",
			"CombatEventWorldRuntimeSmoke");
		yield return RunLayer<SoundInAiRuntimeSmoke>(
			"#9 Sound In AI",
			"SoundInAiRuntimeSmoke");
		yield return RunLayer<Search20RuntimeSmoke>(
			"#10 Search 2.0",
			"Search20RuntimeSmoke");
		yield return RunLayer<CommandPriorityRuntimeSmoke>(
			"#11 Command Priority",
			"CommandPriorityRuntimeSmoke");

		AppendLine("---");
		AppendLine(
			"RESULT=" + (m_LayerFailCount == 0 ? "PASS" : "FAIL") +
			" layersFail=" + m_LayerFailCount +
			" checksPass=" + m_PassCount +
			" checksFail=" + m_FailCount);
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "FrozenLayersPlay_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[FrozenLayers] Play " + (m_LayerFailCount == 0 ? "PASS" : "FAIL") +
			" layersFail=" + m_LayerFailCount +
			" checks=" + m_PassCount + "/" + m_FailCount +
			" → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunFrozenLayersPlay;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}

	private IEnumerator RunLayer<T>(string _label, string _goName) where T : MonoBehaviour, IPlaySmokeSuite
	{
		AppendLine("[" + _label + "]");
		var go = new GameObject(_goName);
		T smoke = go.AddComponent<T>();
		yield return smoke.RunAndWait();
		int pass = smoke.LastPassCount;
		int fail = smoke.LastFailCount;
		m_PassCount += pass;
		m_FailCount += fail;
		if (fail > 0)
			m_LayerFailCount++;
		AppendLine(
			(fail == 0 ? "PASS " : "FAIL ") + _label +
			" pass=" + pass + " fail=" + fail);
		Destroy(go);
		yield return null;
	}

	private void AppendLine(string _line)
	{
		m_Report.AppendLine(_line);
	}
	#endregion
}

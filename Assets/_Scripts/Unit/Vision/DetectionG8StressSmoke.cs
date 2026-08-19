using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// G8 stress: lightweight vision stubs at 10/25/50/100 observers. Writes DetectionG8_Stress_LAST.txt.
/// Not full Unit.prefab clones.
/// </summary>
[DefaultExecutionOrder(850)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class DetectionG8StressSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private float m_WarmupSeconds = 120f;
	[SerializeField] private float m_SampleSeconds = 1.2f;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(8192);
	private readonly List<GameObject> m_Spawned = new List<GameObject>(128);
	private int m_PassCount;
	private int m_FailCount;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		if (DetectionHarnessPlayMode.ShouldRunGAutoSmoke(m_RunOnStart, "G8Stress"))
			StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		Teardown();
		if (DetectionHarnessPlayMode.RunGStage == "G8Stress")
			DetectionHarnessPlayMode.ResetFlags();
	}
	#endregion

	#region Public Methods
	public void RunFromEditor()
	{
		if (!isActiveAndEnabled)
			return;
		StopAllCoroutines();
		Teardown();
		StartCoroutine(RunSuite());
	}
	#endregion

	#region Private Methods
	public IEnumerator RunSuite()
	{
		float warmup = DetectionHarnessPlayMode.GWarmupSeconds(m_WarmupSeconds);
		if (warmup > 0f)
			yield return new WaitForSeconds(warmup);
		else
			yield return null;
		m_Report.Clear();
		m_PassCount = 0;
		m_FailCount = 0;
		AppendLine($"DetectionG8 StressSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("---");

		int[] sizes = { 10, 25, 50, 100 };
		string[] modes = { "idle", "mixed", "combat" };
		for (int s = 0; s < sizes.Length; s++)
		{
			for (int m = 0; m < modes.Length; m++)
			{
				yield return RunCase(sizes[s], modes[m]);
				Teardown();
				yield return null;
			}
		}

		Finish();
	}

	private IEnumerator RunCase(int _observers, string _mode)
	{
		Vector3 enemyPos = new Vector3(40f, 0f, 40f);
		GameObject enemy = CreateStub($"G8StressEnemy_{_observers}_{_mode}", UnitTeamId.Enemy, enemyPos);
		m_Spawned.Add(enemy);
		for (int i = 0; i < _observers; i++)
		{
			float ang = i * Mathf.PI * 2f / _observers;
			Vector3 pos = new Vector3(Mathf.Cos(ang) * 30f, 0f, Mathf.Sin(ang) * 30f);
			GameObject obs = CreateStub($"G8StressObs_{i}", UnitTeamId.Player, pos);
			bool lookAt = _mode == "combat" || (_mode == "mixed" && (i % 2 == 0));
			if (lookAt)
				obs.transform.LookAt(enemyPos);
			else
				obs.transform.rotation = Quaternion.LookRotation(Vector3.back);
			m_Spawned.Add(obs);
		}

		yield return null;
		if (_mode == "combat")
		{
			for (int i = 0; i < m_Spawned.Count; i++)
			{
				UnitVision vision = m_Spawned[i].GetComponent<UnitVision>();
				if (vision != null && vision.GetComponent<UnitTeam>().Team == UnitTeamId.Player)
					vision.RequestImmediateScan();
			}
		}

		float t0 = Time.realtimeSinceStartup;
		int scans = 0;
		int los = 0;
		int hz = 0;
		int contacts = 0;
		float maxMs = 0f;
		float elapsed = 0f;
		int frames = 0;
		while (elapsed < m_SampleSeconds)
		{
			float frameStart = Time.realtimeSinceStartup;
			yield return null;
			frames++;
			float frameMs = (Time.realtimeSinceStartup - frameStart) * 1000f;
			if (frameMs > maxMs)
				maxMs = frameMs;
			elapsed = Time.realtimeSinceStartup - t0;
		}

		for (int i = 0; i < m_Spawned.Count; i++)
		{
			UnitVision vision = m_Spawned[i].GetComponent<UnitVision>();
			if (vision == null)
				continue;
			scans += vision.ScanStats.VisionScanCount;
			los += vision.ScanStats.LosCheckCount;
			hz += vision.ScanStats.HitZoneCheckCount;
			DetectionProcessor processor = vision.GetComponent<DetectionProcessor>();
			if (processor != null)
				contacts += processor.Contacts.Count;
		}

		float avgMs = frames > 0 ? (elapsed * 1000f) / frames : maxMs;
		AppendLine(
			$"CASE observers={_observers} mode={_mode} scans={scans} los={los} hitZones={hz} contacts={contacts} avgMs~={avgMs:F2} maxMs={maxMs:F2}");
		Check($"Stress_{_observers}_{_mode}_Completed", true, $"scans={scans} los={los}");
		Check($"Stress_{_observers}_{_mode}_LosNotExploding",
			los < _observers * 80 * 20,
			$"los={los}");
	}

	private static GameObject CreateStub(string _name, UnitTeamId _team, Vector3 _position)
	{
		var go = new GameObject(_name);
		go.transform.position = _position;
		UnitTeam team = go.AddComponent<UnitTeam>();
		team.SetTeam(_team);
		go.AddComponent<UnitObservationSource>();
		go.AddComponent<UnitPerception>();
		CapsuleCollider col = go.AddComponent<CapsuleCollider>();
		col.height = 1.8f;
		col.radius = 0.3f;
		col.center = new Vector3(0f, 0.9f, 0f);
		UnitVision vision = go.AddComponent<UnitVision>();
		vision.SetVisionRange(80f);
		return go;
	}

	private void Teardown()
	{
		for (int i = 0; i < m_Spawned.Count; i++)
		{
			if (m_Spawned[i] != null)
				Destroy(m_Spawned[i]);
		}

		m_Spawned.Clear();
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		string body = m_Report.ToString();
		File.WriteAllText(Path.Combine(dir, $"DetectionG8_Stress_{stamp}.txt"), body, Encoding.UTF8);
		File.WriteAllText(Path.Combine(dir, "DetectionG8_Stress_LAST.txt"), body, Encoding.UTF8);
		Debug.Log($"[DetectionG8StressSmoke] wrote DetectionG8_Stress_LAST.txt RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}", this);
	}

	private void Check(string _name, bool _ok, string _detail)
	{
		if (_ok)
		{
			m_PassCount++;
			AppendLine($"PASS {_name} | {_detail}");
		}
		else
		{
			m_FailCount++;
			AppendLine($"FAIL {_name} | {_detail}");
			Debug.LogError($"[DetectionG8StressSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line)
	{
		m_Report.AppendLine(_line);
	}
	#endregion
}

using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #12 Play TargetCalibrationArena: selection, hysteresis, lost LOS, mismatch diagnostic.
/// Does not retune Vision / G6 / A10 / weapon envelope. Selection ≠ Fire.
/// Report: Assets/_Docs/Logs/Tests/TargetCalibration_LAST.txt
/// Menu: Tools/Tests/Run Target Calibration (Play)
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
public sealed class TargetCalibrationRuntimeSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(8192);
	private int m_PassCount;
	private int m_FailCount;
	private GameObject m_Arena;
	private GameObject m_Observer;
	private GameObject m_E1;
	private GameObject m_E2;
	private GameObject m_E3;
	private readonly GameObject[] m_Baseline = new GameObject[10];
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunTargetCalibration;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunTargetCalibration)
			return;
		if (FindAnyObjectByType<TargetCalibrationRuntimeSmoke>() != null)
			return;
		var go = new GameObject("TargetCalibrationRuntimeSmoke");
		go.AddComponent<TargetCalibrationRuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyArena();
		if (DetectionHarnessPlayMode.RunTargetCalibration)
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
		AppendLine("STAGE 12 — TARGET + FIRE CALIBRATION");
		AppendLine("====================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Selection ≠ Fire. Hysteresis. AI/Combat mismatch is diagnostic.");
		AppendLine("---");

		SpawnArena();
		DetectionProcessor processor = m_Observer.GetComponent<DetectionProcessor>();
		TargetSelector selector = m_Observer.GetComponent<TargetSelector>();
		EngagementDecisionController engagement = m_Observer.GetComponent<EngagementDecisionController>();
		UnitPerception perception = m_Observer.GetComponent<UnitPerception>();
		processor.SetSimulatedTime(0f);

		AppendLine("[S0] Baseline 10 contacts (snapshot, determinism)");
		SpawnBaseline();
		ObserveMany(processor, perception, m_Baseline, 16);
		DumpBaseline(processor, selector, engagement);
		Transform first = selector.SelectedTarget;
		selector.SelectFromContacts();
		Check("S0_Deterministic", selector.SelectedTarget == first, Slot(selector.SelectedTarget));
		Check("S0_HasSelected", selector.SelectedTarget != null, "none");
		ClearBaseline();
		selector.ClearSelection(false);
		processor.ClearContacts();

		AppendLine("[SA] E1 visible alone → selected");
		PlaceArenaTargets();
		Observe(processor, m_E1.transform, 12f, 16);
		Check("SA_E1", selector.SelectedTarget == m_E1.transform, Slot(selector.SelectedTarget));

		AppendLine("[SB] E2 slightly closer → remain E1");
		m_E2.transform.position = new Vector3(-7.5f, 0f, 4f);
		ObserveTwo(processor, perception, m_E1.transform, 12f, m_E2.transform, 8.6f, 10);
		Check("SB_HoldE1", selector.SelectedTarget == m_E1.transform, Slot(selector.SelectedTarget));
		Check(
			"SB_Hysteresis",
			selector.LastSelection.SwitchReason == TargetSwitchReason.Hysteresis,
			selector.LastSelection.SwitchReason.ToString());

		AppendLine("[SC] E2 High Hostile → switch E2");
		Stamp(processor, m_E2.transform, ThreatLevel.High, PerceivedIdentity.Hostile);
		selector.SelectFromContacts();
		Check("SC_SwitchE2", selector.SelectedTarget == m_E2.transform, Slot(selector.SelectedTarget));
		Check(
			"SC_HigherScore",
			selector.LastSelection.SwitchReason == TargetSwitchReason.HigherScore,
			selector.LastSelection.SwitchReason.ToString());
		Check("SC_Switched", selector.LastSelection.Switched, "switch=0");

		AppendLine("[SD] E2 loses LOS → remain selected, Track");
		float now = processor.PerceptionClock;
		processor.ApplyEmptyObservationFrame();
		processor.Advance(0.3f, now + 0.3f);
		Check("SD_RemainE2", selector.SelectedTarget == m_E2.transform, Slot(selector.SelectedTarget));
		Check("SD_NotEngageable", selector.GetEngageableSelectedTarget() == null, "engageable");
		engagement.RefreshDecisionNow();
		Check("SD_Track", engagement.CurrentDecision == EngagementDecision.Track,
			engagement.CurrentDecision.ToString());

		AppendLine("[SE] E2 forgotten, E3 visible → E3");
		now = processor.PerceptionClock;
		perception.ApplyVisionFrame(new[] { Obs(m_E3.transform, m_E3.transform.position, 9f) });
		processor.Advance(
			MemoryDecayMath.DefaultHorizonSeconds + 0.5f,
			now + MemoryDecayMath.DefaultHorizonSeconds + 0.5f);
		Check("SE_E3", selector.SelectedTarget == m_E3.transform, Slot(selector.SelectedTarget));

		AppendLine("[SG] Golden: hold / switch / track / no oscillation");
		processor.ClearContacts();
		selector.ClearSelection(false);
		m_E1.transform.position = new Vector3(0f, 0f, 10f);
		m_E2.transform.position = new Vector3(0f, 0f, -10f);
		Observe(processor, m_E1.transform, 10f, 16);
		Check("SG_A", selector.SelectedTarget == m_E1.transform, Slot(selector.SelectedTarget));
		m_E2.transform.position = new Vector3(0f, 0f, -9.2f);
		ObserveTwo(processor, perception, m_E1.transform, 10f, m_E2.transform, 9.2f, 8);
		Check("SG_Hold", selector.SelectedTarget == m_E1.transform, Slot(selector.SelectedTarget));
		Stamp(processor, m_E2.transform, ThreatLevel.High, PerceivedIdentity.Hostile);
		selector.SelectFromContacts();
		Check("SG_SwitchB", selector.SelectedTarget == m_E2.transform, Slot(selector.SelectedTarget));
		now = processor.PerceptionClock;
		processor.ApplyEmptyObservationFrame();
		processor.Advance(0.3f, now + 0.3f);
		Check("SG_RemainB", selector.SelectedTarget == m_E2.transform, Slot(selector.SelectedTarget));
		Check("SG_NotEngageable", !selector.HasSelectedAimPoint, "aim");
		engagement.RefreshDecisionNow();
		Check("SG_Track", engagement.CurrentDecision == EngagementDecision.Track,
			engagement.CurrentDecision.ToString());
		ObserveTwo(processor, perception, m_E1.transform, 10f, m_E2.transform, 9.2f, 8);
		Check("SG_NoOscillation", selector.SelectedTarget == m_E2.transform, Slot(selector.SelectedTarget));

		AppendLine("[SH] Friendly never selected; Unknown may be");
		processor.ClearContacts();
		selector.ClearSelection(false);
		processor.SetAffiliationCue(m_E1.transform, ObservableAffiliation.Friendly);
		Observe(processor, m_E1.transform, 10f, 40);
		Check("SH_Friendly", selector.SelectedTarget == null, Slot(selector.SelectedTarget));
		processor.ClearAffiliationCue(m_E1.transform);
		processor.ClearContacts();
		selector.ClearSelection(false);
		Observe(processor, m_E3.transform, 8f, 16);
		Check("SH_Unknown", selector.SelectedTarget == m_E3.transform, Slot(selector.SelectedTarget));

		AppendLine("[SJ] AI/Combat mismatch is diagnostic, not merged");
		UnitAIController ai = m_Observer.GetComponent<UnitAIController>() ??
		                      m_Observer.AddComponent<UnitAIController>();
		ai.TryApplyCommand(
			UnitAICommand.Defense(UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
		ai.SetPerceptionFrame(HostileVisible(m_E1.transform));
		ai.Tick(0.05f);
		selector.SetSelectedTargetForDiagnostics(m_E2.transform, m_E2.transform.position);
		engagement.RefreshDecisionNow();
		Check("SJ_Mismatch", engagement.EngageTargetMismatch, "mismatch=0");
		Check(
			"SJ_Reason",
			engagement.EngageTargetMismatchReason == TargetCombatMismatch.Explanation,
			engagement.EngageTargetMismatchReason);
		Check("SJ_CombatKept", selector.SelectedTarget == m_E2.transform, Slot(selector.SelectedTarget));
		Check("SJ_AiKept", ai.CurrentEngageTarget == m_E1.transform, Slot(ai.CurrentEngageTarget));

		AppendLine("[SF] High Threat + no LOS ≠ Fire");
		EngagementDecisionContext ctx = new EngagementDecisionContext
		{
			HasSelectedTarget = true,
			HasContact = true,
			Identity = PerceivedIdentity.Hostile,
			Relationship = PerceivedRelationship.Hostile,
			Threat = ThreatLevel.High,
			ObservationState = ObservationState.Lost,
			LastSeenConfidence = 0.8f,
			HasKnowledge = true,
			IsWorldEngageable = true,
			HasLosConfirmedAim = false,
			WeaponCanFireEventually = true,
			AimReadyToFire = true
		};
		Check("SF_Track", EngagementDecisionMath.Evaluate(ctx) == EngagementDecision.Track,
			EngagementDecisionMath.Evaluate(ctx).ToString());

		Finish();
		yield return null;
	}

	private void SpawnArena()
	{
		DestroyArena();
		m_Arena = new GameObject("TargetCalibrationArena");
		m_Observer = new GameObject("TCA_AI");
		m_Observer.transform.SetParent(m_Arena.transform, false);
		m_Observer.SetActive(false);
		m_Observer.AddComponent<UnitObservationSource>();
		m_Observer.AddComponent<UnitPerception>();
		m_Observer.AddComponent<DetectionProcessor>();
		m_Observer.AddComponent<TargetSelector>();
		m_Observer.AddComponent<EngagementDecisionController>();
		m_Observer.SetActive(true);
		m_E1 = new GameObject("E1");
		m_E2 = new GameObject("E2");
		m_E3 = new GameObject("E3");
		m_E1.transform.SetParent(m_Arena.transform, false);
		m_E2.transform.SetParent(m_Arena.transform, false);
		m_E3.transform.SetParent(m_Arena.transform, false);
		PlaceArenaTargets();
	}

	private void PlaceArenaTargets()
	{
		m_E1.transform.position = new Vector3(0f, 0f, 12f);
		m_E2.transform.position = new Vector3(-8f, 0f, 4f);
		m_E3.transform.position = new Vector3(8f, 0f, 4f);
	}

	private void SpawnBaseline()
	{
		for (int i = 0; i < m_Baseline.Length; i++)
		{
			m_Baseline[i] = new GameObject("BL_" + (i + 1).ToString("00"));
			m_Baseline[i].transform.SetParent(m_Arena.transform, false);
			float angle = i * 36f * Mathf.Deg2Rad;
			float radius = 6f + (i % 5) * 4f;
			m_Baseline[i].transform.position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
		}
	}

	private void ClearBaseline()
	{
		for (int i = 0; i < m_Baseline.Length; i++)
		{
			if (m_Baseline[i] != null)
				Destroy(m_Baseline[i]);
			m_Baseline[i] = null;
		}
	}

	private void DumpBaseline(
		DetectionProcessor _processor,
		TargetSelector _selector,
		EngagementDecisionController _engagement)
	{
		_engagement.RefreshDecisionNow();
		AppendLine(
			"selected=" + Slot(_selector.SelectedTarget) +
			" runnerUp=" + Slot(_selector.LastSelection.RunnerUp) +
			" score=" + _selector.LastSelection.SelectedScore.ToString("0.00") +
			" engageable=" + (_selector.LastSelection.Engageable ? "1" : "0") +
			" G6=" + _engagement.CurrentDecision +
			" switch=" + (_selector.LastSelection.Switched ? "1" : "0"));
		for (int i = 0; i < m_Baseline.Length; i++)
		{
			if (m_Baseline[i] == null)
				continue;
			Transform t = m_Baseline[i].transform;
			if (!_processor.TryGetContact(t, out PerceivedContact contact) || contact == null)
			{
				AppendLine("  " + t.name + " contact=none");
				continue;
			}

			AppendLine(
				"  " + t.name +
				" obs=" + contact.ObservationState +
				" threat=" + contact.Threat +
				" id=" + contact.Identity +
				" selected=" + (_selector.SelectedTarget == t ? "1" : "0"));
		}
	}

	private static void Observe(DetectionProcessor _processor, Transform _target, float _distance, int _ticks)
	{
		float now = _processor.PerceptionClock;
		for (int i = 0; i < _ticks; i++)
		{
			_processor.ApplySyntheticObservation(_target, _distance, 0f, 1f, _target.position);
			now += 0.05f;
			_processor.Advance(0.05f, now);
		}
	}

	private static void ObserveTwo(
		DetectionProcessor _processor,
		UnitPerception _perception,
		Transform _a, float _aDist,
		Transform _b, float _bDist,
		int _ticks)
	{
		float now = _processor.PerceptionClock;
		for (int i = 0; i < _ticks; i++)
		{
			_perception.ApplyVisionFrame(new[]
			{
				Obs(_a, _a.position, _aDist),
				Obs(_b, _b.position, _bDist)
			});
			now += 0.05f;
			_processor.Advance(0.05f, now);
		}
	}

	private static void ObserveMany(
		DetectionProcessor _processor,
		UnitPerception _perception,
		GameObject[] _targets,
		int _ticks)
	{
		var obs = new VisionObservation[_targets.Length];
		float now = _processor.PerceptionClock;
		for (int i = 0; i < _ticks; i++)
		{
			for (int t = 0; t < _targets.Length; t++)
			{
				Transform tr = _targets[t].transform;
				obs[t] = Obs(tr, tr.position, tr.position.magnitude);
			}

			_perception.ApplyVisionFrame(obs);
			now += 0.05f;
			_processor.Advance(0.05f, now);
		}
	}

	private static VisionObservation Obs(Transform _target, Vector3 _position, float _distance)
	{
		float dist = Mathf.Max(0.01f, _distance);
		return new VisionObservation
		{
			Target = _target,
			Position = _position,
			AimPoint = _position + Vector3.up * 1.2f,
			HasAimPoint = true,
			DistanceSq = dist * dist,
			IsVisible = true,
			FovOffsetDegrees = 0f,
			Exposure01 = 1f
		};
	}

	private static void Stamp(
		DetectionProcessor _processor,
		Transform _target,
		ThreatLevel _threat,
		PerceivedIdentity _identity)
	{
		if (!_processor.TryGetContact(_target, out PerceivedContact contact) || contact == null)
			return;
		contact.Threat = _threat;
		contact.Identity = _identity;
		if (_identity == PerceivedIdentity.Hostile)
			contact.Relationship = PerceivedRelationship.Hostile;
	}

	private static AIPerceptionFrame HostileVisible(Transform _target)
	{
		var knowledge = new AIContactKnowledge(
			_target,
			DetectionState.Detected,
			ObservationState.Observed,
			PerceivedIdentity.Hostile,
			1f,
			PerceivedRelationship.Hostile,
			ThreatLevel.High,
			_target.position,
			_target.position,
			0f,
			1f,
			true,
			false,
			false,
			true,
			false,
			false,
			true,
			false,
			false,
			true,
			false,
			false,
			false,
			true);
		return new AIPerceptionFrame(
			new[] { knowledge },
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			ThreatLevel.High);
	}

	private void Check(string _id, bool _pass, string _detail)
	{
		if (_pass)
		{
			m_PassCount++;
			AppendLine("PASS " + _id);
			return;
		}

		m_FailCount++;
		AppendLine("FAIL " + _id + " " + _detail);
	}

	private static string Slot(Transform _t)
	{
		return _t != null ? _t.name : "none";
	}

	private void DestroyArena()
	{
		ClearBaseline();
		if (m_Arena != null)
			Destroy(m_Arena);
		m_Arena = null;
		m_Observer = null;
		m_E1 = null;
		m_E2 = null;
		m_E3 = null;
	}

	private void AppendLine(string _line)
	{
		m_Report.AppendLine(_line);
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine("RESULT=" + (m_FailCount == 0 ? "PASS" : "FAIL") +
		           " pass=" + m_PassCount + " fail=" + m_FailCount);
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "TargetCalibration_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[TargetCalibration] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunTargetCalibration;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}

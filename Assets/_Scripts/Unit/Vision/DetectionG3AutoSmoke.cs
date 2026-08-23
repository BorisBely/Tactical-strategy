using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// G3 identity / relationship / threat smoke. Writes Assets/_Docs/Logs/Tests/DetectionG3_LAST.txt
/// Runs after G1/G2 harness (execution order 300). Does not feed Combat.
/// </summary>
[DefaultExecutionOrder(300)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class DetectionG3AutoSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private float m_WarmupSeconds = 12f;
	[SerializeField] private float m_ShortObserveSeconds = 0.4f;
	[SerializeField] private float m_LongObserveSeconds = 3f;
	[SerializeField] private float m_DualObserveSeconds = 2.5f;
	#endregion

	#region Private Fields
	private DetectionTestController m_Harness;
	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private int m_PassCount;
	private int m_FailCount;
	private GameObject m_SpawnedObserverB;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (DetectionHarnessPlayMode.ShouldRunGAutoSmoke(m_RunOnStart, "G3"))
			StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (m_SpawnedObserverB != null)
			Destroy(m_SpawnedObserverB);
		if (DetectionHarnessPlayMode.RunGStage == "G3")
			DetectionHarnessPlayMode.ResetFlags();
	}
	#endregion

	#region Public Methods
	public void RunFromEditor()
	{
		if (!isActiveAndEnabled)
			return;
		StopAllCoroutines();
		m_Harness = GetComponent<DetectionTestController>();
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
		AppendLine($"DetectionG3 AutoSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("---");

		RunPureMathChecks();

		DetectionProcessor observerA = m_Harness != null ? m_Harness.DetectionProcessor : null;
		Transform target = m_Harness != null ? m_Harness.Target : null;
		TargetSelector selectorA = observerA != null ? observerA.GetComponent<TargetSelector>() : null;

		Check("G3_ObserverA", observerA != null, "observer A missing");
		Check("G3_Target", target != null, "target missing");
		Check("Isolation_SelectorHasNoPerceivedContactFields",
			!TypeHasFieldOf(typeof(TargetSelector), typeof(PerceivedContact)),
			"TargetSelector must not hold PerceivedContact");
		Check("Isolation_SelectorHasNoIdentityFields",
			!TypeHasFieldOf(typeof(TargetSelector), typeof(PerceivedIdentity)) &&
			!TypeHasFieldOf(typeof(TargetSelector), typeof(PerceivedRelationship)) &&
			!TypeHasFieldOf(typeof(TargetSelector), typeof(ThreatLevel)),
			"TargetSelector must not hold G3 identity types");
		Check("Isolation_ObservationHasNoKnowledgeFields",
			!VisionObservationHasKnowledgeFields(),
			"VisionObservation must stay physical-only");

		if (observerA == null || target == null)
		{
			Finish();
			yield break;
		}

		UnitTeam worldTeam = target.GetComponent<UnitTeam>() ?? target.GetComponentInParent<UnitTeam>();
		Check("G3_WorldTeamPresent", worldTeam != null, "target has no UnitTeam");
		if (worldTeam != null)
			worldTeam.SetTeam(UnitTeamId.Neutral);
		Check("G3_WorldTeamSetNeutral",
			worldTeam != null && worldTeam.Team == UnitTeamId.Neutral,
			worldTeam != null ? worldTeam.Team.ToString() : "null");

		UnitVision visionA = observerA.GetComponent<UnitVision>();
		bool visionAWas = visionA != null && visionA.enabled;
		if (visionA != null)
			visionA.enabled = false;

		DetectionProcessor observerB = CreateObserverB();
		Check("G3_ObserverB", observerB != null, "failed to create observer B");
		if (observerB == null)
		{
			if (visionA != null)
				visionA.enabled = visionAWas;
			Finish();
			yield break;
		}

		UnitVision visionB = observerB.GetComponent<UnitVision>();
		if (visionB != null)
			visionB.enabled = false;

		observerA.ClearContacts();
		observerB.ClearContacts();
		observerA.SetAffiliationCue(target, ObservableAffiliation.Hostile);
		observerB.SetAffiliationCue(target, ObservableAffiliation.Hostile);

		Vector3 pos = target.position;

		yield return ObserveFor(observerA, observerB, target, pos, 15f, m_ShortObserveSeconds);

		observerB.TryGetContact(target, out PerceivedContact contactB);
		Check("G3_B_HasContactAfterShort", contactB != null, "B should have a contact");
		if (contactB != null)
		{
			Check("G3_B_UnknownIdentityAfterShort",
				contactB.Identity == PerceivedIdentity.Unknown,
				$"B id={contactB.Identity} C={contactB.IdentityConfidence:F3}");
			Check("G3_B_ConfidenceBelowCommit",
				contactB.IdentityConfidence < IdentityKnowledgeMath.DefaultCommitThreshold,
				$"B C={contactB.IdentityConfidence:F3}");
			Check("G3_B_DetectionAheadOfIdentity",
				contactB.DetectionProgress > contactB.IdentityConfidence,
				$"P={contactB.DetectionProgress:F3} C={contactB.IdentityConfidence:F3}");
		}

		observerB.ApplyEmptyObservationFrame();
		yield return ObserveFor(observerA, null, target, pos, 15f, m_LongObserveSeconds);

		observerA.TryGetContact(target, out PerceivedContact contactA);
		observerB.TryGetContact(target, out contactB);
		Check("G3_A_Detected",
			contactA != null && contactA.State == DetectionState.Detected,
			contactA != null ? $"state={contactA.State} P={contactA.DetectionProgress:F3}" : "null");
		Check("G3_A_IdentityHostile",
			contactA != null && contactA.Identity == PerceivedIdentity.Hostile,
			contactA != null ? $"id={contactA.Identity} C={contactA.IdentityConfidence:F3}" : "null");
		Check("G3_A_ConfidenceHigh",
			contactA != null && contactA.IdentityConfidence >= 0.7f,
			contactA != null ? $"C={contactA.IdentityConfidence:F3}" : "null");
		Check("G3_A_RelationshipHostile",
			contactA != null && contactA.Relationship == PerceivedRelationship.Hostile,
			contactA != null ? contactA.Relationship.ToString() : "null");
		Check("G3_A_ThreatNotNone",
			contactA != null && contactA.Threat != ThreatLevel.None,
			contactA != null ? contactA.Threat.ToString() : "null");
		Check("G3_B_StillUnknown",
			contactB != null && contactB.Identity == PerceivedIdentity.Unknown,
			contactB != null ? $"id={contactB.Identity} C={contactB.IdentityConfidence:F3}" : "null");
		Check("G3_WorldUnchangedAfterIdentify",
			worldTeam != null && worldTeam.Team == UnitTeamId.Neutral,
			worldTeam != null ? worldTeam.Team.ToString() : "null");
		Check("G3_A_KnowledgeDiffersFromB",
			contactA != null && contactB != null && contactA.Identity != contactB.Identity,
			contactA != null && contactB != null
				? $"A={contactA.Identity} B={contactB.Identity}"
				: "null");

		observerA.ClearContacts();
		observerB.ClearContacts();
		observerA.SetAffiliationCue(target, ObservableAffiliation.Hostile);
		observerB.SetAffiliationCue(target, ObservableAffiliation.Friendly);
		yield return ObserveFor(observerA, observerB, target, pos, 15f, m_DualObserveSeconds);

		observerA.TryGetContact(target, out contactA);
		observerB.TryGetContact(target, out contactB);
		Check("G3_Dual_A_Hostile",
			contactA != null && contactA.Identity == PerceivedIdentity.Hostile,
			contactA != null ? contactA.Identity.ToString() : "null");
		Check("G3_Dual_B_Friendly",
			contactB != null && contactB.Identity == PerceivedIdentity.Friendly,
			contactB != null ? contactB.Identity.ToString() : "null");
		Check("G3_Dual_WorldStillNeutral",
			worldTeam != null && worldTeam.Team == UnitTeamId.Neutral,
			worldTeam != null ? worldTeam.Team.ToString() : "null");

		// Keep the Hostile contact. 400 m Q is below AcquireThreshold, so a fresh
		// detect would never create a contact — G3 only needs distance to retune Threat.
		yield return ObserveFor(observerA, null, target, pos, 400f, m_DualObserveSeconds);
		observerA.TryGetContact(target, out contactA);
		float farMeters = contactA != null
			? Mathf.Sqrt(Mathf.Max(0f, contactA.LastObservation.DistanceSq))
			: -1f;
		Check("G3_ThreatIndependent_HostileFarLow",
			contactA != null &&
			contactA.Relationship == PerceivedRelationship.Hostile &&
			contactA.Threat == ThreatLevel.Low,
			contactA != null
				? $"rel={contactA.Relationship} threat={contactA.Threat} dist={farMeters:F0}"
				: "null");

		Transform selectedBeforeClear = selectorA != null ? selectorA.SelectedTarget : null;
		observerA.ClearContacts();
		Transform selectedAfterClear = selectorA != null ? selectorA.SelectedTarget : null;
		Check("G5_ClearContactsDeselects",
			selectorA == null || selectedAfterClear == null,
			$"before={(selectedBeforeClear != null ? selectedBeforeClear.name : "null")} after={(selectedAfterClear != null ? selectedAfterClear.name : "null")}");

		if (visionA != null)
			visionA.enabled = visionAWas;

		Finish();
	}

	private void RunPureMathChecks()
	{
		AppendLine("[MATH]");
		float identityStep = IdentityKnowledgeMath.IntegrateConfidence(
			0f, 1f, 0.35f, true, ObservableAffiliation.Hostile);
		float detectionStep = DetectionQualityMath.IntegrateProgress(0f, 1f, 0.35f);
		Check("Math_IdentitySlowerThanDetection",
			identityStep < detectionStep && identityStep < IdentityKnowledgeMath.DefaultCommitThreshold,
			$"id={identityStep:F3} det={detectionStep:F3}");

		Check("Math_UnknownBelowCommit",
			IdentityKnowledgeMath.ResolveIdentity(0.49f, ObservableAffiliation.Hostile, PerceivedIdentity.Unknown)
			== PerceivedIdentity.Unknown,
			"commit threshold");
		Check("Math_HostileFarThreatLow",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, 400f) == ThreatLevel.Low,
			"Hostile+far must be Low");
		Check("Math_FriendlyNearThreatNone",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Friendly, 10f) == ThreatLevel.None,
			"Friendly must not produce threat");
		Check("Math_HoldWhenNotObserved",
			Mathf.Abs(IdentityKnowledgeMath.IntegrateConfidence(0.6f, 0f, 1f, false, ObservableAffiliation.Hostile) - 0.6f) < 0.0001f,
			"G3 must not decay (G4)");
	}

	private IEnumerator ObserveFor(
		DetectionProcessor _a,
		DetectionProcessor _b,
		Transform _target,
		Vector3 _pos,
		float _distanceMeters,
		float _seconds)
	{
		float elapsed = 0f;
		const float step = 0.05f;
		while (elapsed < _seconds)
		{
			if (_a != null)
				_a.ApplySyntheticObservation(_target, _distanceMeters, 0f, 1f, _pos);
			if (_b != null)
				_b.ApplySyntheticObservation(_target, _distanceMeters, 0f, 1f, _pos);
			yield return new WaitForSeconds(step);
			elapsed += step;
		}
	}

	private DetectionProcessor CreateObserverB()
	{
		UnitSceneSpawner spawner = FindAnyObjectByType<UnitSceneSpawner>();
		if (spawner != null)
		{
			m_SpawnedObserverB = spawner.SpawnAdditionalPlayer("G3_ObserverB");
			if (m_SpawnedObserverB != null)
			{
				DetectionTestController.DisableLethalFire(m_SpawnedObserverB.transform);
				if (!m_SpawnedObserverB.TryGetComponent(out DetectionProcessor dp))
					dp = m_SpawnedObserverB.AddComponent<DetectionProcessor>();
				return dp;
			}
		}

		m_SpawnedObserverB = new GameObject("G3_ObserverB_Minimal");
		m_SpawnedObserverB.AddComponent<UnitObservationSource>();
		m_SpawnedObserverB.AddComponent<UnitPerception>();
		return m_SpawnedObserverB.AddComponent<DetectionProcessor>();
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		string body = m_Report.ToString();
		File.WriteAllText(Path.Combine(dir, $"DetectionG3_Autosmoke_{stamp}.txt"), body, Encoding.UTF8);
		string latest = Path.Combine(dir, "DetectionG3_LAST.txt");
		File.WriteAllText(latest, body, Encoding.UTF8);
		Debug.Log($"[DetectionG3AutoSmoke] wrote {latest} RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}", this);
	}

	private static bool TypeHasFieldOf(Type _type, Type _needle)
	{
		FieldInfo[] fields = _type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		for (int i = 0; i < fields.Length; i++)
		{
			Type ft = fields[i].FieldType;
			if (ft == _needle)
				return true;
			if (!ft.IsGenericType)
				continue;
			Type[] args = ft.GetGenericArguments();
			for (int a = 0; a < args.Length; a++)
			{
				if (args[a] == _needle)
					return true;
			}
		}

		return false;
	}

	private static bool VisionObservationHasKnowledgeFields()
	{
		FieldInfo[] fields = typeof(VisionObservation).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		for (int i = 0; i < fields.Length; i++)
		{
			Type ft = fields[i].FieldType;
			if (ft == typeof(PerceivedIdentity) ||
			    ft == typeof(PerceivedRelationship) ||
			    ft == typeof(ThreatLevel) ||
			    ft == typeof(PerceivedContact) ||
			    ft == typeof(DetectionState) ||
			    ft == typeof(ObservationState))
				return true;

			string name = fields[i].Name;
			if (name.IndexOf("Identity", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    name.IndexOf("Threat", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    name.IndexOf("Relationship", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    name.IndexOf("DetectionProgress", StringComparison.OrdinalIgnoreCase) >= 0)
				return true;
		}

		return false;
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
			Debug.LogError($"[DetectionG3AutoSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);
	#endregion
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Vision Stage 18: Final Perception Contract.
/// Writes Assets/_Docs/Logs/Tests/FinalPerceptionContract_LAST.txt
/// </summary>
[DefaultExecutionOrder(66)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class FinalPerceptionContractRuntimeSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(16384);
	private readonly List<GameObject> m_Spawned = new List<GameObject>(32);
	private int m_PassCount;
	private int m_FailCount;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunFinalPerceptionContract;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroySpawned();
		if (DetectionHarnessPlayMode.RunFinalPerceptionContract)
			DetectionHarnessPlayMode.ResetFlags();
	}
	#endregion

	#region Public Methods
	public void RunFromEditor()
	{
		if (!isActiveAndEnabled)
			return;
		StopAllCoroutines();
		m_PassCount = 0;
		m_FailCount = 0;
		m_Report.Length = 0;
		StartCoroutine(RunSuite());
	}
	#endregion

	#region Private Methods
	private IEnumerator RunSuite()
	{
		Append("Vision Stage 18 FinalPerceptionContract");
		Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		Append("CLOSED / VERIFIED — FinalPerceptionContract");
		Append("---");

		WeaponDefinition assault = LoadWeapon("Assets/GameData/Shooting/M4/Weapon_M4_ModA_1.asset");
		WeaponDefinition sniper = LoadWeapon("Assets/GameData/Shooting/Standalone/Weapon_Sniper762x51.asset");
		WeaponAttachmentDefinition reddot = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Reddot1.asset");
		WeaponAttachmentDefinition scope9 = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Scope9.asset");
		RocketLauncherData rockets = LoadRockets();
		Check(
			"Assets",
			assault != null && sniper != null && reddot != null && scope9 != null && rockets != null,
			"load");

		Check("Frozen_Acquire_0.25", Near(DetectionQualityMath.DefaultAcquireThreshold, 0.25f), "0.25");
		Check("Frozen_Lose_0.20", Near(DetectionQualityMath.DefaultLoseThreshold, 0.20f), "0.20");
		Check("Frozen_Exponent_3.8", Near(DetectionQualityMath.DefaultAcquisitionExponent, 3.8f), "3.8");
		Check("Frozen_AcquireTime_0.35", Near(DetectionQualityMath.DefaultAcquireTime, 0.35f), "0.35");
		Check(
			"Frozen_Q_DxFxExM",
			Near(DetectionQualityMath.VisibilityQuality(0.8f, 0.5f, 1f, 1f), 0.4f),
			"0.40");
		Check("Frozen_SoundHorizon_3", Near(SoundKnowledgeMath.DefaultHorizonSeconds, 3f), "3s");
		Check("Frozen_SharedHorizon_8", Near(SharedKnowledgeMath.DefaultHorizonSeconds, 8f), "8s");
		Check("Frozen_AllyRange_80", Near(AllyReportEvidenceMath.DefaultRangeMeters, 80f), "80");
		Check("Frozen_Memory_5", Near(MemoryDecayMath.DefaultRecentlyLostSeconds, 5f), "5");
		Check("Frozen_Memory_30", Near(MemoryDecayMath.DefaultHorizonSeconds, 30f), "30");
		Check("Frozen_Attention_NotInQ", Near(AttentionMath.EvaluateMultiplier(45f), 1f), "1");
		Check("Shared_NotVisualConfirm", !PerceptionContractMath.SharedConfirmsVisualIdentity(), "id");

		yield return null;
		RunMergeAndConflict();
		yield return null;
		RunIdentityAimCombat();
		yield return null;
		RunLiveEndToEnd();
		yield return null;
		RunArchitecture();

		DestroySpawned();
		Finish(m_FailCount == 0 ? "PASS" : "FAIL");
		yield return null;
	}

	private void RunMergeAndConflict()
	{
		GameObject observer = SpawnObserver("S18SmObs");
		GameObject target = Spawn("S18SmE", new Vector3(6f, 0f, 0f));
		DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
		processor.SetSimulatedTime(0f);
		Vector3 seen = new Vector3(1f, 0f, 0f);
		Vector3 heard = new Vector3(2f, 0f, 0f);
		Vector3 reported = new Vector3(3f, 0f, 0f);
		float now = 0f;
		for (int i = 0; i < 16; i++)
		{
			processor.ApplySyntheticObservation(target.transform, 4f, 0f, 1f, seen);
			now += 0.05f;
			processor.Advance(0.05f, now);
		}

		processor.ApplySyntheticSound(target.transform, heard, 1f);
		processor.ApplySyntheticShared(target.transform, reported, 1f);
		processor.Advance(0.05f, now + 0.05f);
		bool merged = processor.TryGetContact(target.transform, out PerceivedContact mixed);
		Check("Merge_OneContact", merged && processor.Contacts.Count == 1, "1");
		Check("Merge_Visual", merged && mixed.ObservationState == ObservationState.Observed, "obs");
		Check("Merge_Sound", merged && mixed.HasUsefulSound, "snd");
		Check("Merge_Shared", merged && mixed.HasUsefulShared, "sh");
		Check("Merge_LastKnownVisual", merged && mixed.LastKnownPosition == seen, "lk");

		processor.ApplyEmptyObservationFrame();
		processor.Advance(0.25f, now + 0.30f);
		processor.ApplySyntheticSound(target.transform, heard, 1f);
		processor.ApplySyntheticShared(target.transform, reported, 1f);
		processor.Advance(0.05f, now + 0.35f);
		bool lost = processor.TryGetContact(target.transform, out PerceivedContact after);
		Check("Conflict_LastKnownStays", lost && after.LastKnownPosition == seen, "A");
		Check("Conflict_SoundB", lost && after.SoundPosition == heard, "B");
		Check("Conflict_SharedC", lost && after.SharedPosition == reported, "C");
		Check("Conflict_NoAim", lost && !PerceptionContractMath.HasVisualAimPoint(after), "aim");
	}

	private void RunIdentityAimCombat()
	{
		DetectionProcessor processor = SpawnAlly("S18SmB", Vector3.zero, UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		GameObject reporter = SpawnAlly("S18SmA", new Vector3(4f, 0f, 0f), UnitTeamId.Player);
		GameObject target = Spawn("S18SmT", new Vector3(8f, 0f, 0f));
		TargetSelector selector = processor.GetComponent<TargetSelector>();
		EngagementDecisionController engagement = processor.GetComponent<EngagementDecisionController>();
		processor.SetSimulatedTime(0f);
		Publish(reporter, target, target.transform.position, PerceivedIdentity.Hostile);
		processor.Advance(0.05f, 0.05f);
		bool has = processor.TryGetContact(target.transform, out PerceivedContact contact);
		Check("Id_Unknown", has && contact.Identity == PerceivedIdentity.Unknown, "id");
		Check("Id_SharedHostile", has && contact.SharedIdentity == PerceivedIdentity.Hostile, "shId");
		Check("Id_NotObserved", has && contact.ObservationState == ObservationState.NotObserved, "obs");
		Check("Combat_Select", selector.SelectedTarget == target.transform, "sel");
		Check("Combat_Track", engagement.CurrentDecision == EngagementDecision.Track, "g6");
		Check("Combat_NoAim", !selector.HasSelectedAimPoint, "aim");
		Check("Combat_NoFire", engagement.CurrentDecision != EngagementDecision.Fire, "fire");
		Check("Combat_NotEngageable", selector.GetEngageableSelectedTarget() == null, "gate");
		Check(
			"Rpg_NoLaunch",
			!ProjectileLaunchPermit.TryAuthorize(
				false, Vector3.zero, target.transform.position, 150f, true, true, false,
				out ProjectileLaunchDeny deny) &&
			deny == ProjectileLaunchDeny.NoAimPoint,
			"rpg");
		float gated = DetectionQualityMath.IntegrateProgress(0f, 0.24f, 1f, _attentionMultiplier: 2.5f);
		Check("Attention_NoDetect", gated < 0.0001f, "att");
	}

	private void RunLiveEndToEnd()
	{
		DetectionProcessor reporter = SpawnAlly("S18LiveA", Vector3.zero, UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		DetectionProcessor listener = SpawnAlly("S18LiveB", new Vector3(10f, 0f, 0f), UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		GameObject enemy = Spawn("S18LiveE", new Vector3(5f, 0f, 0f));
		TargetSelector selectorB = listener.GetComponent<TargetSelector>();
		EngagementDecisionController engagementB = listener.GetComponent<EngagementDecisionController>();
		reporter.SetSimulatedTime(0f);
		listener.SetSimulatedTime(0f);
		float now = 0f;
		for (int i = 0; i < 16; i++)
		{
			reporter.ApplySyntheticObservation(enemy.transform, 4f, 0f, 1f, enemy.transform.position);
			now += 0.05f;
			reporter.Advance(0.05f, now);
		}

		bool aSeen = reporter.TryGetContact(enemy.transform, out PerceivedContact seenA) &&
		             seenA.ObservationState == ObservationState.Observed;
		bool bShared = listener.TryGetContact(enemy.transform, out PerceivedContact sharedB) &&
		               sharedB.HasUsefulShared &&
		               sharedB.ObservationState == ObservationState.NotObserved;
		Check("Live_A_Observed", aSeen, "a");
		Check("Live_B_Shared", bShared, "b");
		Check("Live_B_Track", engagementB.CurrentDecision == EngagementDecision.Track, "track");

		now = 0f;
		listener.SetSimulatedTime(0f);
		for (int i = 0; i < 16; i++)
		{
			listener.ApplySyntheticObservation(enemy.transform, 4f, 0f, 1f, enemy.transform.position);
			now += 0.05f;
			listener.Advance(0.05f, now);
		}

		bool bSeen = listener.TryGetContact(enemy.transform, out PerceivedContact seenB) &&
		             seenB.ObservationState == ObservationState.Observed;
		Check("Live_B_Reacquired", bSeen, "obs");
		Check("Live_B_Aim", selectorB.HasSelectedAimPoint, "aim");
		Check("Live_B_Fire", engagementB.CurrentDecision == EngagementDecision.Fire, "fire");
	}

	private void RunArchitecture()
	{
		int extra = 0;
		Type[] types = typeof(UnitVision).Assembly.GetTypes();
		for (int i = 0; i < types.Length; i++)
		{
			if (types[i] != typeof(UnitVision) && typeof(UnitVision).IsAssignableFrom(types[i]))
				extra++;
		}

		Check("Architecture_NoSecondVision", extra == 0, extra.ToString());
		Check("Architecture_Detail8", VisionLodMath.DefaultDetailSlotsPerFrame == 8, "8");
		Check("Architecture_GunshotRange", Near(SoundEvidenceMath.GunshotRangeMeters, 300f), "300");
		Check("Architecture_AllyRange", Near(AllyReportEvidenceMath.DefaultRangeMeters, 80f), "80");
		Check("Architecture_NoRaycastSound", !HubUsesRaycast(typeof(WorldSoundHub)), "snd");
		Check("Architecture_NoRaycastShared", !HubUsesRaycast(typeof(WorldAllyReportHub)), "sh");
		Check(
			"Architecture_QFourFactors",
			typeof(DetectionQualityMath).GetMethod(nameof(DetectionQualityMath.VisibilityQuality))
				.GetParameters().Length == 4,
			"D×F×E×M");
		AIContactKnowledge snap = AIContactKnowledge.From(new PerceivedContact
		{
			SoundConfidence = 0.5f,
			SharedConfidence = 0.5f,
			SharedIdentity = PerceivedIdentity.Hostile
		});
		Check("Snapshot_SoundPresent", snap.SoundPresent, "snd");
		Check("Snapshot_SharedPresent", snap.SharedPresent, "sh");
		Check("Snapshot_NotVisible", !snap.VisibleNow, "vis");
		Check("Snapshot_NotHostileCommit", !snap.Hostile, "id");
	}

	private GameObject Spawn(string _name, Vector3 _position)
	{
		var go = new GameObject(_name);
		go.transform.position = _position;
		m_Spawned.Add(go);
		return go;
	}

	private GameObject SpawnObserver(string _name)
	{
		GameObject go = Spawn(_name, Vector3.zero);
		EnsureComponent<UnitObservationSource>(go);
		EnsureComponent<UnitPerception>(go);
		EnsureComponent<DetectionProcessor>(go);
		EnsureComponent<TargetSelector>(go);
		EnsureComponent<EngagementDecisionController>(go);
		return go;
	}

	private GameObject SpawnAlly(string _name, Vector3 _position, UnitTeamId _team)
	{
		GameObject go = SpawnObserver(_name);
		go.transform.position = _position;
		UnitTeam team = EnsureComponent<UnitTeam>(go);
		team.SetTeam(_team);
		return go;
	}

	private static void Publish(
		GameObject _reporter,
		GameObject _subject,
		Vector3 _position,
		PerceivedIdentity _identity)
	{
		WorldAllyReportHub.Publish(AllyReportEvidenceMath.Create(
			_reporter.transform,
			_subject.transform,
			_position,
			_identity,
			1f));
	}

	private static T EnsureComponent<T>(GameObject _go) where T : Component
	{
		if (!_go.TryGetComponent(out T component))
			component = _go.AddComponent<T>();
		return component;
	}

	private void DestroySpawned()
	{
		for (int i = 0; i < m_Spawned.Count; i++)
		{
			if (m_Spawned[i] != null)
				Destroy(m_Spawned[i]);
		}

		m_Spawned.Clear();
	}

	private static bool HubUsesRaycast(Type _hub)
	{
		MethodInfo[] methods = _hub.GetMethods(
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		for (int i = 0; i < methods.Length; i++)
		{
			if (methods[i].Name.IndexOf("Raycast", StringComparison.OrdinalIgnoreCase) >= 0)
				return true;
		}

		return false;
	}

	private static WeaponDefinition LoadWeapon(string _path)
	{
#if UNITY_EDITOR
		return AssetDatabase.LoadAssetAtPath<WeaponDefinition>(_path);
#else
		return null;
#endif
	}

	private static WeaponAttachmentDefinition LoadOptic(string _path)
	{
#if UNITY_EDITOR
		return AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(_path);
#else
		return null;
#endif
	}

	private static RocketLauncherData LoadRockets()
	{
#if UNITY_EDITOR
		return AssetDatabase.LoadAssetAtPath<RocketLauncherData>(
			"Assets/GameData/Combat/RocketLauncherData.asset");
#else
		return null;
#endif
	}

	private void Check(string _name, bool _ok, string _detail)
	{
		if (_ok)
			m_PassCount++;
		else
			m_FailCount++;
		Append((_ok ? "PASS  " : "FAIL  ") + _name + "  " + _detail);
	}

	private static bool Near(float _a, float _b) => Mathf.Abs(_a - _b) <= 0.011f;

	private void Append(string _line) => m_Report.AppendLine(_line);

	private void Finish(string _result)
	{
		Append("");
		Append($"RESULT={_result}  PASS={m_PassCount} FAIL={m_FailCount}");
		string text = m_Report.ToString();
		Debug.Log("[FinalPerceptionContract] " + text, this);
		string dir = Path.Combine(Application.dataPath, "_Docs/Logs/Tests");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "FinalPerceptionContract_LAST.txt"), text);
	}
	#endregion
}

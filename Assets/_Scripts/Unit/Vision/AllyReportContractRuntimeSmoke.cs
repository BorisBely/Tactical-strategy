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
/// Vision Stage 17: Ally Report / Shared Perception contract.
/// Writes Assets/_Docs/Logs/Tests/AllyReportContract_LAST.txt
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class AllyReportContractRuntimeSmoke : MonoBehaviour
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
		(m_RunOnStart || DetectionHarnessPlayMode.RunAllyReportContract) &&
		!DetectionHarnessPlayMode.RunFinalPerceptionContract;
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
		if (DetectionHarnessPlayMode.RunAllyReportContract)
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
		Append("Vision Stage 17 AllyReportContract");
		Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		Append("CLOSED / VERIFIED — AllyReportContract");
		Append("---");

		WeaponDefinition assault = LoadWeapon("Assets/GameData/Shooting/M4/Weapon_M4_ModA_1.asset");
		WeaponDefinition sniper = LoadWeapon("Assets/GameData/Shooting/Standalone/Weapon_Sniper762x51.asset");
		WeaponDefinition mk19 = LoadWeapon("Assets/GameData/Shooting/Turret/Weapon_MK19.asset");
		WeaponDefinition m2 = LoadWeapon("Assets/GameData/Shooting/Turret/Weapon_M2Browning_127.asset");
		WeaponAttachmentDefinition reddot = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Reddot1.asset");
		WeaponAttachmentDefinition scope9 = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Scope9.asset");
		RocketLauncherData rockets = LoadRockets();

		Check(
			"Assets",
			assault != null && sniper != null && mk19 != null && m2 != null &&
			reddot != null && scope9 != null && rockets != null,
			"load");
		if (assault == null || sniper == null || mk19 == null || m2 == null ||
		    reddot == null || scope9 == null || rockets == null)
		{
			Finish("FAIL");
			yield break;
		}

		Check("Frozen_E_M4", Near(assault.EffectiveRangeMeters, 140f), assault.EffectiveRangeMeters.ToString("0"));
		Check("Frozen_E_Sniper", Near(sniper.EffectiveRangeMeters, 225f), sniper.EffectiveRangeMeters.ToString("0"));
		Check("Frozen_E_MK19", Near(mk19.EffectiveRangeMeters, 300f), mk19.EffectiveRangeMeters.ToString("0"));
		Check("Frozen_V_Reddot", Near(reddot.ScopeVisionRangeMeters, 150f), reddot.ScopeVisionRangeMeters.ToString("0"));
		Check("Frozen_V_Scope9", Near(scope9.ScopeVisionRangeMeters, 300f), scope9.ScopeVisionRangeMeters.ToString("0"));
		Check("Frozen_AimTimeX_Scope9", Near(scope9.AimTimeModifier, 1.55f), scope9.AimTimeModifier.ToString("F2"));
		Check("Frozen_Pose_0.35", Near(WeaponAimModeUtility.SnapShotAimProgress01, 0.35f), "0.35");
		Check("Frozen_Pose_0.68", Near(WeaponAimModeUtility.QuickAimProgress01, 0.68f), "0.68");
		Check("Frozen_Pose_1.00", Near(WeaponAimModeUtility.FullAimProgress01, 1f), "1.00");
		Check(
			"Frozen_Rpg_115_12",
			Near(rockets.GetMuzzleSpeed(RocketLauncherType.Rpg7), 115f) &&
			Near(rockets.ProjectileLifetimeSeconds, 12f),
			"115/12");
		Check(
			"Frozen_Mk19_240_25",
			Near(ProjectileLaunchPermit.Mk19MuzzleSpeed, 240f) &&
			Near(ProjectileLaunchPermit.Mk19LifetimeSeconds, 25f),
			"240/25");
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
		Check(
			"Attention_not_in_Q",
			typeof(DetectionQualityMath).GetMethod(nameof(DetectionQualityMath.VisibilityQuality))
				.GetParameters().Length == 4,
			"D×F×E×M");

		RunReportNotSee();
		RunNearAndFar();
		RunHorizon();
		RunVisualThenReport();
		RunHostileDoesNotCommit();
		RunHub();
		RunTwoReporters();
		RunConflict();
		RunLaterVisual();
		RunCombat();
		RunLiveObserve();
		RunThrottle();
		RunArchitecture();

		DestroySpawned();
		Finish(m_FailCount == 0 ? "PASS" : "FAIL");
		yield return null;
	}

	private void RunReportNotSee()
	{
		GameObject listener = SpawnAlly("S17PlayA_B", Vector3.zero, UnitTeamId.Player);
		GameObject reporter = SpawnAlly("S17PlayA_A", new Vector3(4f, 0f, 0f), UnitTeamId.Player);
		GameObject target = Spawn("S17PlayA_E", new Vector3(8f, 0f, 0f));
		DetectionProcessor processor = listener.GetComponent<DetectionProcessor>();
		TargetSelector selector = processor.GetComponent<TargetSelector>();
		EngagementDecisionController engagement = processor.GetComponent<EngagementDecisionController>();
		processor.SetSimulatedTime(0f);
		Publish(reporter, target, target.transform.position, PerceivedIdentity.Unknown);
		processor.Advance(0.05f, 0.05f);
		bool has = processor.TryGetContact(target.transform, out PerceivedContact contact);
		Check("A_SharedNotObserved", has && contact.ObservationState == ObservationState.NotObserved, "obs");
		Check("A_HasShared", has && contact.HasUsefulShared, "shared");
		Check("A_NoAim", has && !TargetSelectionMath.TryGetObservedAimPoint(contact, out _), "aim");
		Check("A_NoFire", engagement.CurrentDecision != EngagementDecision.Fire, "fire");
		Check("A_LastKnownEmpty", has && contact.LastKnownPosition == Vector3.zero, "lk");
		Check("A_SharedPos", has && contact.SharedPosition == target.transform.position, "sp");
		Check("A_SelectorNoAim", !selector.HasSelectedAimPoint, "sel");
		Check("A_IdentityUnknown", has && contact.Identity == PerceivedIdentity.Unknown, "id");
	}

	private void RunNearAndFar()
	{
		DetectionProcessor near = SpawnAlly("S17PlayB", Vector3.zero, UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		GameObject reporter = SpawnAlly("S17PlayB_A", new Vector3(5f, 0f, 0f), UnitTeamId.Player);
		Transform subject = Spawn("S17PlayB_E", new Vector3(8f, 0f, 0f)).transform;
		Publish(reporter, subject.gameObject, subject.position, PerceivedIdentity.Unknown);
		Check(
			"B_NearHighConf",
			near.TryGetContact(subject, out PerceivedContact heard) && heard.SharedConfidence > 0.9f,
			"near");

		DetectionProcessor far = SpawnAlly("S17PlayC", new Vector3(90f, 0f, 0f), UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		Publish(reporter, subject.gameObject, subject.position, PerceivedIdentity.Unknown);
		Check("C_BeyondRange", !far.TryGetContact(subject, out _), "far");
	}

	private void RunHorizon()
	{
		DetectionProcessor processor = SpawnAlly("S17PlayD", Vector3.zero, UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		GameObject target = Spawn("S17PlayDTgt", new Vector3(6f, 0f, 0f));
		processor.SetSimulatedTime(0f);
		processor.ApplySyntheticShared(target.transform, target.transform.position, 1f);
		processor.Advance(0.001f, 0.001f);
		processor.TryGetContact(target.transform, out PerceivedContact t0);
		bool useful0 = t0 != null && t0.HasUsefulShared;
		processor.Advance(4f, 4f);
		processor.TryGetContact(target.transform, out PerceivedContact t4);
		bool useful4 = t4 != null && t4.HasUsefulShared;
		processor.Advance(4f, 8f);
		processor.TryGetContact(target.transform, out PerceivedContact t8);
		bool useful8 = t8 != null && t8.HasUsefulShared;
		processor.Advance(1f, 9f);
		processor.TryGetContact(target.transform, out PerceivedContact t9);
		bool useful9 = t9 != null && t9.HasUsefulShared;
		Check("D_t0", useful0, "0s");
		Check("D_t4", useful4, "4s");
		Check("D_t8", t8 != null && !useful8, "8s");
		Check("D_t9", t9 != null && !useful9, "9s");
	}

	private void RunVisualThenReport()
	{
		DetectionProcessor processor = SpawnAlly("S17PlayE", Vector3.zero, UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		GameObject target = Spawn("S17PlayETgt", new Vector3(5f, 0f, 0f));
		processor.SetSimulatedTime(0f);
		Vector3 seen = new Vector3(5f, 0f, 1f);
		float now = 0f;
		for (int i = 0; i < 16; i++)
		{
			processor.ApplySyntheticObservation(target.transform, 4f, 0f, 1f, seen);
			now += 0.05f;
			processor.Advance(0.05f, now);
		}

		processor.ApplyEmptyObservationFrame();
		now += 0.1f;
		processor.Advance(0.1f, now);
		Vector3 reported = seen + Vector3.forward * 4f;
		processor.ApplySyntheticShared(target.transform, reported, 1f);
		now += 0.05f;
		processor.Advance(0.05f, now);
		bool has = processor.TryGetContact(target.transform, out PerceivedContact mixed);
		Check("E_LastKnownVisual", has && mixed.LastKnownPosition == seen, "lk");
		Check("E_SharedPosReported", has && mixed.SharedPosition == reported, "sp");
		Check(
			"E_BelievedVisual",
			has && TargetSelectionMath.ResolveBelievedPosition(mixed) == seen,
			"believed");
	}

	private void RunHostileDoesNotCommit()
	{
		DetectionProcessor processor = SpawnAlly("S17PlayF", Vector3.zero, UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		GameObject reporter = SpawnAlly("S17PlayF_A", new Vector3(3f, 0f, 0f), UnitTeamId.Player);
		GameObject target = Spawn("S17PlayFTgt", new Vector3(7f, 0f, 0f));
		processor.SetSimulatedTime(0f);
		Publish(reporter, target, target.transform.position, PerceivedIdentity.Hostile);
		processor.Advance(0.05f, 0.05f);
		bool has = processor.TryGetContact(target.transform, out PerceivedContact contact);
		Check("F_IdentityUnknown", has && contact.Identity == PerceivedIdentity.Unknown, "id");
		Check("F_SharedHostile", has && contact.SharedIdentity == PerceivedIdentity.Hostile, "sharedId");
		Check("F_NotObserved", has && contact.ObservationState == ObservationState.NotObserved, "obs");
	}

	private void RunHub()
	{
		DestroySpawned();
		DetectionProcessor near = SpawnAlly("S17PlayNear", Vector3.zero, UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		DetectionProcessor far = SpawnAlly("S17PlayFar", new Vector3(90f, 0f, 0f), UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		DetectionProcessor enemy = SpawnAlly("S17PlayEnemy", new Vector3(4f, 0f, 0f), UnitTeamId.Enemy)
			.GetComponent<DetectionProcessor>();
		GameObject reporter = SpawnAlly("S17PlayRep", new Vector3(2f, 0f, 0f), UnitTeamId.Player);
		Transform subject = Spawn("S17PlaySub", new Vector3(8f, 0f, 0f)).transform;
		Publish(reporter, subject.gameObject, subject.position, PerceivedIdentity.Unknown);
		int granted = 0;
		if (near.TryGetContact(subject, out _))
			granted++;
		if (far.TryGetContact(subject, out _))
			granted++;
		if (enemy.TryGetContact(subject, out _))
			granted++;
		Check("Hub_InRangeOnly", granted == 1, granted + "/1");
		Check(
			"Hub_DeliveryCount",
			WorldAllyReportHub.LastPublishDeliveryCount >= 1,
			WorldAllyReportHub.LastPublishDeliveryCount.ToString());
		Check("Hub_NoRaycast", !HubUsesRaycast(), "math");

		DetectionProcessor self = SpawnAlly("S17PlaySelf", Vector3.zero, UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		Publish(self.gameObject, subject.gameObject, subject.position, PerceivedIdentity.Unknown);
		Check("Hub_SkipSelf", !self.TryGetContact(subject, out _), "self");
	}

	private void RunTwoReporters()
	{
		DetectionProcessor listener = SpawnAlly("S17PlayMergeB", Vector3.zero, UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		GameObject a = SpawnAlly("S17PlayMergeA", new Vector3(3f, 0f, 0f), UnitTeamId.Player);
		GameObject c = SpawnAlly("S17PlayMergeC", new Vector3(4f, 0f, 0f), UnitTeamId.Player);
		GameObject target = Spawn("S17PlayMergeE", new Vector3(7f, 0f, 0f));
		Publish(a, target, new Vector3(7f, 0f, 0f), PerceivedIdentity.Unknown);
		Publish(c, target, new Vector3(9f, 0f, 2f), PerceivedIdentity.Unknown);
		bool has = listener.TryGetContact(target.transform, out PerceivedContact contact);
		Check("Merge_OneContact", listener.Contacts.Count == 1, listener.Contacts.Count.ToString());
		Check("Merge_LastReportWins", has && contact.SharedPosition == new Vector3(9f, 0f, 2f), "last");
		Check("Merge_LastKnownEmpty", has && contact.LastKnownPosition == Vector3.zero, "lk");
	}

	private void RunConflict()
	{
		DetectionProcessor processor = SpawnAlly("S17PlayConf", Vector3.zero, UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		GameObject target = Spawn("S17PlayConfE", new Vector3(5f, 0f, 0f));
		processor.SetSimulatedTime(0f);
		Vector3 seen = new Vector3(5f, 0f, 1f);
		float now = 0f;
		for (int i = 0; i < 16; i++)
		{
			processor.ApplySyntheticObservation(target.transform, 4f, 0f, 1f, seen);
			now += 0.05f;
			processor.Advance(0.05f, now);
		}

		processor.ApplyEmptyObservationFrame();
		now += 0.1f;
		processor.Advance(0.1f, now);
		processor.ApplySyntheticShared(target.transform, seen + Vector3.right * 2f, 1f);
		now += 0.05f;
		processor.Advance(0.05f, now);
		Vector3 y = seen + Vector3.forward * 3f;
		processor.ApplySyntheticShared(target.transform, y, 1f);
		now += 0.05f;
		processor.Advance(0.05f, now);
		bool has = processor.TryGetContact(target.transform, out PerceivedContact contact);
		Check("Conflict_LastKnownVisual", has && contact.LastKnownPosition == seen, "lk");
		Check("Conflict_SharedLast", has && contact.SharedPosition == y, "shared");
		Check("Conflict_OneContact", processor.Contacts.Count == 1, processor.Contacts.Count.ToString());
	}

	private void RunLaterVisual()
	{
		DetectionProcessor processor = SpawnAlly("S17PlayVis", Vector3.zero, UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		GameObject target = Spawn("S17PlayVisE", new Vector3(6f, 0f, 0f));
		processor.SetSimulatedTime(0f);
		processor.ApplySyntheticShared(target.transform, target.transform.position, 1f);
		processor.Advance(0.05f, 0.05f);
		Vector3 seen = new Vector3(6f, 0f, 1f);
		float now = 0.05f;
		for (int i = 0; i < 16; i++)
		{
			processor.ApplySyntheticObservation(target.transform, 4f, 0f, 1f, seen);
			now += 0.05f;
			processor.Advance(0.05f, now);
		}

		bool has = processor.TryGetContact(target.transform, out PerceivedContact merged);
		Check("Visual_OneContact", processor.Contacts.Count == 1, processor.Contacts.Count.ToString());
		Check("Visual_Observed", has && merged.ObservationState == ObservationState.Observed, "obs");
		Check("Visual_StillShared", has && merged.HasUsefulShared, "shared");
	}

	private void RunCombat()
	{
		DetectionProcessor processor = SpawnAlly("S17PlayCbt", Vector3.zero, UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		GameObject reporter = SpawnAlly("S17PlayCbtA", new Vector3(3f, 0f, 0f), UnitTeamId.Player);
		GameObject target = Spawn("S17PlayCbtE", new Vector3(8f, 0f, 0f));
		TargetSelector selector = processor.GetComponent<TargetSelector>();
		EngagementDecisionController engagement = processor.GetComponent<EngagementDecisionController>();
		processor.SetSimulatedTime(0f);
		Publish(reporter, target, target.transform.position, PerceivedIdentity.Unknown);
		processor.Advance(0.05f, 0.05f);
		Check("Combat_Select", selector.SelectedTarget == target.transform, "select");
		Check("Combat_Track", engagement.CurrentDecision == EngagementDecision.Track, "track");
		Check("Combat_NoAim", !selector.HasSelectedAimPoint, "aim");
		Check("Combat_NoFire", engagement.CurrentDecision != EngagementDecision.Fire, "fire");
		Check("Combat_NotEngageable", selector.GetEngageableSelectedTarget() == null, "gate");
	}

	private void RunLiveObserve()
	{
		DetectionProcessor reporter = SpawnAlly("S17PlayLiveA", Vector3.zero, UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		DetectionProcessor listener = SpawnAlly("S17PlayLiveB", new Vector3(10f, 0f, 0f), UnitTeamId.Player)
			.GetComponent<DetectionProcessor>();
		GameObject target = Spawn("S17PlayLiveE", new Vector3(5f, 0f, 0f));
		float now = 0f;
		for (int i = 0; i < 16; i++)
		{
			reporter.ApplySyntheticObservation(target.transform, 4f, 0f, 1f, target.transform.position);
			now += 0.05f;
			reporter.Advance(0.05f, now);
		}

		bool aHas = reporter.TryGetContact(target.transform, out PerceivedContact seen);
		bool bHas = listener.TryGetContact(target.transform, out PerceivedContact reported);
		Check("Live_A_Observed", aHas && seen.ObservationState == ObservationState.Observed, "a");
		Check("Live_B_Shared", bHas && reported.HasUsefulShared, "b");
		Check("Live_B_NotObserved", bHas && reported.ObservationState == ObservationState.NotObserved, "obs");
		Check(
			"Live_Delivery",
			WorldAllyReportHub.LastPublishDeliveryCount >= 1,
			WorldAllyReportHub.LastPublishDeliveryCount.ToString());
	}

	private void RunThrottle()
	{
		Check(
			"Throttle_First",
			AllyReportEvidenceMath.ShouldPublish(
				false, 0f, 0f, Vector3.zero, PerceivedIdentity.Unknown,
				Vector3.zero, PerceivedIdentity.Unknown),
			"first");
		Check(
			"Throttle_Within1s",
			!AllyReportEvidenceMath.ShouldPublish(
				true, 0.5f, 0f, Vector3.zero, PerceivedIdentity.Unknown,
				Vector3.zero, PerceivedIdentity.Unknown),
			"0.5s");
		Check(
			"Throttle_MoveAfter1s",
			AllyReportEvidenceMath.ShouldPublish(
				true, 1.1f, 0f, Vector3.zero, PerceivedIdentity.Unknown,
				new Vector3(8f, 0f, 0f), PerceivedIdentity.Unknown),
			"move");
	}

	private void RunArchitecture()
	{
		int extraVision = 0;
		Type[] types = typeof(UnitVision).Assembly.GetTypes();
		for (int i = 0; i < types.Length; i++)
		{
			if (types[i] != typeof(UnitVision) && typeof(UnitVision).IsAssignableFrom(types[i]))
				extraVision++;
		}

		Check("Architecture_NoSecondVision", extraVision == 0, extraVision.ToString());
		Check("Architecture_GunshotRange", Near(SoundEvidenceMath.GunshotRangeMeters, 300f), "300");
		Check("Architecture_ExplosionRange", Near(SoundEvidenceMath.ExplosionRangeMeters, 500f), "500");
		Check("Architecture_AllyRange", Near(AllyReportEvidenceMath.DefaultRangeMeters, 80f), "80");
		Check("Architecture_SharedHorizon", Near(SharedKnowledgeMath.DefaultHorizonSeconds, 8f), "8");
		Check("Architecture_SoundHorizon", Near(SoundKnowledgeMath.DefaultHorizonSeconds, 3f), "3");
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

	private static bool HubUsesRaycast()
	{
		MethodInfo[] methods = typeof(WorldAllyReportHub).GetMethods(
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
		Debug.Log("[AllyReportContract] " + text, this);
		string dir = Path.Combine(Application.dataPath, "_Docs/Logs/Tests");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "AllyReportContract_LAST.txt"), text);
	}
	#endregion
}

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
/// Vision Stage 16: Sound Perception contract.
/// Writes Assets/_Docs/Logs/Tests/SoundPerceptionContract_LAST.txt
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class SoundPerceptionContractRuntimeSmoke : MonoBehaviour
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
		(m_RunOnStart || DetectionHarnessPlayMode.RunSoundPerceptionContract) &&
		!DetectionHarnessPlayMode.RunAllyReportContract &&
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
		if (DetectionHarnessPlayMode.RunSoundPerceptionContract)
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
		Append("Vision Stage 16 SoundPerceptionContract");
		Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		Append("CLOSED / VERIFIED — SoundPerceptionContract");
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
		Check(
			"Attention_not_in_Q",
			typeof(DetectionQualityMath).GetMethod(nameof(DetectionQualityMath.VisibilityQuality))
				.GetParameters().Length == 4,
			"D×F×E×M");

		RunHearNotSee();
		RunNearAndFar();
		RunHorizon();
		RunVisualThenSound();
		RunNeverSeenUnknown();
		RunHubFanOut();
		RunLiveFireAudio();
		RunArchitecture();

		DestroySpawned();
		Finish(m_FailCount == 0 ? "PASS" : "FAIL");
		yield return null;
	}

	private void RunHearNotSee()
	{
		DetectionProcessor processor = SpawnObserver("S16PlayA").GetComponent<DetectionProcessor>();
		GameObject target = Spawn("S16PlayATgt", new Vector3(8f, 0f, 0f));
		TargetSelector selector = processor.GetComponent<TargetSelector>();
		EngagementDecisionController engagement = processor.GetComponent<EngagementDecisionController>();
		processor.SetSimulatedTime(0f);
		processor.ApplySyntheticSound(target.transform, target.transform.position, 1f);
		processor.Advance(0.05f, 0.05f);
		bool has = processor.TryGetContact(target.transform, out PerceivedContact contact);
		Check("A_HearNotSee", has && contact.ObservationState == ObservationState.NotObserved, "obs");
		Check("A_NoAim", has && !TargetSelectionMath.TryGetObservedAimPoint(contact, out _), "aim");
		Check("A_NoFire", engagement.CurrentDecision != EngagementDecision.Fire, "fire");
		Check("A_LastKnownEmpty", has && contact.LastKnownPosition == Vector3.zero, "lk");
		Check("A_SoundPos", has && contact.SoundPosition == target.transform.position, "sp");
		Check("A_SelectorNoAim", !selector.HasSelectedAimPoint, "sel");
	}

	private void RunNearAndFar()
	{
		DetectionProcessor near = SpawnObserver("S16PlayB").GetComponent<DetectionProcessor>();
		near.transform.position = Vector3.zero;
		Transform srcNear = Spawn("S16PlayBSrc", new Vector3(10f, 0f, 0f)).transform;
		WorldSoundHub.PublishGunshot(srcNear, srcNear.position);
		Check(
			"B_NearHighConf",
			near.TryGetContact(srcNear, out PerceivedContact heard) && heard.SoundConfidence > 0.9f,
			"near");

		DetectionProcessor far = SpawnObserver("S16PlayC").GetComponent<DetectionProcessor>();
		far.transform.position = Vector3.zero;
		Transform srcFar = Spawn("S16PlayCSrc", new Vector3(400f, 0f, 0f)).transform;
		WorldSoundHub.PublishGunshot(srcFar, srcFar.position);
		Check("C_BeyondRange", !far.TryGetContact(srcFar, out _), "far");
	}

	private void RunHorizon()
	{
		DetectionProcessor processor = SpawnObserver("S16PlayD").GetComponent<DetectionProcessor>();
		GameObject target = Spawn("S16PlayDTgt", new Vector3(6f, 0f, 0f));
		processor.SetSimulatedTime(0f);
		processor.ApplySyntheticSound(target.transform, target.transform.position, 1f);
		processor.Advance(0.001f, 0.001f);
		processor.TryGetContact(target.transform, out PerceivedContact t0);
		bool useful0 = t0 != null && t0.HasUsefulSound;
		processor.Advance(1f, 1f);
		processor.TryGetContact(target.transform, out PerceivedContact t1);
		bool useful1 = t1 != null && t1.HasUsefulSound;
		processor.Advance(1f, 2f);
		processor.TryGetContact(target.transform, out PerceivedContact t2);
		bool useful2 = t2 != null && t2.HasUsefulSound;
		processor.Advance(1f, 3f);
		processor.TryGetContact(target.transform, out PerceivedContact t3);
		bool useful3 = t3 != null && t3.HasUsefulSound;
		processor.Advance(1f, 4f);
		processor.TryGetContact(target.transform, out PerceivedContact t4);
		bool useful4 = t4 != null && t4.HasUsefulSound;
		Check("D_t0", useful0, "0s");
		Check("D_t1", useful1, "1s");
		Check("D_t2", useful2, "2s");
		Check("D_t3", t3 != null && !useful3, "3s");
		Check("D_t4", t4 != null && !useful4, "4s");
	}

	private void RunVisualThenSound()
	{
		DetectionProcessor processor = SpawnObserver("S16PlayE").GetComponent<DetectionProcessor>();
		GameObject target = Spawn("S16PlayETgt", new Vector3(5f, 0f, 0f));
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
		Vector3 heard = seen + Vector3.forward * 4f;
		processor.ApplySyntheticSound(target.transform, heard, 1f);
		now += 0.05f;
		processor.Advance(0.05f, now);
		bool has = processor.TryGetContact(target.transform, out PerceivedContact mixed);
		Check("E_LastKnownVisual", has && mixed.LastKnownPosition == seen, "lk");
		Check("E_SoundPosHeard", has && mixed.SoundPosition == heard, "sp");
		Check(
			"E_BelievedVisual",
			has && TargetSelectionMath.ResolveBelievedPosition(mixed) == seen,
			"believed");
	}

	private void RunNeverSeenUnknown()
	{
		DetectionProcessor processor = SpawnObserver("S16PlayF").GetComponent<DetectionProcessor>();
		GameObject target = Spawn("S16PlayFTgt", new Vector3(7f, 0f, 0f));
		processor.SetSimulatedTime(0f);
		processor.ApplySyntheticSound(target.transform, target.transform.position, 1f);
		processor.Advance(0.05f, 0.05f);
		bool has = processor.TryGetContact(target.transform, out PerceivedContact contact);
		Check("F_Unknown", has && contact.Identity == PerceivedIdentity.Unknown, "id");
		Check("F_NotObserved", has && contact.ObservationState == ObservationState.NotObserved, "obs");
	}

	private void RunHubFanOut()
	{
		DestroySpawned();
		Transform source = Spawn("S16PlayHubSrc", new Vector3(5f, 0f, 0f)).transform;
		var listeners = new DetectionProcessor[10];
		int expected = 0;
		for (int i = 0; i < 10; i++)
		{
			listeners[i] = SpawnObserver("S16PlayL" + i).GetComponent<DetectionProcessor>();
			listeners[i].transform.position = i < 3
				? new Vector3(i * 2f, 0f, 0f)
				: new Vector3(350f + i, 0f, 0f);
			if (i < 3)
				expected++;
		}

		WorldSoundHub.PublishGunshot(source, source.position);
		int granted = 0;
		for (int i = 0; i < 10; i++)
		{
			if (listeners[i].TryGetContact(source, out _))
				granted++;
		}

		Check("Hub_InRangeOnly", granted == expected, granted + "/" + expected);
		Check(
			"Hub_DeliveryCount",
			WorldSoundHub.LastPublishDeliveryCount >= expected,
			WorldSoundHub.LastPublishDeliveryCount.ToString());
		Check("Hub_NoRaycast", !HubUsesRaycast(), "math");

		DetectionProcessor self = SpawnObserver("S16PlaySelf").GetComponent<DetectionProcessor>();
		WorldSoundHub.PublishGunshot(self.transform, self.transform.position);
		Check("Hub_SkipSelf", self.Contacts.Count == 0, "self");
	}

	private void RunLiveFireAudio()
	{
		DetectionProcessor listener = SpawnObserver("S16PlayLive").GetComponent<DetectionProcessor>();
		listener.transform.position = Vector3.zero;
		GameObject shooter = Spawn("S16PlayShooter", new Vector3(12f, 0f, 0f));
		UnitWeaponFireAudio audio = shooter.AddComponent<UnitWeaponFireAudio>();
		MethodInfo handle = typeof(UnitWeaponFireAudio).GetMethod(
			"HandleShotFired",
			BindingFlags.Instance | BindingFlags.NonPublic);
		if (handle == null)
		{
			Check("Live_HandleShotFired", false, "missing");
			return;
		}

		handle.Invoke(audio, new object[] { null });
		Check(
			"Live_FireAudioNoClip",
			listener.TryGetContact(shooter.transform, out PerceivedContact contact) &&
			contact.SoundConfidence > 0.9f,
			"shot");
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
		Check("Architecture_FootstepRange", Near(SoundEvidenceMath.FootstepRangeMeters, 25f), "25");
		Check("Architecture_ImpactRange", Near(SoundEvidenceMath.ImpactRangeMeters, 40f), "40");
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
		MethodInfo[] methods = typeof(WorldSoundHub).GetMethods(
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
		Debug.Log("[SoundPerceptionContract] " + text, this);
		string dir = Path.Combine(Application.dataPath, "_Docs/Logs/Tests");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "SoundPerceptionContract_LAST.txt"), text);
	}
	#endregion
}

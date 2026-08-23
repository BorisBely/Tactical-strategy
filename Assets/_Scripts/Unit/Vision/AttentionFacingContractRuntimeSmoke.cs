using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Vision Stage 15: Attention / Facing rate on DetectionProgress.
/// Writes Assets/_Docs/Logs/Tests/AttentionFacingContract_LAST.txt
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class AttentionFacingContractRuntimeSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(16384);
	private int m_PassCount;
	private int m_FailCount;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunAttentionFacingContract) &&
		!DetectionHarnessPlayMode.RunSoundPerceptionContract &&
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
		if (DetectionHarnessPlayMode.RunAttentionFacingContract)
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
		Append("Vision Stage 15 AttentionFacingContract");
		Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		Append("CLOSED / VERIFIED — AttentionFacingContract");
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
		Check("Frozen_M2_Optic_0", Near(m2.OpticVisionRangeMeters, 0f), "0");

		Check("Frozen_Acquire_0.25", Near(DetectionQualityMath.DefaultAcquireThreshold, 0.25f), "0.25");
		Check("Frozen_Lose_0.20", Near(DetectionQualityMath.DefaultLoseThreshold, 0.20f), "0.20");
		Check("Frozen_Exponent_3.8", Near(DetectionQualityMath.DefaultAcquisitionExponent, 3.8f), "3.8");
		Check("Frozen_AcquireTime_0.35", Near(DetectionQualityMath.DefaultAcquireTime, 0.35f), "0.35");
		Check(
			"Frozen_Q_DxFxExM",
			Near(DetectionQualityMath.VisibilityQuality(0.8f, 0.5f, 1f, 1f), 0.4f),
			"0.40");

		float gated = DetectionQualityMath.IntegrateProgress(0f, 0.24f, 1f, _attentionMultiplier: 3f);
		Check("Gate_Q24_mul3_hold", gated < 0.0001f, gated.ToString("F4", CultureInfo.InvariantCulture));
		float slow = DetectionQualityMath.IntegrateProgress(0f, 0.26f, 0.1f, _attentionMultiplier: 1f);
		float fast = DetectionQualityMath.IntegrateProgress(0f, 0.26f, 0.1f, _attentionMultiplier: 2.5f);
		Check("Gate_Q26_mul_faster", fast > slow && slow > 0f, "rate");

		float m0 = AttentionMath.EvaluateMultiplier(0f);
		float m30 = AttentionMath.EvaluateMultiplier(30f);
		float m45 = AttentionMath.EvaluateMultiplier(45f);
		float m60 = AttentionMath.EvaluateMultiplier(60f);
		Check("Curve_0_gt_30", m0 > m30, m0.ToString("F2", CultureInfo.InvariantCulture));
		Check("Curve_30_gt_1", m30 > 1f, m30.ToString("F2", CultureInfo.InvariantCulture));
		Check("Curve_45_eq_1", Near(m45, 1f), m45.ToString("F2", CultureInfo.InvariantCulture));
		Check("Curve_60_eq_1", Near(m60, 1f), m60.ToString("F2", CultureInfo.InvariantCulture));
		Check("Curve_0_le_Max", m0 <= AttentionMath.MultiplierMax + 0.001f, m0.ToString("F2", CultureInfo.InvariantCulture));
		Check("Band_0_High", AttentionMath.EvaluateBand(0f) == AttentionBand.High, "High");
		Check("Band_60_Low", AttentionMath.EvaluateBand(60f) == AttentionBand.Low, "Low");
		Check(
			"Attention_not_in_Q",
			typeof(DetectionQualityMath).GetMethod(nameof(DetectionQualityMath.VisibilityQuality))
				.GetParameters().Length == 4,
			"D×F×E×M");

		Check("Peek_0_0.10_miss", !GrowsToDetected(1f, 0f, 0.10f), "not instant");
		Check("Peek_0_0.20_hit", GrowsToDetected(1f, 0f, 0.20f), "center");
		Check("Peek_60_0.10_miss", !GrowsToDetected(1f, 60f, 0.10f), "edge");
		Check("Peek_60_0.30_miss", !GrowsToDetected(1f, 60f, 0.30f), "baseline");
		Check("Peek_60_0.50_hit", GrowsToDetected(1f, 60f, 0.50f), "1.0s band");
		Check("Peek_A_0_vs_B_45", GrowsToDetected(1f, 0f, 0.20f) && !GrowsToDetected(1f, 45f, 0.20f), "same peek");
		Check("Peek_0_faster_30", TimeToDetected(1f, 0f) < TimeToDetected(1f, 30f), "facing");
		Check("Peek_5_faster_30", TimeToDetected(1f, 5f) < TimeToDetected(1f, 30f), "5°");
		Check("Peek_10_faster_45", TimeToDetected(1f, 10f) < TimeToDetected(1f, 45f), "10°");
		Check("Peek_20_gt_1mul", AttentionMath.EvaluateMultiplier(20f) > 1f, "20°");

		VisionScanScheduler.ResetForTests();
		VisionScanScheduler.DetailSlotsPerFrame = 8;
		const int observers = 50;
		int[] starve = new int[observers];
		int[] consecutive = new int[observers];
		int maxConsecutive = 0;
		bool slotOk = true;
		for (int frame = 0; frame < 16; frame++)
		{
			VisionScanScheduler.BeginFrameForTests(frame);
			for (int i = 0; i < observers; i++)
			{
				VisionScanScheduler.RequestDetailSlot(
					i,
					VisionDetailPriorityMath.Score(1f, false, false, false, starve[i]));
			}

			VisionScanScheduler.FlushPendingDetailIfNeeded();
			int granted = 0;
			for (int i = 0; i < observers; i++)
			{
				if (VisionScanScheduler.WasGranted(i))
				{
					granted++;
					starve[i] = 0;
					consecutive[i] = 0;
				}
				else
				{
					starve[i]++;
					consecutive[i]++;
					if (consecutive[i] > maxConsecutive)
						maxConsecutive = consecutive[i];
				}
			}

			if (granted != 8)
				slotOk = false;
		}

		Check("Fairness_8_slots", slotOk, "8/frame");
		Check(
			"Fairness_no_starve_8",
			maxConsecutive <= VisionDetailPriorityMath.FairnessMaxConsecutiveSkip,
			"maxSkip=" + maxConsecutive);
		VisionScanScheduler.ResetForTests();
		Check("Slots_still_8", VisionLodMath.DefaultDetailSlotsPerFrame == 8, "8");

		int extraVision = 0;
		bool watchingWindow = false;
		Type[] types = typeof(UnitVision).Assembly.GetTypes();
		for (int i = 0; i < types.Length; i++)
		{
			string name = types[i].Name;
			if (name.IndexOf("WatchingWindow", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    name.IndexOf("GuardWindow", StringComparison.OrdinalIgnoreCase) >= 0)
				watchingWindow = true;
			if (types[i] != typeof(UnitVision) && typeof(UnitVision).IsAssignableFrom(types[i]))
				extraVision++;
		}

		Check("Architecture_NoWatchingWindow", !watchingWindow, "none");
		Check("Architecture_NoSecondVision", extraVision == 0, extraVision.ToString());

		FieldInfo cap = typeof(TargetSelector).GetField(
			"m_MaxEngageRange",
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		Check("Architecture_No18Field", cap == null, cap == null ? "field gone" : cap.Name);

		Finish(m_FailCount == 0 ? "PASS" : "FAIL");
		yield return null;
	}

	private static bool GrowsToDetected(float _quality, float _angle, float _duration)
	{
		return TimeToDetected(_quality, _angle) <= _duration + 1e-4f;
	}

	private static float TimeToDetected(float _quality, float _angle)
	{
		float mul = AttentionMath.EvaluateMultiplier(_angle);
		float progress = 0f;
		const float dt = 0.01f;
		float t = 0f;
		while (t < 4f)
		{
			progress = DetectionQualityMath.IntegrateProgress(
				progress, _quality, dt, _attentionMultiplier: mul);
			t += dt;
			if (progress >= 1f)
				return t;
		}

		return 99f;
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
		Debug.Log("[AttentionFacingContract] " + text, this);
		string dir = Path.Combine(Application.dataPath, "_Docs/Logs/Tests");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "AttentionFacingContract_LAST.txt"), text);
	}
	#endregion
}

using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Vision Stage 10: Accuracy / AimTime envelope. Does not retune Q, E, ScopeVisionRange, recoil.
/// Writes Assets/_Docs/Logs/Tests/AccuracyAimCurveContract_LAST.txt
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class AccuracyAimCurveContractRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private static readonly float[] s_Distances =
	{
		0f, 25f, 50f, 75f, 100f, 125f, 150f, 175f, 200f, 225f, 250f, 275f, 300f
	};
	#endregion

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
		m_RunOnStart || DetectionHarnessPlayMode.RunAccuracyAimCurveContract;
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
		if (DetectionHarnessPlayMode.RunAccuracyAimCurveContract)
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
		Append("Vision Stage 10 AccuracyAimCurveContract");
		Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		Append("---");

		WeaponDefinition m4 = LoadWeapon("Assets/GameData/Shooting/M4/Weapon_M4_ModA_1.asset");
		WeaponDefinition cqb = LoadWeapon("Assets/GameData/Shooting/AK/Weapon_AK74U.asset");
		WeaponDefinition dmr = LoadWeapon("Assets/GameData/Shooting/M4/Weapon_MK12.asset");
		WeaponDefinition sniper = LoadWeapon("Assets/GameData/Shooting/Standalone/Weapon_Sniper762x51.asset");
		WeaponAttachmentDefinition reddot = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Reddot1.asset");
		WeaponAttachmentDefinition scope4 = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Scope4.asset");
		WeaponAttachmentDefinition scope9 = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Scope9.asset");

		Check("Assets", m4 != null && cqb != null && dmr != null && sniper != null &&
		                reddot != null && scope4 != null && scope9 != null, "load");
		if (m4 == null || cqb == null || dmr == null || sniper == null ||
		    reddot == null || scope4 == null || scope9 == null)
		{
			Finish("FAIL");
			yield break;
		}

		Check("Frozen_E_M4", Near(m4.EffectiveRangeMeters, 140f), m4.EffectiveRangeMeters.ToString("0"));
		Check("Frozen_E_Sniper", Near(sniper.EffectiveRangeMeters, 225f), sniper.EffectiveRangeMeters.ToString("0"));
		Check("Frozen_V_Reddot", Near(reddot.ScopeVisionRangeMeters, 150f), reddot.ScopeVisionRangeMeters.ToString("0"));
		Check("Frozen_V_Scope9", Near(scope9.ScopeVisionRangeMeters, 300f), scope9.ScopeVisionRangeMeters.ToString("0"));
		Check("Frozen_AimTimeX_Scope9", Near(scope9.AimTimeModifier, 1.55f), scope9.AimTimeModifier.ToString("F2"));
		Check("HeavySlower", scope9.AimTimeModifier > reddot.AimTimeModifier, "aim×");

		Check("CQB_SweetClose", FindSweet(cqb, null, 150f) <= 25f, "cqb");
		Check("Scope4_SweetIn260", FindSweet(null, scope4, 260f) >= 240f, "scope4");
		Check("Scope9_SweetIn300", FindSweet(null, scope9, 300f) >= 260f, "scope9");
		Check("Sniper_SweetFar", FindSweet(sniper, null, 300f) >= 220f, "sniper");

		DumpMatrix("CQB+1x", cqb, reddot, 150f);
		DumpMatrix("Assault iron", m4, null, 150f);
		DumpMatrix("DMR+Scope4", dmr, scope4, 260f);
		DumpMatrix("Sniper+Scope9", sniper, scope9, 300f);

		Finish(m_FailCount == 0 ? "PASS" : "FAIL");
		yield return null;
	}

	private void DumpMatrix(
		string _label,
		WeaponDefinition _weapon,
		WeaponAttachmentDefinition _optic,
		float _vision)
	{
		Append("");
		Append("MATRIX " + _label + " V=" + _vision.ToString("0"));
		WeaponAttachmentDefinition[] attachments = _optic != null
			? new[] { _optic }
			: System.Array.Empty<WeaponAttachmentDefinition>();
		for (int i = 0; i < s_Distances.Length; i++)
		{
			float d = s_Distances[i];
			string tag = d > _vision + 0.01f ? "OUTSIDE_VISION" : "in";
			float disp = WeaponDistanceAimEvaluator.GetDistanceDispersionMultiplier(_weapon, attachments, d);
			float aim = WeaponDistanceAimEvaluator.GetDistanceAimTimeMultiplier(_weapon, attachments, d);
			Append($"  {d,5:0}  disp={disp:F3}  aim={aim:F3}  {tag}");
		}
	}

	private static float FindSweet(WeaponDefinition _weapon, WeaponAttachmentDefinition _optic, float _max)
	{
		WeaponAttachmentDefinition[] attachments = _optic != null
			? new[] { _optic }
			: System.Array.Empty<WeaponAttachmentDefinition>();
		float best = float.MaxValue;
		float at = 0f;
		for (float d = 0f; d <= _max + 0.01f; d += 5f)
		{
			float value = WeaponDistanceAimEvaluator.GetDistanceDispersionMultiplier(_weapon, attachments, d);
			if (value + 0.0005f < best)
			{
				best = value;
				at = d;
			}
		}

		return at;
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
		Debug.Log("[AccuracyAimCurveContract] " + text, this);
		string dir = Path.Combine(Application.dataPath, "_Docs/Logs/Tests");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "AccuracyAimCurveContract_LAST.txt"), text);
	}
	#endregion
}

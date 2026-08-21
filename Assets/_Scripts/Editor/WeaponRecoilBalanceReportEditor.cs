#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Read-only report of stage-1 recoil balance metrics. Does not modify assets or Excel.
/// </summary>
public static class WeaponRecoilBalanceReportEditor
{
	private const string c_MenuPath = "Polygone/Combat/Report Recoil Balance Metrics (Stage 1)";

	[MenuItem(c_MenuPath)]
	private static void ReportMetrics()
	{
		string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { "Assets/GameData/Shooting" });
		if (guids.Length == 0)
		{
			Debug.LogWarning("[RecoilBalance] No WeaponDefinition assets found.");
			return;
		}

		var weapons = new System.Collections.Generic.List<WeaponDefinition>(guids.Length);
		foreach (string guid in guids)
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
			if (weapon != null)
				weapons.Add(weapon);
		}

		weapons.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

		Debug.Log(
			"[RecoilBalance] Stage 1 baseline — no mods/skills/posture/ammo. " +
			"B = |Offset| after N shots (inter-shot recovery ×0.7). " +
			"C = after 5 shots + pause at full recovery. " +
			$"Reference: {WeaponRecoilBalanceContract.ReferenceWeaponAssetName}.");

		foreach (WeaponDefinition weapon in weapons)
		{
			WeaponFireMode fireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(weapon);
			WeaponRecoilBalanceContract.Metrics metrics =
				WeaponRecoilBalanceContract.EvaluateBaseline(weapon, fireMode);

			Debug.Log(
				$"[RecoilBalance] {weapon.name} ({fireMode})\n" +
				$"  A kick: V={metrics.VerticalRecoilDegrees:F3}° H={metrics.HorizontalRecoilDegrees:F3}° recovery={metrics.RecoilRecoveryPerSecond:F2}°/s\n" +
				$"  B |Offset|: 3={metrics.OffsetMagnitudeAfter3Shots:F3}° 5={metrics.OffsetMagnitudeAfter5Shots:F3}° " +
				$"8={metrics.OffsetMagnitudeAfter8Shots:F3}° 10={metrics.OffsetMagnitudeAfter10Shots:F3}°\n" +
				$"  B @100m: 5 shots → {metrics.DisplacementMetersAfter5ShotsAt100m:F2} m\n" +
				$"  C after 5 + pause: 0.2s={metrics.OffsetMagnitudeAfterPause020:F3}° " +
				$"0.4s={metrics.OffsetMagnitudeAfterPause040:F3}° 0.8s={metrics.OffsetMagnitudeAfterPause080:F3}°\n" +
				$"  C @100m: 5+0.4s → {metrics.DisplacementMetersAfterPause040At100m:F2} m");
		}
	}
}
#endif

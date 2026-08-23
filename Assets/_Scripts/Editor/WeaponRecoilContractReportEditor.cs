#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor math log for A10 recoil finish. Does not enter Play Mode.
/// Writes Assets/_Docs/Logs/Tests/RecoilContract_LAST.txt
/// Замороженная регрессия MATH. Новый play-прогон — Tools/Tests/▶ Current Recoil Check, не новая строка здесь.
/// </summary>
public static class WeaponRecoilContractReportEditor
{
	private const string c_MenuPath = "Tools/Tests/Run Recoil Contract (Editor)";
	private const string c_WeaponSearchFolder = "Assets/GameData/Shooting";
	private const string c_LogFolder = "Assets/_Docs/Logs/Tests";
	private const string c_LogFileName = "RecoilContract_LAST.txt";

	[MenuItem(c_MenuPath, false, 27)]
	private static void RunFromMenu()
	{
		RunContractReport();
	}

	/// <summary>Phase B14 final MATH regression. Menu: Current Recoil Check or frozen Run Recoil Contract.</summary>
	public static void RunContractReport()
	{
		string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { c_WeaponSearchFolder });
		if (guids.Length == 0)
			guids = AssetDatabase.FindAssets("t:WeaponDefinition");

		var weapons = new List<WeaponDefinition>(guids.Length);
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			if (path.Replace('\\', '/').IndexOf("/Test/", System.StringComparison.OrdinalIgnoreCase) >= 0)
				continue;

			WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
			if (weapon != null)
				weapons.Add(weapon);
		}

		weapons.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
		string report = WeaponRecoilContract.EvaluateWeapons(weapons.ToArray(), out bool passed);

		Directory.CreateDirectory(c_LogFolder);
		string logPath = Path.Combine(c_LogFolder, c_LogFileName);
		File.WriteAllText(logPath, report, Encoding.UTF8);
		AssetDatabase.Refresh();

		if (passed)
			Debug.Log("[RecoilContract] PASS\n" + report);
		else
			Debug.LogWarning("[RecoilContract] FAIL\n" + report);
	}
}
#endif
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase G editor entry. Writes Assets/_Docs/Logs/Tests/WeaponBalance_LAST.txt
/// </summary>
public static class WeaponBalanceReportEditor
{
	#region Constants
	private const string c_WeaponFolder = "Assets/GameData/Shooting";
	private const string c_MenuSmoke = "Tools/Tests/Archive/Weapon/Run Weapon Balance Matrix (Smoke)";
	private const string c_MenuReference = "Tools/Tests/Archive/Weapon/Run Weapon Balance Matrix (Reference)";
	private const string c_MenuAttachments = "Tools/Tests/Archive/Weapon/Run Weapon Balance Matrix (Attachments)";
	#endregion

	#region Menu
	[MenuItem(c_MenuSmoke, false, 180)]
	public static void RunSmokeFromMenu()
	{
		RunPreset(WeaponBalanceRunConfig.CreateSmokePreset(), "Smoke");
	}

	[MenuItem(c_MenuReference, false, 181)]
	public static void RunReferenceFromMenu()
	{
		RunPreset(WeaponBalanceRunConfig.CreateReferencePreset(), "Reference");
	}

	[MenuItem(c_MenuAttachments, false, 182)]
	public static void RunAttachmentsFromMenu()
	{
		RunPreset(WeaponBalanceRunConfig.CreateAttachmentsPreset(), "Attachments");
	}

	public static void RunSmokeForCurrentCheck()
	{
		RunSmokeFromMenu();
	}
	#endregion

	#region Private Methods
	private static void RunPreset(WeaponBalanceRunConfig _config, string _presetName)
	{
		List<WeaponDefinition> weapons = ResolveWeapons(_config, _presetName);
		List<WeaponAttachmentDefinition> attachments = LoadAttachmentCatalog();
		WeaponBalanceReport report = WeaponBalanceRunner.Run(
			_config,
			weapons,
			attachments,
			_presetName);
		WriteReport(report);
		Debug.Log("[WeaponBalance " + _presetName + "]\n" + report.BuildTextReport());
	}

	private static List<WeaponDefinition> ResolveWeapons(WeaponBalanceRunConfig _config, string _presetName)
	{
		string[] names = null;
		if (_presetName == "Smoke")
			names = WeaponBalanceRunConfig.SmokeWeaponAssetNames;
		else if (_presetName == "Reference" || _presetName == "Attachments")
			names = WeaponBalanceRunConfig.ReferenceWeaponAssetNames;

		if (names != null)
		{
			var list = new List<WeaponDefinition>(names.Length);
			for (int i = 0; i < names.Length; i++)
			{
				WeaponDefinition weapon = LoadWeapon(names[i]);
				if (weapon != null)
					list.Add(weapon);
			}

			return list;
		}

		if (_config.Weapons != null && _config.Weapons.Length > 0)
		{
			var list = new List<WeaponDefinition>(_config.Weapons.Length);
			for (int i = 0; i < _config.Weapons.Length; i++)
			{
				if (_config.Weapons[i] != null)
					list.Add(_config.Weapons[i]);
			}

			return list;
		}

		return LoadAllCombatWeapons();
	}

	private static List<WeaponDefinition> LoadAllCombatWeapons()
	{
		string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { c_WeaponFolder });
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
		return weapons;
	}

	private static List<WeaponAttachmentDefinition> LoadAttachmentCatalog()
	{
		string[] guids = AssetDatabase.FindAssets("t:WeaponAttachmentDefinition", new[] { c_WeaponFolder });
		var list = new List<WeaponAttachmentDefinition>(guids.Length);
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			WeaponAttachmentDefinition attachment =
				AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(path);
			if (attachment != null)
				list.Add(attachment);
		}

		return list;
	}

	private static WeaponDefinition LoadWeapon(string _assetName)
	{
		string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition " + _assetName, new[] { c_WeaponFolder });
		if (guids.Length == 0)
			guids = AssetDatabase.FindAssets("t:WeaponDefinition " + _assetName);
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
			if (weapon != null && weapon.name == _assetName)
				return weapon;
		}

		return null;
	}

	private static void WriteReport(WeaponBalanceReport _report)
	{
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, WeaponBalanceReport.LogFileName);
		File.WriteAllText(path, _report.BuildTextReport(), Encoding.UTF8);
		AssetDatabase.Refresh();
	}
	#endregion
}
#endif

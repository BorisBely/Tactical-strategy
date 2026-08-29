#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Phase H editor entry. Writes WeaponBalanceH_LAST.txt</summary>
public static class WeaponBalanceHReportEditor
{
	#region Constants
	private const string c_WeaponFolder = "Assets/GameData/Shooting";
	private const string c_MenuPath = "Tools/Tests/Archive/Weapon/Run Weapon Balance Report (H)";
	#endregion

	#region Menu
	[MenuItem(c_MenuPath, false, 184)]
	public static void RunFromMenu()
	{
		List<WeaponDefinition> weapons = LoadReferenceWeapons();
		List<WeaponAttachmentDefinition> attachments = LoadAttachmentCatalog();

		WeaponBalanceHInput input = WeaponBalanceHRunner.RunFrozenGInput(weapons, attachments);
		WriteSnapshot(input.ReferenceReport, WeaponBalanceHReport.ReferenceSnapshotFileName);
		WriteSnapshot(input.AttachmentsReport, WeaponBalanceHReport.AttachmentsSnapshotFileName);

		WeaponBalanceHReport report = WeaponBalanceHReportBuilder.Build(in input, weapons);
		WriteReport(report);
		Debug.Log("[WeaponBalance H]\n" + report.BuildTextReport());
	}
	#endregion

	#region Private Methods
	private static List<WeaponDefinition> LoadReferenceWeapons()
	{
		var list = new List<WeaponDefinition>(WeaponBalanceRunConfig.ReferenceWeaponAssetNames.Length);
		for (int i = 0; i < WeaponBalanceRunConfig.ReferenceWeaponAssetNames.Length; i++)
		{
			WeaponDefinition weapon = LoadWeapon(WeaponBalanceRunConfig.ReferenceWeaponAssetNames[i]);
			if (weapon != null)
				list.Add(weapon);
		}

		return list;
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
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
			if (weapon != null && weapon.name == _assetName)
				return weapon;
		}

		return null;
	}

	private static void WriteReport(WeaponBalanceHReport _report)
	{
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, WeaponBalanceHReport.LogFileName);
		File.WriteAllText(path, _report.BuildTextReport(), Encoding.UTF8);
		AssetDatabase.Refresh();
	}

	private static void WriteSnapshot(WeaponBalanceReport _report, string _fileName)
	{
		if (_report == null)
			return;
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, _fileName);
		File.WriteAllText(path, _report.BuildTextReport(), Encoding.UTF8);
	}
	#endregion
}
#endif

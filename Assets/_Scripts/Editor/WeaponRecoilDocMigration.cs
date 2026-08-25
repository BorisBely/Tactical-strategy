#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Точечная сверка/запись чисел отдачи канона Стрельба_и_отдача.md §7.
/// Global Migrate Recoil Offset Fields заблокирован.
/// </summary>
public static class WeaponRecoilDocMigration
{
	#region Nested Types
	private readonly struct WeaponPatch
	{
		public readonly string AssetName;
		public readonly float Vertical;
		public readonly float Horizontal;
		public readonly float RecoveryPerSecond;
		public readonly float SemiAutoMultiplier;
		public readonly float AutoMultiplier;

		public WeaponPatch(
			string _assetName,
			float _vertical,
			float _horizontal,
			float _recoveryPerSecond,
			float _semiAutoMultiplier,
			float _autoMultiplier)
		{
			AssetName = _assetName;
			Vertical = _vertical;
			Horizontal = _horizontal;
			RecoveryPerSecond = _recoveryPerSecond;
			SemiAutoMultiplier = _semiAutoMultiplier;
			AutoMultiplier = _autoMultiplier;
		}
	}

	private readonly struct AttachmentPatch
	{
		public readonly string AssetName;
		public readonly float RecoilModifier;
		public readonly float SemiAutoRecoilModifier;
		public readonly float AutomaticRecoilModifier;

		public AttachmentPatch(
			string _assetName,
			float _recoilModifier,
			float _semiAutoRecoilModifier,
			float _automaticRecoilModifier)
		{
			AssetName = _assetName;
			RecoilModifier = _recoilModifier;
			SemiAutoRecoilModifier = _semiAutoRecoilModifier;
			AutomaticRecoilModifier = _automaticRecoilModifier;
		}
	}
	#endregion

	#region Constants
	private const string c_MenuRoot = "Polygone/Shooting/Recoil Doc Migration/";
	private const float c_Epsilon = 0.0001f;

	private static readonly WeaponPatch[] s_Stage1WeaponPatches =
	{
		new WeaponPatch("Weapon_M249", 0.07f, 0.11f, 0.50f, 0.86f, 1.10f),
		new WeaponPatch("Weapon_MK18", 0.09f, 0.035f, 0.75f, 0.88f, 1.30f),
		new WeaponPatch("Weapon_AK74U", 0.115f, 0.055f, 0.65f, 0.92f, 1.45f),
		new WeaponPatch("Weapon_AK74UMOD1", 0.115f, 0.055f, 0.65f, 0.90f, 1.40f)
	};

	private static readonly AttachmentPatch[] s_Stage2AttachmentPatches =
	{
		new AttachmentPatch("Attachment_M4_ForeGrip1", 0.92f, 1f, 1f),
		new AttachmentPatch("Attachment_M4_Stock1", 0.97f, 1f, 1f),
		new AttachmentPatch("Attachment_M4_MuzzleBrakeM4", 1f, 0.92f, 1f),
		new AttachmentPatch("Attachment_AK_MuzzleBrakeAK", 1f, 0.92f, 1f),
		new AttachmentPatch("Attachment_AK_MuzzleBrakeAK_545", 1f, 0.92f, 1f),
		new AttachmentPatch("Attachment_SVD_MuzzleBrake", 1f, 0.92f, 1f)
	};
	#endregion

	#region Menu
	[MenuItem(c_MenuRoot + "Report Diff", false, 20)]
	private static void ReportDiffFromMenu()
	{
		string report = BuildStage1Report();
		report += "\n" + BuildStage2Report();
		Debug.Log(report);
		EditorUtility.DisplayDialog(
			"Recoil Doc Migration",
			"Diff в Console. YAML не записывался.\nStage 1 = 4 ствола. Stage 2 = модули (не применять до MATH).",
			"OK");
	}

	[MenuItem(c_MenuRoot + "Apply Stage 1 Weapons", false, 21)]
	private static void ApplyStage1FromMenu()
	{
		if (!EditorUtility.DisplayDialog(
			    "Apply Stage 1",
			    "Записать V/H/Rec/Semi×/Auto× на M249, MK18, AK-74U, AK-74U MOD1 по §23.2?\nОстальные стволы и модули не трогаются.",
			    "Записать 4 ассета",
			    "Отмена"))
			return;

		int changed = ApplyWeaponPatches(s_Stage1WeaponPatches, _write: true);
		AssetDatabase.SaveAssets();
		Debug.Log("[RecoilDocMigration] Stage 1 applied. weaponsChanged=" + changed);
		EditorUtility.DisplayDialog(
			"Recoil Doc Migration",
			"Stage 1: обновлено стволов — " + changed + ".\nДальше RecoilContract / Tuning Scene.",
			"OK");
	}

	[MenuItem(c_MenuRoot + "Apply Stage 2 Modules (wait for MATH)", false, 22)]
	private static void ApplyStage2FromMenu()
	{
		if (!EditorUtility.DisplayDialog(
			    "Apply Stage 2",
			    "§24 ещё теория. Писать FG1 0.92, Stock1 0.97, ДТК Semi 0.92 / Auto 1.00?\nSniper ДТК не входит.",
			    "Записать модули",
			    "Отмена"))
			return;

		int changed = ApplyAttachmentPatches(s_Stage2AttachmentPatches, _write: true);
		AssetDatabase.SaveAssets();
		Debug.Log("[RecoilDocMigration] Stage 2 applied. attachmentsChanged=" + changed);
		EditorUtility.DisplayDialog(
			"Recoil Doc Migration",
			"Stage 2: обновлено модулей — " + changed + ".",
			"OK");
	}
	#endregion

	#region Private Methods
	private static string BuildStage1Report()
	{
		var sb = new StringBuilder();
		sb.AppendLine("RecoilDocMigration STAGE 1  (Стрельба_и_отдача.md §7 live YAML)");
		sb.AppendLine("Live YAML vs target. Write=false.");
		int pending = 0;
		for (int i = 0; i < s_Stage1WeaponPatches.Length; i++)
		{
			WeaponPatch patch = s_Stage1WeaponPatches[i];
			WeaponDefinition weapon = FindWeapon(patch.AssetName);
			if (weapon == null)
			{
				sb.AppendLine("  MISSING  " + patch.AssetName);
				pending++;
				continue;
			}

			bool differs = WeaponDiffers(weapon, patch);
			if (differs)
				pending++;
			sb.Append("  ");
			sb.Append(differs ? "PATCH   " : "MATCH   ");
			sb.Append(patch.AssetName);
			sb.Append("  V ");
			sb.Append(FormatPair(weapon.VerticalRecoil, patch.Vertical));
			sb.Append("  H ");
			sb.Append(FormatPair(weapon.HorizontalRecoil, patch.Horizontal));
			sb.Append("  Rec ");
			sb.Append(FormatPair(weapon.RecoilRecoveryPerSecond, patch.RecoveryPerSecond));
			sb.Append("  Semi ");
			sb.Append(FormatPair(weapon.SemiAutoRecoilMultiplier, patch.SemiAutoMultiplier));
			sb.Append("  Auto ");
			sb.AppendLine(FormatPair(weapon.AutoRecoilMultiplier, patch.AutoMultiplier));
		}

		sb.AppendLine("pending=" + pending + " / " + s_Stage1WeaponPatches.Length);
		return sb.ToString();
	}

	private static string BuildStage2Report()
	{
		var sb = new StringBuilder();
		sb.AppendLine("RecoilDocMigration STAGE 2  (модули — не в каноне live, не писать без отдельного решения)");
		int pending = 0;
		for (int i = 0; i < s_Stage2AttachmentPatches.Length; i++)
		{
			AttachmentPatch patch = s_Stage2AttachmentPatches[i];
			WeaponAttachmentDefinition attachment = FindAttachment(patch.AssetName);
			if (attachment == null)
			{
				sb.AppendLine("  MISSING  " + patch.AssetName);
				pending++;
				continue;
			}

			bool differs = AttachmentDiffers(attachment, patch);
			if (differs)
				pending++;
			sb.Append("  ");
			sb.Append(differs ? "PATCH   " : "MATCH   ");
			sb.Append(patch.AssetName);
			sb.Append("  Recoil ");
			sb.Append(FormatPair(attachment.RecoilModifier, patch.RecoilModifier));
			sb.Append("  Semi ");
			sb.Append(FormatPair(attachment.SemiAutoRecoilModifier, patch.SemiAutoRecoilModifier));
			sb.Append("  Auto ");
			sb.AppendLine(FormatPair(attachment.AutomaticRecoilModifier, patch.AutomaticRecoilModifier));
		}

		sb.AppendLine("pending=" + pending + " / " + s_Stage2AttachmentPatches.Length);
		return sb.ToString();
	}

	private static int ApplyWeaponPatches(WeaponPatch[] _patches, bool _write)
	{
		int changed = 0;
		for (int i = 0; i < _patches.Length; i++)
		{
			WeaponPatch patch = _patches[i];
			WeaponDefinition weapon = FindWeapon(patch.AssetName);
			if (weapon == null)
			{
				Debug.LogError("[RecoilDocMigration] missing " + patch.AssetName);
				continue;
			}

			if (!WeaponDiffers(weapon, patch))
				continue;

			if (!_write)
			{
				changed++;
				continue;
			}

			SerializedObject so = new SerializedObject(weapon);
			SetFloat(so, "m_VerticalRecoil", patch.Vertical);
			SetFloat(so, "m_HorizontalRecoil", patch.Horizontal);
			SetFloat(so, "m_RecoilRecoveryPerSecond", patch.RecoveryPerSecond);
			SetFloat(so, "m_SemiAutoRecoilMultiplier", patch.SemiAutoMultiplier);
			SetFloat(so, "m_AutoRecoilMultiplier", patch.AutoMultiplier);
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(weapon);
			changed++;
		}

		return changed;
	}

	private static int ApplyAttachmentPatches(AttachmentPatch[] _patches, bool _write)
	{
		int changed = 0;
		for (int i = 0; i < _patches.Length; i++)
		{
			AttachmentPatch patch = _patches[i];
			WeaponAttachmentDefinition attachment = FindAttachment(patch.AssetName);
			if (attachment == null)
			{
				Debug.LogError("[RecoilDocMigration] missing " + patch.AssetName);
				continue;
			}

			if (!AttachmentDiffers(attachment, patch))
				continue;

			if (!_write)
			{
				changed++;
				continue;
			}

			SerializedObject so = new SerializedObject(attachment);
			SetFloat(so, "m_RecoilModifier", patch.RecoilModifier);
			SetFloat(so, "m_SemiAutoRecoilModifier", patch.SemiAutoRecoilModifier);
			SetFloat(so, "m_AutomaticRecoilModifier", patch.AutomaticRecoilModifier);
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(attachment);
			changed++;
		}

		return changed;
	}

	private static bool WeaponDiffers(WeaponDefinition _weapon, WeaponPatch _patch)
	{
		return !Approximately(_weapon.VerticalRecoil, _patch.Vertical)
		       || !Approximately(_weapon.HorizontalRecoil, _patch.Horizontal)
		       || !Approximately(_weapon.RecoilRecoveryPerSecond, _patch.RecoveryPerSecond)
		       || !Approximately(_weapon.SemiAutoRecoilMultiplier, _patch.SemiAutoMultiplier)
		       || !Approximately(_weapon.AutoRecoilMultiplier, _patch.AutoMultiplier);
	}

	private static bool AttachmentDiffers(WeaponAttachmentDefinition _attachment, AttachmentPatch _patch)
	{
		return !Approximately(_attachment.RecoilModifier, _patch.RecoilModifier)
		       || !Approximately(_attachment.SemiAutoRecoilModifier, _patch.SemiAutoRecoilModifier)
		       || !Approximately(_attachment.AutomaticRecoilModifier, _patch.AutomaticRecoilModifier);
	}

	private static WeaponDefinition FindWeapon(string _assetName)
	{
		string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { "Assets/GameData/Shooting" });
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
			if (weapon != null && weapon.name == _assetName)
				return weapon;
		}

		return null;
	}

	private static WeaponAttachmentDefinition FindAttachment(string _assetName)
	{
		string[] guids = AssetDatabase.FindAssets(
			"t:WeaponAttachmentDefinition",
			new[] { "Assets/GameData/Shooting" });
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			WeaponAttachmentDefinition attachment = AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(path);
			if (attachment != null && attachment.name == _assetName)
				return attachment;
		}

		return null;
	}

	private static void SetFloat(SerializedObject _so, string _propertyName, float _value)
	{
		SerializedProperty property = _so.FindProperty(_propertyName);
		if (property != null)
			property.floatValue = _value;
	}

	private static bool Approximately(float _a, float _b)
	{
		return Mathf.Abs(_a - _b) <= c_Epsilon;
	}

	private static string FormatPair(float _live, float _target)
	{
		if (Approximately(_live, _target))
			return _live.ToString("0.###");
		return _live.ToString("0.###") + "->" + _target.ToString("0.###");
	}
	#endregion
}
#endif

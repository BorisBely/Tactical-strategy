#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Запекает дистанционные кривые оптики из <see cref="OpticDistanceCurveLibrary"/> в WeaponAttachmentDefinition assets.
/// </summary>
public static class OpticDistanceProfileBaker
{
	private const string c_ShootingRoot = "Assets/GameData/Shooting";

	[MenuItem("Polygone/Combat Balance/Bake Optic Distance Profiles")]
	public static void BakeAllOpticProfiles()
	{
		string[] guids = AssetDatabase.FindAssets("t:WeaponAttachmentDefinition", new[] { c_ShootingRoot });
		int baked = 0;

		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			var attachment = AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(path);
			if (attachment == null || attachment.AttachmentType != WeaponAttachmentType.Optic)
				continue;

			BakeAttachment(attachment);
			EditorUtility.SetDirty(attachment);
			baked++;
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log($"Optic distance profiles baked for {baked} optic attachments.");
	}

	public static void BakeAttachment(WeaponAttachmentDefinition _attachment)
	{
		if (_attachment == null)
			return;

		OpticDistanceCurveLibrary.ApplyToProfile(_attachment.DistanceAimProfile, _attachment);
	}
}
#endif

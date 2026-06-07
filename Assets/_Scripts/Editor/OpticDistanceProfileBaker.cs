#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Запекает дистанционные кривые оптики из <see cref="OpticDistanceCurveLibrary"/> в WeaponAttachmentDefinition assets.
/// </summary>
public static class OpticDistanceProfileBaker
{
	private const string c_ShootingRoot = "Assets/GameData/Shooting";
	private const string c_DispersionCurveProperty = "m_DispersionMultiplierByDistance";
	private const string c_AimTimeCurveProperty = "m_AimTimeMultiplierByDistance";

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

			if (BakeAttachment(attachment))
				baked++;
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log($"Optic distance profiles baked for {baked} optic attachments.");
	}

	public static bool BakeAttachment(WeaponAttachmentDefinition _attachment)
	{
		if (_attachment == null)
			return false;

		var curves = OpticDistanceCurveLibrary.GetCurvesForAttachment(_attachment);
		AnimationCurve dispersionCurve = OpticDistanceCurveLibrary.BuildCurve(curves.DispersionKeyframes);
		AnimationCurve aimTimeCurve = OpticDistanceCurveLibrary.BuildCurve(curves.AimTimeKeyframes);

		SerializedObject serializedAttachment = new SerializedObject(_attachment);
		SerializedProperty profileProperty = serializedAttachment.FindProperty("m_DistanceAimProfile");
		if (profileProperty == null)
			return false;

		SerializedProperty dispersionProperty = profileProperty.FindPropertyRelative(c_DispersionCurveProperty);
		SerializedProperty aimTimeProperty = profileProperty.FindPropertyRelative(c_AimTimeCurveProperty);
		if (dispersionProperty == null || aimTimeProperty == null)
			return false;

		dispersionProperty.animationCurveValue = dispersionCurve;
		aimTimeProperty.animationCurveValue = aimTimeCurve;
		serializedAttachment.ApplyModifiedPropertiesWithoutUndo();

		_attachment.DistanceAimProfile.SetCurves(dispersionCurve, aimTimeCurve);
		EditorUtility.SetDirty(_attachment);
		return true;
	}
}
#endif

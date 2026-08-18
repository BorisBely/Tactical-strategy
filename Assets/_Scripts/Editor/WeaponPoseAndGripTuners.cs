#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pose tuner: edits WeaponPoseDefinition only (never LeftHandGrip / runtime ItemDefinition writes).
/// Select an ItemDefinition with WeaponPoseDefinition assigned, then use the menu to push Scene weapon TRS into the active pose slot.
/// </summary>
public static class WeaponPoseTuner
{
	[MenuItem("Polygone/Weapons/Architecture/Pose Tuner/Capture Selected Weapon → Standing Ready")]
	public static void CaptureStandingReady() => Capture(WeaponStance.Standing, WeaponPoseState.PointAim);

	[MenuItem("Polygone/Weapons/Architecture/Pose Tuner/Capture Selected Weapon → Standing NotReady")]
	public static void CaptureStandingNotReady() => Capture(WeaponStance.Standing, WeaponPoseState.LowReady);

	[MenuItem("Polygone/Weapons/Architecture/Pose Tuner/Capture Selected Weapon → Crouch Ready")]
	public static void CaptureCrouchReady() => Capture(WeaponStance.Crouching, WeaponPoseState.PointAim);

	[MenuItem("Polygone/Weapons/Architecture/Pose Tuner/Capture Selected Weapon → Crouch NotReady")]
	public static void CaptureCrouchNotReady() => Capture(WeaponStance.Crouching, WeaponPoseState.LowReady);

	[MenuItem("Polygone/Weapons/Architecture/Pose Tuner/Capture Selected Weapon → Vehicle Ready")]
	public static void CaptureVehicleReady() => Capture(WeaponStance.Vehicle, WeaponPoseState.PointAim);

	[MenuItem("Polygone/Weapons/Architecture/Pose Tuner/Capture Selected Weapon → Vehicle NotReady")]
	public static void CaptureVehicleNotReady() => Capture(WeaponStance.Vehicle, WeaponPoseState.LowReady);

	private static void Capture(WeaponStance _stance, WeaponPoseState _pose)
	{
		Transform t = Selection.activeTransform;
		if (t == null)
		{
			EditorUtility.DisplayDialog("Pose Tuner", "Select the weapon root Transform in Hierarchy.", "OK");
			return;
		}

		UnitEquipment eq = t.GetComponentInParent<UnitEquipment>();
		ItemDefinition def = eq != null ? eq.EquippedDefinition : null;
		if (def == null || def.WeaponPoseDefinition == null)
		{
			EditorUtility.DisplayDialog("Pose Tuner", "Equipped ItemDefinition needs WeaponPoseDefinition.", "OK");
			return;
		}

		Transform weapon = eq.MainWeaponRoot != null ? eq.MainWeaponRoot : t;
		def.WeaponPoseDefinition.SetOrAddPose(_stance, _pose, weapon.localPosition, weapon.localEulerAngles);
		EditorUtility.SetDirty(def.WeaponPoseDefinition);
		AssetDatabase.SaveAssets();
		Debug.Log($"[WeaponPoseTuner] Saved {_stance}/{_pose} for {def.name}: pos={weapon.localPosition} eu={weapon.localEulerAngles}");
	}
}

/// <summary>Grip tuner: edits prefab LeftHandGrip / RightHand stance targets Transform locals only.</summary>
public static class WeaponGripTuner
{
	[MenuItem("Polygone/Weapons/Architecture/Grip Tuner/Log Selected Grip Transform")]
	public static void LogSelected()
	{
		Transform t = Selection.activeTransform;
		if (t == null)
			return;
		Debug.Log($"[WeaponGripTuner] {t.name} localPos={t.localPosition} localEu={t.localEulerAngles} path={AnimationUtility.CalculateTransformPath(t, t.root)}");
	}

	[MenuItem("Polygone/Weapons/Architecture/Grip Tuner/Apply Selection Local → Prefab Override")]
	public static void ApplyToPrefab()
	{
		GameObject go = Selection.activeGameObject;
		if (go == null)
			return;
		PrefabUtility.ApplyObjectOverride(go, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go), InteractionMode.UserAction);
		Debug.Log($"[WeaponGripTuner] Applied overrides for {go.name}");
	}
}
#endif

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(UnitEquippedWeaponPoseRuntimeTuner))]
public sealed class UnitEquippedWeaponPoseRuntimeTunerEditor : Editor
{
	private static readonly HashSet<int> s_CollapsedGameObjects = new HashSet<int>();

	private SerializedProperty m_UnitEquipment;
	private SerializedProperty m_EquippedWeaponPose;
	private SerializedProperty m_EnableRuntimeTuning;
	private SerializedProperty m_ActiveTarget;
	private SerializedProperty m_ActivePosture;
	private SerializedProperty m_NotReadyLocalPosition;
	private SerializedProperty m_NotReadyLocalEulerAngles;
	private SerializedProperty m_ReadyLocalPosition;
	private SerializedProperty m_ReadyLocalEulerAngles;
	private SerializedProperty m_NotReadyIkLocalPosition;
	private SerializedProperty m_NotReadyIkLocalEulerAngles;
	private SerializedProperty m_ReadyIkLocalPosition;
	private SerializedProperty m_ReadyIkLocalEulerAngles;
	private SerializedProperty m_LeftNotReadyIkLocalPosition;
	private SerializedProperty m_LeftNotReadyIkLocalEulerAngles;
	private SerializedProperty m_LeftReadyIkLocalPosition;
	private SerializedProperty m_LeftReadyIkLocalEulerAngles;

	private void OnEnable()
	{
		m_UnitEquipment = serializedObject.FindProperty("m_UnitEquipment");
		m_EquippedWeaponPose = serializedObject.FindProperty("m_EquippedWeaponPose");
		m_EnableRuntimeTuning = serializedObject.FindProperty("m_EnableRuntimeTuning");
		m_ActiveTarget = serializedObject.FindProperty("m_ActiveTarget");
		m_ActivePosture = serializedObject.FindProperty("m_ActivePosture");
		m_NotReadyLocalPosition = serializedObject.FindProperty("m_NotReadyLocalPosition");
		m_NotReadyLocalEulerAngles = serializedObject.FindProperty("m_NotReadyLocalEulerAngles");
		m_ReadyLocalPosition = serializedObject.FindProperty("m_ReadyLocalPosition");
		m_ReadyLocalEulerAngles = serializedObject.FindProperty("m_ReadyLocalEulerAngles");
		m_NotReadyIkLocalPosition = serializedObject.FindProperty("m_NotReadyIkLocalPosition");
		m_NotReadyIkLocalEulerAngles = serializedObject.FindProperty("m_NotReadyIkLocalEulerAngles");
		m_ReadyIkLocalPosition = serializedObject.FindProperty("m_ReadyIkLocalPosition");
		m_ReadyIkLocalEulerAngles = serializedObject.FindProperty("m_ReadyIkLocalEulerAngles");
		m_LeftNotReadyIkLocalPosition = serializedObject.FindProperty("m_LeftNotReadyIkLocalPosition");
		m_LeftNotReadyIkLocalEulerAngles = serializedObject.FindProperty("m_LeftNotReadyIkLocalEulerAngles");
		m_LeftReadyIkLocalPosition = serializedObject.FindProperty("m_LeftReadyIkLocalPosition");
		m_LeftReadyIkLocalEulerAngles = serializedObject.FindProperty("m_LeftReadyIkLocalEulerAngles");

		CollapseOtherComponents();
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();

		EditorGUILayout.PropertyField(m_UnitEquipment);
		EditorGUILayout.PropertyField(m_EquippedWeaponPose);

		EditorGUILayout.Space(8f);
		EditorGUILayout.LabelField("Hierarchy Scene Tuning", EditorStyles.boldLabel);

		bool wasEnabled = m_EnableRuntimeTuning.boolValue;
		EditorGUI.BeginChangeCheck();
		EditorGUILayout.PropertyField(m_EnableRuntimeTuning, new GUIContent("Enable Runtime Tuning"));
		bool enableChanged = EditorGUI.EndChangeCheck();

		SerializedProperty m_CrouchNotReadyLocalPosition = serializedObject.FindProperty("m_CrouchNotReadyLocalPosition");
		SerializedProperty m_CrouchNotReadyLocalEulerAngles = serializedObject.FindProperty("m_CrouchNotReadyLocalEulerAngles");
		SerializedProperty m_CrouchReadyLocalPosition = serializedObject.FindProperty("m_CrouchReadyLocalPosition");
		SerializedProperty m_CrouchReadyLocalEulerAngles = serializedObject.FindProperty("m_CrouchReadyLocalEulerAngles");
		SerializedProperty m_CrouchNotReadyIkLocalPosition = serializedObject.FindProperty("m_CrouchNotReadyIkLocalPosition");
		SerializedProperty m_CrouchNotReadyIkLocalEulerAngles = serializedObject.FindProperty("m_CrouchNotReadyIkLocalEulerAngles");
		SerializedProperty m_CrouchReadyIkLocalPosition = serializedObject.FindProperty("m_CrouchReadyIkLocalPosition");
		SerializedProperty m_CrouchReadyIkLocalEulerAngles = serializedObject.FindProperty("m_CrouchReadyIkLocalEulerAngles");
		SerializedProperty m_CrouchLeftNotReadyIkLocalPosition = serializedObject.FindProperty("m_CrouchLeftNotReadyIkLocalPosition");
		SerializedProperty m_CrouchLeftNotReadyIkLocalEulerAngles = serializedObject.FindProperty("m_CrouchLeftNotReadyIkLocalEulerAngles");
		SerializedProperty m_CrouchLeftReadyIkLocalPosition = serializedObject.FindProperty("m_CrouchLeftReadyIkLocalPosition");
		SerializedProperty m_CrouchLeftReadyIkLocalEulerAngles = serializedObject.FindProperty("m_CrouchLeftReadyIkLocalEulerAngles");

		using (new EditorGUI.DisabledScope(!Application.isPlaying))
		{
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(m_ActivePosture, new GUIContent("Active Posture"));
			EditorGUILayout.PropertyField(m_ActiveTarget, new GUIContent("Active Target"));
			bool postureOrTargetChanged = EditorGUI.EndChangeCheck();

			var tuner = (UnitEquippedWeaponPoseRuntimeTuner)target;
			DrawModeHint(tuner.ActiveTarget, tuner.ActivePosture);

			EditorGUILayout.Space(4f);
			EditorGUILayout.LabelField("Captured — active posture (live edit buffer)", EditorStyles.boldLabel);
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.Vector3Field("Weapon Not Ready Pos", tuner.NotReadyLocalPosition);
				EditorGUILayout.Vector3Field("Weapon Not Ready Rot", tuner.NotReadyLocalEulerAngles);
				EditorGUILayout.Vector3Field("Weapon Ready Pos", tuner.ReadyLocalPosition);
				EditorGUILayout.Vector3Field("Weapon Ready Rot", tuner.ReadyLocalEulerAngles);
				EditorGUILayout.Vector3Field("Right IK Not Ready Pos", tuner.NotReadyIkLocalPosition);
				EditorGUILayout.Vector3Field("Right IK Not Ready Rot", tuner.NotReadyIkLocalEulerAngles);
				EditorGUILayout.Vector3Field("Right IK Ready Pos", tuner.ReadyIkLocalPosition);
				EditorGUILayout.Vector3Field("Right IK Ready Rot", tuner.ReadyIkLocalEulerAngles);
				EditorGUILayout.Vector3Field("Left IK Not Ready Pos", tuner.LeftNotReadyIkLocalPosition);
				EditorGUILayout.Vector3Field("Left IK Not Ready Rot", tuner.LeftNotReadyIkLocalEulerAngles);
				EditorGUILayout.Vector3Field("Left IK Ready Pos", tuner.LeftReadyIkLocalPosition);
				EditorGUILayout.Vector3Field("Left IK Ready Rot", tuner.LeftReadyIkLocalEulerAngles);
			}

			EditorGUILayout.Space(4f);
			EditorGUILayout.LabelField("Captured — standing (saved separately)", EditorStyles.boldLabel);
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.Vector3Field("Not Ready Pos", tuner.StandingNotReadyLocalPosition);
				EditorGUILayout.Vector3Field("Not Ready Rot", tuner.StandingNotReadyLocalEulerAngles);
				EditorGUILayout.Vector3Field("Ready Pos", tuner.StandingReadyLocalPosition);
				EditorGUILayout.Vector3Field("Ready Rot", tuner.StandingReadyLocalEulerAngles);
			}

			EditorGUILayout.Space(4f);
			EditorGUILayout.LabelField("Captured — crouch (saved separately)", EditorStyles.boldLabel);
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.PropertyField(m_CrouchNotReadyLocalPosition);
				EditorGUILayout.PropertyField(m_CrouchNotReadyLocalEulerAngles);
				EditorGUILayout.PropertyField(m_CrouchReadyLocalPosition);
				EditorGUILayout.PropertyField(m_CrouchReadyLocalEulerAngles);
				EditorGUILayout.PropertyField(m_CrouchNotReadyIkLocalPosition);
				EditorGUILayout.PropertyField(m_CrouchNotReadyIkLocalEulerAngles);
				EditorGUILayout.PropertyField(m_CrouchReadyIkLocalPosition);
				EditorGUILayout.PropertyField(m_CrouchReadyIkLocalEulerAngles);
				EditorGUILayout.PropertyField(m_CrouchLeftNotReadyIkLocalPosition);
				EditorGUILayout.PropertyField(m_CrouchLeftNotReadyIkLocalEulerAngles);
				EditorGUILayout.PropertyField(m_CrouchLeftReadyIkLocalPosition);
				EditorGUILayout.PropertyField(m_CrouchLeftReadyIkLocalEulerAngles);
			}

			ItemDefinition equipped = tuner.ActiveTuningDefinition;

			EditorGUILayout.Space(8f);
			if (GUILayout.Button("Copy Base Weapon → Ready Pose"))
				tuner.CopyBaseWeaponPoseToReady();

			if (GUILayout.Button("Copy Standing Capture → Crouch Capture"))
				tuner.CopyStandingCaptureToCrouchCapture();

			if (GUILayout.Button("Copy Standing Capture → Vehicle Capture"))
				tuner.CopyStandingCaptureToVehicleCapture();

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Reload From Asset"))
					tuner.LoadFromEquippedDefinition();

				using (new EditorGUI.DisabledScope(equipped == null))
				{
					if (GUILayout.Button("Save Standing"))
						SaveStandingToAsset(tuner, equipped);

					if (GUILayout.Button("Save Crouch"))
						SaveCrouchToAsset(tuner, equipped);
				}
			}

			using (new EditorGUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(equipped == null))
				{
					if (GUILayout.Button("Save Vehicle"))
						SaveVehicleToAsset(tuner, equipped);
				}
			}

			using (new EditorGUI.DisabledScope(!tuner.IsLeftHandIkDrivenByForegrip))
			{
				EditorGUILayout.LabelField("Save ForeGrip Left IK:", EditorStyles.boldLabel);
				using (new EditorGUILayout.HorizontalScope())
				{
					if (GUILayout.Button("Stand"))
						SaveForeGripLeftHandIkToAsset(tuner, equipped, UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Standing);
					if (GUILayout.Button("Crouch"))
						SaveForeGripLeftHandIkToAsset(tuner, equipped, UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Crouch);
					if (GUILayout.Button("Vehicle"))
						SaveForeGripLeftHandIkToAsset(tuner, equipped, UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Vehicle);
				}
			}

			if (GUILayout.Button("Copy YAML"))
				EditorGUIUtility.systemCopyBuffer = tuner.BuildYamlSnippet();

			if (tuner.UsesRocketLauncherContext)
				EditorGUILayout.HelpBox(
					"Rocket launcher mode: press H to hold launcher. Aim animation (Stand_Aim_RPG / Stand_Aim_RL) " +
					"stays active for Hands Frozen / Not Ready / Ready tuning.",
					MessageType.Info);
			else if (equipped != null)
				EditorGUILayout.HelpBox($"Asset: {equipped.name}", MessageType.None);
			else
				EditorGUILayout.HelpBox("Equip a weapon or press H to hold a rocket launcher in Play Mode first.", MessageType.Warning);

			if (tuner.IsLeftHandIkDrivenByForegrip)
			{
				EditorGUILayout.HelpBox(
					"Foregrip installed: left IK saves to ForeGrip IK fields on the weapon asset.\n" +
					"Save Standing / Crouch / Vehicle + Save ForeGrip Left IK buttons each target their own posture fields.",
					MessageType.Info);
			}

			serializedObject.ApplyModifiedProperties();

			if (Application.isPlaying && enableChanged && m_EnableRuntimeTuning.boolValue && !wasEnabled)
				tuner.LoadFromEquippedDefinition();

			if (Application.isPlaying && postureOrTargetChanged && tuner.IsTuningActive)
				tuner.ApplyActiveTargetSwitch();
		}

		if (!Application.isPlaying)
			serializedObject.ApplyModifiedProperties();

		EditorGUILayout.Space(6f);

		if (GUILayout.Button("Collapse Other Components"))
			CollapseOtherComponents();

		EditorGUILayout.Space(3f);
		var helpTuner = (UnitEquippedWeaponPoseRuntimeTuner)target;
		string leftSaveNote = helpTuner.IsLeftHandIkDrivenByForegrip
			? "• left hand IK: Save ForeGrip Left IK Stand / Crouch / Vehicle\n"
			: "• left hand IK ready + not ready\n";

		EditorGUILayout.HelpBox(
			"Standing / Crouch / Vehicle are saved separately — tuning one will not overwrite the others.\n\n" +
			"ORDER\n" +
			"1. Enable Runtime Tuning\n" +
			"2. Active Posture = Standing / Crouch / Vehicle\n" +
			"   (Vehicle: mount a fire-capable seat, or tune buffers then Save Vehicle)\n" +
			"3. Hands Frozen → move the held weapon root\n" +
			"4. Not Ready → RightHandIkTarget_NotReady + LeftHandIkTarget_NotReady\n" +
			"5. Ready → RightHandIkTarget + LeftHandIkTarget (base pose auto-copied from Frozen)\n" +
			"6. Save Standing / Save Crouch / Save Vehicle\n" +
			leftSaveNote,
			MessageType.Info);
	}

	private static void DrawModeHint(
		UnitEquippedWeaponPoseRuntimeTuner.TuningTarget _target,
		UnitEquippedWeaponPoseRuntimeTuner.TuningPosture _posture)
	{
		string postureNote;
		switch (_posture)
		{
			case UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Crouch:
				postureNote = "Crouch: unit should be in crouch (Stance=1). Saves only crouch fields.\n";
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Vehicle:
				postureNote =
					"Vehicle: edits vehicle capture buffers. Prefer a fire-capable seat in Play Mode. " +
					"Save Vehicle writes only vehicle fields.\n";
				break;
			default:
				postureNote = "Standing: saves only standing fields.\n";
				break;
		}

		switch (_target)
		{
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HandsFrozen:
				EditorGUILayout.HelpBox(
					postureNote + "Hands Frozen: IK OFF. Move Equipped_* only.",
					MessageType.Warning);
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.NotReady:
				EditorGUILayout.HelpBox(
					postureNote +
					"Not Ready: move RightHandIkTarget_NotReady / LeftHandIkTarget_NotReady" +
					" (on foregrip if installed). Hands follow IK. Rocket launchers skip Not Ready IK save.",
					MessageType.None);
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.Ready:
				EditorGUILayout.HelpBox(
					postureNote +
					"Ready: move RightHandIkTarget and LeftHandIkTarget" +
					" (on foregrip if installed). Hands follow IK live.",
					MessageType.None);
				break;
		}
	}

	private static void SaveStandingToAsset(UnitEquippedWeaponPoseRuntimeTuner _tuner, ItemDefinition _definition)
	{
		if (_definition == null)
			return;

		if (_tuner.ActivePosture == UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Standing)
			_tuner.CaptureAllForSave();
		bool leftOnForegrip = _tuner.IsLeftHandIkDrivenByForegrip;

		Undo.RecordObject(_definition, "Save Standing Weapon Pose To ItemDefinition");

		SerializedObject so = new SerializedObject(_definition);
		so.FindProperty("m_RightHandLocalPosition").vector3Value = _tuner.StandingNotReadyLocalPosition;
		so.FindProperty("m_RightHandLocalEulerAngles").vector3Value = _tuner.StandingNotReadyLocalEulerAngles;
		so.FindProperty("m_RightHandReadyLocalPosition").vector3Value = _tuner.StandingReadyLocalPosition;
		so.FindProperty("m_RightHandReadyLocalEulerAngles").vector3Value = _tuner.StandingReadyLocalEulerAngles;
		so.FindProperty("m_RightHandIkReadyLocalPosition").vector3Value = _tuner.StandingReadyIkLocalPosition;
		so.FindProperty("m_RightHandIkReadyLocalEulerAngles").vector3Value = _tuner.StandingReadyIkLocalEulerAngles;

		// Rocket launchers only author Ready IK — leave Not Ready IK fields untouched.
		if (!_tuner.UsesRocketLauncherContext)
		{
			so.FindProperty("m_RightHandIkNotReadyLocalPosition").vector3Value = _tuner.StandingNotReadyIkLocalPosition;
			so.FindProperty("m_RightHandIkNotReadyLocalEulerAngles").vector3Value = _tuner.StandingNotReadyIkLocalEulerAngles;
		}

		if (!leftOnForegrip)
		{
			so.FindProperty("m_LeftHandIkReadyLocalPosition").vector3Value = _tuner.StandingLeftReadyIkLocalPosition;
			so.FindProperty("m_LeftHandIkReadyLocalEulerAngles").vector3Value = _tuner.StandingLeftReadyIkLocalEulerAngles;

			if (!_tuner.UsesRocketLauncherContext)
			{
				so.FindProperty("m_LeftHandIkNotReadyLocalPosition").vector3Value = _tuner.StandingLeftNotReadyIkLocalPosition;
				so.FindProperty("m_LeftHandIkNotReadyLocalEulerAngles").vector3Value = _tuner.StandingLeftNotReadyIkLocalEulerAngles;
			}
		}
		else
		{
			int fgIndex = _tuner.GetForegripIndex();
			if (fgIndex >= 1 && fgIndex <= 5)
			{
				string prefix = $"m_ForeGrip{fgIndex}LeftHandIk";
				so.FindProperty($"{prefix}ReadyLocalPosition").vector3Value = _tuner.StandingLeftReadyIkLocalPosition;
				so.FindProperty($"{prefix}ReadyLocalEulerAngles").vector3Value = _tuner.StandingLeftReadyIkLocalEulerAngles;

				if (!_tuner.UsesRocketLauncherContext)
				{
					so.FindProperty($"{prefix}NotReadyLocalPosition").vector3Value = _tuner.StandingLeftNotReadyIkLocalPosition;
					so.FindProperty($"{prefix}NotReadyLocalEulerAngles").vector3Value = _tuner.StandingLeftNotReadyIkLocalEulerAngles;
				}
			}
		}

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_definition);
		AssetDatabase.SaveAssets();
		Debug.Log($"[WeaponPoseTuner] Saved STANDING hand pose to '{_definition.name}'.", _definition);
	}

	private static void SaveCrouchToAsset(UnitEquippedWeaponPoseRuntimeTuner _tuner, ItemDefinition _definition)
	{
		if (_definition == null)
			return;

		if (_tuner.ActivePosture == UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Crouch)
			_tuner.CaptureAllForSave();

		Undo.RecordObject(_definition, "Save Crouch Weapon Pose To ItemDefinition");

		SerializedObject so = new SerializedObject(_definition);
		so.FindProperty("m_CrouchRightHandLocalPosition").vector3Value = _tuner.CrouchNotReadyLocalPosition;
		so.FindProperty("m_CrouchRightHandLocalEulerAngles").vector3Value = _tuner.CrouchNotReadyLocalEulerAngles;
		so.FindProperty("m_CrouchRightHandReadyLocalPosition").vector3Value = _tuner.CrouchReadyLocalPosition;
		so.FindProperty("m_CrouchRightHandReadyLocalEulerAngles").vector3Value = _tuner.CrouchReadyLocalEulerAngles;
		so.FindProperty("m_CrouchRightHandIkNotReadyLocalPosition").vector3Value = _tuner.CrouchNotReadyIkLocalPosition;
		so.FindProperty("m_CrouchRightHandIkNotReadyLocalEulerAngles").vector3Value = _tuner.CrouchNotReadyIkLocalEulerAngles;
		so.FindProperty("m_CrouchRightHandIkReadyLocalPosition").vector3Value = _tuner.CrouchReadyIkLocalPosition;
		so.FindProperty("m_CrouchRightHandIkReadyLocalEulerAngles").vector3Value = _tuner.CrouchReadyIkLocalEulerAngles;

		int fgIdx = _tuner.GetForegripIndex();
		if (_tuner.IsLeftHandIkDrivenByForegrip && fgIdx >= 1 && fgIdx <= 5)
		{
			string prefix = $"m_CrouchForeGrip{fgIdx}LeftHandIk";
			so.FindProperty($"{prefix}NotReadyLocalPosition").vector3Value = _tuner.CrouchLeftNotReadyIkLocalPosition;
			so.FindProperty($"{prefix}NotReadyLocalEulerAngles").vector3Value = _tuner.CrouchLeftNotReadyIkLocalEulerAngles;
			so.FindProperty($"{prefix}ReadyLocalPosition").vector3Value = _tuner.CrouchLeftReadyIkLocalPosition;
			so.FindProperty($"{prefix}ReadyLocalEulerAngles").vector3Value = _tuner.CrouchLeftReadyIkLocalEulerAngles;
		}
		else
		{
			so.FindProperty("m_CrouchLeftHandIkNotReadyLocalPosition").vector3Value = _tuner.CrouchLeftNotReadyIkLocalPosition;
			so.FindProperty("m_CrouchLeftHandIkNotReadyLocalEulerAngles").vector3Value = _tuner.CrouchLeftNotReadyIkLocalEulerAngles;
			so.FindProperty("m_CrouchLeftHandIkReadyLocalPosition").vector3Value = _tuner.CrouchLeftReadyIkLocalPosition;
			so.FindProperty("m_CrouchLeftHandIkReadyLocalEulerAngles").vector3Value = _tuner.CrouchLeftReadyIkLocalEulerAngles;
		}

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_definition);
		AssetDatabase.SaveAssets();
		Debug.Log($"[WeaponPoseTuner] Saved CROUCH hand pose to '{_definition.name}'.", _definition);
	}

	private static void SaveVehicleToAsset(UnitEquippedWeaponPoseRuntimeTuner _tuner, ItemDefinition _definition)
	{
		if (_definition == null)
			return;

		if (_tuner.ActivePosture == UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Vehicle)
			_tuner.CaptureAllForSave();
		else
			Debug.LogWarning(
				"[WeaponPoseTuner] Active Posture is not Vehicle — saving previously captured vehicle buffers " +
				"(may be stale). Set Active Posture = Vehicle, retune, then Save Vehicle.",
				_tuner);

		Undo.RecordObject(_definition, "Save Vehicle Weapon Pose To ItemDefinition");

		SerializedObject so = new SerializedObject(_definition);
		so.FindProperty("m_VehicleRightHandLocalPosition").vector3Value = _tuner.VehicleNotReadyLocalPosition;
		so.FindProperty("m_VehicleRightHandLocalEulerAngles").vector3Value = _tuner.VehicleNotReadyLocalEulerAngles;
		so.FindProperty("m_VehicleRightHandReadyLocalPosition").vector3Value = _tuner.VehicleReadyLocalPosition;
		so.FindProperty("m_VehicleRightHandReadyLocalEulerAngles").vector3Value = _tuner.VehicleReadyLocalEulerAngles;
		so.FindProperty("m_VehicleRightHandIkNotReadyLocalPosition").vector3Value = _tuner.VehicleNotReadyIkLocalPosition;
		so.FindProperty("m_VehicleRightHandIkNotReadyLocalEulerAngles").vector3Value = _tuner.VehicleNotReadyIkLocalEulerAngles;
		so.FindProperty("m_VehicleRightHandIkReadyLocalPosition").vector3Value = _tuner.VehicleReadyIkLocalPosition;
		so.FindProperty("m_VehicleRightHandIkReadyLocalEulerAngles").vector3Value = _tuner.VehicleReadyIkLocalEulerAngles;

		int fgIdx = _tuner.GetForegripIndex();
		if (_tuner.IsLeftHandIkDrivenByForegrip && fgIdx >= 1 && fgIdx <= 5)
		{
			string prefix = $"m_VehicleForeGrip{fgIdx}LeftHandIk";
			so.FindProperty($"{prefix}NotReadyLocalPosition").vector3Value = _tuner.VehicleLeftNotReadyIkLocalPosition;
			so.FindProperty($"{prefix}NotReadyLocalEulerAngles").vector3Value = _tuner.VehicleLeftNotReadyIkLocalEulerAngles;
			so.FindProperty($"{prefix}ReadyLocalPosition").vector3Value = _tuner.VehicleLeftReadyIkLocalPosition;
			so.FindProperty($"{prefix}ReadyLocalEulerAngles").vector3Value = _tuner.VehicleLeftReadyIkLocalEulerAngles;
		}
		else
		{
			so.FindProperty("m_VehicleLeftHandIkNotReadyLocalPosition").vector3Value = _tuner.VehicleLeftNotReadyIkLocalPosition;
			so.FindProperty("m_VehicleLeftHandIkNotReadyLocalEulerAngles").vector3Value = _tuner.VehicleLeftNotReadyIkLocalEulerAngles;
			so.FindProperty("m_VehicleLeftHandIkReadyLocalPosition").vector3Value = _tuner.VehicleLeftReadyIkLocalPosition;
			so.FindProperty("m_VehicleLeftHandIkReadyLocalEulerAngles").vector3Value = _tuner.VehicleLeftReadyIkLocalEulerAngles;
		}

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_definition);
		AssetDatabase.SaveAssets();
		Debug.Log($"[WeaponPoseTuner] Saved VEHICLE hand pose to '{_definition.name}'.", _definition);
	}

	private static void SaveForeGripLeftHandIkToAsset(
		UnitEquippedWeaponPoseRuntimeTuner _tuner,
		ItemDefinition _definition,
		UnitEquippedWeaponPoseRuntimeTuner.TuningPosture _posture)
	{
		if (_definition == null || _tuner == null)
			return;

		int fgIndex = _tuner.GetForegripIndex();
		if (fgIndex < 1 || fgIndex > 5)
		{
			Debug.LogWarning("[WeaponPoseTuner] Cannot determine foregrip index — make sure a foregrip is attached.", _tuner);
			return;
		}

		if (_tuner.ActivePosture == _posture)
		{
			_tuner.EnsureAllHandIkTargetsExist();
			_tuner.CaptureLiveIkFromScene();
		}

		string postureLabel;
		string prefix;
		Vector3 readyPosition, readyEuler, notReadyPosition, notReadyEuler;

		switch (_posture)
		{
			case UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Crouch:
				postureLabel = "Crouch";
				prefix = $"m_CrouchForeGrip{fgIndex}LeftHandIk";
				readyPosition = _tuner.CrouchLeftReadyIkLocalPosition;
				readyEuler = _tuner.CrouchLeftReadyIkLocalEulerAngles;
				notReadyPosition = _tuner.CrouchLeftNotReadyIkLocalPosition;
				notReadyEuler = _tuner.CrouchLeftNotReadyIkLocalEulerAngles;
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Vehicle:
				postureLabel = "Vehicle";
				prefix = $"m_VehicleForeGrip{fgIndex}LeftHandIk";
				readyPosition = _tuner.VehicleLeftReadyIkLocalPosition;
				readyEuler = _tuner.VehicleLeftReadyIkLocalEulerAngles;
				notReadyPosition = _tuner.VehicleLeftNotReadyIkLocalPosition;
				notReadyEuler = _tuner.VehicleLeftNotReadyIkLocalEulerAngles;
				break;
			default:
				postureLabel = "Standing";
				prefix = $"m_ForeGrip{fgIndex}LeftHandIk";
				readyPosition = _tuner.StandingLeftReadyIkLocalPosition;
				readyEuler = _tuner.StandingLeftReadyIkLocalEulerAngles;
				notReadyPosition = _tuner.StandingLeftNotReadyIkLocalPosition;
				notReadyEuler = _tuner.StandingLeftNotReadyIkLocalEulerAngles;
				break;
		}

		Undo.RecordObject(_definition, $"Save {postureLabel} ForeGrip{fgIndex} Left IK To ItemDefinition");

		SerializedObject so = new SerializedObject(_definition);
		so.FindProperty($"{prefix}ReadyLocalPosition").vector3Value = readyPosition;
		so.FindProperty($"{prefix}ReadyLocalEulerAngles").vector3Value = readyEuler;
		so.FindProperty($"{prefix}NotReadyLocalPosition").vector3Value = notReadyPosition;
		so.FindProperty($"{prefix}NotReadyLocalEulerAngles").vector3Value = notReadyEuler;

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_definition);
		AssetDatabase.SaveAssets();

		Debug.Log(
			$"[WeaponPoseTuner] Saved ForeGrip{fgIndex} left IK ({postureLabel}) to '{_definition.name}':\n" +
			$"  Ready    {readyPosition} / {readyEuler}\n" +
			$"  NotReady {notReadyPosition} / {notReadyEuler}",
			_definition);
	}

	private void CollapseOtherComponents()
	{
		Component tuner = target as Component;
		if (tuner == null)
			return;

		GameObject go = tuner.gameObject;
		int id = go.GetInstanceID();
		s_CollapsedGameObjects.Add(id);

		Component[] all = go.GetComponents<Component>();
		foreach (Component c in all)
		{
			if (c == null || c == tuner || c is Transform)
				continue;

			InternalEditorUtility.SetIsInspectorExpanded(c, false);
		}

		InternalEditorUtility.SetIsInspectorExpanded(tuner, true);
		ActiveEditorTracker.sharedTracker.ForceRebuild();
	}
}
#endif

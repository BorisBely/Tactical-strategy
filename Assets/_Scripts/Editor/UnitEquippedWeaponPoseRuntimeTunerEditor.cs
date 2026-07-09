#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UnitEquippedWeaponPoseRuntimeTuner))]
public sealed class UnitEquippedWeaponPoseRuntimeTunerEditor : Editor
{
	private SerializedProperty m_UnitEquipment;
	private SerializedProperty m_EquippedWeaponPose;
	private SerializedProperty m_EnableRuntimeTuning;
	private SerializedProperty m_ActiveTarget;
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

		using (new EditorGUI.DisabledScope(!Application.isPlaying))
		{
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(m_ActiveTarget, new GUIContent("Active Target"));
			bool targetChanged = EditorGUI.EndChangeCheck();

			var tuner = (UnitEquippedWeaponPoseRuntimeTuner)target;
			DrawModeHint(tuner.ActiveTarget);

			EditorGUILayout.Space(4f);
			EditorGUILayout.LabelField("Captured values", EditorStyles.boldLabel);
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.LabelField("Weapon — base / not ready", EditorStyles.miniBoldLabel);
				EditorGUILayout.PropertyField(m_NotReadyLocalPosition);
				EditorGUILayout.PropertyField(m_NotReadyLocalEulerAngles);
				EditorGUILayout.LabelField("Weapon — ready", EditorStyles.miniBoldLabel);
				EditorGUILayout.PropertyField(m_ReadyLocalPosition);
				EditorGUILayout.PropertyField(m_ReadyLocalEulerAngles);
				EditorGUILayout.LabelField("Right hand IK (not ready / ready)", EditorStyles.miniBoldLabel);
				EditorGUILayout.PropertyField(m_NotReadyIkLocalPosition);
				EditorGUILayout.PropertyField(m_NotReadyIkLocalEulerAngles);
				EditorGUILayout.PropertyField(m_ReadyIkLocalPosition);
				EditorGUILayout.PropertyField(m_ReadyIkLocalEulerAngles);
				EditorGUILayout.LabelField("Left hand IK (not ready / ready)", EditorStyles.miniBoldLabel);
				EditorGUILayout.PropertyField(m_LeftNotReadyIkLocalPosition);
				EditorGUILayout.PropertyField(m_LeftNotReadyIkLocalEulerAngles);
				EditorGUILayout.PropertyField(m_LeftReadyIkLocalPosition);
				EditorGUILayout.PropertyField(m_LeftReadyIkLocalEulerAngles);
			}

			ItemDefinition equipped = tuner.UnitEquipment != null ? tuner.UnitEquipment.EquippedDefinition : null;

			EditorGUILayout.Space(8f);
			if (GUILayout.Button("Copy Base Weapon → Ready Pose"))
				tuner.CopyBaseWeaponPoseToReady();

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Reload From Asset"))
					tuner.LoadFromEquippedDefinition();

				using (new EditorGUI.DisabledScope(equipped == null))
				{
					if (GUILayout.Button("Save To Asset"))
						SaveToAsset(tuner, equipped);
				}
			}

			using (new EditorGUI.DisabledScope(!tuner.IsLeftHandIkDrivenByForegrip))
			{
				if (GUILayout.Button("Save Left IK To Foregrip Prefab"))
					SaveLeftHandIkToForegripPrefab(tuner);
			}

			if (GUILayout.Button("Copy YAML"))
				EditorGUIUtility.systemCopyBuffer = tuner.BuildYamlSnippet();

			if (equipped != null)
				EditorGUILayout.HelpBox($"Asset: {equipped.name}", MessageType.None);
			else
				EditorGUILayout.HelpBox("Equip a weapon in Play Mode first.", MessageType.Warning);

			if (tuner.IsLeftHandIkDrivenByForegrip)
			{
				EditorGUILayout.HelpBox(
					"Foregrip installed: left IK lives on the grip prefab.\n" +
					"Tune LeftHandIkTarget / LeftHandIkTarget_NotReady under the grip, then use " +
					"Save Left IK To Foregrip Prefab. Save To Asset skips weapon left IK.",
					MessageType.Info);
			}

			serializedObject.ApplyModifiedProperties();

			if (Application.isPlaying && enableChanged && m_EnableRuntimeTuning.boolValue && !wasEnabled)
				tuner.LoadFromEquippedDefinition();

			if (Application.isPlaying && targetChanged && tuner.IsTuningActive)
				tuner.ApplyActiveTargetPoseToWeapon();
		}

		if (!Application.isPlaying)
			serializedObject.ApplyModifiedProperties();

		EditorGUILayout.Space(6f);
		var helpTuner = (UnitEquippedWeaponPoseRuntimeTuner)target;
		string leftSaveNote = helpTuner.IsLeftHandIkDrivenByForegrip
			? "• left hand IK: Save Left IK To Foregrip Prefab\n"
			: "• left hand IK ready + not ready\n";

		EditorGUILayout.HelpBox(
			"Save To Asset writes:\n" +
			"• weapon pose ready + not ready\n" +
			"• right hand IK ready + not ready\n" +
			leftSaveNote +
			"\nORDER\n" +
			"1. Enable Runtime Tuning\n" +
			"2. Hands Frozen → Equipped_*\n" +
			"3. Not Ready → RightHandIkTarget_NotReady + LeftHandIkTarget_NotReady\n" +
			"4. Ready → RightHandIkTarget + LeftHandIkTarget\n" +
			"5. Save To Asset\n" +
			"6. If foregrip: Save Left IK To Foregrip Prefab",
			MessageType.Info);
	}

	private static void DrawModeHint(UnitEquippedWeaponPoseRuntimeTuner.TuningTarget _target)
	{
		switch (_target)
		{
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HandsFrozen:
				EditorGUILayout.HelpBox(
					"Hands Frozen: IK OFF. Move Equipped_* only.",
					MessageType.Warning);
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.NotReady:
				EditorGUILayout.HelpBox(
					"Not Ready: move RightHandIkTarget_NotReady and LeftHandIkTarget_NotReady" +
					" (on foregrip if installed).",
					MessageType.None);
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.Ready:
				EditorGUILayout.HelpBox(
					"Ready: move RightHandIkTarget and LeftHandIkTarget" +
					" (on foregrip if installed).",
					MessageType.None);
				break;
		}
	}

	private static void SaveToAsset(UnitEquippedWeaponPoseRuntimeTuner _tuner, ItemDefinition _definition)
	{
		if (_definition == null)
			return;

		_tuner.CaptureAllForSave();

		bool leftOnForegrip = _tuner.IsLeftHandIkDrivenByForegrip;

		Undo.RecordObject(_definition, "Save Weapon Pose + All Hand IK To ItemDefinition");

		SerializedObject so = new SerializedObject(_definition);
		so.FindProperty("m_RightHandLocalPosition").vector3Value = _tuner.NotReadyLocalPosition;
		so.FindProperty("m_RightHandLocalEulerAngles").vector3Value = _tuner.NotReadyLocalEulerAngles;
		so.FindProperty("m_RightHandReadyLocalPosition").vector3Value = _tuner.ReadyLocalPosition;
		so.FindProperty("m_RightHandReadyLocalEulerAngles").vector3Value = _tuner.ReadyLocalEulerAngles;

		so.FindProperty("m_RightHandIkNotReadyLocalPosition").vector3Value = _tuner.NotReadyIkLocalPosition;
		so.FindProperty("m_RightHandIkNotReadyLocalEulerAngles").vector3Value = _tuner.NotReadyIkLocalEulerAngles;
		so.FindProperty("m_RightHandIkReadyLocalPosition").vector3Value = _tuner.ReadyIkLocalPosition;
		so.FindProperty("m_RightHandIkReadyLocalEulerAngles").vector3Value = _tuner.ReadyIkLocalEulerAngles;

		if (!leftOnForegrip)
		{
			so.FindProperty("m_LeftHandIkNotReadyLocalPosition").vector3Value = _tuner.LeftNotReadyIkLocalPosition;
			so.FindProperty("m_LeftHandIkNotReadyLocalEulerAngles").vector3Value = _tuner.LeftNotReadyIkLocalEulerAngles;
			so.FindProperty("m_LeftHandIkReadyLocalPosition").vector3Value = _tuner.LeftReadyIkLocalPosition;
			so.FindProperty("m_LeftHandIkReadyLocalEulerAngles").vector3Value = _tuner.LeftReadyIkLocalEulerAngles;

			SerializedProperty leftNotReadyName = so.FindProperty("m_LeftHandIkTargetNotReadyChildName");
			if (leftNotReadyName != null && string.IsNullOrWhiteSpace(leftNotReadyName.stringValue))
				leftNotReadyName.stringValue = "LeftHandIkTarget_NotReady";
		}

		so.ApplyModifiedPropertiesWithoutUndo();

		_tuner.ApplyStoredIkToTargets();

		EditorUtility.SetDirty(_definition);
		AssetDatabase.SaveAssets();

		string leftNote = leftOnForegrip
			? "  Left IK: skipped (driven by foregrip LeftHandIkTarget* — use Save Left IK To Foregrip Prefab)\n"
			: $"  Left IK NotReady  {_tuner.LeftNotReadyIkLocalPosition} / {_tuner.LeftNotReadyIkLocalEulerAngles}\n" +
			  $"  Left IK Ready     {_tuner.LeftReadyIkLocalPosition} / {_tuner.LeftReadyIkLocalEulerAngles}";

		Debug.Log(
			$"[WeaponPoseTuner] Saved to '{_definition.name}':\n" +
			$"  Weapon NotReady {_tuner.NotReadyLocalPosition} / {_tuner.NotReadyLocalEulerAngles}\n" +
			$"  Weapon Ready    {_tuner.ReadyLocalPosition} / {_tuner.ReadyLocalEulerAngles}\n" +
			$"  Right IK NotReady {_tuner.NotReadyIkLocalPosition} / {_tuner.NotReadyIkLocalEulerAngles}\n" +
			$"  Right IK Ready    {_tuner.ReadyIkLocalPosition} / {_tuner.ReadyIkLocalEulerAngles}\n" +
			leftNote,
			_definition);
	}

	private static void SaveLeftHandIkToForegripPrefab(UnitEquippedWeaponPoseRuntimeTuner _tuner)
	{
		if (_tuner == null)
			return;

		_tuner.EnsureAllHandIkTargetsExist();
		_tuner.CaptureLiveIkFromScene();

		Transform foregripRoot = _tuner.GetForegripVisualRoot();
		if (foregripRoot == null)
		{
			Debug.LogWarning("[WeaponPoseTuner] No foregrip installed — cannot save left IK to grip prefab.", _tuner);
			return;
		}

		GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(foregripRoot.gameObject);
		if (instanceRoot == null)
			instanceRoot = foregripRoot.gameObject;

		string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
		if (string.IsNullOrEmpty(prefabPath))
		{
			Debug.LogWarning(
				$"[WeaponPoseTuner] Foregrip '{instanceRoot.name}' is not a prefab instance — cannot save.",
				instanceRoot);
			return;
		}

		Transform liveReady = _tuner.UnitEquipment != null ? _tuner.UnitEquipment.LeftHandIkTargetTransform : null;
		Transform liveNotReady = _tuner.UnitEquipment != null ? _tuner.UnitEquipment.LeftHandIkTargetNotReadyTransform : null;

		GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
		try
		{
			Transform ready = FindChildRecursive(contents.transform, "LeftHandIkTarget");
			if (ready == null)
			{
				GameObject go = new GameObject("LeftHandIkTarget");
				ready = go.transform;
				ready.SetParent(contents.transform, false);
			}

			Transform notReady = FindChildRecursive(contents.transform, "LeftHandIkTarget_NotReady");
			if (notReady == null)
			{
				GameObject go = new GameObject("LeftHandIkTarget_NotReady");
				notReady = go.transform;
				notReady.SetParent(contents.transform, false);
			}

			if (liveReady != null && IsUnderOrSame(foregripRoot, liveReady))
			{
				ready.localPosition = liveReady.localPosition;
				ready.localRotation = liveReady.localRotation;
				ready.localScale = Vector3.one;
			}
			else
			{
				ready.localPosition = _tuner.LeftReadyIkLocalPosition;
				ready.localRotation = Quaternion.Euler(_tuner.LeftReadyIkLocalEulerAngles);
				ready.localScale = Vector3.one;
			}

			if (liveNotReady != null && IsUnderOrSame(foregripRoot, liveNotReady) && liveNotReady != liveReady)
			{
				notReady.localPosition = liveNotReady.localPosition;
				notReady.localRotation = liveNotReady.localRotation;
				notReady.localScale = Vector3.one;
			}
			else
			{
				notReady.localPosition = _tuner.LeftNotReadyIkLocalPosition;
				notReady.localRotation = Quaternion.Euler(_tuner.LeftNotReadyIkLocalEulerAngles);
				notReady.localScale = Vector3.one;
			}

			PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
			AssetDatabase.SaveAssets();

			Debug.Log(
				$"[WeaponPoseTuner] Saved left IK to foregrip prefab '{prefabPath}':\n" +
				$"  Ready    {ready.localPosition} / {ready.localEulerAngles}\n" +
				$"  NotReady {notReady.localPosition} / {notReady.localEulerAngles}",
				_tuner);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(contents);
		}
	}

	private static bool IsUnderOrSame(Transform _root, Transform _child)
	{
		return _root != null && _child != null && (_child == _root || _child.IsChildOf(_root));
	}

	private static Transform FindChildRecursive(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrWhiteSpace(_name))
			return null;

		Transform direct = _root.Find(_name);
		if (direct != null)
			return direct;

		for (int i = 0; i < _root.childCount; i++)
		{
			Transform found = FindChildRecursive(_root.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}
}
#endif

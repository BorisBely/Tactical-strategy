#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(UnitEquippedWeaponPoseRuntimeTuner))]
public sealed class UnitEquippedWeaponPoseRuntimeTunerEditor : Editor
{
	private static readonly HashSet<EntityId> s_CollapsedGameObjects = new HashSet<EntityId>();
	private static readonly string[] s_TargetLabels =
	{
		"Hands Frozen — только оружие",
		"Не готов — оружие не готово",
		"LowReady — оружие вниз",
		"HipFire — от бедра",
		"PointAim — по ЛЦУ",
		"Aiming — полный прицел",
		"HighReady — ствол вверх (authored)",
		"Патруль — не готов (патруль)",
		"HipFire walk — шаг стоя от бедра",
		"HipFire crouch walk — шаг в приседе",
	};

	private SerializedProperty m_UnitEquipment;
	private SerializedProperty m_EquippedWeaponPose;
	private SerializedProperty m_EnableRuntimeTuning;
	private SerializedProperty m_ActiveTarget;
	private SerializedProperty m_ActivePosture;

	private void OnEnable()
	{
		m_UnitEquipment = serializedObject.FindProperty("m_UnitEquipment");
		m_EquippedWeaponPose = serializedObject.FindProperty("m_EquippedWeaponPose");
		m_EnableRuntimeTuning = serializedObject.FindProperty("m_EnableRuntimeTuning");
		m_ActiveTarget = serializedObject.FindProperty("m_ActiveTarget");
		m_ActivePosture = serializedObject.FindProperty("m_ActivePosture");
		CollapseOtherComponents();
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();

		var tuner = (UnitEquippedWeaponPoseRuntimeTuner)target;

		EditorGUILayout.PropertyField(m_UnitEquipment);
		EditorGUILayout.PropertyField(m_EquippedWeaponPose);

		EditorGUILayout.Space(6f);
		DrawSimpleHelp();

		EditorGUILayout.Space(6f);
		bool wasEnabled = m_EnableRuntimeTuning.boolValue;
		EditorGUI.BeginChangeCheck();
		EditorGUILayout.PropertyField(m_EnableRuntimeTuning, new GUIContent("Включить тюнинг"));
		bool enableChanged = EditorGUI.EndChangeCheck();

		using (new EditorGUI.DisabledScope(!Application.isPlaying))
		{
			DrawRocketLauncherButtons(tuner);
			DrawForegripButtons(tuner);

			EditorGUI.BeginChangeCheck();
			int targetIdx = Mathf.Clamp(m_ActiveTarget.intValue, 0, s_TargetLabels.Length - 1);
			int newTargetIdx = EditorGUILayout.Popup(new GUIContent("Что крутим"), targetIdx, s_TargetLabels);
			if (newTargetIdx != targetIdx)
				m_ActiveTarget.intValue = newTargetIdx;

			bool lockWalkPosture =
				newTargetIdx == (int)UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFireWalk
				|| newTargetIdx == (int)UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFireCrouchWalk;
			if (newTargetIdx == (int)UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFireWalk
			    && m_ActivePosture.intValue != (int)UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Vehicle)
				m_ActivePosture.intValue = (int)UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Standing;
			else if (newTargetIdx == (int)UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFireCrouchWalk)
				m_ActivePosture.intValue = (int)UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Crouch;

			using (new EditorGUI.DisabledScope(lockWalkPosture))
				EditorGUILayout.PropertyField(m_ActivePosture, new GUIContent("Стойка"));
			bool modeChanged = EditorGUI.EndChangeCheck();

			DrawModeHint(tuner);
			if (tuner.ShouldFreezeWalkAnimator)
			{
				EditorGUILayout.HelpBox(
					"Анимация шага заморожена: клип и переходы не проигрываются. IK и поза оружия правятся на одном кадре.",
					MessageType.None);
			}

			EditorGUILayout.Space(6f);
			EditorGUILayout.LabelField("Поза оружия (буфер активного режима)", EditorStyles.boldLabel);
			using (new EditorGUI.DisabledScope(true))
			{
				DrawActivePoseBuffer(tuner);
			}

			Transform rightTarget = tuner.GetActiveRightHandTarget();
			Transform leftGrip = tuner.GetLiveLeftHandGripTransform();
			EditorGUILayout.Space(4f);
			EditorGUILayout.LabelField("Руки (в сцене)", EditorStyles.boldLabel);
			EditorGUILayout.LabelField(
				"Правая: " + (rightTarget != null ? GetTransformPath(rightTarget) : "— (нет IK в этом режиме)"));
			EditorGUILayout.LabelField(
				"Левая: " + (leftGrip != null ? GetTransformPath(leftGrip) : "— нет LeftHandGrip"));

			using (new EditorGUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(rightTarget == null))
				{
					if (GUILayout.Button("Выбрать правую цель"))
						Selection.activeTransform = rightTarget;
				}
				using (new EditorGUI.DisabledScope(leftGrip == null))
				{
					if (GUILayout.Button("Выбрать левую цель"))
						Selection.activeTransform = leftGrip;
				}
			}

			if (tuner.IsNonAiTunerPose)
			{
				EditorGUILayout.HelpBox(
					"AI Auto эти режимы не переключает — только ручной выбор в тюнере.",
					MessageType.Info);
			}

			if (tuner.ActiveTarget != UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HandsFrozen)
			{
				EditorGUILayout.Space(4f);
				EditorGUILayout.HelpBox(
					"Live IK включён — руки обновляются как в игре (веса 0.35 / 0.90).\n" +
					"Крути Local у IK-точек в Scene или полях ниже.",
					MessageType.Info);
				DrawHandEditFields(tuner, rightTarget, leftGrip);

				using (new EditorGUILayout.HorizontalScope())
				{
					using (new EditorGUI.DisabledScope(rightTarget == null && leftGrip == null))
					{
						if (GUILayout.Button("Копировать руки"))
						{
							if (tuner.CopyHandGripToClipboard())
								Debug.Log("[WeaponPoseTuner] Скопированы local pos/rot правой и левой точки.", tuner);
							else
								Debug.LogWarning("[WeaponPoseTuner] Нечего копировать — нет IK-целей.", tuner);
						}
					}
					using (new EditorGUI.DisabledScope(!tuner.HasHandGripClipboard))
					{
						if (GUILayout.Button("Вставить руки"))
						{
							if (PasteHandGripWithUndo(tuner))
								Debug.Log("[WeaponPoseTuner] Вставлены local pos/rot в текущие IK-цели.", tuner);
							else
								Debug.LogWarning("[WeaponPoseTuner] Вставка не удалась — нет целей или буфер пуст.", tuner);
						}
					}
				}

				if (tuner.HasHandGripClipboard)
				{
				 EditorGUILayout.LabelField(
						"Буфер рук: скопировано — переключи режим/стойку и жми «Вставить руки».",
						EditorStyles.miniLabel);
				}
			}

			EditorGUILayout.Space(8f);
			EditorGUILayout.LabelField("Руки → префаб", EditorStyles.boldLabel);
			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Создать GripRig"))
					tuner.EnsureWeaponGripRig();
				if (GUILayout.Button("Создать ForeGrip Left"))
					tuner.EnsureForeGripLeftHandGrip();
			}
			if (GUILayout.Button("Сохранить руки в префаб", GUILayout.Height(28f)))
				tuner.SaveGripTransformsToPrefabs();

			ItemDefinition equipped = tuner.ActiveTuningDefinition;

			EditorGUILayout.Space(8f);
			EditorGUILayout.LabelField("Оружие в правой руке", EditorStyles.boldLabel);
			bool frozen = tuner.ActiveTarget == UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HandsFrozen;
			Transform weaponRoot = tuner.GetActiveWeaponRoot();
			using (new EditorGUI.DisabledScope(!frozen || weaponRoot == null))
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					if (GUILayout.Button("Выбрать оружие"))
						Selection.activeTransform = weaponRoot;
					if (GUILayout.Button("Копировать оружие"))
					{
						if (tuner.CopyWeaponInHandToClipboard())
							Debug.Log("[WeaponPoseTuner] Скопирован local pos/rot Equipped_* в правой руке.", tuner);
						else
							Debug.LogWarning("[WeaponPoseTuner] Нечего копировать — нет Equipped_* или не Hands Frozen.", tuner);
					}
					using (new EditorGUI.DisabledScope(!tuner.HasWeaponInHandClipboard))
					{
						if (GUILayout.Button("Вставить оружие"))
						{
							if (PasteWeaponInHandWithUndo(tuner))
								Debug.Log("[WeaponPoseTuner] Вставлен local pos/rot в Equipped_*.", tuner);
							else
								Debug.LogWarning("[WeaponPoseTuner] Вставка не удалась — не Hands Frozen или буфер пуст.", tuner);
						}
					}
				}
			}

			if (!frozen)
			{
				EditorGUILayout.HelpBox(
					"Копировать / вставить оружие — только в Hands Frozen.",
					MessageType.None);
			}
			else if (tuner.HasWeaponInHandClipboard)
			{
				EditorGUILayout.LabelField(
					"Буфер оружия: скопировано — переключи стойку и жми «Вставить оружие».",
					EditorStyles.miniLabel);
			}

			EditorGUILayout.Space(8f);
			EditorGUILayout.LabelField("Поза оружия → WeaponPoseDefinition", EditorStyles.boldLabel);
			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Reload"))
					tuner.LoadFromEquippedDefinition();

				using (new EditorGUI.DisabledScope(equipped == null))
				{
					if (GUILayout.Button("Save Standing"))
						tuner.SaveWeaponPose(UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Standing);
					if (GUILayout.Button("Save Crouch"))
						tuner.SaveWeaponPose(UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Crouch);
					if (GUILayout.Button("Save Vehicle"))
						tuner.SaveWeaponPose(UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Vehicle);
				}
			}

			EditorGUILayout.HelpBox(
				"Save Standing/Crouch/Vehicle пишет все pose-слоты выбранной стойки.\n" +
				"Hands Frozen не пишет буферы каждый кадр. Смена стойки в Frozen ставит LowReady этой стойки на Equipped_*.\n" +
				"Save в Frozen пишет живой Equipped_* во все слоты этой стойки.\n" +
				"Если присед/машина уже затёрлись в этой сессии — Reload из WeaponPoseDefinition.\n" +
				"«Не готов» / Патруль сохраняются, но AI Auto их не переключает.",
				MessageType.None);

			if (tuner.UsesRocketLauncherContext)
			{
				EditorGUILayout.HelpBox(
					"Гранатомёт в руках (не винтовка).\n" +
					"Труба — отдельный префаб на правой руке, тело держит клип RocketLauncherAim.\n" +
					"Hands Frozen: крути саму трубу. Остальные режимы: IK-точки на GripRig трубы.\n" +
					"Save пишет WeaponPose гранатомёта. «Сохранить руки в префаб» → Equipped_Rpg7 / Disposable.",
					MessageType.Info);
			}
			else if (equipped != null)
			{
				string poseName = equipped.WeaponPoseDefinition != null
					? equipped.WeaponPoseDefinition.name
					: "НЕТ WeaponPoseDefinition";
				EditorGUILayout.HelpBox($"{equipped.name}\nПоза: {poseName}", MessageType.None);
			}
			else
			{
				EditorGUILayout.HelpBox("Сначала экипируй оружие в Play Mode.", MessageType.Warning);
			}

			if (tuner.HasForegripLeftHand)
			{
				EditorGUILayout.HelpBox(
					"Стоит рукоятка: левая рука = ForeGrip/LeftHandGrip этой рукоятки.\n" +
					"«Сохранить руки в префаб» пишет точку в префаб текущей рукоятки, не в тело винтовки.",
					MessageType.Info);
			}

			serializedObject.ApplyModifiedProperties();

			if (Application.isPlaying && enableChanged && m_EnableRuntimeTuning.boolValue && !wasEnabled)
				tuner.LoadFromEquippedDefinition();

			if (Application.isPlaying && modeChanged && tuner.IsTuningActive)
				tuner.ApplyActiveTargetSwitch();
		}

		if (!Application.isPlaying)
			serializedObject.ApplyModifiedProperties();

		EditorGUILayout.Space(6f);
		if (GUILayout.Button("Свернуть остальные компоненты"))
			CollapseOtherComponents();
	}

	private static void DrawActivePoseBuffer(UnitEquippedWeaponPoseRuntimeTuner _tuner)
	{
		switch (_tuner.ActiveTarget)
		{
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.NotReady:
				EditorGUILayout.Vector3Field("Не готов (оружие не готово) pos", _tuner.HoldNotReadyLocalPosition);
				EditorGUILayout.Vector3Field("Не готов (оружие не готово) rot", _tuner.HoldNotReadyLocalEulerAngles);
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.LowReady:
				EditorGUILayout.Vector3Field("LowReady (оружие вниз) pos", _tuner.LowReadyLocalPosition);
				EditorGUILayout.Vector3Field("LowReady (оружие вниз) rot", _tuner.LowReadyLocalEulerAngles);
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFire:
				EditorGUILayout.Vector3Field("HipFire pos", _tuner.HipFireLocalPosition);
				EditorGUILayout.Vector3Field("HipFire rot", _tuner.HipFireLocalEulerAngles);
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFireWalk:
				EditorGUILayout.Vector3Field("HipFire walk pos", _tuner.HipFireWalkLocalPosition);
				EditorGUILayout.Vector3Field("HipFire walk rot", _tuner.HipFireWalkLocalEulerAngles);
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFireCrouchWalk:
				EditorGUILayout.Vector3Field("HipFire crouch walk pos", _tuner.HipFireCrouchWalkLocalPosition);
				EditorGUILayout.Vector3Field("HipFire crouch walk rot", _tuner.HipFireCrouchWalkLocalEulerAngles);
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.PointAim:
				EditorGUILayout.Vector3Field("PointAim pos", _tuner.PointAimLocalPosition);
				EditorGUILayout.Vector3Field("PointAim rot", _tuner.PointAimLocalEulerAngles);
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.Aiming:
				EditorGUILayout.Vector3Field("Aiming pos", _tuner.AimingLocalPosition);
				EditorGUILayout.Vector3Field("Aiming rot", _tuner.AimingLocalEulerAngles);
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HighReady:
				EditorGUILayout.Vector3Field("HighReady pos", _tuner.HighReadyLocalPosition);
				EditorGUILayout.Vector3Field("HighReady rot", _tuner.HighReadyLocalEulerAngles);
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.NotReadyPatrol:
				EditorGUILayout.Vector3Field("Патруль (не готов) pos", _tuner.HoldNotReadyPatrolLocalPosition);
				EditorGUILayout.Vector3Field("Патруль (не готов) rot", _tuner.HoldNotReadyPatrolLocalEulerAngles);
				break;
			case UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HandsFrozen:
				EditorGUILayout.HelpBox(
					"Hands Frozen — двигаешь Equipped_* напрямую. Буферы не пишутся, пока не Save / Вставить оружие.\n" +
					"Смена стойки подставляет LowReady этой стойки.",
					MessageType.None);
				break;
		}
	}

	private static void DrawSimpleHelp()
	{
		EditorGUILayout.HelpBox(
			"ТЮНЕР = две разные вещи\n\n" +
			"А) ПОЗА ОРУЖИЯ — куда смотрит ствол в руке.\n" +
			"   Hands Frozen: двигаешь Equipped_* → Копировать/Вставить оружие → Save Standing/Crouch/Vehicle.\n\n" +
			"Б) РУКИ — куда ставятся кисти (live IK).\n" +
			"   Не готов / Патруль / LowReady / HighReady / HipFire / HipFire walk / HipFire crouch walk / PointAim / Aiming.\n" +
			"   PreAim — расчётный (LowReady→Aiming), в тюнере нет.\n\n" +
			"Без Play Mode кнопки серые.\n" +
			"Стрелковое — экипируй винтовку. Гранатомёт — кнопки «Выдать / сменить» и «Взять в руки».",
			MessageType.Info);
	}

	private static void DrawRocketLauncherButtons(UnitEquippedWeaponPoseRuntimeTuner _tuner)
	{
		EditorGUILayout.Space(4f);
		EditorGUILayout.LabelField("Гранатомёт", EditorStyles.boldLabel);
		EditorGUILayout.LabelField(_tuner.RocketLauncherTunerStatus, EditorStyles.wordWrappedMiniLabel);

		using (new EditorGUILayout.HorizontalScope())
		{
			if (GUILayout.Button("Выдать / сменить", GUILayout.Height(24f)))
			{
				if (_tuner.TryCycleSpawnRocketLauncherForTuning(out string message))
					Debug.Log("[WeaponPoseTuner] " + message, _tuner);
				else
					Debug.LogWarning("[WeaponPoseTuner] " + message, _tuner);
			}

			if (GUILayout.Button("Взять в руки", GUILayout.Height(24f)))
			{
				if (_tuner.TryActivateRocketLauncherForTuning(out string message))
					Debug.Log("[WeaponPoseTuner] " + message, _tuner);
				else
					Debug.LogWarning("[WeaponPoseTuner] " + message, _tuner);
			}

			if (GUILayout.Button("Убрать из рук", GUILayout.Height(24f)))
			{
				if (_tuner.TryHolsterRocketLauncherForTuning(out string message))
					Debug.Log("[WeaponPoseTuner] " + message, _tuner);
				else
					Debug.LogWarning("[WeaponPoseTuner] " + message, _tuner);
			}
		}
	}

	private static void DrawForegripButtons(UnitEquippedWeaponPoseRuntimeTuner _tuner)
	{
		EditorGUILayout.Space(4f);
		EditorGUILayout.LabelField("Тактическая рукоятка", EditorStyles.boldLabel);
		EditorGUILayout.LabelField(_tuner.ForegripTunerStatus, EditorStyles.wordWrappedMiniLabel);

		using (new EditorGUILayout.HorizontalScope())
		{
			using (new EditorGUI.DisabledScope(!_tuner.CanTuneForegrip))
			{
				string cycleLabel = _tuner.HasForegripLeftHand ? "Сменить рукоятку" : "Поставить рукоятку";
				if (GUILayout.Button(cycleLabel, GUILayout.Height(24f)))
				{
					if (_tuner.TryCycleForegripForTuning(out string message))
						Debug.Log("[WeaponPoseTuner] " + message, _tuner);
					else
						Debug.LogWarning("[WeaponPoseTuner] " + message, _tuner);
				}
			}

			using (new EditorGUI.DisabledScope(!_tuner.HasForegripLeftHand))
			{
				if (GUILayout.Button("Снять", GUILayout.Height(24f)))
				{
					if (_tuner.TryRemoveForegripForTuning(out string message))
						Debug.Log("[WeaponPoseTuner] " + message, _tuner);
					else
						Debug.LogWarning("[WeaponPoseTuner] " + message, _tuner);
				}
			}
		}
	}

	private static void DrawModeHint(UnitEquippedWeaponPoseRuntimeTuner _tuner)
	{
		string posture = _tuner.ActivePosture switch
		{
			UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Crouch =>
				"Стойка: присед — юнит сам приседает.\n" +
				"Не готов / Патруль / HipFire / Hands Frozen → RifleCrouch_Idle.\n" +
				"HipFire crouch walk → RifleCrouch_Move.\n" +
				"LowReady / HighReady / PointAim / Aiming → RifleCrouch_Idle_Ready.\n" +
				"HighReady и Aiming — один клип тела (как стоя Aim idle).",
			UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Vehicle =>
				"Стойка: машина — юнит садится как пассажир (огневое место, правый борт).\n" +
				"Не готов / Патруль / Hands Frozen → Seat_relax.\n" +
				"HipFire / LowReady / HighReady / PointAim / Aiming → Seat_Aim.\n" +
				"HighReady и Aiming — один клип тела (как стоя Aim idle).",
			_ => "Стойка: стоя — юнит встаёт.",
		};

		string mode = _tuner.ActiveTarget switch
		{
			UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HandsFrozen =>
				"Сейчас: только ОРУЖИЕ.\n" +
				"Тело как «Не готов»: standing relaxed / RifleCrouch_Idle / Seat_relax.\n" +
				"1) Выбери Equipped_* (или «Выбрать оружие»).\n" +
				"2) Двигай Move/Rotate.\n" +
				"3) Копировать оружие → другая стойка → Вставить оружие.\n" +
				"4) Save Standing (или Crouch/Vehicle).",
			UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.NotReady =>
				"Сейчас: Не готов — оружие не готово.\n" +
				"IK → …/HoldNotReady. Save Standing / «Сохранить руки в префаб».",
			UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.LowReady =>
				"Сейчас: LowReady — оружие вниз.\n" +
				"IK → …/LowReady. «Сохранить руки в префаб».",
			UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFire =>
				"Сейчас: HipFire (от бедра, idle).\n" +
				"IK → …/HipFire. «Сохранить руки в префаб».",
			UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFireWalk =>
				"Сейчас: HipFire walk — шаг стоя от бедра.\n" +
				"Стойка принудительно Standing. Тело: Walk_Aim_F_Loop (заморожен кадр, клип и переходы не идут).\n" +
				"IK → …/Standing/HipFireWalk. Save Standing / «Сохранить руки в префаб».\n" +
				"Idle HipFire и HipFire crouch walk не затираются.",
			UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFireCrouchWalk =>
				"Сейчас: HipFire crouch walk — шаг в приседе.\n" +
				"Стойка принудительно Crouch. Тело: RifleCrouch_Move (заморожен кадр, клип и переходы не идут).\n" +
				"IK → …/Crouch/HipFireCrouchWalk. Save Crouch / «Сохранить руки в префаб».\n" +
				"Idle HipFire и HipFire walk стоя не затираются.",
			UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.PointAim =>
				"Сейчас: PointAim (по ЛЦУ).\n" +
				"IK → …/PointAim. «Сохранить руки в префаб».",
			UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.Aiming =>
				"Сейчас: Aiming (полный прицел).\n" +
				"IK → …/Aiming. «Сохранить руки в префаб».",
			UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HighReady =>
				"Сейчас: HighReady — ствол вверх над угрозой.\n" +
				"IK → …/HighReady. Authored, огонь запрещён. PreAim в тюнере нет (derived).",
			UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.NotReadyPatrol =>
				"Сейчас: Патруль — те же правила, что у «Не готов».\n" +
				"IK → …/HoldNotReadyPatrol. Save Standing / «Сохранить руки в префаб».",
			_ => string.Empty,
		};

		EditorGUILayout.HelpBox(posture + "\n\n" + mode, MessageType.Warning);
	}

	private static string GetTransformPath(Transform _t)
	{
		if (_t == null)
			return "—";
		return AnimationUtility.CalculateTransformPath(_t, _t.root);
	}

	private static void DrawHandEditFields(
		UnitEquippedWeaponPoseRuntimeTuner _tuner,
		Transform _rightTarget,
		Transform _leftGrip)
	{
		Transform right = _tuner.GetActiveRightHandTarget() ?? _rightTarget;
		Transform left = _tuner.GetLiveLeftHandGripTransform() ?? _leftGrip;

		if (right != null)
			DrawTransformEditFields("Правая цель (Local)", right);
		if (left != null)
			DrawTransformEditFields("Левая LeftHandGrip (Local)", left);
	}

	private static bool PasteWeaponInHandWithUndo(UnitEquippedWeaponPoseRuntimeTuner _tuner)
	{
		Transform weaponRoot = _tuner.GetActiveWeaponRoot();
		if (weaponRoot != null)
			Undo.RecordObject(weaponRoot, "Paste weapon in hand");

		if (!_tuner.PasteWeaponInHandFromClipboard())
			return false;

		if (weaponRoot != null)
			EditorUtility.SetDirty(weaponRoot);
		return true;
	}

	private static bool PasteHandGripWithUndo(UnitEquippedWeaponPoseRuntimeTuner _tuner)
	{
		Transform right = _tuner.GetActiveRightHandTarget();
		Transform left = _tuner.GetLiveLeftHandGripTransform();
		if (right != null)
			Undo.RecordObject(right, "Paste hand grip");
		if (left != null)
			Undo.RecordObject(left, "Paste hand grip");

		if (!_tuner.PasteHandGripFromClipboard())
			return false;

		if (right != null)
			EditorUtility.SetDirty(right);
		if (left != null)
			EditorUtility.SetDirty(left);
		return true;
	}

	private static void DrawTransformEditFields(string _label, Transform _t)
	{
		EditorGUILayout.LabelField(_label, EditorStyles.boldLabel);
		EditorGUI.BeginChangeCheck();
		Vector3 pos = EditorGUILayout.Vector3Field("Position", _t.localPosition);
		Vector3 euler = EditorGUILayout.Vector3Field("Rotation", _t.localEulerAngles);
		if (!EditorGUI.EndChangeCheck())
			return;

		Undo.RecordObject(_t, "Tune hand grip");
		_t.localPosition = pos;
		_t.localRotation = Quaternion.Euler(euler);
		EditorUtility.SetDirty(_t);
	}

	private void CollapseOtherComponents()
	{
		Component tuner = target as Component;
		if (tuner == null)
			return;

		GameObject go = tuner.gameObject;
		s_CollapsedGameObjects.Add(go.GetEntityId());

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

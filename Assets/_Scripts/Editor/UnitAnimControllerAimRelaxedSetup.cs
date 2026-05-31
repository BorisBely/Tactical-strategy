#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Настраивает граф relaxed-перезарядки на слое Aim_Point_U90-D90 через Unity API
/// (правки YAML Unity часто не показывает, пока Animator открыт).
/// </summary>
public static class UnitAnimControllerAimRelaxedSetup
{
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";
	private const string c_AimLayerName = "Aim_Point_U90-D90";

	private const string c_PitchBlend = "Stand_Aim_Pitch_Blend";
	private const string c_CrouchPitchBlend = "Crouch_Aim_Pitch_Blend";
	private const string c_AimReload = "Stand_Aim_Reload";
	private const string c_AimBolt = "Stand_CyclingBolt";
	private const string c_RelaxedIdle = "Stand_Relaxed_Idle";
	private const string c_RelaxedReload = "Stand_Relaxed_Reload";
	private const string c_RelaxedBolt = "Stand_Relaxed__CyclingBolt";

	private const string c_ClipRelaxedIdle = "Assets/Animations/Rifle/Stand/Stand_Relaxed_Idle.anim";
	private const string c_ClipRelaxedReload = "Assets/Animations/Rifle/Stand/Stand_Relaxed_Reload.anim";
	private const string c_ClipRelaxedBolt = "Assets/Animations/Rifle/Stand/Stand_Relaxed__CyclingBolt.anim";

	private const string c_ParamWeaponReady = "WeaponReady";
	private const string c_ParamIsReloading = "IsReloadingWeapon";
	private const string c_ParamIsCyclingBolt = "IsCyclingBolt";
	private const string c_ParamStance = "Stance";

	[MenuItem("Tools/Polygone/Setup Aim Layer Relaxed Reload")]
	public static void SetupAimLayerRelaxedReloadGraph()
	{
		CloseAnimatorWindowsForController(c_ControllerPath);

		var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"Не найден Animator Controller: {c_ControllerPath}");
			return;
		}

		AnimatorControllerLayer aimLayer = FindLayer(controller, c_AimLayerName);
		if (aimLayer.stateMachine == null)
		{
			Debug.LogError($"Слой «{c_AimLayerName}» не найден в {c_ControllerPath}");
			return;
		}

		Undo.RecordObject(controller, "Setup Aim Layer Relaxed Reload");

		EnsureParameter(controller, c_ParamWeaponReady, AnimatorControllerParameterType.Bool);
		EnsureParameter(controller, c_ParamIsReloading, AnimatorControllerParameterType.Bool);
		EnsureParameter(controller, c_ParamIsCyclingBolt, AnimatorControllerParameterType.Bool);
		EnsureParameter(controller, c_ParamStance, AnimatorControllerParameterType.Int);

		AnimatorStateMachine sm = aimLayer.stateMachine;

		AnimatorState pitchBlend = RequireState(sm, c_PitchBlend);
		AnimatorState crouchPitch = RequireState(sm, c_CrouchPitchBlend);
		AnimatorState aimReload = RequireState(sm, c_AimReload);
		AnimatorState aimBolt = RequireState(sm, c_AimBolt);

		AnimationClip relaxedIdleClip = LoadClip(c_ClipRelaxedIdle);
		AnimationClip relaxedReloadClip = LoadClip(c_ClipRelaxedReload);
		AnimationClip relaxedBoltClip = LoadClip(c_ClipRelaxedBolt);

		AnimatorState relaxedIdle = EnsureMotionState(sm, c_RelaxedIdle, relaxedIdleClip);
		AnimatorState relaxedReload = EnsureMotionState(sm, c_RelaxedReload, relaxedReloadClip);
		AnimatorState relaxedBolt = EnsureMotionState(sm, c_RelaxedBolt, relaxedBoltClip);

		RemoveDuplicateNamedStates(sm, c_RelaxedIdle, relaxedIdle);
		RemoveDuplicateNamedStates(sm, c_RelaxedReload, relaxedReload);
		RemoveDuplicateNamedStates(sm, c_RelaxedBolt, relaxedBolt);

		RemoveTransition(pitchBlend, relaxedReload);
		RemoveTransition(pitchBlend, relaxedBolt);
		RemoveTransition(pitchBlend, relaxedIdle);
		RemoveTransition(crouchPitch, relaxedReload);

		RemovePitchExitWithoutWeaponReady(relaxedReload, pitchBlend);
		RemovePitchExitWithoutWeaponReady(relaxedBolt, pitchBlend);

		EnsureTransition(pitchBlend, aimReload, 0.12f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(pitchBlend, relaxedIdle, 0.15f,
			CondIfNot(c_ParamWeaponReady),
			CondEquals(c_ParamStance, 0f));

		EnsureTransition(pitchBlend, aimBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsReloading),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(crouchPitch, aimReload, 0.12f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(crouchPitch, relaxedReload, 0.12f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(crouchPitch, aimBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsReloading),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(crouchPitch, relaxedBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedIdle, relaxedReload, 0.18f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedIdle, relaxedBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedIdle, pitchBlend, 0.15f, CondIf(c_ParamWeaponReady));
		EnsureTransition(relaxedIdle, crouchPitch, 0.12f, CondEquals(c_ParamStance, 1f));

		EnsureTransition(relaxedReload, relaxedBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsReloading));

		EnsureTransition(relaxedReload, relaxedBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIf(c_ParamIsReloading));

		EnsureTransition(relaxedReload, pitchBlend, 0.15f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 0f),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(relaxedReload, pitchBlend, 0.15f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 2f),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(relaxedReload, crouchPitch, 0.15f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 1f));

		EnsureTransition(relaxedReload, relaxedIdle, 0.15f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 0f),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedReload, relaxedIdle, 0.15f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 2f),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedBolt, relaxedReload, 0.1f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedBolt, pitchBlend, 0.12f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 0f),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(relaxedBolt, pitchBlend, 0.12f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 2f),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(relaxedBolt, crouchPitch, 0.12f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 1f));

		EnsureTransition(relaxedBolt, relaxedIdle, 0.12f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 0f),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedBolt, relaxedIdle, 0.12f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 2f),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(aimReload, aimBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsReloading),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(aimReload, aimBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIf(c_ParamIsReloading),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(aimBolt, aimReload, 0.1f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(aimBolt, relaxedReload, 0.1f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamWeaponReady));

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		AssetDatabase.ImportAsset(c_ControllerPath, ImportAssetOptions.ForceUpdate);

		LogAimLayerReport(aimLayer.stateMachine);

		Debug.Log(
			$"Aim layer «{c_AimLayerName}» обновлён: {c_RelaxedIdle}, {c_RelaxedReload}, {c_RelaxedBolt}. " +
			"Откройте Animator и слой Aim_Point_U90-D90.",
			controller);
	}

	[MenuItem("Tools/Polygone/Log Aim Layer Relaxed Reload Status")]
	public static void LogAimLayerStatus()
	{
		var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"Не найден Animator Controller: {c_ControllerPath}");
			return;
		}

		AnimatorControllerLayer aimLayer = FindLayer(controller, c_AimLayerName);
		if (aimLayer.stateMachine == null)
		{
			Debug.LogError($"Слой «{c_AimLayerName}» не найден в {c_ControllerPath}");
			return;
		}

		LogAimLayerReport(aimLayer.stateMachine);
	}

	/// <summary>Для batchmode: Unity.exe -batchmode -quit -executeMethod UnitAnimControllerAimRelaxedSetup.RunSetupFromBatch</summary>
	public static void RunSetupFromBatch()
	{
		SetupAimLayerRelaxedReloadGraph();
		EditorApplication.Exit(0);
	}

	[MenuItem("Tools/Polygone/Reimport Unit Anim Controller")]
	public static void ReimportController()
	{
		AssetDatabase.ImportAsset(c_ControllerPath, ImportAssetOptions.ForceUpdate);
		Debug.Log($"Reimport: {c_ControllerPath}");
	}

	private readonly struct ConditionSpec
	{
		public readonly AnimatorConditionMode Mode;
		public readonly string Parameter;
		public readonly float Threshold;

		public ConditionSpec(AnimatorConditionMode _mode, string _parameter, float _threshold)
		{
			Mode = _mode;
			Parameter = _parameter;
			Threshold = _threshold;
		}
	}

	private static ConditionSpec CondIf(string _param) =>
		new ConditionSpec(AnimatorConditionMode.If, _param, 0f);

	private static ConditionSpec CondIfNot(string _param) =>
		new ConditionSpec(AnimatorConditionMode.IfNot, _param, 0f);

	private static ConditionSpec CondEquals(string _param, float _value) =>
		new ConditionSpec(AnimatorConditionMode.Equals, _param, _value);

	private static AnimatorControllerLayer FindLayer(AnimatorController _controller, string _layerName)
	{
		for (int i = 0; i < _controller.layers.Length; i++)
		{
			if (_controller.layers[i].name == _layerName)
				return _controller.layers[i];
		}

		return default;
	}

	private static void EnsureParameter(AnimatorController _controller, string _name, AnimatorControllerParameterType _type)
	{
		foreach (AnimatorControllerParameter p in _controller.parameters)
		{
			if (p.name == _name)
				return;
		}

		_controller.AddParameter(_name, _type);
	}

	private static AnimationClip LoadClip(string _path)
	{
		var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(_path);
		if (clip == null)
			Debug.LogWarning($"Клип не найден: {_path}");
		return clip;
	}

	private static AnimatorState RequireState(AnimatorStateMachine _sm, string _name)
	{
		foreach (ChildAnimatorState child in _sm.states)
		{
			if (child.state.name == _name)
				return child.state;
		}

		Debug.LogError($"На слое Aim не найден стейт «{_name}». Сначала создайте базовый граф вручную.");
		return null;
	}

	private static AnimatorState EnsureMotionState(AnimatorStateMachine _sm, string _name, Motion _motion)
	{
		AnimatorState best = null;
		int bestScore = -1;

		foreach (ChildAnimatorState child in _sm.states)
		{
			if (child.state.name != _name)
				continue;

			int score = child.state.transitions.Length;
			if (child.state.motion != null)
				score += 10;

			if (score <= bestScore)
				continue;

			bestScore = score;
			best = child.state;
		}

		if (best != null)
		{
			if (_motion != null)
				best.motion = _motion;

			RemoveDuplicateNamedStates(_sm, _name, best);
			return best;
		}

		AnimatorState created = _sm.AddState(_name);
		created.motion = _motion;
		return created;
	}

	private static void RemoveDuplicateNamedStates(AnimatorStateMachine _sm, string _name, AnimatorState _keep)
	{
		var duplicates = new List<AnimatorState>();
		foreach (ChildAnimatorState child in _sm.states)
		{
			if (child.state.name == _name && child.state != _keep)
				duplicates.Add(child.state);
		}

		for (int i = 0; i < duplicates.Count; i++)
			_sm.RemoveState(duplicates[i]);
	}

	private static void RemoveTransition(AnimatorState _from, AnimatorState _to)
	{
		if (_from == null || _to == null)
			return;

		for (int i = _from.transitions.Length - 1; i >= 0; i--)
		{
			if (_from.transitions[i].destinationState == _to)
				_from.RemoveTransition(_from.transitions[i]);
		}
	}

	private static void RemovePitchExitWithoutWeaponReady(AnimatorState _from, AnimatorState _pitchBlend)
	{
		if (_from == null || _pitchBlend == null)
			return;

		for (int i = _from.transitions.Length - 1; i >= 0; i--)
		{
			AnimatorStateTransition transition = _from.transitions[i];
			if (transition.destinationState != _pitchBlend)
				continue;

			bool hasWeaponReadyIf = false;
			foreach (AnimatorCondition condition in transition.conditions)
			{
				if (condition.parameter != c_ParamWeaponReady)
					continue;
				if (condition.mode == AnimatorConditionMode.If)
					hasWeaponReadyIf = true;
			}

			if (!hasWeaponReadyIf)
				_from.RemoveTransition(transition);
		}
	}

	private static void EnsureTransition(
		AnimatorState _from,
		AnimatorState _to,
		float _duration,
		params ConditionSpec[] _conditions)
	{
		if (_from == null || _to == null)
			return;

		foreach (AnimatorStateTransition existing in _from.transitions)
		{
			if (existing.destinationState != _to)
				continue;
			if (ConditionsMatch(existing.conditions, _conditions))
				return;
		}

		AnimatorStateTransition transition = _from.AddTransition(_to);
		transition.hasExitTime = false;
		transition.exitTime = 0f;
		transition.duration = _duration;
		transition.offset = 0f;
		transition.interruptionSource = TransitionInterruptionSource.None;
		transition.orderedInterruption = true;
		transition.canTransitionToSelf = false;

		transition.conditions = BuildConditions(_conditions);
	}

	private static AnimatorCondition[] BuildConditions(ConditionSpec[] _specs)
	{
		var result = new AnimatorCondition[_specs.Length];
		for (int i = 0; i < _specs.Length; i++)
		{
			result[i] = new AnimatorCondition
			{
				mode = _specs[i].Mode,
				parameter = _specs[i].Parameter,
				threshold = _specs[i].Threshold
			};
		}

		return result;
	}

	private static bool ConditionsMatch(AnimatorCondition[] _existing, ConditionSpec[] _expected)
	{
		if (_existing.Length != _expected.Length)
			return false;

		for (int i = 0; i < _expected.Length; i++)
		{
			if (_existing[i].mode != _expected[i].Mode)
				return false;
			if (_existing[i].parameter != _expected[i].Parameter)
				return false;
			if (!Mathf.Approximately(_existing[i].threshold, _expected[i].Threshold))
				return false;
		}

		return true;
	}

	private static void CloseAnimatorWindowsForController(string _controllerPath)
	{
		EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
		for (int i = 0; i < windows.Length; i++)
		{
			EditorWindow window = windows[i];
			if (window != null && window.GetType().Name == "AnimatorControllerWindow")
				window.Close();
		}
	}

	private static void LogAimLayerReport(AnimatorStateMachine _sm)
	{
		var lines = new List<string> { $"[{c_AimLayerName}] states ({_sm.states.Length}):" };

		foreach (ChildAnimatorState child in _sm.states)
		{
			AnimatorState state = child.state;
			string motion = state.motion != null ? state.motion.name : "(no motion)";
			lines.Add($"  • {state.name}: motion={motion}, transitions={state.transitions.Length}");
		}

		AnimatorState pitch = null;
		foreach (ChildAnimatorState child in _sm.states)
		{
			if (child.state.name == c_PitchBlend)
			{
				pitch = child.state;
				break;
			}
		}

		if (pitch != null)
		{
			lines.Add($"{c_PitchBlend} →");
			foreach (AnimatorStateTransition t in pitch.transitions)
			{
				string dst = t.destinationState != null ? t.destinationState.name : "(null)";
				lines.Add($"  → {dst} ({t.conditions.Length} cond)");
			}
		}

		Debug.Log(string.Join("\n", lines));
	}
}
#endif

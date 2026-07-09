#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Клипы поштучной зарядки дробовика и переходы в UnitAnimController (слой Aim_Point_U90-D90).
/// </summary>
public static class ShotgunShellReloadAnimSetup
{
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";
	private const string c_AimLayerName = "Aim_Point_U90-D90";

	private const string c_PitchBlend = "Stand_Aim_Pitch_Blend";
	private const string c_CrouchPitchBlend = "Crouch_Aim_Pitch_Blend";
	private const string c_RelaxedIdle = "Stand_Relaxed_Idle";
	private const string c_AimShellReload = "Stand_Aim_ShellReload";
	private const string c_RelaxedShellReload = "Stand_Relaxed_ShellReload";

	private const string c_ClipAimShellReload = "Assets/Animations/Shotgun/Stand/Stand_Aim_ShellReload.anim";
	private const string c_ClipRelaxedShellReload = "Assets/Animations/Shotgun/Stand/Stand_Relaxed_ShellReload.anim";

	private const string c_ParamWeaponReady = "WeaponReady";
	private const string c_ParamIsReloading = "IsReloadingWeapon";
	private const string c_ParamIsCyclingBolt = "IsCyclingBolt";
	private const string c_ParamIsShellReload = "IsShellByShellReload";
	private const string c_ParamStance = "Stance";

	private const float c_ShellReloadEnterBlendSeconds = 0.28f;
	private const float c_ShellReloadExitBlendSeconds = 0.32f;

	[MenuItem("Tools/Shotgun/Setup Shell Reload Animator")]
	public static void SetupShellReloadAnimator()
	{
		EnsureClipNames();
		SetupAimLayerShellReloadGraph();
	}

	[MenuItem("Tools/Shotgun/Rename Shell Reload Clip Names")]
	public static void EnsureClipNames()
	{
		RenameClipAssetName(c_ClipRelaxedShellReload, c_RelaxedShellReload);
		RenameClipAssetName(c_ClipAimShellReload, c_AimShellReload);
		AssetDatabase.SaveAssets();
	}

	private static void SetupAimLayerShellReloadGraph()
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

		Undo.RecordObject(controller, "Setup Shotgun Shell Reload");

		EnsureParameter(controller, c_ParamIsShellReload, AnimatorControllerParameterType.Bool);

		AnimatorStateMachine sm = aimLayer.stateMachine;
		AnimatorState pitchBlend = RequireState(sm, c_PitchBlend);
		AnimatorState crouchPitchBlend = RequireState(sm, c_CrouchPitchBlend);
		AnimatorState relaxedIdle = RequireState(sm, c_RelaxedIdle);

		AnimationClip aimShellClip = LoadClip(c_ClipAimShellReload);
		AnimationClip relaxedShellClip = LoadClip(c_ClipRelaxedShellReload);
		AnimatorState aimShellReload = EnsureMotionState(sm, c_AimShellReload, aimShellClip);
		AnimatorState relaxedShellReload = EnsureMotionState(sm, c_RelaxedShellReload, relaxedShellClip);

		GuardMagazineTransitionsFromCrouch(crouchPitchBlend);
		GuardMagazineTransitionsFromStand(pitchBlend);
		GuardMagazineTransitionsFromRelaxedIdle(relaxedIdle);

		EnsureTransition(pitchBlend, aimShellReload, c_ShellReloadEnterBlendSeconds,
			CondIf(c_ParamIsReloading),
			CondIf(c_ParamIsShellReload),
			CondIf(c_ParamWeaponReady),
			CondIfNot(c_ParamIsCyclingBolt));

		EnsureTransition(crouchPitchBlend, aimShellReload, c_ShellReloadEnterBlendSeconds,
			CondIf(c_ParamIsReloading),
			CondIf(c_ParamIsShellReload),
			CondIf(c_ParamWeaponReady),
			CondIfNot(c_ParamIsCyclingBolt));

		EnsureTransition(relaxedIdle, relaxedShellReload, c_ShellReloadEnterBlendSeconds,
			CondIf(c_ParamIsReloading),
			CondIf(c_ParamIsShellReload),
			CondIfNot(c_ParamWeaponReady),
			CondEquals(c_ParamStance, 0));

		EnsureTransition(relaxedIdle, relaxedShellReload, c_ShellReloadEnterBlendSeconds,
			CondIf(c_ParamIsReloading),
			CondIf(c_ParamIsShellReload),
			CondIfNot(c_ParamWeaponReady),
			CondEquals(c_ParamStance, 1));

		EnsureTransition(crouchPitchBlend, relaxedShellReload, c_ShellReloadEnterBlendSeconds,
			CondIf(c_ParamIsReloading),
			CondIf(c_ParamIsShellReload),
			CondIfNot(c_ParamWeaponReady),
			CondIfNot(c_ParamIsCyclingBolt));

		EnsureExitTransition(aimShellReload, pitchBlend, c_ShellReloadExitBlendSeconds,
			CondIfNot(c_ParamIsReloading),
			CondEquals(c_ParamStance, 0));

		EnsureExitTransition(aimShellReload, crouchPitchBlend, c_ShellReloadExitBlendSeconds,
			CondIfNot(c_ParamIsReloading),
			CondEquals(c_ParamStance, 1));

		EnsureExitTransition(relaxedShellReload, relaxedIdle, c_ShellReloadExitBlendSeconds,
			CondIfNot(c_ParamIsReloading),
			CondEquals(c_ParamStance, 0));

		EnsureExitTransition(relaxedShellReload, crouchPitchBlend, c_ShellReloadExitBlendSeconds,
			CondIfNot(c_ParamIsReloading),
			CondEquals(c_ParamStance, 1));

		ApplyShellReloadTransitionSmoothing(sm, aimShellReload, relaxedShellReload);

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		Debug.Log("[ShotgunShellReloadAnimSetup] Shell reload states wired on Aim layer (stand + crouch).");
	}

	private static void ApplyShellReloadTransitionSmoothing(
		AnimatorStateMachine _sm,
		AnimatorState _aimShellReload,
		AnimatorState _relaxedShellReload)
	{
		foreach (ChildAnimatorState child in _sm.states)
		{
			if (child.state == null)
				continue;

			foreach (AnimatorStateTransition transition in child.state.transitions)
			{
				if (transition == null)
					continue;

				if (transition.destinationState != _aimShellReload &&
				    transition.destinationState != _relaxedShellReload)
					continue;

				if (!HasShellReloadEnterCondition(transition))
					continue;

				transition.duration = c_ShellReloadEnterBlendSeconds;
				transition.hasExitTime = false;
			}
		}

		TuneShellReloadExitTransitions(_aimShellReload);
		TuneShellReloadExitTransitions(_relaxedShellReload);
	}

	private static bool HasShellReloadEnterCondition(AnimatorStateTransition _transition)
	{
		for (int i = 0; i < _transition.conditions.Length; i++)
		{
			AnimatorCondition condition = _transition.conditions[i];
			if (condition.parameter == c_ParamIsShellReload && condition.mode == AnimatorConditionMode.If)
				return true;
		}

		return false;
	}

	private static void TuneShellReloadExitTransitions(AnimatorState _shellState)
	{
		foreach (AnimatorStateTransition transition in _shellState.transitions)
		{
			if (transition == null)
				continue;

			for (int i = 0; i < transition.conditions.Length; i++)
			{
				AnimatorCondition condition = transition.conditions[i];
				if (condition.parameter != c_ParamIsReloading || condition.mode != AnimatorConditionMode.IfNot)
					continue;

				transition.duration = c_ShellReloadExitBlendSeconds;
				transition.hasExitTime = false;
				break;
			}
		}
	}

	private static void GuardMagazineTransitionsFromCrouch(AnimatorState _crouchPitchBlend)
	{
		foreach (AnimatorStateTransition transition in _crouchPitchBlend.transitions)
		{
			if (transition == null || transition.conditions == null)
				continue;

			bool isMagazineReload = false;
			for (int i = 0; i < transition.conditions.Length; i++)
			{
				AnimatorCondition condition = transition.conditions[i];
				if (condition.parameter == c_ParamIsReloading && condition.mode == AnimatorConditionMode.If)
					isMagazineReload = true;
			}

			if (!isMagazineReload)
				continue;

			if (HasCondition(transition, c_ParamIsShellReload))
				continue;

			AddConditionIfMissing(transition, CondIfNot(c_ParamIsShellReload));
		}
	}

	private static void GuardMagazineTransitionsFromStand(AnimatorState _pitchBlend)
	{
		GuardMagazineReloadTransitions(_pitchBlend);
	}

	private static void GuardMagazineTransitionsFromRelaxedIdle(AnimatorState _relaxedIdle)
	{
		GuardMagazineReloadTransitions(_relaxedIdle);
	}

	private static void GuardMagazineReloadTransitions(AnimatorState _state)
	{
		foreach (AnimatorStateTransition transition in _state.transitions)
		{
			if (transition == null || transition.conditions == null)
				continue;

			bool isMagazineReload = false;
			for (int i = 0; i < transition.conditions.Length; i++)
			{
				AnimatorCondition condition = transition.conditions[i];
				if (condition.parameter == c_ParamIsReloading && condition.mode == AnimatorConditionMode.If)
					isMagazineReload = true;
			}

			if (!isMagazineReload)
				continue;

			if (HasCondition(transition, c_ParamIsShellReload))
				continue;

			AddConditionIfMissing(transition, CondIfNot(c_ParamIsShellReload));
		}
	}

	private static bool HasCondition(AnimatorStateTransition _transition, string _param)
	{
		for (int i = 0; i < _transition.conditions.Length; i++)
		{
			if (_transition.conditions[i].parameter == _param)
				return true;
		}

		return false;
	}

	private static void AddConditionIfMissing(AnimatorStateTransition _transition, AnimatorCondition _condition)
	{
		if (HasCondition(_transition, _condition.parameter))
			return;

		var conditions = new AnimatorCondition[_transition.conditions.Length + 1];
		for (int i = 0; i < _transition.conditions.Length; i++)
			conditions[i] = _transition.conditions[i];

		conditions[conditions.Length - 1] = _condition;
		_transition.conditions = conditions;
	}

	private static void RenameClipAssetName(string _path, string _name)
	{
		AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(_path);
		if (clip == null)
		{
			Debug.LogWarning($"[ShotgunShellReloadAnimSetup] Clip not found: {_path}");
			return;
		}

		if (clip.name == _name)
			return;

		clip.name = _name;
		EditorUtility.SetDirty(clip);
	}

	private static AnimatorControllerLayer FindLayer(AnimatorController _controller, string _name)
	{
		for (int i = 0; i < _controller.layers.Length; i++)
		{
			if (_controller.layers[i].name == _name)
				return _controller.layers[i];
		}

		return null;
	}

	private static void EnsureParameter(AnimatorController _controller, string _name, AnimatorControllerParameterType _type)
	{
		for (int i = 0; i < _controller.parameters.Length; i++)
		{
			if (_controller.parameters[i].name == _name)
				return;
		}

		_controller.AddParameter(_name, _type);
	}

	private static AnimatorState RequireState(AnimatorStateMachine _sm, string _name)
	{
		foreach (ChildAnimatorState child in _sm.states)
		{
			if (child.state != null && child.state.name == _name)
				return child.state;
		}

		throw new System.InvalidOperationException($"State not found: {_name}");
	}

	private static AnimatorState EnsureMotionState(AnimatorStateMachine _sm, string _name, Motion _motion)
	{
		foreach (ChildAnimatorState child in _sm.states)
		{
			if (child.state == null || child.state.name != _name)
				continue;

			child.state.motion = _motion;
			return child.state;
		}

		var state = _sm.AddState(_name);
		state.motion = _motion;
		return state;
	}

	private static AnimationClip LoadClip(string _path)
	{
		AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(_path);
		if (clip == null)
			throw new System.InvalidOperationException($"Missing animation clip: {_path}");

		return clip;
	}

	private static void EnsureTransition(AnimatorState _from, AnimatorState _to, float _duration, params AnimatorCondition[] _conditions)
	{
		foreach (AnimatorStateTransition existing in _from.transitions)
		{
			if (existing.destinationState == _to && ConditionsMatch(existing.conditions, _conditions))
			{
				existing.duration = _duration;
				existing.hasExitTime = false;
				return;
			}
		}

		AnimatorStateTransition transition = _from.AddTransition(_to);
		transition.hasExitTime = false;
		transition.duration = _duration;
		transition.conditions = _conditions;
	}

	private static void EnsureExitTransition(AnimatorState _from, AnimatorState _to, float _duration, params AnimatorCondition[] _conditions)
	{
		foreach (AnimatorStateTransition existing in _from.transitions)
		{
			if (existing.destinationState == _to && ConditionsMatch(existing.conditions, _conditions))
			{
				existing.duration = _duration;
				existing.hasExitTime = false;
				return;
			}
		}

		AnimatorStateTransition transition = _from.AddTransition(_to);
		transition.hasExitTime = false;
		transition.duration = _duration;
		transition.conditions = _conditions;
	}

	private static bool ConditionsMatch(AnimatorCondition[] _existing, AnimatorCondition[] _expected)
	{
		if (_existing == null || _expected == null || _existing.Length != _expected.Length)
			return false;

		for (int i = 0; i < _expected.Length; i++)
		{
			if (_existing[i].mode != _expected[i].mode ||
			    _existing[i].parameter != _expected[i].parameter ||
			    !Mathf.Approximately(_existing[i].threshold, _expected[i].threshold))
				return false;
		}

		return true;
	}

	private static AnimatorCondition CondIf(string _param) =>
		new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = _param, threshold = 0f };

	private static AnimatorCondition CondIfNot(string _param) =>
		new AnimatorCondition { mode = AnimatorConditionMode.IfNot, parameter = _param, threshold = 0f };

	private static AnimatorCondition CondEquals(string _param, int _value) =>
		new AnimatorCondition { mode = AnimatorConditionMode.Equals, parameter = _param, threshold = _value };
}
#endif

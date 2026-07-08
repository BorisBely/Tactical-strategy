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
	private const string c_RelaxedIdle = "Stand_Relaxed_Idle";
	private const string c_AimShellReload = "Stand_Aim_ShellReload";
	private const string c_RelaxedShellReload = "Stand_Relaxed_ShellReload";

	private const string c_ClipAimShellReload = "Assets/Animations/Shotgun/Stand/Stand_Aim_ShellReload.anim";
	private const string c_ClipRelaxedShellReload = "Assets/Animations/Shotgun/Stand/Stand_Relaxed_ShellReload.anim";

	private const string c_ParamWeaponReady = "WeaponReady";
	private const string c_ParamIsReloading = "IsReloadingWeapon";
	private const string c_ParamIsShellReload = "IsShellByShellReload";
	private const string c_ParamStance = "Stance";

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
		AnimatorState relaxedIdle = RequireState(sm, c_RelaxedIdle);

		AnimationClip aimShellClip = LoadClip(c_ClipAimShellReload);
		AnimationClip relaxedShellClip = LoadClip(c_ClipRelaxedShellReload);
		AnimatorState aimShellReload = EnsureMotionState(sm, c_AimShellReload, aimShellClip);
		AnimatorState relaxedShellReload = EnsureMotionState(sm, c_RelaxedShellReload, relaxedShellClip);

		EnsureTransition(pitchBlend, aimShellReload, 0.12f,
			CondIf(c_ParamIsReloading),
			CondIf(c_ParamIsShellReload),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(relaxedIdle, relaxedShellReload, 0.12f,
			CondIf(c_ParamIsReloading),
			CondIf(c_ParamIsShellReload),
			CondIfNot(c_ParamWeaponReady),
			CondEquals(c_ParamStance, 0));

		EnsureExitTransition(aimShellReload, pitchBlend, 0.12f, CondIfNot(c_ParamIsReloading));
		EnsureExitTransition(relaxedShellReload, relaxedIdle, 0.12f, CondIfNot(c_ParamIsReloading));

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		Debug.Log("[ShotgunShellReloadAnimSetup] Shell reload states wired on Aim layer.");
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
			if (existing.destinationState == _to)
				return;
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
			if (existing.destinationState == _to)
				return;
		}

		AnimatorStateTransition transition = _from.AddTransition(_to);
		transition.hasExitTime = false;
		transition.duration = _duration;
		transition.conditions = _conditions;
	}

	private static AnimatorCondition CondIf(string _param) =>
		new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = _param, threshold = 0f };

	private static AnimatorCondition CondIfNot(string _param) =>
		new AnimatorCondition { mode = AnimatorConditionMode.IfNot, parameter = _param, threshold = 0f };

	private static AnimatorCondition CondEquals(string _param, int _value) =>
		new AnimatorCondition { mode = AnimatorConditionMode.Equals, parameter = _param, threshold = _value };
}
#endif

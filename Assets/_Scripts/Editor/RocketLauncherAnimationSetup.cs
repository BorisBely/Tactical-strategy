#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Переносит клипы гранатомётов, запекает animation events и вешает состояния на Aim_Point_U90-D90.
/// </summary>
[InitializeOnLoad]
public static class RocketLauncherAnimationSetup
{
	#region Constants
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";
	private const string c_AimLayerName = "Aim_Point_U90-D90";
	private const string c_AnimFolder = "Assets/Animations/RocketLauncher/Stand";

	private const string c_SrcAimRpg = "Assets/Stand_Aim_RPG.anim";
	private const string c_SrcFireRpg = "Assets/Stand_fire_RPG_.anim";
	private const string c_SrcReloadRpg = "Assets/Stand_Aim_Reload_RPG.anim";
	private const string c_SrcAimRl = "Assets/Stand_Aim_RL.anim";
	private const string c_SrcFireRl = "Assets/Stand_fire_RL.anim";

	private const string c_DstAimRpg = c_AnimFolder + "/Stand_Aim_RPG.anim";
	private const string c_DstFireRpg = c_AnimFolder + "/Stand_Fire_RPG.anim";
	private const string c_DstReloadRpg = c_AnimFolder + "/Stand_Aim_Reload_RPG.anim";
	private const string c_DstAimRl = c_AnimFolder + "/Stand_Aim_RL.anim";
	private const string c_DstFireRl = c_AnimFolder + "/Stand_Fire_RL.anim";

	private const string c_StateAimRpg = "RocketLauncher_Aim_RPG";
	private const string c_StateFireRpg = "RocketLauncher_Fire_RPG";
	private const string c_StateReloadRpg = "RocketLauncher_Reload_RPG";
	private const string c_StateAimRl = "RocketLauncher_Aim_RL";
	private const string c_StateFireRl = "RocketLauncher_Fire_RL";

	private const string c_EventFire = "AnimationEvent_RocketLauncherFire";
	private const string c_EventDiscard = "AnimationEvent_DisposableLauncherDiscard";
	private const string c_EventShowRocket = "AnimationEvent_RpgRocketShowInHand";
	private const string c_EventInsertRocket = "AnimationEvent_RpgRocketInsert";
	private const string c_EventFinished = "AnimationEvent_RocketLauncherOrderFinished";
	#endregion

	#region Bootstrap
	static RocketLauncherAnimationSetup()
	{
		EditorApplication.delayCall += TryAutoSetup;
	}
	#endregion

	#region Menu
	[MenuItem("Polygone/Animation/Setup Rocket Launcher Layer")]
	public static void SetupRocketLauncherLayer()
	{
		EnsureAnimationsMoved();

		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"[RocketLauncherAnim] Controller not found: {c_ControllerPath}");
			return;
		}

		AnimationClip aimRpg = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_DstAimRpg);
		AnimationClip fireRpg = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_DstFireRpg);
		AnimationClip reloadRpg = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_DstReloadRpg);
		AnimationClip aimRl = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_DstAimRl);
		AnimationClip fireRl = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_DstFireRl);

		if (aimRpg == null || fireRpg == null || reloadRpg == null || aimRl == null || fireRl == null)
		{
			Debug.LogError("[RocketLauncherAnim] One or more animation clips missing after move.");
			return;
		}

		Undo.RecordObject(controller, "Setup Rocket Launcher Layer");

		EnsureParameter(controller, UnitRocketLauncherOrderController.ParamRocketLauncherAim, AnimatorControllerParameterType.Bool);
		EnsureParameter(controller, UnitRocketLauncherOrderController.ParamRocketLauncherFire, AnimatorControllerParameterType.Trigger);
		EnsureParameter(controller, UnitRocketLauncherOrderController.ParamRocketLauncherReload, AnimatorControllerParameterType.Trigger);
		EnsureParameter(controller, UnitRocketLauncherOrderController.ParamRocketLauncherKind, AnimatorControllerParameterType.Int);

		BakeFireEvents(fireRpg, false);
		BakeFireEvents(fireRl, true);
		BakeReloadEvents(reloadRpg);
		SetLoopTime(aimRpg, true);
		SetLoopTime(aimRl, true);
		SetLoopTime(fireRpg, false);
		SetLoopTime(fireRl, false);
		SetLoopTime(reloadRpg, false);

		int layerIndex = FindLayerIndex(controller, c_AimLayerName);
		if (layerIndex < 0)
		{
			Debug.LogError($"[RocketLauncherAnim] Layer '{c_AimLayerName}' not found.");
			return;
		}

		AnimatorStateMachine sm = controller.layers[layerIndex].stateMachine;
		AnimatorState aimRpgState = EnsureMotionState(sm, c_StateAimRpg, aimRpg);
		AnimatorState fireRpgState = EnsureMotionState(sm, c_StateFireRpg, fireRpg);
		AnimatorState reloadRpgState = EnsureMotionState(sm, c_StateReloadRpg, reloadRpg);
		AnimatorState aimRlState = EnsureMotionState(sm, c_StateAimRl, aimRl);
		AnimatorState fireRlState = EnsureMotionState(sm, c_StateFireRl, fireRl);

		EnsureAnyStateTransition(sm, aimRpgState, UnitRocketLauncherOrderController.ParamRocketLauncherAim, true, UnitRocketLauncherOrderController.ParamRocketLauncherKind, 0);
		EnsureAnyStateTransition(sm, aimRlState, UnitRocketLauncherOrderController.ParamRocketLauncherAim, true, UnitRocketLauncherOrderController.ParamRocketLauncherKind, 1);
		EnsureAnyStateTransition(sm, fireRpgState, UnitRocketLauncherOrderController.ParamRocketLauncherFire, false, UnitRocketLauncherOrderController.ParamRocketLauncherKind, 0);
		EnsureAnyStateTransition(sm, fireRlState, UnitRocketLauncherOrderController.ParamRocketLauncherFire, false, UnitRocketLauncherOrderController.ParamRocketLauncherKind, 1);
		EnsureAnyStateTransition(sm, reloadRpgState, UnitRocketLauncherOrderController.ParamRocketLauncherReload, false, UnitRocketLauncherOrderController.ParamRocketLauncherKind, 0);

		EnsureExitToDefault(sm, fireRpgState);
		EnsureExitToDefault(sm, fireRlState);
		EnsureExitToDefault(sm, reloadRpgState);

		// Aim stays while bool is true; exit when aim ends without fire/reload.
		EnsureBoolExit(sm, aimRpgState, UnitRocketLauncherOrderController.ParamRocketLauncherAim, false);
		EnsureBoolExit(sm, aimRlState, UnitRocketLauncherOrderController.ParamRocketLauncherAim, false);

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[RocketLauncherAnim] Rocket launcher states wired on Aim layer.");
	}

	[MenuItem("Polygone/Animation/Bake Rocket Launcher Animation Events")]
	public static void BakeRocketLauncherEvents()
	{
		EnsureAnimationsMoved();
		BakeFireEvents(AssetDatabase.LoadAssetAtPath<AnimationClip>(c_DstFireRpg), false);
		BakeFireEvents(AssetDatabase.LoadAssetAtPath<AnimationClip>(c_DstFireRl), true);
		BakeReloadEvents(AssetDatabase.LoadAssetAtPath<AnimationClip>(c_DstReloadRpg));
		AssetDatabase.SaveAssets();
		Debug.Log("[RocketLauncherAnim] Events baked.");
	}

	public static void EnsureAnimationsMoved()
	{
		EnsureFolder("Assets/Animations/RocketLauncher");
		EnsureFolder(c_AnimFolder);

		TryMoveOrCopy(c_SrcAimRpg, c_DstAimRpg);
		TryMoveOrCopy(c_SrcFireRpg, c_DstFireRpg);
		TryMoveOrCopy(c_SrcReloadRpg, c_DstReloadRpg);
		TryMoveOrCopy(c_SrcAimRl, c_DstAimRl);
		TryMoveOrCopy(c_SrcFireRl, c_DstFireRl);
		AssetDatabase.Refresh();
	}
	#endregion

	#region Private
	private static void TryAutoSetup()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
			return;

		int layerIndex = FindLayerIndex(controller, c_AimLayerName);
		if (layerIndex < 0)
			return;

		AnimatorStateMachine stateMachine = controller.layers[layerIndex].stateMachine;
		ChildAnimatorState[] states = stateMachine.states;
		for (int i = 0; i < states.Length; i++)
		{
			if (states[i].state != null && states[i].state.name == c_StateAimRpg)
				return;
		}

		if (AssetDatabase.LoadAssetAtPath<AnimationClip>(c_DstAimRpg) != null ||
		    AssetDatabase.LoadAssetAtPath<AnimationClip>(c_SrcAimRpg) != null)
		{
			SetupRocketLauncherLayer();
		}
	}

	private static void TryMoveOrCopy(string _src, string _dst)
	{
		if (AssetDatabase.LoadAssetAtPath<AnimationClip>(_dst) != null)
			return;

		if (AssetDatabase.LoadAssetAtPath<AnimationClip>(_src) == null)
			return;

		string error = AssetDatabase.MoveAsset(_src, _dst);
		if (!string.IsNullOrEmpty(error))
		{
			// Rename case: Stand_fire_RPG_ -> Stand_Fire_RPG
			if (AssetDatabase.CopyAsset(_src, _dst))
				AssetDatabase.DeleteAsset(_src);
			else
				Debug.LogWarning($"[RocketLauncherAnim] Failed to move {_src} -> {_dst}: {error}");
		}

		string metaSrc = _src + ".meta";
		if (File.Exists(metaSrc) && !File.Exists(_dst + ".meta"))
		{
			// MoveAsset handles meta; ignore.
		}
	}

	private static void BakeFireEvents(AnimationClip _clip, bool _disposable)
	{
		if (_clip == null)
			return;

		float length = Mathf.Max(0.01f, _clip.length);
		if (_disposable)
		{
			AnimationUtility.SetAnimationEvents(_clip, new[]
			{
				new AnimationEvent { functionName = c_EventFire, time = 0f },
				new AnimationEvent { functionName = c_EventDiscard, time = length * 0.55f },
				new AnimationEvent { functionName = c_EventFinished, time = length * 0.96f }
			});
		}
		else
		{
			AnimationUtility.SetAnimationEvents(_clip, new[]
			{
				new AnimationEvent { functionName = c_EventFire, time = 0f },
				new AnimationEvent { functionName = c_EventFinished, time = length * 0.96f }
			});
		}

		EditorUtility.SetDirty(_clip);
	}

	private static void BakeReloadEvents(AnimationClip _clip)
	{
		if (_clip == null)
			return;

		float length = Mathf.Max(0.01f, _clip.length);
		AnimationUtility.SetAnimationEvents(_clip, new[]
		{
			new AnimationEvent { functionName = c_EventShowRocket, time = length * 0.22f },
			new AnimationEvent { functionName = c_EventInsertRocket, time = length * 0.62f },
			new AnimationEvent { functionName = c_EventFinished, time = length * 0.96f }
		});
		EditorUtility.SetDirty(_clip);
	}

	private static void EnsureAnyStateTransition(
		AnimatorStateMachine _sm,
		AnimatorState _dest,
		string _param,
		bool _isBool,
		string _kindParam,
		int _kindValue)
	{
		AnimatorStateTransition[] existing = _sm.anyStateTransitions;
		for (int i = 0; i < existing.Length; i++)
		{
			if (existing[i] != null && existing[i].destinationState == _dest)
			{
				existing[i].duration = 0.28f;
				existing[i].hasFixedDuration = true;
				existing[i].canTransitionToSelf = false;
				return;
			}
		}

		AnimatorStateTransition t = _sm.AddAnyStateTransition(_dest);
		if (_isBool)
			t.AddCondition(AnimatorConditionMode.If, 0f, _param);
		else
			t.AddCondition(AnimatorConditionMode.If, 0f, _param);

		t.AddCondition(AnimatorConditionMode.Equals, _kindValue, _kindParam);
		t.duration = 0.28f;
		t.hasExitTime = false;
		t.hasFixedDuration = true;
		t.canTransitionToSelf = false;
	}

	private static void EnsureExitToDefault(AnimatorStateMachine _sm, AnimatorState _from)
	{
		AnimatorState defaultState = _sm.defaultState;
		if (defaultState == null || defaultState == _from)
			return;

		AnimatorStateTransition[] transitions = _from.transitions;
		for (int i = 0; i < transitions.Length; i++)
		{
			if (transitions[i] != null && transitions[i].destinationState == defaultState)
			{
				SofteningExitTransition(transitions[i]);
				return;
			}
		}

		AnimatorStateTransition exit = _from.AddTransition(defaultState);
		SofteningExitTransition(exit);
	}

	private static void EnsureBoolExit(AnimatorStateMachine _sm, AnimatorState _from, string _boolParam, bool _value)
	{
		AnimatorState defaultState = _sm.defaultState;
		if (defaultState == null || defaultState == _from)
			return;

		AnimatorStateTransition[] transitions = _from.transitions;
		for (int i = 0; i < transitions.Length; i++)
		{
			if (transitions[i] != null && transitions[i].destinationState == defaultState)
			{
				SofteningBoolExitTransition(transitions[i], _boolParam, _value);
				return;
			}
		}

		AnimatorStateTransition exit = _from.AddTransition(defaultState);
		SofteningBoolExitTransition(exit, _boolParam, _value);
	}

	private static void SofteningExitTransition(AnimatorStateTransition _exit)
	{
		_exit.hasExitTime = true;
		_exit.exitTime = 0.88f;
		_exit.duration = 0.32f;
		_exit.hasFixedDuration = true;
	}

	private static void SofteningBoolExitTransition(AnimatorStateTransition _exit, string _boolParam, bool _value)
	{
		bool hasCondition = false;
		AnimatorCondition[] conditions = _exit.conditions;
		for (int i = 0; i < conditions.Length; i++)
		{
			if (conditions[i].parameter == _boolParam)
			{
				hasCondition = true;
				break;
			}
		}

		if (!hasCondition)
			_exit.AddCondition(_value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, _boolParam);

		_exit.hasExitTime = false;
		_exit.duration = 0.28f;
		_exit.hasFixedDuration = true;
	}

	private static AnimatorState EnsureMotionState(AnimatorStateMachine _sm, string _name, Motion _motion)
	{
		ChildAnimatorState[] states = _sm.states;
		for (int i = 0; i < states.Length; i++)
		{
			if (states[i].state != null && states[i].state.name == _name)
			{
				states[i].state.motion = _motion;
				return states[i].state;
			}
		}

		AnimatorState state = _sm.AddState(_name);
		state.motion = _motion;
		return state;
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

	private static int FindLayerIndex(AnimatorController _controller, string _layerName)
	{
		AnimatorControllerLayer[] layers = _controller.layers;
		for (int i = 0; i < layers.Length; i++)
		{
			if (layers[i].name == _layerName)
				return i;
		}

		return -1;
	}

	private static void SetLoopTime(AnimationClip _clip, bool _loop)
	{
		if (_clip == null)
			return;

		AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(_clip);
		settings.loopTime = _loop;
		AnimationUtility.SetAnimationClipSettings(_clip, settings);
		EditorUtility.SetDirty(_clip);
	}

	private static void EnsureFolder(string _path)
	{
		if (AssetDatabase.IsValidFolder(_path))
			return;

		string parent = Path.GetDirectoryName(_path)?.Replace('\\', '/');
		string name = Path.GetFileName(_path);
		if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
			EnsureFolder(parent);

		AssetDatabase.CreateFolder(parent, name);
	}
	#endregion
}
#endif

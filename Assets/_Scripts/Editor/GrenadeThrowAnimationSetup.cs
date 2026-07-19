#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Настраивает состояние GrenadeThrowStart на слое Aim_Point_U90-D90 и запекает animation events.
/// </summary>
[InitializeOnLoad]
public static class GrenadeThrowAnimationSetup
{
	#region Constants
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";
	private const string c_AimLayerName = "Aim_Point_U90-D90";
	private const string c_ClipPath = "Assets/Animations/Rifle/Stand/GrenadeThrowStart.anim";
	private const string c_ParamGrenadeThrow = UnitGrenadeThrowController.ParamGrenadeThrow;
	private const string c_StateName = "GrenadeThrowStart";

	private const string c_EventHideWeapon = "AnimationEvent_GrenadeHideWeapon";
	private const string c_EventShowInHand = "AnimationEvent_GrenadeShowInHand";
	private const string c_EventPinPullSound = "AnimationEvent_GrenadePinPullSound";
	private const string c_EventPinPull = "AnimationEvent_GrenadePinPull";
	private const string c_EventRelease = "AnimationEvent_GrenadeRelease";
	private const string c_EventShowWeapon = "AnimationEvent_GrenadeShowWeapon";

	private const float c_EventHideWeaponTime = 0.05f;
	private const float c_EventShowInHandTime = 0.18f;
	private const float c_EventPinPullSoundTime = 0.33f;
	private const float c_EventPinPullTime = 0.38f;
	private const float c_EventReleaseTime = 0.72f;
	private const float c_EventShowWeaponTime = 0.98f;
	#endregion

	#region Bootstrap
	static GrenadeThrowAnimationSetup()
	{
		EditorApplication.delayCall += TryAutoSetup;
	}
	#endregion

	#region Menu
	[MenuItem("Polygone/Animation/Setup Grenade Throw Layer")]
	public static void SetupGrenadeThrowLayer()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"[GrenadeThrowSetup] Animator Controller не найден: {c_ControllerPath}");
			return;
		}

		AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipPath);
		if (clip == null)
		{
			Debug.LogError($"[GrenadeThrowSetup] Анимация не найдена: {c_ClipPath}");
			return;
		}

		Undo.RecordObject(controller, "Setup Grenade Throw Layer");

		EnsureParameter(controller, c_ParamGrenadeThrow, AnimatorControllerParameterType.Trigger);

		BakeAnimationEvents(clip);
		SetLoopTime(clip, false);

		int layerIndex = FindLayerIndex(controller, c_AimLayerName);
		if (layerIndex < 0)
		{
			Debug.LogError($"[GrenadeThrowSetup] Слой '{c_AimLayerName}' не найден в Animator Controller.");
			return;
		}

		AnimatorControllerLayer layer = controller.layers[layerIndex];
		AnimatorStateMachine stateMachine = layer.stateMachine;

		AnimatorState throwState = EnsureMotionState(stateMachine, c_StateName, clip);

		EnsureGrenadeThrowTransition(stateMachine, throwState);

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log($"[GrenadeThrowSetup] Состояние '{c_StateName}' настроено на слое '{c_AimLayerName}', events запечены.");
	}

	[MenuItem("Polygone/Animation/Bake Grenade Throw Animation Events")]
	public static void BakeGrenadeThrowEvents()
	{
		AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipPath);
		if (clip == null)
		{
			Debug.LogError($"[GrenadeThrowSetup] Анимация не найдена: {c_ClipPath}");
			return;
		}

		BakeAnimationEvents(clip);
		SetLoopTime(clip, false);
		AssetDatabase.SaveAssets();
		Debug.Log("[GrenadeThrowSetup] Animation events запечены.");
	}
	#endregion

	#region Private Methods
	private static void TryAutoSetup()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
			return;

		int layerIndex = FindLayerIndex(controller, c_AimLayerName);
		if (layerIndex < 0)
			return;

		AnimatorControllerLayer layer = controller.layers[layerIndex];
		AnimatorStateMachine stateMachine = layer.stateMachine;

		bool hasState = false;
		ChildAnimatorState[] states = stateMachine.states;
		for (int i = 0; i < states.Length; i++)
		{
			if (states[i].state != null && states[i].state.name == c_StateName)
			{
				hasState = true;
				break;
			}
		}

		if (!hasState)
			SetupGrenadeThrowLayer();
	}

	private static void BakeAnimationEvents(AnimationClip _clip)
	{
		float length = _clip.length;

		AnimationEvent[] events = new AnimationEvent[6];
		events[0] = new AnimationEvent
		{
			functionName = c_EventHideWeapon,
			time = Mathf.Clamp(length * c_EventHideWeaponTime, 0f, length)
		};
		events[1] = new AnimationEvent
		{
			functionName = c_EventShowInHand,
			time = Mathf.Clamp(length * c_EventShowInHandTime, 0f, length)
		};
		events[2] = new AnimationEvent
		{
			functionName = c_EventPinPullSound,
			time = Mathf.Clamp(length * c_EventPinPullSoundTime, 0f, length)
		};
		events[3] = new AnimationEvent
		{
			functionName = c_EventPinPull,
			time = Mathf.Clamp(length * c_EventPinPullTime, 0f, length)
		};
		events[4] = new AnimationEvent
		{
			functionName = c_EventRelease,
			time = Mathf.Clamp(length * c_EventReleaseTime, 0f, length)
		};
		events[5] = new AnimationEvent
		{
			functionName = c_EventShowWeapon,
			time = Mathf.Clamp(length * c_EventShowWeaponTime, 0f, length)
		};

		AnimationUtility.SetAnimationEvents(_clip, events);
		EditorUtility.SetDirty(_clip);
	}

	private static void EnsureGrenadeThrowTransition(AnimatorStateMachine _stateMachine, AnimatorState _throwState)
	{
		ChildAnimatorState[] states = _stateMachine.states;
		AnimatorState defaultState = _stateMachine.defaultState;

		AnimatorStateTransition[] existingTransitions = _stateMachine.anyStateTransitions;
		for (int i = 0; i < existingTransitions.Length; i++)
		{
			if (existingTransitions[i] != null && existingTransitions[i].destinationState == _throwState)
				return;
		}

		AnimatorStateTransition anyToThrow = _stateMachine.AddAnyStateTransition(_throwState);
		anyToThrow.AddCondition(AnimatorConditionMode.If, 0f, c_ParamGrenadeThrow);
		anyToThrow.duration = 0.1f;
		anyToThrow.hasExitTime = false;
		anyToThrow.hasFixedDuration = true;
		anyToThrow.canTransitionToSelf = false;
		anyToThrow.interruptionSource = TransitionInterruptionSource.None;

		if (defaultState != null && defaultState != _throwState)
		{
			AnimatorStateTransition throwToDefault = _throwState.AddTransition(defaultState);
			throwToDefault.duration = 0.15f;
			throwToDefault.hasExitTime = true;
			throwToDefault.exitTime = 0.95f;
			throwToDefault.hasFixedDuration = true;
			throwToDefault.canTransitionToSelf = false;
		}
	}

	private static AnimatorState EnsureMotionState(AnimatorStateMachine _stateMachine, string _stateName, Motion _motion)
	{
		ChildAnimatorState[] states = _stateMachine.states;
		for (int i = 0; i < states.Length; i++)
		{
			if (states[i].state != null && states[i].state.name == _stateName)
			{
				states[i].state.motion = _motion;
				return states[i].state;
			}
		}

		AnimatorState state = _stateMachine.AddState(_stateName);
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
		AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(_clip);
		settings.loopTime = _loop;
		AnimationUtility.SetAnimationClipSettings(_clip, settings);
		EditorUtility.SetDirty(_clip);
	}
	#endregion
}
#endif

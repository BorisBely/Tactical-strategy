#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Настраивает слой рук для самостабилизации IFAK и базовые animation events на heal-клипах.
/// </summary>
[InitializeOnLoad]
public static class UnitAnimControllerSelfHealSetup
{
	#region Constants
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";
	private const string c_MedkitLayerName = UnitSelfStabilizationController.MedkitHandsLayerName;
	private const string c_SourceMaskLayerName = UnitMagazineLoadingController.MagazineLoadingHandsLayerName;
	private const string c_ParamIsSelfHealing = UnitSelfStabilizationController.ParamIsSelfHealing;

	private const string c_ClipHealStart = "Assets/healStart.anim";
	private const string c_ClipHeal = "Assets/heal.anim";
	private const string c_ClipHealEnd = "Assets/healEnd.anim";

	private const string c_StateEmpty = "SelfHeal_Empty";
	private const string c_StateStart = "healStart";
	private const string c_StateLoop = "heal";
	private const string c_StateEnd = "healEnd";
	#endregion

	#region Bootstrap
	static UnitAnimControllerSelfHealSetup()
	{
		EditorApplication.delayCall += TryAutoSetupSelfHealLayer;
	}
	#endregion

	#region Menu
	[MenuItem("Polygone/Animation/Setup Self Heal Layer")]
	public static void SetupSelfHealLayer()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"Не найден Animator Controller: {c_ControllerPath}");
			return;
		}

		Undo.RecordObject(controller, "Setup Self Heal Layer");
		EnsureParameter(controller, c_ParamIsSelfHealing, AnimatorControllerParameterType.Bool);

		AnimationClip healStart = LoadClip(c_ClipHealStart);
		AnimationClip heal = LoadClip(c_ClipHeal);
		AnimationClip healEnd = LoadClip(c_ClipHealEnd);
		if (healStart == null || heal == null || healEnd == null)
			return;

		EnsureEvents(healStart, "AnimationEvent_SelfHealShowMedkitInHand", 0.05f);
		EnsureEvents(heal, "AnimationEvent_SelfHealCycleCompleted", Mathf.Max(0.01f, heal.length - 0.05f));
		EnsureEvents(healEnd, "AnimationEvent_SelfHealHideMedkitFromHand", Mathf.Max(0.01f, healEnd.length - 0.05f));
		SetLoopTime(heal, true);
		SetLoopTime(healStart, false);
		SetLoopTime(healEnd, false);

		int layerIndex = EnsureLayer(controller);
		AnimatorControllerLayer layer = controller.layers[layerIndex];
		AnimatorStateMachine stateMachine = layer.stateMachine;
		AnimatorState empty = EnsureMotionState(stateMachine, c_StateEmpty, null);
		AnimatorState start = EnsureMotionState(stateMachine, c_StateStart, healStart);
		AnimatorState loop = EnsureMotionState(stateMachine, c_StateLoop, heal);
		AnimatorState end = EnsureMotionState(stateMachine, c_StateEnd, healEnd);

		stateMachine.defaultState = empty;
		RemoveTransitions(stateMachine);
		RemoveTransitions(empty);
		RemoveTransitions(start);
		RemoveTransitions(loop);
		RemoveTransitions(end);

		AnimatorStateTransition enter = empty.AddTransition(start);
		ConfigureTransition(enter, 0.05f, false, 0f);
		enter.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsSelfHealing);

		AnimatorStateTransition startToLoop = start.AddTransition(loop);
		ConfigureTransition(startToLoop, 0.08f, true, 0.95f);

		AnimatorStateTransition startAbortToEnd = start.AddTransition(end);
		ConfigureTransition(startAbortToEnd, 0.05f, false, 0f);
		startAbortToEnd.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsSelfHealing);

		AnimatorStateTransition loopToEnd = loop.AddTransition(end);
		ConfigureTransition(loopToEnd, 0.08f, false, 0f);
		loopToEnd.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsSelfHealing);

		AnimatorStateTransition endToEmpty = end.AddTransition(empty);
		ConfigureTransition(endToEmpty, 0.08f, true, 0.95f);

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[UnitAnimControllerSelfHealSetup] Medkit_Hands layer configured.");
	}
	#endregion

	#region Helpers
	private static void TryAutoSetupSelfHealLayer()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null || !NeedsSetup(controller))
			return;

		SetupSelfHealLayer();
	}

	private static bool NeedsSetup(AnimatorController _controller)
	{
		if (FindLayerIndex(_controller, c_MedkitLayerName) < 0)
			return true;

		bool hasParameter = false;
		for (int i = 0; i < _controller.parameters.Length; i++)
		{
			if (_controller.parameters[i].name == c_ParamIsSelfHealing)
			{
				hasParameter = true;
				break;
			}
		}

		if (!hasParameter)
			return true;

		if (UsesLegacyAnyStateSelfHealEntry(_controller))
			return true;

		AnimationClip healStart = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipHealStart);
		AnimationClip heal = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipHeal);
		AnimationClip healEnd = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipHealEnd);
		return !HasEvent(healStart, "AnimationEvent_SelfHealShowMedkitInHand") ||
		       !HasEvent(heal, "AnimationEvent_SelfHealCycleCompleted") ||
		       !HasEvent(healEnd, "AnimationEvent_SelfHealHideMedkitFromHand");
	}

	private static bool UsesLegacyAnyStateSelfHealEntry(AnimatorController _controller)
	{
		int layerIndex = FindLayerIndex(_controller, c_MedkitLayerName);
		if (layerIndex < 0)
			return false;

		AnimatorStateMachine stateMachine = _controller.layers[layerIndex].stateMachine;
		AnimatorStateTransition[] anyStateTransitions = stateMachine.anyStateTransitions;
		for (int i = 0; i < anyStateTransitions.Length; i++)
		{
			AnimatorStateTransition transition = anyStateTransitions[i];
			if (transition == null || transition.destinationState == null)
				continue;

			if (transition.destinationState.name == c_StateStart)
				return true;
		}

		return false;
	}

	private static int EnsureLayer(AnimatorController _controller)
	{
		int existing = FindLayerIndex(_controller, c_MedkitLayerName);
		if (existing < 0)
		{
			_controller.AddLayer(c_MedkitLayerName);
			existing = FindLayerIndex(_controller, c_MedkitLayerName);
		}

		AnimatorControllerLayer[] layers = _controller.layers;
		AnimatorControllerLayer layer = layers[existing];
		layer.name = c_MedkitLayerName;
		layer.defaultWeight = 0f;
		layer.blendingMode = AnimatorLayerBlendingMode.Override;

		int sourceMaskLayer = FindLayerIndex(_controller, c_SourceMaskLayerName);
		if (sourceMaskLayer >= 0)
			layer.avatarMask = layers[sourceMaskLayer].avatarMask;

		if (layer.stateMachine == null)
		{
			layer.stateMachine = new AnimatorStateMachine { name = c_MedkitLayerName };
			AssetDatabase.AddObjectToAsset(layer.stateMachine, _controller);
		}

		layers[existing] = layer;
		_controller.layers = layers;
		return existing;
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

	private static void EnsureParameter(
		AnimatorController _controller,
		string _name,
		AnimatorControllerParameterType _type)
	{
		for (int i = 0; i < _controller.parameters.Length; i++)
		{
			if (_controller.parameters[i].name == _name)
				return;
		}

		_controller.AddParameter(_name, _type);
	}

	private static AnimationClip LoadClip(string _path)
	{
		AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(_path);
		if (clip == null)
			Debug.LogError($"Не найден heal-клип: {_path}");

		return clip;
	}

	private static bool HasEvent(AnimationClip _clip, string _functionName)
	{
		if (_clip == null)
			return false;

		AnimationEvent[] events = AnimationUtility.GetAnimationEvents(_clip);
		for (int i = 0; i < events.Length; i++)
		{
			if (events[i].functionName == _functionName)
				return true;
		}

		return false;
	}

	private static void EnsureEvents(AnimationClip _clip, string _functionName, float _time)
	{
		AnimationEvent[] oldEvents = AnimationUtility.GetAnimationEvents(_clip);
		for (int i = 0; i < oldEvents.Length; i++)
		{
			if (oldEvents[i].functionName == _functionName)
				return;
		}

		AnimationEvent[] newEvents = new AnimationEvent[oldEvents.Length + 1];
		Array.Copy(oldEvents, newEvents, oldEvents.Length);
		newEvents[newEvents.Length - 1] = new AnimationEvent
		{
			functionName = _functionName,
			time = Mathf.Clamp(_time, 0f, Mathf.Max(0f, _clip.length))
		};
		AnimationUtility.SetAnimationEvents(_clip, newEvents);
		EditorUtility.SetDirty(_clip);
	}

	private static void SetLoopTime(AnimationClip _clip, bool _loop)
	{
		AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(_clip);
		settings.loopTime = _loop;
		AnimationUtility.SetAnimationClipSettings(_clip, settings);
		EditorUtility.SetDirty(_clip);
	}

	private static void RemoveTransitions(AnimatorStateMachine _stateMachine)
	{
		AnimatorStateTransition[] transitions = _stateMachine.anyStateTransitions;
		for (int i = transitions.Length - 1; i >= 0; i--)
			_stateMachine.RemoveAnyStateTransition(transitions[i]);
	}

	private static void RemoveTransitions(AnimatorState _state)
	{
		AnimatorStateTransition[] transitions = _state.transitions;
		for (int i = transitions.Length - 1; i >= 0; i--)
			_state.RemoveTransition(transitions[i]);
	}

	private static void ConfigureTransition(
		AnimatorStateTransition _transition,
		float _duration,
		bool _hasExitTime,
		float _exitTime)
	{
		_transition.duration = _duration;
		_transition.hasExitTime = _hasExitTime;
		_transition.exitTime = _exitTime;
		_transition.hasFixedDuration = true;
		_transition.canTransitionToSelf = false;
	}
	#endregion
}
#endif

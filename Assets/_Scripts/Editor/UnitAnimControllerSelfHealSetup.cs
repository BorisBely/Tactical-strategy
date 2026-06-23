#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Настраивает слой рук Medkit_Hands для самостабилизации IFAK и стабилизации другого юнита.
/// Оба сценария используют healStart/healEnd, различаются циклом: heal (self) / heal2 (other).
/// </summary>
[InitializeOnLoad]
public static class UnitAnimControllerSelfHealSetup
{
	#region Constants
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";
	private const string c_MedkitLayerName = UnitSelfStabilizationController.MedkitHandsLayerName;
	private const string c_SourceMaskLayerName = UnitMagazineLoadingController.MagazineLoadingHandsLayerName;
	private const string c_ParamIsSelfHealing = UnitSelfStabilizationController.ParamIsSelfHealing;
	private const string c_ParamIsStabilizingOther = UnitStabilizeOtherController.ParamIsStabilizingOther;
	private const string c_ParamIsCarryingFallen = UnitFiremanCarryController.ParamIsCarryingFallen;

	private const string c_ClipHealStart = "Assets/Animations/heal/healStart.anim";
	private const string c_ClipHeal = "Assets/Animations/heal/heal.anim";
	private const string c_ClipHeal2 = "Assets/Animations/heal/heal2.anim";
	private const string c_ClipHealEnd = "Assets/Animations/heal/healEnd.anim";
	private const string c_ClipFiremanCarry2 = "Assets/Animations/heal/Fireman'sCarry2.anim";

	private const string c_ParamIsBeingCarried = UnitFiremanCarryController.ParamIsBeingCarried;
	private const string c_CarriedPoseLayerName = UnitFiremanCarryController.CarriedPoseLayerName;
	private const string c_ClipFiremanCarry1 = "Assets/Fireman'sCarry1.anim";
	private const string c_StateCarriedEmpty = "Carried_Empty";
	private const string c_StateCarriedPose = "Fireman'sCarry1";

	private const string c_StateEmpty = "SelfHeal_Empty";
	private const string c_StateStart = "healStart";
	private const string c_StateLoop = "heal";
	private const string c_StateLoopOther = "heal2";
	private const string c_StateEnd = "healEnd";
	private const string c_StateCarry = "Fireman'sCarry2";

	private const string c_LegacyLayerName = "HealOther_Hands";
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

		RemoveLegacyHealOtherLayer(controller);

		EnsureParameter(controller, c_ParamIsSelfHealing, AnimatorControllerParameterType.Bool);
		EnsureParameter(controller, c_ParamIsStabilizingOther, AnimatorControllerParameterType.Bool);
		EnsureParameter(controller, c_ParamIsCarryingFallen, AnimatorControllerParameterType.Bool);

		AnimationClip healStart = LoadClip(c_ClipHealStart);
		AnimationClip heal = LoadClip(c_ClipHeal);
		AnimationClip heal2 = LoadClip(c_ClipHeal2);
		AnimationClip healEnd = LoadClip(c_ClipHealEnd);
		AnimationClip firemanCarry2 = LoadClip(c_ClipFiremanCarry2);
		if (healStart == null || heal == null || heal2 == null || healEnd == null || firemanCarry2 == null)
			return;

		EnsureEvents(healStart, "AnimationEvent_SelfHealShowMedkitInHand", 0.05f);
		EnsureEvents(healStart, "AnimationEvent_StabilizeOtherShowMedkitInHand", 0.08f);
		EnsureEvents(heal, "AnimationEvent_SelfHealCycleCompleted", Mathf.Max(0.01f, heal.length - 0.05f));
		EnsureEvents(heal2, "AnimationEvent_StabilizeOtherCycleCompleted", Mathf.Max(0.01f, heal2.length - 0.05f));
		EnsureEvents(healEnd, "AnimationEvent_SelfHealHideMedkitFromHand", Mathf.Max(0.01f, healEnd.length - 0.05f));
		EnsureEvents(healEnd, "AnimationEvent_StabilizeOtherHideMedkitFromHand", Mathf.Max(0.01f, healEnd.length - 0.03f));
		SetLoopTime(heal, true);
		SetLoopTime(heal2, true);
		SetLoopTime(firemanCarry2, true);
		SetLoopTime(healStart, false);
		SetLoopTime(healEnd, false);

		int layerIndex = EnsureLayer(controller);
		AnimatorControllerLayer layer = controller.layers[layerIndex];
		AnimatorStateMachine stateMachine = layer.stateMachine;
		AnimatorState empty = EnsureMotionState(stateMachine, c_StateEmpty, null);
		AnimatorState start = EnsureMotionState(stateMachine, c_StateStart, healStart);
		AnimatorState loop = EnsureMotionState(stateMachine, c_StateLoop, heal);
		AnimatorState loopOther = EnsureMotionState(stateMachine, c_StateLoopOther, heal2);
		AnimatorState end = EnsureMotionState(stateMachine, c_StateEnd, healEnd);
		AnimatorState carry = EnsureMotionState(stateMachine, c_StateCarry, firemanCarry2);

		stateMachine.defaultState = empty;
		RemoveTransitions(stateMachine);
		RemoveTransitions(empty);
		RemoveTransitions(start);
		RemoveTransitions(loop);
		RemoveTransitions(loopOther);
		RemoveTransitions(end);
		RemoveTransitions(carry);

		// Entry: SelfHeal_Empty → healStart (self-heal)
		AnimatorStateTransition enterSelf = empty.AddTransition(start);
		ConfigureTransition(enterSelf, 0.05f, false, 0f);
		enterSelf.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsSelfHealing);

		// Entry: SelfHeal_Empty → healStart (stabilize-other)
		AnimatorStateTransition enterOther = empty.AddTransition(start);
		ConfigureTransition(enterOther, 0.05f, false, 0f);
		enterOther.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsStabilizingOther);

		// Entry: SelfHeal_Empty → Fireman'sCarry2 (carry fallen)
		AnimatorStateTransition enterCarry = empty.AddTransition(carry);
		ConfigureTransition(enterCarry, 0.08f, false, 0f);
		enterCarry.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsCarryingFallen);

		// healStart → heal (self-heal loop)
		AnimatorStateTransition startToLoop = start.AddTransition(loop);
		ConfigureTransition(startToLoop, 0.08f, true, 0.95f);
		startToLoop.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsSelfHealing);

		// healStart → heal2 (stabilize-other loop)
		AnimatorStateTransition startToLoopOther = start.AddTransition(loopOther);
		ConfigureTransition(startToLoopOther, 0.08f, true, 0.95f);
		startToLoopOther.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsStabilizingOther);

		// healStart → healEnd (abort: both params false)
		AnimatorStateTransition startAbortToEnd = start.AddTransition(end);
		ConfigureTransition(startAbortToEnd, 0.05f, false, 0f);
		startAbortToEnd.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsSelfHealing);
		startAbortToEnd.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsStabilizingOther);

		// heal → healEnd (self-heal done)
		AnimatorStateTransition loopToEnd = loop.AddTransition(end);
		ConfigureTransition(loopToEnd, 0.08f, false, 0f);
		loopToEnd.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsSelfHealing);

		// heal2 → healEnd (stabilize-other done)
		AnimatorStateTransition loopOtherToEnd = loopOther.AddTransition(end);
		ConfigureTransition(loopOtherToEnd, 0.08f, false, 0f);
		loopOtherToEnd.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsStabilizingOther);

		// Fireman'sCarry2 → SelfHeal_Empty (release)
		AnimatorStateTransition carryToEmpty = carry.AddTransition(empty);
		ConfigureTransition(carryToEmpty, 0.08f, false, 0f);
		carryToEmpty.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsCarryingFallen);

		// healEnd → SelfHeal_Empty
		AnimatorStateTransition endToEmpty = end.AddTransition(empty);
		ConfigureTransition(endToEmpty, 0.08f, true, 0.95f);

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[UnitAnimControllerSelfHealSetup] Medkit_Hands layer configured (self-heal + stabilize-other + carry).");
	}

	[MenuItem("Polygone/Animation/Setup Carried Pose Layer")]
	public static void SetupCarriedPoseLayer()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"Не найден Animator Controller: {c_ControllerPath}");
			return;
		}

		Undo.RecordObject(controller, "Setup Carried Pose Layer");

		EnsureParameter(controller, c_ParamIsBeingCarried, AnimatorControllerParameterType.Bool);

		AnimationClip firemanCarry1 = LoadClip(c_ClipFiremanCarry1);
		if (firemanCarry1 == null)
			return;

		SetLoopTime(firemanCarry1, true);

		int layerIndex = EnsureCarriedPoseLayer(controller);
		AnimatorControllerLayer layer = controller.layers[layerIndex];
		AnimatorStateMachine stateMachine = layer.stateMachine;
		AnimatorState empty = EnsureMotionState(stateMachine, c_StateCarriedEmpty, null);
		AnimatorState pose = EnsureMotionState(stateMachine, c_StateCarriedPose, firemanCarry1);

		stateMachine.defaultState = empty;
		RemoveTransitions(stateMachine);
		RemoveTransitions(empty);
		RemoveTransitions(pose);

		AnimatorStateTransition enter = empty.AddTransition(pose);
		ConfigureTransition(enter, 0.08f, false, 0f);
		enter.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsBeingCarried);

		AnimatorStateTransition exit = pose.AddTransition(empty);
		ConfigureTransition(exit, 0.08f, false, 0f);
		exit.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsBeingCarried);

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[UnitAnimControllerSelfHealSetup] Carried_Pose layer configured.");
	}
	#endregion

	#region Helpers
	private static void TryAutoSetupSelfHealLayer()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
			return;

		if (NeedsSetup(controller))
			SetupSelfHealLayer();

		if (NeedsCarriedPoseSetup(controller))
			SetupCarriedPoseLayer();
	}

	private static bool NeedsSetup(AnimatorController _controller)
	{
		if (FindLayerIndex(_controller, c_MedkitLayerName) < 0)
			return true;

		bool hasSelfHealParam = false;
		bool hasStabilizeOtherParam = false;
		bool hasCarryingFallenParam = false;
		for (int i = 0; i < _controller.parameters.Length; i++)
		{
			if (_controller.parameters[i].name == c_ParamIsSelfHealing)
				hasSelfHealParam = true;
			if (_controller.parameters[i].name == c_ParamIsStabilizingOther)
				hasStabilizeOtherParam = true;
			if (_controller.parameters[i].name == c_ParamIsCarryingFallen)
				hasCarryingFallenParam = true;
		}

		if (!hasSelfHealParam || !hasStabilizeOtherParam || !hasCarryingFallenParam)
			return true;

		if (UsesLegacyAnyStateSelfHealEntry(_controller))
			return true;

		AnimationClip healStart = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipHealStart);
		AnimationClip heal = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipHeal);
		AnimationClip heal2 = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipHeal2);
		AnimationClip healEnd = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipHealEnd);
		return !HasEvent(healStart, "AnimationEvent_SelfHealShowMedkitInHand") ||
		       !HasEvent(healStart, "AnimationEvent_StabilizeOtherShowMedkitInHand") ||
		       !HasEvent(heal, "AnimationEvent_SelfHealCycleCompleted") ||
		       !HasEvent(heal2, "AnimationEvent_StabilizeOtherCycleCompleted") ||
		       !HasEvent(healEnd, "AnimationEvent_SelfHealHideMedkitFromHand") ||
		       !HasEvent(healEnd, "AnimationEvent_StabilizeOtherHideMedkitFromHand");
	}

	private static bool NeedsCarriedPoseSetup(AnimatorController _controller)
	{
		if (FindLayerIndex(_controller, c_CarriedPoseLayerName) < 0)
			return true;

		bool hasParam = false;
		for (int i = 0; i < _controller.parameters.Length; i++)
		{
			if (_controller.parameters[i].name == c_ParamIsBeingCarried)
			{
				hasParam = true;
				break;
			}
		}

		return !hasParam;
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

	private static void RemoveLegacyHealOtherLayer(AnimatorController _controller)
	{
		int index = FindLayerIndex(_controller, c_LegacyLayerName);
		if (index >= 0)
		{
			_controller.RemoveLayer(index);
			Debug.Log($"[UnitAnimControllerSelfHealSetup] Removed legacy layer '{c_LegacyLayerName}'.");
		}
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

	private static int EnsureCarriedPoseLayer(AnimatorController _controller)
	{
		int existing = FindLayerIndex(_controller, c_CarriedPoseLayerName);
		if (existing < 0)
		{
			_controller.AddLayer(c_CarriedPoseLayerName);
			existing = FindLayerIndex(_controller, c_CarriedPoseLayerName);
		}

		AnimatorControllerLayer[] layers = _controller.layers;
		AnimatorControllerLayer layer = layers[existing];
		layer.name = c_CarriedPoseLayerName;
		layer.defaultWeight = 0f;
		layer.blendingMode = AnimatorLayerBlendingMode.Override;
		layer.avatarMask = null;

		if (layer.stateMachine == null)
		{
			layer.stateMachine = new AnimatorStateMachine { name = c_CarriedPoseLayerName };
			AssetDatabase.AddObjectToAsset(layer.stateMachine, _controller);
		}

		layers[existing] = layer;
		_controller.layers = layers;
		return existing;
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

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Настраивает overlay-слой левой руки для оттаскивания сражённого юнита.
/// </summary>
[InitializeOnLoad]
public static class UnitAnimControllerDragLayerSetup
{
	#region Constants
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";
	private const string c_LeftArmMaskPath = "Assets/Animations/LeftArm.mask";
	private const string c_DragClipPath = "Assets/WalkDrag_Aim_B_Loop.anim";
	private const string c_DragLayerName = UnitFallenDragController.DragLeftHandLayerName;
	private const string c_ParamIsDraggingFallen = UnitFallenDragController.ParamIsDraggingFallen;
	private const string c_StateEmpty = "Drag_Empty";
	private const string c_StateLoop = "WalkDrag_Aim_B_Loop";
	#endregion

	#region Bootstrap
	static UnitAnimControllerDragLayerSetup()
	{
		EditorApplication.delayCall += TryAutoSetupDragLayer;
	}
	#endregion

	#region Menu
	[MenuItem("Polygone/Animation/Setup Drag Left Hand Layer")]
	public static void SetupDragLeftHandLayer()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"Не найден Animator Controller: {c_ControllerPath}");
			return;
		}

		AvatarMask leftArmMask = EnsureLeftArmMask();
		AnimationClip dragClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_DragClipPath);
		if (dragClip == null)
		{
			Debug.LogError($"Не найден drag-клип: {c_DragClipPath}");
			return;
		}

		Undo.RecordObject(controller, "Setup Drag Left Hand Layer");
		EnsureParameter(controller, c_ParamIsDraggingFallen, AnimatorControllerParameterType.Bool);
		SetLoopTime(dragClip, true);

		int layerIndex = EnsureLayer(controller, leftArmMask);
		AnimatorControllerLayer layer = controller.layers[layerIndex];
		AnimatorStateMachine stateMachine = layer.stateMachine;
		AnimatorState empty = EnsureMotionState(stateMachine, c_StateEmpty, null);
		AnimatorState loop = EnsureMotionState(stateMachine, c_StateLoop, dragClip);

		stateMachine.defaultState = empty;
		RemoveTransitions(stateMachine);
		RemoveTransitions(empty);
		RemoveTransitions(loop);

		AnimatorStateTransition enter = empty.AddTransition(loop);
		ConfigureTransition(enter, 0.08f, false, 0f);
		enter.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsDraggingFallen);

		AnimatorStateTransition exit = loop.AddTransition(empty);
		ConfigureTransition(exit, 0.08f, false, 0f);
		exit.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsDraggingFallen);

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[UnitAnimControllerDragLayerSetup] Drag_LeftHand layer configured.");
	}
	#endregion

	#region Helpers
	private static void TryAutoSetupDragLayer()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null || !NeedsSetup(controller))
			return;

		SetupDragLeftHandLayer();
	}

	private static bool NeedsSetup(AnimatorController _controller)
	{
		if (FindLayerIndex(_controller, c_DragLayerName) < 0)
			return true;

		bool hasParameter = false;
		for (int i = 0; i < _controller.parameters.Length; i++)
		{
			if (_controller.parameters[i].name == c_ParamIsDraggingFallen)
			{
				hasParameter = true;
				break;
			}
		}

		return !hasParameter;
	}

	private static AvatarMask EnsureLeftArmMask()
	{
		AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(c_LeftArmMaskPath);
		if (mask == null)
		{
			mask = new AvatarMask();
			AssetDatabase.CreateAsset(mask, c_LeftArmMaskPath);
		}

		for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
			mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);

		mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
		EditorUtility.SetDirty(mask);
		AssetDatabase.SaveAssets();
		return mask;
	}

	private static int EnsureLayer(AnimatorController _controller, AvatarMask _mask)
	{
		int existing = FindLayerIndex(_controller, c_DragLayerName);
		if (existing < 0)
		{
			_controller.AddLayer(c_DragLayerName);
			existing = FindLayerIndex(_controller, c_DragLayerName);
		}

		AnimatorControllerLayer[] layers = _controller.layers;
		AnimatorControllerLayer layer = layers[existing];
		layer.name = c_DragLayerName;
		layer.defaultWeight = 0f;
		layer.blendingMode = AnimatorLayerBlendingMode.Override;
		layer.avatarMask = _mask;

		if (layer.stateMachine == null)
		{
			layer.stateMachine = new AnimatorStateMachine { name = c_DragLayerName };
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

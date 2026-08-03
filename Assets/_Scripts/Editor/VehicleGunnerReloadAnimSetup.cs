#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Stand_Gunner_Reload в Carried_Pose + animation events (общая перезарядка турели M2/MK19).
/// </summary>
public static class VehicleGunnerReloadAnimSetup
{
	#region Constants
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";
	private const string c_ClipPath = "Assets/Animations/Vehicle/Stand_Gunner_Reload.anim";
	private const string c_CarriedPoseLayerName = UnitFiremanCarryController.CarriedPoseLayerName;
	private const string c_ParamIsVehicleGunner = UnitVehicleSeatPoseController.ParamIsVehicleGunner;
	private const string c_ParamIsGunnerReloadingM2 = VehicleTurretReloadController.ParamIsGunnerReloadingM2;
	private const string c_StateStandGunner = "Stand_Gunner";
	private const string c_StateStandGunnerCover = "Stand_Gunner_Cover";
	private const string c_StateStandGunnerReload = "Stand_Gunner_Reload";
	private const float c_ReloadClipFps = 30f;

	/// <summary>
	/// Тайминги @ 30 fps (Stand_Gunner_Reload, 520 кадров = 17.333 с).
	/// </summary>
	private static readonly (string function, float time)[] s_Events =
	{
		("AnimationEvent_TurretAttachMagToLeftHand", FrameTime(41)),
		("AnimationEvent_TurretDisableRightHandIk", FrameTime(60)),
		("AnimationEvent_TurretSwapEmptyForFullMag", FrameTime(60)),
		("AnimationEvent_TurretEnableRightHandIk", FrameTime(210)),
		("AnimationEvent_TurretReturnMagToWeapon", FrameTime(252)),
		("AnimationEvent_TurretEnableLeftHandIk", FrameTime(401)),
		("AnimationEvent_TurretHandToHandle", FrameTime(420)),
		("AnimationEvent_TurretHandleYankDown", FrameTime(429)),
		("AnimationEvent_TurretHandleFirstReturnUp", FrameTime(443)),
		("AnimationEvent_TurretHandleSecondYankDown", FrameTime(466)),
		("AnimationEvent_TurretHandleSecondReturnUp", FrameTime(475)),
		("AnimationEvent_TurretReleaseHandleIk", FrameTime(497)),
		("AnimationEvent_TurretEnableRightHandIk", FrameTime(519)),
		("AnimationEvent_TurretFinishReload", FrameTime(520)),
	};

	private static float FrameTime(int _frame) => _frame / c_ReloadClipFps;
	#endregion

	#region Menu
	[MenuItem("Polygone/Animation/Setup Vehicle Gunner Turret Reload")]
	public static void SetupGunnerTurretReload()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipPath);
		if (controller == null || clip == null)
		{
			Debug.LogError("[VehicleGunnerReloadAnimSetup] Controller or clip missing.");
			return;
		}

		Undo.RecordObject(controller, "Setup Gunner Turret Reload");
		EnsureBoolParam(controller, c_ParamIsGunnerReloadingM2);
		ApplyAnimationEvents(clip);

		int layerIndex = FindLayerIndex(controller, c_CarriedPoseLayerName);
		if (layerIndex < 0)
		{
			Debug.LogError("[VehicleGunnerReloadAnimSetup] Carried_Pose layer missing.");
			return;
		}

		AnimatorStateMachine sm = controller.layers[layerIndex].stateMachine;
		AnimatorState gunner = FindState(sm, c_StateStandGunner);
		AnimatorState cover = FindState(sm, c_StateStandGunnerCover);

		// Rename legacy M2 state if present.
		AnimatorState reload = FindState(sm, c_StateStandGunnerReload)
			?? FindState(sm, "Stand_Gunner_Reload_M2");
		if (reload != null)
			reload.name = c_StateStandGunnerReload;
		reload = EnsureMotionState(sm, c_StateStandGunnerReload, clip);

		if (gunner != null)
			EnsureReloadTransition(gunner, reload);
		if (cover != null)
			EnsureReloadTransition(cover, reload);

		EnsureExitReload(reload, gunner, cover);

		EditorUtility.SetDirty(controller);
		EditorUtility.SetDirty(clip);
		AssetDatabase.SaveAssets();
		Debug.Log("[VehicleGunnerReloadAnimSetup] Gunner turret reload setup complete.");
	}
	#endregion

	#region Events
	private static void ApplyAnimationEvents(AnimationClip _clip)
	{
		AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(_clip);
		settings.loopTime = false;
		settings.stopTime = FrameTime(520);
		AnimationUtility.SetAnimationClipSettings(_clip, settings);

		AnimationEvent[] events = new AnimationEvent[s_Events.Length];
		for (int i = 0; i < s_Events.Length; i++)
		{
			events[i] = new AnimationEvent
			{
				time = s_Events[i].time,
				functionName = s_Events[i].function
			};
		}

		AnimationUtility.SetAnimationEvents(_clip, events);
		EditorUtility.SetDirty(_clip);
	}
	#endregion

	#region Animator
	private static void EnsureReloadTransition(AnimatorState _from, AnimatorState _reload)
	{
		if (HasTransition(_from, _reload))
			return;

		AnimatorStateTransition t = _from.AddTransition(_reload);
		t.hasExitTime = false;
		t.duration = 0.08f;
		t.hasFixedDuration = true;
		t.canTransitionToSelf = false;
		t.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsGunnerReloadingM2);
		t.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsVehicleGunner);
	}

	private static void EnsureExitReload(AnimatorState _reload, AnimatorState _gunner, AnimatorState _cover)
	{
		if (_gunner != null && !HasTransition(_reload, _gunner))
		{
			AnimatorStateTransition toGunner = _reload.AddTransition(_gunner);
			toGunner.hasExitTime = true;
			toGunner.exitTime = 1f;
			toGunner.duration = 0.08f;
			toGunner.hasFixedDuration = true;
			toGunner.canTransitionToSelf = false;
			toGunner.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsGunnerReloadingM2);
			toGunner.AddCondition(AnimatorConditionMode.IfNot, 0f, UnitVehicleSeatPoseController.ParamIsGunnerCover);
			toGunner.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsVehicleGunner);
		}

		if (_cover != null && !HasTransition(_reload, _cover))
		{
			AnimatorStateTransition toCover = _reload.AddTransition(_cover);
			toCover.hasExitTime = true;
			toCover.exitTime = 1f;
			toCover.duration = 0.08f;
			toCover.hasFixedDuration = true;
			toCover.canTransitionToSelf = false;
			toCover.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsGunnerReloadingM2);
			toCover.AddCondition(AnimatorConditionMode.If, 0f, UnitVehicleSeatPoseController.ParamIsGunnerCover);
			toCover.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsVehicleGunner);
		}
	}
	#endregion

	#region Helpers
	private static bool HasParam(AnimatorController _controller, string _name)
	{
		AnimatorControllerParameter[] parameters = _controller.parameters;
		for (int i = 0; i < parameters.Length; i++)
		{
			if (parameters[i].name == _name)
				return true;
		}

		return false;
	}

	private static void EnsureBoolParam(AnimatorController _controller, string _name)
	{
		if (!HasParam(_controller, _name))
			_controller.AddParameter(_name, AnimatorControllerParameterType.Bool);
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

	private static AnimatorState FindState(AnimatorStateMachine _sm, string _name)
	{
		ChildAnimatorState[] states = _sm.states;
		for (int i = 0; i < states.Length; i++)
		{
			if (states[i].state != null && states[i].state.name == _name)
				return states[i].state;
		}

		return null;
	}

	private static AnimatorState EnsureMotionState(AnimatorStateMachine _sm, string _name, AnimationClip _clip)
	{
		AnimatorState state = FindState(_sm, _name);
		if (state == null)
			state = _sm.AddState(_name);

		state.motion = _clip;
		return state;
	}

	private static bool HasTransition(AnimatorState _from, AnimatorState _to)
	{
		AnimatorStateTransition[] transitions = _from.transitions;
		for (int i = 0; i < transitions.Length; i++)
		{
			if (transitions[i] != null && transitions[i].destinationState == _to)
				return true;
		}

		return false;
	}
	#endregion
}
#endif

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
	private const string c_ClipAbovePath = "Assets/Animations/Vehicle/Stand_Gunner_Reload_Above.anim";
	private const string c_ClipCoverPath = "Assets/Animations/Vehicle/Stand_Gunner_Reload.anim";
	private const string c_ClipAboveSourcePath = "Assets/Stand_Gunner_rel_MK19_copy.anim";
	private const string c_CarriedPoseLayerName = UnitFiremanCarryController.CarriedPoseLayerName;
	private const string c_ParamIsVehicleGunner = UnitVehicleSeatPoseController.ParamIsVehicleGunner;
	private const string c_ParamIsGunnerReloadingM2 = VehicleTurretReloadController.ParamIsGunnerReloadingM2;
	private const string c_StateStandGunner = "Stand_Gunner";
	private const string c_StateStandGunnerReload = "Stand_Gunner_Reload";
	private const string c_StateStandGunnerReloadAbove = "Stand_Gunner_Reload_Above";
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
		("AnimationEvent_TurretShowBelt", FrameTime(310)),
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
		AnimationClip clipAbove = LoadOrCopyAboveReloadClip();
		AnimationClip clipCover = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipCoverPath);
		if (controller == null)
		{
			Debug.LogError("[VehicleGunnerReloadAnimSetup] Controller missing.");
			return;
		}

		if (clipAbove == null)
		{
			Debug.LogError("[VehicleGunnerReloadAnimSetup] Above-shield reload clip not found.");
			return;
		}

		Undo.RecordObject(controller, "Setup Gunner Turret Reload");
		Undo.RecordObject(clipAbove, "Setup Above Reload Events");
		ApplyAnimationEvents(clipAbove);
		if (clipCover != null)
		{
			Undo.RecordObject(clipCover, "Setup Cover Reload Events");
			ApplyAnimationEvents(clipCover);
		}

		int layerIndex = FindLayerIndex(controller, c_CarriedPoseLayerName);
		if (layerIndex < 0)
		{
			Debug.LogError("[VehicleGunnerReloadAnimSetup] Carried_Pose layer missing.");
			return;
		}

		AnimatorStateMachine sm = controller.layers[layerIndex].stateMachine;
		AnimatorState gunner = FindState(sm, c_StateStandGunner);
		AnimatorState reload = FindState(sm, c_StateStandGunnerReload);

		if (gunner == null)
		{
			Debug.LogError("[VehicleGunnerReloadAnimSetup] Stand_Gunner state missing.");
			return;
		}

		// Remove Stand_Gunner → Stand_Gunner_Reload (no longer entered from above-shield).
		if (reload != null)
			RemoveTransition(gunner, reload);

		// Above-shield reload state + transitions.
		AnimatorState reloadAbove = EnsureMotionState(sm, c_StateStandGunnerReloadAbove, clipAbove);
		EnsureReloadTransition(gunner, reloadAbove);
		EnsureExitReloadAbove(reloadAbove, gunner);

		// Mutual transitions: switch reload pose without interrupting.
		if (reload != null)
		{
			EnsureCrossReloadTransition(reload, reloadAbove, _toCover: false);
			EnsureCrossReloadTransition(reloadAbove, reload, _toCover: true);
		}

		EditorUtility.SetDirty(controller);
		EditorUtility.SetDirty(clipAbove);
		if (clipCover != null)
			EditorUtility.SetDirty(clipCover);
		AssetDatabase.SaveAssets();
		Debug.Log("[VehicleGunnerReloadAnimSetup] Above-shield reload setup complete.");
	}
	private static AnimationClip LoadOrCopyAboveReloadClip()
	{
		AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipAbovePath);
		if (clip != null)
			return clip;

		AnimationClip source = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipAboveSourcePath);
		if (source == null)
		{
			Debug.LogWarning($"[VehicleGunnerReloadAnimSetup] Above-shield reload source clip not found at '{c_ClipAboveSourcePath}'.");
			return null;
		}

		if (!AssetDatabase.CopyAsset(c_ClipAboveSourcePath, c_ClipAbovePath))
		{
			Debug.LogError($"[VehicleGunnerReloadAnimSetup] Failed to copy '{c_ClipAboveSourcePath}' → '{c_ClipAbovePath}'.");
			return null;
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		return AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipAbovePath);
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

	private static void EnsureExitReloadAbove(AnimatorState _reloadAbove, AnimatorState _gunner)
	{
		if (_reloadAbove == null || _gunner == null)
			return;

		if (!HasTransition(_reloadAbove, _gunner))
		{
			AnimatorStateTransition toGunner = _reloadAbove.AddTransition(_gunner);
			toGunner.hasExitTime = true;
			toGunner.exitTime = 1f;
			toGunner.duration = 0.08f;
			toGunner.hasFixedDuration = true;
			toGunner.canTransitionToSelf = false;
			toGunner.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsGunnerReloadingM2);
			toGunner.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsVehicleGunner);
		}
	}

	private static void EnsureCrossReloadTransition(AnimatorState _from, AnimatorState _to, bool _toCover)
	{
		if (_from == null || _to == null)
			return;
		if (HasTransition(_from, _to))
			return;

		AnimatorStateTransition t = _from.AddTransition(_to);
		t.hasExitTime = false;
		t.duration = 0.12f;
		t.hasFixedDuration = true;
		t.canTransitionToSelf = false;
		t.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsGunnerReloadingM2);
		t.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsVehicleGunner);
		t.AddCondition(
			_toCover ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
			0f,
			UnitVehicleSeatPoseController.ParamIsGunnerCover);
	}
	#endregion

	#region Helpers
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

	private static void RemoveTransition(AnimatorState _from, AnimatorState _to)
	{
		if (_from == null || _to == null)
			return;

		AnimatorStateTransition[] transitions = _from.transitions;
		for (int i = 0; i < transitions.Length; i++)
		{
			if (transitions[i] != null && transitions[i].destinationState == _to)
			{
				_from.RemoveTransition(transitions[i]);
				return;
			}
		}
	}
	#endregion
}
#endif

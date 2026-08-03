#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Driving в Carried_Pose + слой Vehicle_Passenger_Hands:
/// Seat_relax ↔ Seat_Aim_L_Blend / Seat_Aim_R_Blend.
/// </summary>
[InitializeOnLoad]
public static class VehicleDrivingAnimSetup
{
	#region Constants
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";
	private const string c_ClipDriving = "Assets/Animations/Vehicle/Driving.anim";
	private const string c_ClipStandGunner = "Assets/Animations/Vehicle/Stand_Gunner.anim";
	private const string c_ClipStandGunnerCover = "Assets/Animations/Vehicle/Stand_Gunner_Cover.anim";
	private const string c_HandsMaskPath = "Assets/Animations/HandsMask.mask";
	private const string c_CarriedPoseLayerName = UnitFiremanCarryController.CarriedPoseLayerName;
	private const string c_PassengerHandsLayerName = UnitVehicleSeatPoseController.PassengerHandsLayerName;
	private const string c_ParamIsVehicleDriving = UnitVehicleSeatPoseController.ParamIsVehicleDriving;
	private const string c_ParamIsVehicleGunner = UnitVehicleSeatPoseController.ParamIsVehicleGunner;
	private const string c_ParamIsGunnerCover = UnitVehicleSeatPoseController.ParamIsGunnerCover;
	private const string c_ParamVehicleReady = UnitVehicleSeatPoseController.ParamVehicleReady;
	private const string c_ParamVehicleAimYaw = UnitVehicleSeatPoseController.ParamVehicleAimYaw;
	private const string c_ParamVehicleAimSide = UnitVehicleSeatPoseController.ParamVehicleAimSide;
	private const string c_ParamIsBeingCarried = UnitFiremanCarryController.ParamIsBeingCarried;
	private const string c_ParamIsStabilizedSleeping = UnitStabilizedUnconsciousPoseController.ParamIsStabilizedSleeping;
	private const string c_StateEmpty = "Carried_Empty";
	private const string c_StateCarry = "Fireman'sCarry1";
	private const string c_StateSleep = "LayingSleeping";
	private const string c_StateDriving = "Driving";
	private const string c_StateStandGunner = "Stand_Gunner";
	private const string c_StateStandGunnerCover = "Stand_Gunner_Cover";
	private const string c_StatePassengerEmpty = "PassengerHands_Empty";
	private const string c_StateSeatRelax = "Seat_relax";
	private const string c_StateSeatAimL = "Seat_Aim_L_Blend";
	private const string c_StateSeatAimR = "Seat_Aim_R_Blend";
	private const string c_MarkerPath = "Assets/.vehicle_driving_anim_setup_done";

	private static readonly string[] s_SeatAimLClips =
	{
		"Assets/Animations/Vehicle/Seat_aim_L-10.anim",
		"Assets/Animations/Vehicle/Seat_aim_L.anim",
		"Assets/Animations/Vehicle/Seat_aim_L+30.anim"
	};

	private static readonly float[] s_SeatAimLThresholds = { -10f, 0f, 30f };

	private static readonly string[] s_SeatAimRClips =
	{
		"Assets/Animations/Vehicle/Seat_aim_R-10.anim",
		"Assets/Animations/Vehicle/Seat_aim_R.anim",
		"Assets/Animations/Vehicle/Seat_aim_R+45.anim"
	};

	private static readonly float[] s_SeatAimRThresholds = { -10f, 0f, 45f };
	#endregion

	#region Bootstrap
	static VehicleDrivingAnimSetup()
	{
		EditorApplication.delayCall += EnsureSetupIfNeeded;
	}

	private static void EnsureSetupIfNeeded()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
			return;

		bool needsParams = !HasParam(controller, c_ParamVehicleReady)
		                || !HasParam(controller, c_ParamVehicleAimYaw);
		bool needsHands = FindLayerIndex(controller, c_PassengerHandsLayerName) < 0;
		bool needsGunnerCover = !HasParam(controller, c_ParamIsGunnerCover)
		                    || !HasGunnerCoverState(controller);
		bool needsMarker = !File.Exists(c_MarkerPath);

		if (needsParams || needsHands || needsMarker)
			SetupDrivingInCarriedPose();
		else if (needsGunnerCover)
			SetupGunnerCoverOnly();
	}
	#endregion

	#region Menu
	[MenuItem("Polygone/Animation/Setup Vehicle Driving Pose")]
	public static void SetupDrivingInCarriedPose()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"[VehicleDrivingAnimSetup] Controller not found: {c_ControllerPath}");
			return;
		}

		AnimationClip driving = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipDriving);
		if (driving == null)
		{
			Debug.LogError($"[VehicleDrivingAnimSetup] Clip not found: {c_ClipDriving}");
			return;
		}

		Undo.RecordObject(controller, "Setup Vehicle Driving Pose");
		EnsureBoolParam(controller, c_ParamIsVehicleDriving);
		SetLoopTime(driving, true);

		int layerIndex = FindLayerIndex(controller, c_CarriedPoseLayerName);
		if (layerIndex < 0)
		{
			Debug.LogError($"[VehicleDrivingAnimSetup] Layer '{c_CarriedPoseLayerName}' not found.");
			return;
		}

		AnimatorControllerLayer[] layers = controller.layers;
		AnimatorControllerLayer layer = layers[layerIndex];
		AnimatorStateMachine sm = layer.stateMachine;
		AnimatorState empty = FindState(sm, c_StateEmpty);
		AnimatorState carry = FindState(sm, c_StateCarry);
		AnimatorState sleep = FindState(sm, c_StateSleep);
		if (empty == null)
		{
			Debug.LogError("[VehicleDrivingAnimSetup] Carried_Empty state missing.");
			return;
		}

		AnimatorState drivingState = EnsureMotionState(sm, c_StateDriving, driving);

		EnsureTransition(empty, drivingState, c_ParamIsVehicleDriving, true,
			c_ParamIsBeingCarried, c_ParamIsStabilizedSleeping);
		EnsureExitDriving(drivingState, empty);

		if (carry != null)
			EnsureMutualExclusion(drivingState, carry, c_ParamIsBeingCarried);
		if (sleep != null)
			EnsureMutualExclusion(drivingState, sleep, c_ParamIsStabilizedSleeping);

		controller.layers = layers;
		EnsureCarriedPoseIkPass(controller);
		EnsureGunnerInCarriedPose(controller, sm, empty);
		EnsureGunnerCoverInCarriedPose(controller, sm);
		EnsurePassengerHandsLayer(controller);

		EditorUtility.SetDirty(controller);
		EditorUtility.SetDirty(driving);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		File.WriteAllText(c_MarkerPath, "driving+passenger_hands+blend_trees");
		Debug.Log("[VehicleDrivingAnimSetup] Setup complete: Driving, Gunner, Seat_relax + 2 blend trees.");
	}

	private static void SetupGunnerCoverOnly()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
			return;

		int layerIndex = FindLayerIndex(controller, c_CarriedPoseLayerName);
		if (layerIndex < 0)
			return;

		Undo.RecordObject(controller, "Setup Gunner Cover");
		AnimatorStateMachine sm = controller.layers[layerIndex].stateMachine;
		EnsureGunnerCoverInCarriedPose(controller, sm);

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		Debug.Log("[VehicleDrivingAnimSetup] Gunner Cover setup complete: Stand_Gunner_Cover state + IsGunnerCover param.");
	}
	#endregion

	#region Passenger Hands Layer
	private static void EnsurePassengerHandsLayer(AnimatorController _controller)
	{
		AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(c_HandsMaskPath);
		if (mask == null)
		{
			Debug.LogError($"[VehicleDrivingAnimSetup] Mask not found: {c_HandsMaskPath}");
			return;
		}

		AnimationClip seatRelax = LoadSeatAnimation("Seat_relax");
		if (seatRelax == null)
		{
			Debug.LogError("[VehicleDrivingAnimSetup] Seat_relax.anim not found.");
			return;
		}

		SetLoopTime(seatRelax, true);

		int existing = FindLayerIndex(_controller, c_PassengerHandsLayerName);
		if (existing < 0)
		{
			_controller.AddLayer(c_PassengerHandsLayerName);
			existing = FindLayerIndex(_controller, c_PassengerHandsLayerName);
		}

		if (existing < 0)
		{
			Debug.LogError("[VehicleDrivingAnimSetup] Failed to add Vehicle_Passenger_Hands layer.");
			return;
		}

		AnimatorControllerLayer[] layers = _controller.layers;
		AnimatorControllerLayer layer = layers[existing];
		layer.name = c_PassengerHandsLayerName;
		layer.defaultWeight = 0f;
		layer.blendingMode = AnimatorLayerBlendingMode.Override;
		layer.avatarMask = mask;
		layer.iKPass = true;

		if (layer.stateMachine == null)
		{
			layer.stateMachine = new AnimatorStateMachine { name = c_PassengerHandsLayerName };
			AssetDatabase.AddObjectToAsset(layer.stateMachine, _controller);
		}

		AnimatorStateMachine sm = layer.stateMachine;
		AnimatorState empty = EnsureEmptyState(sm, c_StatePassengerEmpty);
		sm.defaultState = empty;

		EnsureBoolParam(_controller, c_ParamVehicleReady);
		EnsureFloatParam(_controller, c_ParamVehicleAimYaw);
		EnsureIntParam(_controller, c_ParamVehicleAimSide);

		AnimatorState relaxState = EnsureMotionState(sm, c_StateSeatRelax, seatRelax);
		EditorUtility.SetDirty(seatRelax);

		BlendTree aimLBlend = EnsureBlendTree(sm, c_StateSeatAimL,
			s_SeatAimLClips, s_SeatAimLThresholds, c_ParamVehicleAimYaw);
		BlendTree aimRBlend = EnsureBlendTree(sm, c_StateSeatAimR,
			s_SeatAimRClips, s_SeatAimRThresholds, c_ParamVehicleAimYaw);
		_ = aimLBlend;
		_ = aimRBlend;

		// Transition: Seat_relax → Seat_Aim_L_Blend when VehicleReady && VehicleAimSide==0
		AnimatorState aimLState = FindState(sm, c_StateSeatAimL);
		AnimatorState aimRState = FindState(sm, c_StateSeatAimR);

		if (aimLState != null)
		{
			EnsureTransitionSide(relaxState, aimLState, c_ParamVehicleReady,
				c_ParamVehicleAimSide, 0);
			EnsureTransitionBool(aimLState, relaxState, c_ParamVehicleReady, false);
		}

		if (aimRState != null)
		{
			EnsureTransitionSide(relaxState, aimRState, c_ParamVehicleReady,
				c_ParamVehicleAimSide, 1);
			EnsureTransitionBool(aimRState, relaxState, c_ParamVehicleReady, false);
		}

		layers[existing] = layer;
		_controller.layers = layers;
	}
	#endregion

	#region Blend Tree Builder
	private static BlendTree EnsureBlendTree(
		AnimatorStateMachine _sm,
		string _name,
		string[] _clipPaths,
		float[] _thresholds,
		string _param)
	{
		AnimatorState state = FindState(_sm, _name);
		if (state == null)
			state = _sm.AddState(_name);

		if (state.motion is BlendTree existing)
		{
			existing.blendParameter = _param;
			return existing;
		}

		BlendTree tree = new BlendTree
		{
			name = _name,
			blendParameter = _param,
			blendType = BlendTreeType.Simple1D,
			useAutomaticThresholds = false
		};

		for (int i = 0; i < _clipPaths.Length; i++)
		{
			AnimationClip clip = LoadSeatAnimation(System.IO.Path.GetFileNameWithoutExtension(_clipPaths[i]));
			if (clip == null)
			{
				Debug.LogWarning($"[VehicleDrivingAnimSetup] Clip not found: {_clipPaths[i]}");
				continue;
			}

			SetLoopTime(clip, true);
			tree.AddChild(clip, _thresholds[i]);
		}

		if (tree.children.Length > 0)
		{
			AssetDatabase.AddObjectToAsset(tree, _sm);
			state.motion = tree;
		}

		return tree;
	}
	#endregion

	#region Gunner
	private static void EnsureCarriedPoseIkPass(AnimatorController _controller)
	{
		int layerIndex = FindLayerIndex(_controller, c_CarriedPoseLayerName);
		if (layerIndex < 0)
			return;

		AnimatorControllerLayer[] layers = _controller.layers;
		if (layers[layerIndex].iKPass)
			return;

		layers[layerIndex].iKPass = true;
		_controller.layers = layers;
	}

	private static void EnsureGunnerInCarriedPose(
		AnimatorController _controller,
		AnimatorStateMachine _sm,
		AnimatorState _empty)
	{
		AnimationClip standGunner = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipStandGunner);
		if (standGunner == null)
		{
			Debug.LogError($"[VehicleDrivingAnimSetup] Clip not found: {c_ClipStandGunner}");
			return;
		}

		EnsureBoolParam(_controller, c_ParamIsVehicleGunner);
		SetLoopTime(standGunner, true);

		AnimatorState gunnerState = EnsureMotionState(_sm, c_StateStandGunner, standGunner);

		AnimatorStateTransition t = _empty.AddTransition(gunnerState);
		t.hasExitTime = false;
		t.duration = 0.05f;
		t.hasFixedDuration = true;
		t.canTransitionToSelf = false;
		t.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsVehicleGunner);
		t.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsBeingCarried);
		t.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsStabilizedSleeping);
		t.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsVehicleDriving);

		AnimatorStateTransition exitT = gunnerState.AddTransition(_empty);
		exitT.hasExitTime = false;
		exitT.duration = 0.05f;
		exitT.hasFixedDuration = true;
		exitT.canTransitionToSelf = false;
		exitT.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsVehicleGunner);

		EditorUtility.SetDirty(standGunner);
	}

	private static void EnsureGunnerCoverInCarriedPose(
		AnimatorController _controller,
		AnimatorStateMachine _sm)
	{
		AnimationClip standGunnerCover = AssetDatabase.LoadAssetAtPath<AnimationClip>(c_ClipStandGunnerCover);
		if (standGunnerCover == null)
			return;

		EnsureBoolParam(_controller, c_ParamIsGunnerCover);
		SetLoopTime(standGunnerCover, true);

		AnimatorState normalState = FindState(_sm, c_StateStandGunner);
		AnimatorState emptyState = FindState(_sm, c_StateEmpty);
		if (normalState == null)
			return;

		AnimatorState coverState = EnsureMotionState(_sm, c_StateStandGunnerCover, standGunnerCover);

		if (!HasTransition(normalState, coverState))
		{
			AnimatorStateTransition toCover = normalState.AddTransition(coverState);
			toCover.hasExitTime = false;
			toCover.duration = 0.25f;
			toCover.hasFixedDuration = true;
			toCover.canTransitionToSelf = false;
			toCover.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsGunnerCover);
			toCover.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsVehicleGunner);
		}

		if (!HasTransition(coverState, normalState))
		{
			AnimatorStateTransition toNormal = coverState.AddTransition(normalState);
			toNormal.hasExitTime = false;
			toNormal.duration = 0.25f;
			toNormal.hasFixedDuration = true;
			toNormal.canTransitionToSelf = false;
			toNormal.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsGunnerCover);
			toNormal.AddCondition(AnimatorConditionMode.If, 0f, c_ParamIsVehicleGunner);
		}

		if (emptyState != null && !HasTransition(coverState, emptyState))
		{
			AnimatorStateTransition exitFromCover = coverState.AddTransition(emptyState);
			exitFromCover.hasExitTime = false;
			exitFromCover.duration = 0.05f;
			exitFromCover.hasFixedDuration = true;
			exitFromCover.canTransitionToSelf = false;
			exitFromCover.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsVehicleGunner);
		}

		EditorUtility.SetDirty(standGunnerCover);
	}

	private static bool HasGunnerCoverState(AnimatorController _controller)
	{
		int layerIndex = FindLayerIndex(_controller, c_CarriedPoseLayerName);
		if (layerIndex < 0)
			return false;
		AnimatorControllerLayer[] layers = _controller.layers;
		AnimatorStateMachine sm = layers[layerIndex].stateMachine;
		return FindState(sm, c_StateStandGunnerCover) != null;
	}
	#endregion

	#region Helpers — Load
	private static AnimationClip LoadSeatAnimation(string _nameWithoutExt)
	{
		string path = $"Assets/Animations/Vehicle/{_nameWithoutExt}.anim";
		AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
		if (clip == null)
		{
			string altPath = $"Assets/{_nameWithoutExt}.anim";
			clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(altPath);
		}

		return clip;
	}
	#endregion

	#region Helpers — Params
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

	private static void EnsureFloatParam(AnimatorController _controller, string _name)
	{
		if (!HasParam(_controller, _name))
			_controller.AddParameter(_name, AnimatorControllerParameterType.Float);
	}

	private static void EnsureIntParam(AnimatorController _controller, string _name)
	{
		if (!HasParam(_controller, _name))
			_controller.AddParameter(_name, AnimatorControllerParameterType.Int);
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

	private static AnimatorState EnsureMotionState(AnimatorStateMachine _sm, string _name, Motion _motion)
	{
		AnimatorState state = FindState(_sm, _name);
		if (state == null)
			state = _sm.AddState(_name);
		state.motion = _motion;
		return state;
	}

	private static AnimatorState EnsureEmptyState(AnimatorStateMachine _sm, string _name)
	{
		AnimatorState state = FindState(_sm, _name);
		if (state == null)
			state = _sm.AddState(_name);
		state.motion = null;
		return state;
	}
	#endregion

	#region Helpers — Transitions
	private static void EnsureTransition(
		AnimatorState _from,
		AnimatorState _to,
		string _param,
		bool _ifTrue,
		string _mustBeFalseA = null,
		string _mustBeFalseB = null)
	{
		if (HasTransition(_from, _to))
			return;

		AnimatorStateTransition t = _from.AddTransition(_to);
		t.hasExitTime = false;
		t.duration = 0.05f;
		t.hasFixedDuration = true;
		t.canTransitionToSelf = false;
		t.AddCondition(_ifTrue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, _param);
		if (!string.IsNullOrEmpty(_mustBeFalseA))
			t.AddCondition(AnimatorConditionMode.IfNot, 0f, _mustBeFalseA);
		if (!string.IsNullOrEmpty(_mustBeFalseB))
			t.AddCondition(AnimatorConditionMode.IfNot, 0f, _mustBeFalseB);
	}

	private static void EnsureTransitionBool(
		AnimatorState _from,
		AnimatorState _to,
		string _param,
		bool _ifTrue)
	{
		if (HasTransition(_from, _to))
			return;

		AnimatorStateTransition t = _from.AddTransition(_to);
		t.hasExitTime = false;
		t.duration = 0.08f;
		t.hasFixedDuration = true;
		t.canTransitionToSelf = false;
		t.AddCondition(
			_ifTrue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, _param);
	}

	private static void EnsureTransitionSide(
		AnimatorState _from,
		AnimatorState _to,
		string _readyParam,
		string _sideParam,
		int _sideValue)
	{
		if (HasTransition(_from, _to))
			return;

		AnimatorStateTransition t = _from.AddTransition(_to);
		t.hasExitTime = false;
		t.duration = 0.15f;
		t.hasFixedDuration = true;
		t.canTransitionToSelf = false;
		t.AddCondition(AnimatorConditionMode.If, 0f, _readyParam);
		t.AddCondition(AnimatorConditionMode.Equals, _sideValue, _sideParam);
	}

	private static bool HasTransition(AnimatorState _from, AnimatorState _to)
	{
		AnimatorStateTransition[] transitions = _from.transitions;
		for (int i = 0; i < transitions.Length; i++)
		{
			if (transitions[i].destinationState == _to)
				return true;
		}

		return false;
	}

	private static void EnsureExitDriving(AnimatorState _driving, AnimatorState _empty)
	{
		if (HasTransition(_driving, _empty))
			return;

		AnimatorStateTransition t = _driving.AddTransition(_empty);
		t.hasExitTime = false;
		t.duration = 0.05f;
		t.hasFixedDuration = true;
		t.canTransitionToSelf = false;
		t.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsVehicleDriving);
	}

	private static void EnsureMutualExclusion(AnimatorState _driving, AnimatorState _other, string _otherParam)
	{
		AnimatorStateTransition[] transitions = _other.transitions;
		bool hasNotDriving = false;
		for (int i = 0; i < transitions.Length; i++)
		{
			AnimatorCondition[] conditions = transitions[i].conditions;
			for (int c = 0; c < conditions.Length; c++)
			{
				if (conditions[c].parameter == c_ParamIsVehicleDriving &&
				    conditions[c].mode == AnimatorConditionMode.IfNot)
					hasNotDriving = true;
			}
		}

		_ = hasNotDriving;
		_ = _otherParam;
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

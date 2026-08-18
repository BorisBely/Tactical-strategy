#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Play uses the in-memory AnimatorController (open Animator window), not disk YAML.
/// Patch that live object on enter play so HipFire walk uses aim locomotion
/// (Walk_Aim_F_Loop / RifleCrouch_Move). NotReady AnyState is left on Walk_F_Loop / CrouchWalk_F_Loop.
/// </summary>
[InitializeOnLoad]
public static class UnitAnimControllerHipFireCrouchPatch
{
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";

	static UnitAnimControllerHipFireCrouchPatch()
	{
		EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
	}

	[InitializeOnEnterPlayMode]
	private static void OnEnterPlayMode(EnterPlayModeOptions _)
	{
		PatchAsset("enter-play");
	}

	private static void OnPlayModeStateChanged(PlayModeStateChange _state)
	{
		if (_state == PlayModeStateChange.ExitingEditMode)
			PatchAsset("exiting-edit");
	}

	[MenuItem("Tools/Polygone/Patch HipFire Crouch PingPong")]
	public static void PatchFromMenu()
	{
		PatchAsset("menu");
		AssetDatabase.SaveAssets();
	}

	public static void PatchAsset(string _reason)
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"[HipFireCrouchPatch] Controller not found: {c_ControllerPath}");
			return;
		}

		int patched = PatchController(controller);
		EditorUtility.SetDirty(controller);
		Debug.Log($"[HipFireCrouchPatch] {_reason} patched={patched}");
	}

	private static int PatchController(AnimatorController _controller)
	{
		if (_controller == null || _controller.layers.Length == 0)
			return 0;

		AnimatorStateMachine root = _controller.layers[0].stateMachine;
		AnimatorStateMachine crouch = FindMachine(root, UnitAnimatorWeaponMode.SubStateMachineRifleCrouch);
		AnimatorStateMachine standing = FindMachine(root, UnitAnimatorWeaponMode.SubStateMachineRifleStanding);
		if (root == null)
			return 0;

		int count = 0;
		if (crouch != null)
		{
			AnimatorState crouchWalk = FindState(crouch, "CrouchWalk_F_Loop");
			AnimatorState rifleMove = FindState(crouch, "RifleCrouch_Move");
			AnimatorState rifleIdle = FindState(crouch, "RifleCrouch_Idle");
			AnimatorState rifleIdleReady = FindState(crouch, "RifleCrouch_Idle_Ready");
			if (rifleMove != null)
			{
				count += PatchAnyStateReadyCrouchWalk(root, rifleMove, crouchWalk);
				count += RemoveHipFireWalk(rifleMove, crouchWalk);
				if (rifleIdle != null)
					count += RetargetHipFireWalk(rifleIdle, crouchWalk, rifleMove);
				if (rifleIdleReady != null)
					count += RetargetHipFireWalk(rifleIdleReady, crouchWalk, rifleMove);
			}
		}

		if (standing != null)
		{
			AnimatorState walkLoop = FindState(standing, "Walk_F_Loop");
			AnimatorState walkAim = FindState(standing, "Walk_Aim_F_Loop");
			AnimatorState standRelaxed = FindState(standing, "Stand_Relaxed_Idle");
			if (walkAim != null)
			{
				count += PatchAnyStateReadyStandWalk(root, walkLoop, walkAim);
				if (standRelaxed != null)
					count += RetargetHipFireWalk(standRelaxed, walkLoop, walkAim);
			}
		}

		return count;
	}

	private static int PatchAnyStateReadyCrouchWalk(
		AnimatorStateMachine _root,
		AnimatorState _rifleMove,
		AnimatorState _crouchWalk)
	{
		int count = 0;
		AnimatorStateTransition[] any = _root.anyStateTransitions;
		for (int i = 0; i < any.Length; i++)
		{
			AnimatorStateTransition t = any[i];
			if (t == null)
				continue;
			if (t.destinationState != _rifleMove && t.destinationState != _crouchWalk)
				continue;
			if (!HasIntEquals(t, UnitAnimatorWeaponMode.ParamStance, 1))
				continue;
			if (!HasFloatGreater(t, UnitClickToMove.ParamNavSpeed, 0.05f))
				continue;
			if (!HasBoolIf(t, UnitAnimatorWeaponMode.ParamWeaponReady))
				continue;

			t.destinationState = _rifleMove;
			t.mute = false;
			t.duration = 0.18f;
			t.hasExitTime = false;
			t.canTransitionToSelf = false;
			if (HasIntEquals(t, UnitAnimatorWeaponMode.ParamWeaponStandIdle, 1))
			{
				t.name = HasIntEquals(t, UnitAnimatorWeaponMode.ParamWeaponMode, 3)
					? "HipFire_RifleCrouch_Move_Pistol"
					: "HipFire_RifleCrouch_Move";
			}

			count++;
		}

		return count;
	}

	private static int PatchAnyStateReadyStandWalk(
		AnimatorStateMachine _root,
		AnimatorState _walkLoop,
		AnimatorState _walkAim)
	{
		if (_walkLoop == null || _walkAim == null)
			return 0;

		int count = 0;
		AnimatorStateTransition[] any = _root.anyStateTransitions;
		for (int i = 0; i < any.Length; i++)
		{
			AnimatorStateTransition t = any[i];
			if (t == null || t.destinationState != _walkLoop)
				continue;
			if (!HasBoolIf(t, UnitAnimatorWeaponMode.ParamWeaponReady))
				continue;
			if (!HasFloatGreater(t, UnitClickToMove.ParamNavSpeed, 0.05f))
				continue;
			if (!IsHipFireWalkTransition(t))
				continue;

			t.destinationState = _walkAim;
			t.mute = false;
			t.duration = 0.18f;
			t.hasExitTime = false;
			t.canTransitionToSelf = false;
			t.name = HasIntEquals(t, UnitAnimatorWeaponMode.ParamWeaponMode, 3)
				? "HipFire_WalkAim_Pistol"
				: "HipFire_WalkAim";
			count++;
		}

		return count;
	}

	private static int RemoveHipFireWalk(AnimatorState _from, AnimatorState _toWalk)
	{
		if (_from == null || _toWalk == null)
			return 0;

		int count = 0;
		bool removed;
		do
		{
			removed = false;
			AnimatorStateTransition[] transitions = _from.transitions;
			for (int i = 0; i < transitions.Length; i++)
			{
				AnimatorStateTransition t = transitions[i];
				if (t == null || t.destinationState != _toWalk)
					continue;
				if (!IsHipFireWalkTransition(t))
					continue;
				_from.RemoveTransition(t);
				removed = true;
				count++;
				break;
			}
		} while (removed);

		return count;
	}

	private static int RetargetHipFireWalk(AnimatorState _from, AnimatorState _fromWalk, AnimatorState _toWalk)
	{
		if (_from == null || _fromWalk == null || _toWalk == null)
			return 0;

		int count = 0;
		AnimatorStateTransition[] transitions = _from.transitions;
		for (int i = 0; i < transitions.Length; i++)
		{
			AnimatorStateTransition t = transitions[i];
			if (t == null || t.destinationState != _fromWalk)
				continue;
			if (!IsHipFireWalkTransition(t))
				continue;
			t.destinationState = _toWalk;
			t.duration = 0.22f;
			t.hasExitTime = false;
			count++;
		}

		return count;
	}

	private static bool IsHipFireWalkTransition(AnimatorStateTransition _transition)
	{
		return _transition != null
		       && HasBoolIf(_transition, UnitAnimatorWeaponMode.ParamWeaponReady)
		       && HasIntEquals(
			       _transition,
			       UnitAnimatorWeaponMode.ParamWeaponStandIdle,
			       (int)UnitAnimatorWeaponMode.WeaponStandIdleStyle.RelaxedIdle);
	}

	private static bool HasBoolIf(AnimatorStateTransition _transition, string _parameter)
	{
		AnimatorCondition[] conditions = _transition.conditions;
		for (int i = 0; i < conditions.Length; i++)
		{
			if (conditions[i].parameter == _parameter && conditions[i].mode == AnimatorConditionMode.If)
				return true;
		}

		return false;
	}

	private static bool HasIntEquals(AnimatorStateTransition _transition, string _parameter, int _value)
	{
		AnimatorCondition[] conditions = _transition.conditions;
		for (int i = 0; i < conditions.Length; i++)
		{
			if (conditions[i].parameter != _parameter)
				continue;
			if (conditions[i].mode != AnimatorConditionMode.Equals)
				continue;
			if (Mathf.Approximately(conditions[i].threshold, _value))
				return true;
		}

		return false;
	}

	private static bool HasFloatGreater(AnimatorStateTransition _transition, string _parameter, float _threshold)
	{
		AnimatorCondition[] conditions = _transition.conditions;
		for (int i = 0; i < conditions.Length; i++)
		{
			if (conditions[i].parameter != _parameter)
				continue;
			if (conditions[i].mode != AnimatorConditionMode.Greater)
				continue;
			if (conditions[i].threshold >= _threshold - 0.001f)
				return true;
		}

		return false;
	}

	private static AnimatorStateMachine FindMachine(AnimatorStateMachine _sm, string _name)
	{
		if (_sm.name == _name)
			return _sm;
		ChildAnimatorStateMachine[] nested = _sm.stateMachines;
		for (int i = 0; i < nested.Length; i++)
		{
			if (nested[i].stateMachine == null)
				continue;
			AnimatorStateMachine found = FindMachine(nested[i].stateMachine, _name);
			if (found != null)
				return found;
		}

		return null;
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
}
#endif

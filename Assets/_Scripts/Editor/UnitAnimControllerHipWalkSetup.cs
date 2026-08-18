#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Removes HipFire hand clips (Walk_F_Hip_Loop / CrouchWalk_Hip) from Aim_Point and base.
/// Hands during HipFire walk are posed by IK only.
/// </summary>
public static class UnitAnimControllerHipWalkSetup
{
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";
	private const string c_ObsoleteOverlayLayer = "Hip_Walk_Hands";
	private const string c_AimPointLayer = "Aim_Point_U90-D90";
	private const string c_WalkHip = "Walk_F_Hip_Loop";
	private const string c_CrouchHip = "CrouchWalk_Hip";
	private const string c_HoldStand = "HipFire_Hold_Stand";
	private const string c_HoldCrouch = "HipFire_Hold_Crouch";
	private const string c_WalkLoop = "Walk_F_Loop";
	private const string c_WalkAim = "Walk_Aim_F_Loop";
	private const string c_CrouchLoop = "CrouchWalk_F_Loop";
	private const string c_RifleCrouchMove = "RifleCrouch_Move";

	[MenuItem("Tools/Polygone/Setup Hip Walk States")]
	public static void SetupHipWalkStates()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"[HipWalk] Controller not found: {c_ControllerPath}");
			return;
		}

		Undo.RecordObject(controller, "Remove Hip Walk hand clips");
		RemoveObsoleteOverlayLayer(controller);

		AnimatorStateMachine standing = FindMachine(controller.layers[0].stateMachine, UnitAnimatorWeaponMode.SubStateMachineRifleStanding);
		AnimatorStateMachine crouch = FindMachine(controller.layers[0].stateMachine, UnitAnimatorWeaponMode.SubStateMachineRifleCrouch);
		int removed = 0;
		int retargeted = 0;
		int lengthened = 0;

		if (standing != null)
		{
			AnimatorState walkLoop = FindState(standing, c_WalkLoop);
			AnimatorState walkAim = FindState(standing, c_WalkAim);
			AnimatorState standRelaxed = FindState(standing, "Stand_Relaxed_Idle");
			AnimatorState baseWalkHip = FindState(standing, c_WalkHip);
			if (standRelaxed != null && walkLoop != null)
				retargeted += RetargetToLoop(standRelaxed, baseWalkHip, walkLoop);
			if (standRelaxed != null && walkAim != null)
			{
				retargeted += RetargetHipFireWalk(standRelaxed, walkLoop, walkAim);
				lengthened += LengthenIdleToWalk(standRelaxed, walkAim, 0.18f);
			}
			else if (standRelaxed != null && walkLoop != null)
				lengthened += LengthenIdleToWalk(standRelaxed, walkLoop, 0.18f);
			removed += RemoveNamedState(standing, c_WalkHip);
			removed += RemoveNamedState(standing, c_HoldStand);
		}

		if (crouch != null)
		{
			AnimatorState crouchLoop = FindState(crouch, c_CrouchLoop);
			AnimatorState rifleCrouchIdle = FindState(crouch, "RifleCrouch_Idle");
			AnimatorState rifleCrouchIdleReady = FindState(crouch, "RifleCrouch_Idle_Ready");
			AnimatorState rifleCrouchMove = FindState(crouch, c_RifleCrouchMove);
			AnimatorState baseCrouchHip = FindState(crouch, c_CrouchHip);
			if (rifleCrouchIdle != null && crouchLoop != null)
				retargeted += RetargetToLoop(rifleCrouchIdle, baseCrouchHip, crouchLoop);
			if (rifleCrouchIdle != null && rifleCrouchMove != null)
			{
				retargeted += RetargetHipFireWalk(rifleCrouchIdle, crouchLoop, rifleCrouchMove);
				lengthened += LengthenIdleToWalk(rifleCrouchIdle, rifleCrouchMove, 0.22f);
			}
			if (rifleCrouchIdleReady != null && rifleCrouchMove != null)
				lengthened += LengthenIdleToWalk(rifleCrouchIdleReady, rifleCrouchMove, 0.22f);
			removed += RemoveNamedState(crouch, c_CrouchHip);
			removed += RemoveNamedState(crouch, c_HoldCrouch);
		}

		int aimIndex = FindLayerIndex(controller, c_AimPointLayer);
		if (aimIndex >= 0)
		{
			AnimatorStateMachine aim = controller.layers[aimIndex].stateMachine;
			removed += RemoveNamedState(aim, c_WalkHip);
			removed += RemoveNamedState(aim, c_CrouchHip);
			removed += RemoveNamedState(aim, c_HoldStand);
			removed += RemoveNamedState(aim, c_HoldCrouch);
		}

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		Debug.Log($"[HipWalk] Removed hand clips. removed={removed} retargeted={retargeted} lengthened={lengthened}");
	}

	private static int RetargetToLoop(AnimatorState _fromIdle, AnimatorState _fromWalk, AnimatorState _toWalk)
	{
		if (_fromIdle == null || _toWalk == null || _fromWalk == null)
			return 0;

		int count = 0;
		AnimatorStateTransition[] transitions = _fromIdle.transitions;
		for (int i = 0; i < transitions.Length; i++)
		{
			AnimatorStateTransition t = transitions[i];
			if (t == null || t.destinationState != _fromWalk)
				continue;
			t.destinationState = _toWalk;
			count++;
		}

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
			t.offset = 0f;
			t.hasExitTime = false;
			t.exitTime = 0f;
			count++;
		}

		return count;
	}

	private static bool IsHipFireWalkTransition(AnimatorStateTransition _transition)
	{
		if (_transition == null)
			return false;

		bool weaponReady = false;
		bool relaxedIdle = false;
		AnimatorCondition[] conditions = _transition.conditions;
		for (int i = 0; i < conditions.Length; i++)
		{
			AnimatorCondition c = conditions[i];
			if (c.parameter == "WeaponReady" && c.mode == AnimatorConditionMode.If)
				weaponReady = true;
			if (c.parameter == UnitAnimatorWeaponMode.ParamWeaponStandIdle
			    && c.mode == AnimatorConditionMode.Equals
			    && Mathf.Approximately(c.threshold, (float)UnitAnimatorWeaponMode.WeaponStandIdleStyle.RelaxedIdle))
				relaxedIdle = true;
		}

		return weaponReady && relaxedIdle;
	}

	private static int LengthenIdleToWalk(AnimatorState _from, AnimatorState _toWalk, float _seconds)
	{
		if (_from == null || _toWalk == null)
			return 0;

		int count = 0;
		AnimatorStateTransition[] transitions = _from.transitions;
		for (int i = 0; i < transitions.Length; i++)
		{
			AnimatorStateTransition t = transitions[i];
			if (t == null || t.destinationState != _toWalk)
				continue;
			t.duration = _seconds;
			t.offset = 0f;
			t.hasExitTime = false;
			t.exitTime = 0f;
			count++;
		}

		return count;
	}

	private static int RemoveNamedState(AnimatorStateMachine _sm, string _name)
	{
		AnimatorState state = FindState(_sm, _name);
		if (state == null)
			return 0;
		_sm.RemoveState(state);
		return 1;
	}

	private static void RemoveObsoleteOverlayLayer(AnimatorController _controller)
	{
		int index = FindLayerIndex(_controller, c_ObsoleteOverlayLayer);
		if (index < 0)
			return;

		AnimatorStateMachine sm = _controller.layers[index].stateMachine;
		_controller.RemoveLayer(index);
		if (sm != null)
			Object.DestroyImmediate(sm, true);
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

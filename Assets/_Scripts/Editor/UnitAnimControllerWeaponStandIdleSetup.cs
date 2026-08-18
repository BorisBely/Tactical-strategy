#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Adds <see cref="UnitAnimatorWeaponMode.ParamWeaponStandIdle"/> and rewires idle transitions
/// (Stand_Aim_Idle ↔ Stand_Relaxed_Idle, RifleCrouch_Idle ↔ RifleCrouch_Idle_Ready)
/// off WeaponReady onto the new int param.
/// </summary>
public static class UnitAnimControllerWeaponStandIdleSetup
{
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";
	private const string c_ParamWeaponReady = "WeaponReady";
	private const string c_ParamWeaponStandIdle = UnitAnimatorWeaponMode.ParamWeaponStandIdle;
	private const string c_AimIdleState = "Stand_Aim_Idle";
	private const string c_RelaxedIdleState = "Stand_Relaxed_Idle";
	private const string c_CrouchAimIdleState = "RifleCrouch_Idle_Ready";
	private const string c_CrouchRelaxedIdleState = "RifleCrouch_Idle";

	[MenuItem("Tools/Polygone/Setup Weapon Stand Idle Param")]
	public static void SetupWeaponStandIdleParam()
	{
		var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"[WeaponStandIdle] Controller not found: {c_ControllerPath}");
			return;
		}

		Undo.RecordObject(controller, "Setup WeaponStandIdle");
		EnsureParameter(controller, c_ParamWeaponStandIdle, AnimatorControllerParameterType.Int);

		int patched = 0;
		for (int layerIndex = 0; layerIndex < controller.layers.Length; layerIndex++)
		{
			AnimatorStateMachine root = controller.layers[layerIndex].stateMachine;
			if (root != null)
				patched += PatchStateMachine(root);
		}

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		Debug.Log($"[WeaponStandIdle] Done. Patched {patched} idle transition(s) on {c_ControllerPath}.");
	}

	private static int PatchStateMachine(AnimatorStateMachine _sm)
	{
		int count = 0;

		for (int i = 0; i < _sm.states.Length; i++)
		{
			AnimatorState state = _sm.states[i].state;
			if (state == null)
				continue;

			AnimatorStateTransition[] outgoing = state.transitions;
			for (int t = 0; t < outgoing.Length; t++)
				count += TryPatchTransition(outgoing[t]);
		}

		for (int i = 0; i < _sm.anyStateTransitions.Length; i++)
		{
			if (_sm.anyStateTransitions[i] is AnimatorStateTransition ast)
				count += TryPatchTransition(ast);
		}

		for (int i = 0; i < _sm.stateMachines.Length; i++)
		{
			if (_sm.stateMachines[i].stateMachine != null)
				count += PatchStateMachine(_sm.stateMachines[i].stateMachine);
		}

		return count;
	}

	private static int TryPatchTransition(AnimatorStateTransition _transition)
	{
		if (_transition == null || _transition.destinationState == null)
			return 0;

		string dstName = _transition.destinationState.name;
		bool wantAim;
		bool isCrouchIdle;
		if (dstName == c_AimIdleState || dstName == c_CrouchAimIdleState)
		{
			wantAim = true;
			isCrouchIdle = dstName == c_CrouchAimIdleState;
		}
		else if (dstName == c_RelaxedIdleState || dstName == c_CrouchRelaxedIdleState)
		{
			wantAim = false;
			isCrouchIdle = dstName == c_CrouchRelaxedIdleState;
		}
		else
			return 0;

		// Standing: only idle-speed returns. Crouch also has a direct Idle ↔ Ready edge (no NavSpeed).
		if (!isCrouchIdle && !HasNavSpeedIdleCondition(_transition))
			return 0;

		return ReplaceWeaponReadyWithStandIdle(_transition, wantAim) ? 1 : 0;
	}

	private static bool HasNavSpeedIdleCondition(AnimatorStateTransition _transition)
	{
		AnimatorCondition[] conditions = _transition.conditions;
		for (int i = 0; i < conditions.Length; i++)
		{
			if (conditions[i].parameter != UnitClickToMove.ParamNavSpeed)
				continue;
			if (conditions[i].mode == AnimatorConditionMode.Less
			    || conditions[i].mode == AnimatorConditionMode.Equals)
				return true;
		}

		return false;
	}

	private static bool ReplaceWeaponReadyWithStandIdle(AnimatorStateTransition _transition, bool _wantAimIdle)
	{
		AnimatorCondition[] conditions = _transition.conditions;
		int weaponReadyIndex = -1;
		for (int i = 0; i < conditions.Length; i++)
		{
			if (conditions[i].parameter == c_ParamWeaponReady)
			{
				weaponReadyIndex = i;
				break;
			}
		}

		if (weaponReadyIndex < 0)
			return false;

		var next = new AnimatorCondition[conditions.Length];
		for (int i = 0; i < conditions.Length; i++)
		{
			if (i == weaponReadyIndex)
			{
				next[i] = new AnimatorCondition
				{
					mode = AnimatorConditionMode.Equals,
					parameter = c_ParamWeaponStandIdle,
					threshold = _wantAimIdle
						? (float)UnitAnimatorWeaponMode.WeaponStandIdleStyle.AimIdle
						: (float)UnitAnimatorWeaponMode.WeaponStandIdleStyle.RelaxedIdle,
				};
			}
			else
			{
				next[i] = conditions[i];
			}
		}

		_transition.conditions = next;
		return true;
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
}
#endif

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Добавляет в Base-слой AnimatorController стейт-пустышку CrouchWalk_Drag_NotReady
/// для анимации волочения с оружием не на готове.
/// Ищет CrouchWalk_F_Loop во ВСЕХ sub-state machine, копирует его входы/выходы.
/// Оригинальные переходы к CrouchWalk_F_Loop блокируются при IsDraggingNotReady=true.
/// </summary>
[InitializeOnLoad]
public static class UnitAnimControllerDragNotReadySetup
{
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";
	private const string c_ParamIsDraggingNotReady = "IsDraggingNotReady";
	private const string c_StateDragNotReady = "CrouchWalk_Drag_NotReady";
	private const string c_StateSourceTemplate = "CrouchWalk_F_Loop";

	static UnitAnimControllerDragNotReadySetup()
	{
		EditorApplication.delayCall += TryAutoSetup;
	}

	[MenuItem("Polygone/Animation/Setup Drag Not Ready Crouch Walk State")]
	public static void SetupDragNotReadyState()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"Animator Controller not found: {c_ControllerPath}");
			return;
		}

		Undo.RecordObject(controller, "Setup Drag Not Ready State");
		EnsureParameter(controller, c_ParamIsDraggingNotReady, AnimatorControllerParameterType.Bool);

		AnimatorControllerLayer baseLayer = controller.layers[0];
		AnimatorStateMachine rootMachine = baseLayer.stateMachine;

		// Ищем CrouchWalk_F_Loop во всех sub-state machine (рекурсивно)
		var found = FindStateInMachineRecursive(rootMachine, c_StateSourceTemplate);
		if (found.State == null)
		{
			Debug.LogError($"State '{c_StateSourceTemplate}' not found anywhere in base layer.");
			return;
		}

		AnimatorStateMachine parentMachine = found.ParentMachine;
		AnimatorState templateState = found.State;
		Debug.Log($"[DragNotReadySetup] Found '{c_StateSourceTemplate}' in '{parentMachine.name}'");

		// === ШАГ 1: блокируем все оригинальные входы в CrouchWalk_F_Loop ===
		int blocked = 0;
		blocked += BlockTransitionsToStateInMachine(parentMachine, templateState);
		blocked += BlockTransitionsToStateInMachine(rootMachine, templateState);
		Debug.Log($"[DragNotReadySetup] Blocked {blocked} original transitions into '{c_StateSourceTemplate}'");

		// === ШАГ 2: создаём Drag_NotReady в том же sub-state machine ===
		AnimatorState dragState = AddOrGetState(parentMachine, c_StateDragNotReady);

		// === ШАГ 3: входы в Drag_NotReady ===
		// Очищаем старые
		ClearAllTransitionsToTargetInMachine(parentMachine, dragState);
		ClearAllTransitionsToTargetInMachine(rootMachine, dragState);
		ClearAnyStateTransitionsTo(parentMachine, dragState);
		ClearAnyStateTransitionsTo(rootMachine, dragState);

		// Копируем входы из CrouchWalk_F_Loop → Drag_NotReady (из того же machine + root)
		int entriesCloned = 0;
		entriesCloned += CloneTransitionsToTargetInMachine(parentMachine, templateState, dragState, true);
		entriesCloned += CloneTransitionsToTargetInMachine(rootMachine, templateState, dragState, true);
		Debug.Log($"[DragNotReadySetup] Cloned {entriesCloned} entry transitions into '{c_StateDragNotReady}'");

		// === ШАГ 4: выходы из Drag_NotReady ===
		ClearAllTransitionsFrom(dragState);
		int exitsCloned = CloneOutgoingTransitions(templateState, dragState);
		Debug.Log($"[DragNotReadySetup] Cloned {exitsCloned} exit transitions from '{c_StateDragNotReady}'");

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log($"[DragNotReadySetup] Done. All CrouchWalk_F_Loop entries now guarded by IsDraggingNotReady=false; '{c_StateDragNotReady}' takes over when IsDraggingNotReady=true.");
	}

	#region Core: block original + clone to drag

	private static int BlockTransitionsToStateInMachine(AnimatorStateMachine _sm, AnimatorState _target)
	{
		int count = 0;

		// Из других стейтов
		ChildAnimatorState[] children = _sm.states;
		for (int i = 0; i < children.Length; i++)
		{
			AnimatorState from = children[i].state;
			if (from == null || from == _target) continue;
			AnimatorStateTransition[] trans = from.transitions;
			for (int j = 0; j < trans.Length; j++)
			{
				if (trans[j].destinationState == _target)
				{
					trans[j].AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsDraggingNotReady);
					count++;
				}
			}
		}

		// AnyState
		AnimatorStateTransition[] any = _sm.anyStateTransitions;
		for (int i = 0; i < any.Length; i++)
		{
			if (any[i].destinationState == _target)
			{
				any[i].AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsDraggingNotReady);
				count++;
			}
		}

		// Рекурсивно в sub-state machines
		ChildAnimatorStateMachine[] subMachines = _sm.stateMachines;
		for (int i = 0; i < subMachines.Length; i++)
		{
			if (subMachines[i].stateMachine != null)
				count += BlockTransitionsToStateInMachine(subMachines[i].stateMachine, _target);
		}

		return count;
	}

	/// <summary>Копирует все переходы ведущие к _source → теперь ведут к _dest, с условием IsDraggingNotReady.</summary>
	private static int CloneTransitionsToTargetInMachine(
		AnimatorStateMachine _sm,
		AnimatorState _source,
		AnimatorState _dest,
		bool _enterDrag)
	{
		int count = 0;

		ChildAnimatorState[] children = _sm.states;
		for (int i = 0; i < children.Length; i++)
		{
			AnimatorState from = children[i].state;
			if (from == null || from == _source || from == _dest) continue;
			AnimatorStateTransition[] trans = from.transitions;
			for (int j = 0; j < trans.Length; j++)
			{
				if (trans[j].destinationState == _source)
				{
					AnimatorStateTransition clone = from.AddTransition(_dest);
					CopyTransitionProps(clone, trans[j]);
					AddDragCondition(clone, _enterDrag);
					count++;
				}
			}
		}

		AnimatorStateTransition[] any = _sm.anyStateTransitions;
		for (int i = 0; i < any.Length; i++)
		{
			if (any[i].destinationState == _source)
			{
				AnimatorStateTransition clone = _sm.AddAnyStateTransition(_dest);
				CopyTransitionProps(clone, any[i]);
				AddDragCondition(clone, _enterDrag);
				count++;
			}
		}

		ChildAnimatorStateMachine[] subMachines = _sm.stateMachines;
		for (int i = 0; i < subMachines.Length; i++)
		{
			if (subMachines[i].stateMachine != null)
				count += CloneTransitionsToTargetInMachine(subMachines[i].stateMachine, _source, _dest, _enterDrag);
		}

		return count;
	}

	private static int CloneOutgoingTransitions(AnimatorState _source, AnimatorState _dest)
	{
		int count = 0;
		AnimatorStateTransition[] trans = _source.transitions;
		for (int i = 0; i < trans.Length; i++)
		{
			if (trans[i].destinationState == null || trans[i].destinationState == _dest) continue;

			AnimatorStateTransition clone = _dest.AddTransition(trans[i].destinationState);
			CopyTransitionProps(clone, trans[i]);
			clone.AddCondition(AnimatorConditionMode.IfNot, 0f, c_ParamIsDraggingNotReady);
			count++;
		}
		return count;
	}

	private static void CopyTransitionProps(AnimatorStateTransition _dst, AnimatorStateTransition _src)
	{
		_dst.duration = _src.duration;
		_dst.hasExitTime = _src.hasExitTime;
		_dst.exitTime = _src.exitTime;
		_dst.hasFixedDuration = _src.hasFixedDuration;
		_dst.canTransitionToSelf = false;
		_dst.offset = _src.offset;
		_dst.interruptionSource = _src.interruptionSource;
		foreach (AnimatorCondition c in _src.conditions)
			_dst.AddCondition(c.mode, c.threshold, c.parameter);
	}

	private static void AddDragCondition(AnimatorStateTransition _t, bool _enterDrag)
	{
		_t.AddCondition(
			_enterDrag ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
			0f,
			c_ParamIsDraggingNotReady);
	}

	#endregion

	#region Helpers

	private static void EnsureParameter(AnimatorController _c, string _n, AnimatorControllerParameterType _t)
	{
		for (int i = 0; i < _c.parameters.Length; i++)
			if (_c.parameters[i].name == _n) return;
		_c.AddParameter(_n, _t);
	}

	private static AnimatorState AddOrGetState(AnimatorStateMachine _sm, string _n)
	{
		var s = FindStateInMachine(_sm, _n);
		return s ?? _sm.AddState(_n);
	}

	private struct StateFindResult
	{
		public AnimatorState State;
		public AnimatorStateMachine ParentMachine;
	}

	private static StateFindResult FindStateInMachineRecursive(AnimatorStateMachine _sm, string _name)
	{
		// Прямые стейты
		ChildAnimatorState[] states = _sm.states;
		for (int i = 0; i < states.Length; i++)
			if (states[i].state?.name == _name)
				return new StateFindResult { State = states[i].state, ParentMachine = _sm };

		// Рекурсивно в sub-state machines
		ChildAnimatorStateMachine[] sub = _sm.stateMachines;
		for (int i = 0; i < sub.Length; i++)
		{
			if (sub[i].stateMachine == null) continue;
			var found = FindStateInMachineRecursive(sub[i].stateMachine, _name);
			if (found.State != null) return found;
		}

		return default;
	}

	private static AnimatorState FindStateInMachine(AnimatorStateMachine _sm, string _n)
	{
		var states = _sm.states;
		for (int i = 0; i < states.Length; i++)
			if (states[i].state?.name == _n) return states[i].state;
		return null;
	}

	private static void ClearAllTransitionsFrom(AnimatorState _s)
	{
		if (_s == null) return;
		var t = _s.transitions;
		for (int i = t.Length - 1; i >= 0; i--) _s.RemoveTransition(t[i]);
	}

	private static void ClearAllTransitionsToTargetInMachine(AnimatorStateMachine _sm, AnimatorState _target)
	{
		var children = _sm.states;
		for (int i = 0; i < children.Length; i++)
		{
			AnimatorState from = children[i].state;
			if (from == null) continue;
			var t = from.transitions;
			for (int j = t.Length - 1; j >= 0; j--)
				if (t[j].destinationState == _target) from.RemoveTransition(t[j]);
		}

		var sub = _sm.stateMachines;
		for (int i = 0; i < sub.Length; i++)
			if (sub[i].stateMachine != null)
				ClearAllTransitionsToTargetInMachine(sub[i].stateMachine, _target);
	}

	private static void ClearAnyStateTransitionsTo(AnimatorStateMachine _sm, AnimatorState _d)
	{
		var t = _sm.anyStateTransitions;
		for (int i = t.Length - 1; i >= 0; i--)
			if (t[i].destinationState == _d) _sm.RemoveAnyStateTransition(t[i]);

		var sub = _sm.stateMachines;
		for (int i = 0; i < sub.Length; i++)
			if (sub[i].stateMachine != null)
				ClearAnyStateTransitionsTo(sub[i].stateMachine, _d);
	}

	#endregion

	private static void TryAutoSetup()
	{
		var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (c == null) return;
		for (int i = 0; i < c.parameters.Length; i++)
			if (c.parameters[i].name == c_ParamIsDraggingNotReady) return;
		SetupDragNotReadyState();
	}
}
#endif

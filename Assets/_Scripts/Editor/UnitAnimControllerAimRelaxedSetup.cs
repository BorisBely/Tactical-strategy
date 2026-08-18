#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Одноразовый автозапуск настройки Animator после recompilation/import в Unity.
/// Нужен, чтобы изменения были созданы через Unity API, а не только YAML-правкой.
/// </summary>
[InitializeOnLoad]
internal static class UnitAnimControllerAimRelaxedSetupBootstrap
{
	private const string c_MarkerPath = "Assets/.unit_anim_controller_setup_marker";

	static UnitAnimControllerAimRelaxedSetupBootstrap()
	{
		EditorApplication.delayCall += TryRunFromMarker;
	}

	private static void TryRunFromMarker()
	{
		if (!File.Exists(c_MarkerPath))
			return;

		try
		{
			File.Delete(c_MarkerPath);
			File.Delete(c_MarkerPath + ".meta");
			UnitAnimControllerAimRelaxedSetup.SetupAimLayerRelaxedReloadGraph();
		}
		catch (System.Exception exception)
		{
			Debug.LogError($"[UnitAnimControllerAimRelaxedSetup] Auto-run failed: {exception}");
		}
	}
}

/// <summary>
/// Настраивает граф relaxed-перезарядки на слое Aim_Point_U90-D90 через Unity API
/// (правки YAML Unity часто не показывает, пока Animator открыт).
/// </summary>
public static class UnitAnimControllerAimRelaxedSetup
{
	private const string c_ControllerPath = "Assets/Animations/UnitAnimController.controller";
	private const string c_AimLayerName = "Aim_Point_U90-D90";

	private const string c_PitchBlend = "Stand_Aim_Pitch_Blend";
	private const string c_CrouchPitchBlend = "Crouch_Aim_Pitch_Blend";
	private const string c_AimReload = "Stand_Aim_Reload";
	private const string c_AimBolt = "Stand_CyclingBolt";
	private const string c_AimBoltAk = "Stand_CyclingBolt_AK";
	private const string c_AimBoltAction = "Stand_Aim_BoltCycle";
	private const string c_RifleCrouchIdle = "RifleCrouch_Idle";
	private const string c_RifleCrouchIdleReady = "RifleCrouch_Idle_Ready";
	private const string c_RelaxedIdle = "Stand_Relaxed_Idle";
	private const string c_RelaxedReload = "Stand_Relaxed_Reload";
	private const string c_RelaxedBolt = "Stand_Relaxed__CyclingBolt";
	private const string c_RelaxedBoltAk = "Stand_Relaxed__CyclingBolt_AK";
	private const string c_RelaxedBoltAction = "Stand_Relaxed_BoltCycle";
	private const string c_AimLmgReload = "Stand_Reload_LMG";
	private const string c_RelaxedLmgReload = "Stand_Relaxed_Reload_LMG";

	private const string c_ClipRelaxedIdle = "Assets/Animations/Rifle/Stand/Stand_Relaxed_Rifle_Idle.anim";
	private const string c_ClipRelaxedReload = "Assets/Animations/Rifle/Stand/Stand_Relaxed_Reload.anim";
	private const string c_ClipRelaxedBolt = "Assets/Animations/Rifle/Stand/Stand_Relaxed__CyclingBolt.anim";
	private const string c_ClipRelaxedBoltAk = "Assets/Animations/Rifle/Stand/Stand_Relaxed__CyclingBolt_AK.anim";
	private const string c_ClipAimBoltAk = "Assets/Animations/Rifle/Stand/Stand_CyclingBolt_AK.anim";
	private const string c_ClipAimBoltAction = "Assets/Animations/Rifle/Stand/Stand_Aim_BoltCycle.anim";
	private const string c_ClipRelaxedBoltAction = "Assets/Animations/Rifle/Stand/Stand_Relaxed_BoltCycle.anim";
	private const string c_ClipRifleCrouchIdleLegacy = "Assets/Animations/Rifle/Crouch/Crouch_Idle_LegacyRifle.anim";
	private const string c_ClipCrouchAimLegacyPitch = "Assets/Animations/Rifle/Crouch/Crouch_Aim_Idle_LegacyPitch.anim";
	private const string c_ClipLmgReload = "Assets/Animations/Rifle/Stand/Stand_Reload_LMG.anim";

	private const string c_EventBoltActionCycleSoundStarted = "AnimationEvent_BoltActionCycleSoundStarted";
	private const string c_EventFinishWeaponReload = "AnimationEvent_FinishWeaponReload";
	private const float c_BoltCycleFinishEventTime = 0.6666667f;

	private const string c_EventLmgCoverOpenStarted = "AnimationEvent_LmgCoverOpenStarted";
	private const string c_EventLmgBeltInserted = "AnimationEvent_LmgBeltInserted";
	private const string c_EventLmgCoverCloseStarted = "AnimationEvent_LmgCoverCloseStarted";

	private const string c_ParamWeaponReady = "WeaponReady";
	private const string c_ParamIsReloading = "IsReloadingWeapon";
	private const string c_ParamIsCyclingBolt = "IsCyclingBolt";
	private const string c_ParamIsLoadingLmgBelt = "IsLoadingLmgBelt";
	private const string c_ParamStance = "Stance";

	[MenuItem("Tools/Polygone/Setup Aim Layer Relaxed Reload")]
	public static void SetupAimLayerRelaxedReloadGraph()
	{
		CloseAnimatorWindowsForController(c_ControllerPath);
		ImportRequiredAssets();

		var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"Не найден Animator Controller: {c_ControllerPath}");
			return;
		}

		AnimatorControllerLayer aimLayer = FindLayer(controller, c_AimLayerName);
		if (aimLayer.stateMachine == null)
		{
			Debug.LogError($"Слой «{c_AimLayerName}» не найден в {c_ControllerPath}");
			return;
		}

		Undo.RecordObject(controller, "Setup Aim Layer Relaxed Reload");

		EnsureParameter(controller, c_ParamWeaponReady, AnimatorControllerParameterType.Bool);
		EnsureParameter(controller, c_ParamIsReloading, AnimatorControllerParameterType.Bool);
		EnsureParameter(controller, c_ParamIsCyclingBolt, AnimatorControllerParameterType.Bool);
		EnsureParameter(controller, c_ParamIsLoadingLmgBelt, AnimatorControllerParameterType.Bool);
		EnsureParameter(controller, c_ParamStance, AnimatorControllerParameterType.Int);

		AnimatorStateMachine sm = aimLayer.stateMachine;

		AnimatorState pitchBlend = RequireState(sm, c_PitchBlend);
		AnimatorState crouchPitch = RequireState(sm, c_CrouchPitchBlend);
		AnimatorState aimReload = RequireState(sm, c_AimReload);
		AnimatorState aimBolt = RequireState(sm, c_AimBolt);

		AnimationClip relaxedIdleClip = LoadClip(c_ClipRelaxedIdle);
		AnimationClip relaxedReloadClip = LoadClip(c_ClipRelaxedReload);
		AnimationClip relaxedBoltClip = LoadClip(c_ClipRelaxedBolt);
		AnimationClip rifleCrouchIdleLegacyClip = LoadClip(c_ClipRifleCrouchIdleLegacy);
		AnimationClip crouchAimLegacyPitchClip = LoadClip(c_ClipCrouchAimLegacyPitch);

		EnsureStateMotion(controller, c_RifleCrouchIdle, rifleCrouchIdleLegacyClip);
		EnsureStateMotion(controller, c_RifleCrouchIdleReady, crouchAimLegacyPitchClip);

		AnimatorState relaxedIdle = EnsureMotionState(sm, c_RelaxedIdle, relaxedIdleClip);
		AnimatorState relaxedReload = EnsureMotionState(sm, c_RelaxedReload, relaxedReloadClip);
		AnimatorState relaxedBolt = EnsureMotionState(sm, c_RelaxedBolt, relaxedBoltClip);

		RemoveDuplicateNamedStates(sm, c_RelaxedIdle, relaxedIdle);
		RemoveDuplicateNamedStates(sm, c_RelaxedReload, relaxedReload);
		RemoveDuplicateNamedStates(sm, c_RelaxedBolt, relaxedBolt);
		RepointTransitionsByDestinationName(sm, c_RelaxedIdle, relaxedIdle);

		RemoveTransition(pitchBlend, relaxedReload);
		RemoveTransition(pitchBlend, relaxedBolt);
		RemoveTransition(pitchBlend, relaxedIdle);
		RemoveTransition(crouchPitch, relaxedReload);

		RemovePitchExitWithoutWeaponReady(relaxedReload, pitchBlend);
		RemovePitchExitWithoutWeaponReady(relaxedBolt, pitchBlend);

		EnsureTransition(pitchBlend, aimReload, 0.12f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(pitchBlend, relaxedIdle, 0.15f,
			CondIfNot(c_ParamWeaponReady),
			CondEquals(c_ParamStance, 0f));

		EnsureTransition(pitchBlend, aimBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsReloading),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(crouchPitch, aimReload, 0.12f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(crouchPitch, relaxedReload, 0.12f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(crouchPitch, aimBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsReloading),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(crouchPitch, relaxedBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedIdle, relaxedReload, 0.18f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedIdle, relaxedBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedIdle, pitchBlend, 0.15f, CondIf(c_ParamWeaponReady));
		EnsureTransition(relaxedIdle, crouchPitch, 0.12f, CondEquals(c_ParamStance, 1f));

		EnsureTransition(relaxedReload, relaxedBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsReloading));

		EnsureTransition(relaxedReload, relaxedBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIf(c_ParamIsReloading));

		EnsureTransition(relaxedReload, pitchBlend, 0.15f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 0f),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(relaxedReload, pitchBlend, 0.15f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 2f),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(relaxedReload, crouchPitch, 0.15f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 1f));

		EnsureTransition(relaxedReload, relaxedIdle, 0.15f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 0f),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedReload, relaxedIdle, 0.15f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 2f),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedBolt, relaxedReload, 0.1f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedBolt, pitchBlend, 0.12f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 0f),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(relaxedBolt, pitchBlend, 0.12f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 2f),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(relaxedBolt, crouchPitch, 0.12f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 1f));

		EnsureTransition(relaxedBolt, relaxedIdle, 0.12f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 0f),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(relaxedBolt, relaxedIdle, 0.12f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 2f),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(aimReload, aimBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsReloading),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(aimReload, aimBolt, 0.1f,
			CondIf(c_ParamIsCyclingBolt),
			CondIf(c_ParamIsReloading),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(aimBolt, aimReload, 0.1f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIf(c_ParamWeaponReady));

		EnsureTransition(aimBolt, relaxedReload, 0.1f,
			CondIf(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamWeaponReady));

		EnsureTransition(aimReload, pitchBlend, 0.22f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsLoadingLmgBelt),
			CondEquals(c_ParamStance, 0f));
		EnsureTransition(aimReload, pitchBlend, 0.22f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsLoadingLmgBelt),
			CondEquals(c_ParamStance, 2f));
		EnsureTransition(aimReload, crouchPitch, 0.22f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondIfNot(c_ParamIsLoadingLmgBelt),
			CondEquals(c_ParamStance, 1f));

		EnsureTransition(aimBolt, pitchBlend, 0.22f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 0f));
		EnsureTransition(aimBolt, pitchBlend, 0.22f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 2f));
		EnsureTransition(aimBolt, crouchPitch, 0.22f,
			CondIfNot(c_ParamIsReloading),
			CondIfNot(c_ParamIsCyclingBolt),
			CondEquals(c_ParamStance, 1f));

		// Болтовые винтовки: отдельные клипы (вход через Animator.Play из ReloadController).
		AnimationClip aimBoltActionClip = LoadClip(c_ClipAimBoltAction);
		AnimationClip relaxedBoltActionClip = LoadClip(c_ClipRelaxedBoltAction);
		if (aimBoltActionClip != null && relaxedBoltActionClip != null)
		{
			EnsureBoltCycleEvents(aimBoltActionClip);
			EnsureBoltCycleEvents(relaxedBoltActionClip);

			AnimatorState aimBoltAction = EnsureMotionState(sm, c_AimBoltAction, aimBoltActionClip);
			AnimatorState relaxedBoltAction = EnsureMotionState(sm, c_RelaxedBoltAction, relaxedBoltActionClip);
			RemoveDuplicateNamedStates(sm, c_AimBoltAction, aimBoltAction);
			RemoveDuplicateNamedStates(sm, c_RelaxedBoltAction, relaxedBoltAction);
			RemoveTransition(aimBoltAction, relaxedIdle);
			RemoveTransition(relaxedBoltAction, relaxedIdle);

			EnsureTransition(aimBoltAction, pitchBlend, 0.22f,
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 0f),
				CondIf(c_ParamWeaponReady));
			EnsureTransition(aimBoltAction, crouchPitch, 0.22f,
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 1f));

			EnsureTransition(relaxedBoltAction, pitchBlend, 0.22f,
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 0f),
				CondIf(c_ParamWeaponReady));
			EnsureTransition(relaxedBoltAction, crouchPitch, 0.22f,
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 1f));

			EnsureTransition(aimBoltAction, aimReload, 0.1f,
				CondIf(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIf(c_ParamWeaponReady));
			EnsureTransition(relaxedBoltAction, relaxedReload, 0.1f,
				CondIf(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIfNot(c_ParamWeaponReady));
		}

		EnsureCrouchPitchMiddleClip(crouchPitch, crouchAimLegacyPitchClip);

		AnimationClip aimBoltAkClip = LoadClip(c_ClipAimBoltAk);
		AnimationClip relaxedBoltAkClip = LoadClip(c_ClipRelaxedBoltAk);
		if (aimBoltAkClip != null && relaxedBoltAkClip != null)
		{
			AnimatorState aimBoltAk = EnsureMotionState(sm, c_AimBoltAk, aimBoltAkClip);
			AnimatorState relaxedBoltAk = EnsureMotionState(sm, c_RelaxedBoltAk, relaxedBoltAkClip);
			RemoveDuplicateNamedStates(sm, c_AimBoltAk, aimBoltAk);
			RemoveDuplicateNamedStates(sm, c_RelaxedBoltAk, relaxedBoltAk);

			EnsureTransition(aimBoltAk, pitchBlend, 0.22f,
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 0f),
				CondIf(c_ParamWeaponReady));
			EnsureTransition(aimBoltAk, crouchPitch, 0.22f,
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 1f));
			EnsureTransition(aimBoltAk, relaxedIdle, 0.12f,
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIfNot(c_ParamWeaponReady));
			EnsureTransition(aimBoltAk, aimReload, 0.1f,
				CondIf(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIf(c_ParamWeaponReady));

			EnsureTransition(relaxedBoltAk, pitchBlend, 0.22f,
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 0f),
				CondIf(c_ParamWeaponReady));
			EnsureTransition(relaxedBoltAk, crouchPitch, 0.12f,
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 1f));
			EnsureTransition(relaxedBoltAk, relaxedIdle, 0.12f,
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIfNot(c_ParamWeaponReady));
			EnsureTransition(relaxedBoltAk, relaxedReload, 0.1f,
				CondIf(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIfNot(c_ParamWeaponReady));
		}

		AnimationClip lmgReloadClip = LoadClip(c_ClipLmgReload);
		if (lmgReloadClip != null)
		{
			EnsureLmgReloadEvents(lmgReloadClip);

			AnimatorState aimLmgReload = EnsureMotionState(sm, c_AimLmgReload, lmgReloadClip);
			AnimatorState relaxedLmgReload = EnsureMotionState(sm, c_RelaxedLmgReload, lmgReloadClip);
			RemoveDuplicateNamedStates(sm, c_AimLmgReload, aimLmgReload);
			RemoveDuplicateNamedStates(sm, c_RelaxedLmgReload, relaxedLmgReload);

			// From pitch blend / crouch → LMG reload
			EnsureTransition(pitchBlend, aimLmgReload, 0.25f,
				CondIf(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIf(c_ParamWeaponReady));
			EnsureTransition(crouchPitch, aimLmgReload, 0.25f,
				CondIf(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIf(c_ParamWeaponReady));
			EnsureTransition(crouchPitch, relaxedLmgReload, 0.25f,
				CondIf(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIfNot(c_ParamWeaponReady));
			EnsureTransition(relaxedIdle, relaxedLmgReload, 0.25f,
				CondIf(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIfNot(c_ParamWeaponReady));

			// From bolt/reload states → LMG reload
			EnsureTransition(relaxedReload, relaxedLmgReload, 0.25f,
				CondIf(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsCyclingBolt));
			EnsureTransition(relaxedBolt, relaxedLmgReload, 0.25f,
				CondIf(c_ParamIsLoadingLmgBelt));
			EnsureTransition(aimReload, aimLmgReload, 0.25f,
				CondIf(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIf(c_ParamWeaponReady));
			EnsureTransition(aimReload, relaxedLmgReload, 0.25f,
				CondIf(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIfNot(c_ParamWeaponReady));
			EnsureTransition(aimBolt, aimLmgReload, 0.25f,
				CondIf(c_ParamIsLoadingLmgBelt),
				CondIf(c_ParamWeaponReady));

			EnsureReloadExitGuardsAgainstLmgBelt(aimReload);
			EnsureReloadExitGuardsAgainstLmgBelt(relaxedReload);

			// Cross-variant: ready ↔ not ready while in LMG reload
			EnsureTransition(aimLmgReload, relaxedLmgReload, 0.15f,
				CondIfNot(c_ParamWeaponReady));
			EnsureTransition(relaxedLmgReload, aimLmgReload, 0.15f,
				CondIf(c_ParamWeaponReady));

			// From LMG reload → exit states
			EnsureTransition(aimLmgReload, pitchBlend, 0.3f,
				CondIfNot(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 0f),
				CondIf(c_ParamWeaponReady));
			EnsureTransition(aimLmgReload, pitchBlend, 0.3f,
				CondIfNot(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 2f),
				CondIf(c_ParamWeaponReady));
			EnsureTransition(aimLmgReload, crouchPitch, 0.3f,
				CondIfNot(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 1f));
			EnsureTransition(aimLmgReload, relaxedIdle, 0.3f,
				CondIfNot(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIfNot(c_ParamWeaponReady));

			EnsureTransition(relaxedLmgReload, pitchBlend, 0.3f,
				CondIfNot(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 0f),
				CondIf(c_ParamWeaponReady));
			EnsureTransition(relaxedLmgReload, pitchBlend, 0.3f,
				CondIfNot(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 2f),
				CondIf(c_ParamWeaponReady));
			EnsureTransition(relaxedLmgReload, crouchPitch, 0.3f,
				CondIfNot(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondEquals(c_ParamStance, 1f));
			EnsureTransition(relaxedLmgReload, relaxedIdle, 0.3f,
				CondIfNot(c_ParamIsLoadingLmgBelt),
				CondIfNot(c_ParamIsReloading),
				CondIfNot(c_ParamIsCyclingBolt),
				CondIfNot(c_ParamWeaponReady));
		}

		EditorUtility.SetDirty(controller);
		AssetDatabase.SaveAssets();
		AssetDatabase.ImportAsset(c_ControllerPath, ImportAssetOptions.ForceUpdate);

		LogAimLayerReport(aimLayer.stateMachine);

		Debug.Log(
			$"Aim layer «{c_AimLayerName}» обновлён: {c_RelaxedIdle}, {c_RelaxedReload}, {c_RelaxedBolt}, {c_AimBoltAction}, {c_RelaxedBoltAction}, {c_AimBoltAk}, {c_RelaxedBoltAk}, {c_AimLmgReload}, {c_RelaxedLmgReload}. " +
			"Откройте Animator и слой Aim_Point_U90-D90.",
			controller);
	}

	[MenuItem("Tools/Polygone/Log Aim Layer Relaxed Reload Status")]
	public static void LogAimLayerStatus()
	{
		var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c_ControllerPath);
		if (controller == null)
		{
			Debug.LogError($"Не найден Animator Controller: {c_ControllerPath}");
			return;
		}

		AnimatorControllerLayer aimLayer = FindLayer(controller, c_AimLayerName);
		if (aimLayer.stateMachine == null)
		{
			Debug.LogError($"Слой «{c_AimLayerName}» не найден в {c_ControllerPath}");
			return;
		}

		LogAimLayerReport(aimLayer.stateMachine);
	}

	/// <summary>Для batchmode: Unity.exe -batchmode -quit -executeMethod UnitAnimControllerAimRelaxedSetup.RunSetupFromBatch</summary>
	public static void RunSetupFromBatch()
	{
		SetupAimLayerRelaxedReloadGraph();
		EditorApplication.Exit(0);
	}

	[MenuItem("Tools/Polygone/Reimport Unit Anim Controller")]
	public static void ReimportController()
	{
		AssetDatabase.ImportAsset(c_ControllerPath, ImportAssetOptions.ForceUpdate);
		Debug.Log($"Reimport: {c_ControllerPath}");
	}

	private readonly struct ConditionSpec
	{
		public readonly AnimatorConditionMode Mode;
		public readonly string Parameter;
		public readonly float Threshold;

		public ConditionSpec(AnimatorConditionMode _mode, string _parameter, float _threshold)
		{
			Mode = _mode;
			Parameter = _parameter;
			Threshold = _threshold;
		}
	}

	private static ConditionSpec CondIf(string _param) =>
		new ConditionSpec(AnimatorConditionMode.If, _param, 0f);

	private static ConditionSpec CondIfNot(string _param) =>
		new ConditionSpec(AnimatorConditionMode.IfNot, _param, 0f);

	private static ConditionSpec CondEquals(string _param, float _value) =>
		new ConditionSpec(AnimatorConditionMode.Equals, _param, _value);

	private static AnimatorControllerLayer FindLayer(AnimatorController _controller, string _layerName)
	{
		for (int i = 0; i < _controller.layers.Length; i++)
		{
			if (_controller.layers[i].name == _layerName)
				return _controller.layers[i];
		}

		return default;
	}

	private static void EnsureParameter(AnimatorController _controller, string _name, AnimatorControllerParameterType _type)
	{
		foreach (AnimatorControllerParameter p in _controller.parameters)
		{
			if (p.name == _name)
				return;
		}

		_controller.AddParameter(_name, _type);
	}

	private static void ImportRequiredAssets()
	{
		string[] assetPaths =
		{
			c_ControllerPath,
			c_ClipAimBoltAction,
			c_ClipRelaxedBoltAction,
			c_ClipRifleCrouchIdleLegacy,
			c_ClipCrouchAimLegacyPitch
		};

		AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
		for (int i = 0; i < assetPaths.Length; i++)
		{
			if (!File.Exists(assetPaths[i]))
			{
				Debug.LogWarning($"Asset для setup не найден на диске: {assetPaths[i]}");
				continue;
			}

			AssetDatabase.ImportAsset(
				assetPaths[i],
				ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
		}
	}

	private static void EnsureBoltCycleEvents(AnimationClip _clip)
	{
		if (_clip == null)
			return;

		AnimationEvent[] events =
		{
			new AnimationEvent
			{
				functionName = c_EventBoltActionCycleSoundStarted,
				time = 0f
			},
			new AnimationEvent
			{
				functionName = c_EventFinishWeaponReload,
				time = Mathf.Clamp(c_BoltCycleFinishEventTime, 0f, Mathf.Max(0f, _clip.length))
			}
		};

		AnimationUtility.SetAnimationEvents(_clip, events);
		EditorUtility.SetDirty(_clip);
	}

	private static void EnsureLmgReloadEvents(AnimationClip _clip)
	{
		if (_clip == null)
			return;

		float clipLength = Mathf.Max(0.01f, _clip.length);
		AnimationEvent[] events =
		{
			new AnimationEvent
			{
				functionName = c_EventLmgCoverOpenStarted,
				time = clipLength * 0.15f
			},
			new AnimationEvent
			{
				functionName = c_EventLmgBeltInserted,
				time = clipLength * 0.40f
			},
			new AnimationEvent
			{
				functionName = c_EventLmgCoverCloseStarted,
				time = clipLength * 0.65f
			},
			new AnimationEvent
			{
				functionName = c_EventFinishWeaponReload,
				time = clipLength * 0.95f
			}
		};

		AnimationUtility.SetAnimationEvents(_clip, events);
		EditorUtility.SetDirty(_clip);
	}

	private static void EnsureStateMotion(AnimatorController _controller, string _stateName, Motion _motion)
	{
		if (_controller == null || _motion == null)
			return;

		for (int i = 0; i < _controller.layers.Length; i++)
		{
			AnimatorState state = FindStateRecursive(_controller.layers[i].stateMachine, _stateName);
			if (state == null)
				continue;

			state.motion = _motion;
			EditorUtility.SetDirty(state);
			return;
		}

		Debug.LogWarning($"State для setup не найден: {_stateName}");
	}

	private static AnimatorState FindStateRecursive(AnimatorStateMachine _stateMachine, string _stateName)
	{
		if (_stateMachine == null)
			return null;

		foreach (ChildAnimatorState child in _stateMachine.states)
		{
			if (child.state != null && child.state.name == _stateName)
				return child.state;
		}

		foreach (ChildAnimatorStateMachine childMachine in _stateMachine.stateMachines)
		{
			AnimatorState state = FindStateRecursive(childMachine.stateMachine, _stateName);
			if (state != null)
				return state;
		}

		return null;
	}

	private static void EnsureCrouchPitchMiddleClip(AnimatorState _crouchPitchState, AnimationClip _middleClip)
	{
		if (_crouchPitchState == null || _middleClip == null)
			return;

		var blendTree = _crouchPitchState.motion as BlendTree;
		if (blendTree == null)
		{
			Debug.LogWarning($"{c_CrouchPitchBlend} не содержит BlendTree.");
			return;
		}

		ChildMotion[] children = blendTree.children;
		for (int i = 0; i < children.Length; i++)
		{
			if (!Mathf.Approximately(children[i].threshold, 0f))
				continue;

			children[i].motion = _middleClip;
			blendTree.children = children;
			EditorUtility.SetDirty(blendTree);
			EditorUtility.SetDirty(_crouchPitchState);
			return;
		}

		Debug.LogWarning($"{c_CrouchPitchBlend}: не найден middle child с threshold 0.");
	}

	private static AnimationClip LoadClip(string _path)
	{
		var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(_path);
		if (clip == null)
			Debug.LogWarning($"Клип не найден: {_path}");
		return clip;
	}

	private static AnimatorState RequireState(AnimatorStateMachine _sm, string _name)
	{
		foreach (ChildAnimatorState child in _sm.states)
		{
			if (child.state.name == _name)
				return child.state;
		}

		Debug.LogError($"На слое Aim не найден стейт «{_name}». Сначала создайте базовый граф вручную.");
		return null;
	}

	private static AnimatorState EnsureMotionState(AnimatorStateMachine _sm, string _name, Motion _motion)
	{
		AnimatorState best = null;
		int bestScore = -1;

		foreach (ChildAnimatorState child in _sm.states)
		{
			if (child.state.name != _name)
				continue;

			int score = child.state.transitions.Length;
			if (child.state.motion != null)
				score += 10;

			if (score <= bestScore)
				continue;

			bestScore = score;
			best = child.state;
		}

		if (best != null)
		{
			if (_motion != null)
				best.motion = _motion;

			RemoveDuplicateNamedStates(_sm, _name, best);
			return best;
		}

		AnimatorState created = _sm.AddState(_name);
		created.motion = _motion;
		return created;
	}

	private static void RemoveDuplicateNamedStates(AnimatorStateMachine _sm, string _name, AnimatorState _keep)
	{
		var duplicates = new List<AnimatorState>();
		foreach (ChildAnimatorState child in _sm.states)
		{
			if (child.state.name == _name && child.state != _keep)
				duplicates.Add(child.state);
		}

		for (int i = 0; i < duplicates.Count; i++)
			_sm.RemoveState(duplicates[i]);
	}

	private static void RepointTransitionsByDestinationName(AnimatorStateMachine _sm, string _destinationName, AnimatorState _destination)
	{
		if (_sm == null || _destination == null)
			return;

		foreach (ChildAnimatorState child in _sm.states)
		{
			AnimatorState state = child.state;
			if (state == null)
				continue;

			foreach (AnimatorStateTransition transition in state.transitions)
			{
				if (transition.destinationState == null)
					continue;
				if (transition.destinationState == _destination)
					continue;
				if (transition.destinationState.name != _destinationName)
					continue;

				transition.destinationState = _destination;
				EditorUtility.SetDirty(transition);
				EditorUtility.SetDirty(state);
			}
		}
	}

	private static void RemoveTransition(AnimatorState _from, AnimatorState _to)
	{
		if (_from == null || _to == null)
			return;

		for (int i = _from.transitions.Length - 1; i >= 0; i--)
		{
			if (_from.transitions[i].destinationState == _to)
				_from.RemoveTransition(_from.transitions[i]);
		}
	}

	private static void RemovePitchExitWithoutWeaponReady(AnimatorState _from, AnimatorState _pitchBlend)
	{
		if (_from == null || _pitchBlend == null)
			return;

		for (int i = _from.transitions.Length - 1; i >= 0; i--)
		{
			AnimatorStateTransition transition = _from.transitions[i];
			if (transition.destinationState != _pitchBlend)
				continue;

			bool hasWeaponReadyIf = false;
			foreach (AnimatorCondition condition in transition.conditions)
			{
				if (condition.parameter != c_ParamWeaponReady)
					continue;
				if (condition.mode == AnimatorConditionMode.If)
					hasWeaponReadyIf = true;
			}

			if (!hasWeaponReadyIf)
				_from.RemoveTransition(transition);
		}
	}

	/// <summary>
	/// Выход из reload-состояния при !IsReloadingWeapon не должен срабатывать во время LMG belt-load.
	/// </summary>
	private static void EnsureReloadExitGuardsAgainstLmgBelt(AnimatorState _state)
	{
		if (_state == null)
			return;

		foreach (AnimatorStateTransition transition in _state.transitions)
		{
			bool exitsWhenNotReloading = false;
			bool alreadyGuardsLmgBelt = false;

			foreach (AnimatorCondition condition in transition.conditions)
			{
				if (condition.parameter == c_ParamIsReloading && condition.mode == AnimatorConditionMode.IfNot)
					exitsWhenNotReloading = true;
				if (condition.parameter == c_ParamIsLoadingLmgBelt)
					alreadyGuardsLmgBelt = true;
			}

			if (!exitsWhenNotReloading || alreadyGuardsLmgBelt)
				continue;

			var conditions = new List<AnimatorCondition>(transition.conditions)
			{
				new AnimatorCondition
				{
					mode = AnimatorConditionMode.IfNot,
					parameter = c_ParamIsLoadingLmgBelt,
					threshold = 0f
				}
			};
			transition.conditions = conditions.ToArray();
			EditorUtility.SetDirty(transition);
			EditorUtility.SetDirty(_state);
		}
	}

	private static void EnsureTransition(
		AnimatorState _from,
		AnimatorState _to,
		float _duration,
		params ConditionSpec[] _conditions)
	{
		if (_from == null || _to == null)
			return;

		foreach (AnimatorStateTransition existing in _from.transitions)
		{
			if (existing.destinationState != _to)
				continue;
			if (!ConditionsMatch(existing.conditions, _conditions))
				continue;

			existing.hasExitTime = false;
			existing.exitTime = 0f;
			existing.duration = _duration;
			existing.offset = 0f;
			return;
		}

		AnimatorStateTransition transition = _from.AddTransition(_to);
		transition.hasExitTime = false;
		transition.exitTime = 0f;
		transition.duration = _duration;
		transition.offset = 0f;
		transition.interruptionSource = TransitionInterruptionSource.None;
		transition.orderedInterruption = true;
		transition.canTransitionToSelf = false;

		transition.conditions = BuildConditions(_conditions);
	}

	private static AnimatorCondition[] BuildConditions(ConditionSpec[] _specs)
	{
		var result = new AnimatorCondition[_specs.Length];
		for (int i = 0; i < _specs.Length; i++)
		{
			result[i] = new AnimatorCondition
			{
				mode = _specs[i].Mode,
				parameter = _specs[i].Parameter,
				threshold = _specs[i].Threshold
			};
		}

		return result;
	}

	private static bool ConditionsMatch(AnimatorCondition[] _existing, ConditionSpec[] _expected)
	{
		if (_existing.Length != _expected.Length)
			return false;

		for (int i = 0; i < _expected.Length; i++)
		{
			if (_existing[i].mode != _expected[i].Mode)
				return false;
			if (_existing[i].parameter != _expected[i].Parameter)
				return false;
			if (!Mathf.Approximately(_existing[i].threshold, _expected[i].Threshold))
				return false;
		}

		return true;
	}

	private static void CloseAnimatorWindowsForController(string _controllerPath)
	{
		EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
		for (int i = 0; i < windows.Length; i++)
		{
			EditorWindow window = windows[i];
			if (window != null && window.GetType().Name == "AnimatorControllerWindow")
				window.Close();
		}
	}

	private static void LogAimLayerReport(AnimatorStateMachine _sm)
	{
		var lines = new List<string> { $"[{c_AimLayerName}] states ({_sm.states.Length}):" };

		foreach (ChildAnimatorState child in _sm.states)
		{
			AnimatorState state = child.state;
			string motion = state.motion != null ? state.motion.name : "(no motion)";
			lines.Add($"  • {state.name}: motion={motion}, transitions={state.transitions.Length}");
		}

		AnimatorState pitch = null;
		foreach (ChildAnimatorState child in _sm.states)
		{
			if (child.state.name == c_PitchBlend)
			{
				pitch = child.state;
				break;
			}
		}

		if (pitch != null)
		{
			lines.Add($"{c_PitchBlend} →");
			foreach (AnimatorStateTransition t in pitch.transitions)
			{
				string dst = t.destinationState != null ? t.destinationState.name : "(null)";
				lines.Add($"  → {dst} ({t.conditions.Length} cond)");
			}
		}

		Debug.Log(string.Join("\n", lines));
	}
}
#endif

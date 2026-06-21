using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RTS-обёртка над существующими компонентами юнита:
/// регистрация в списке selectable-юнитов, групповые команды и состояние выделения.
/// </summary>
[DisallowMultipleComponent]
public sealed class RtsUnitMember : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoadingController;
	[SerializeField] private UnitWeaponReloadController m_WeaponReloadController;
	[SerializeField] private UnitSelfStabilizationController m_SelfStabilizationController;
	[SerializeField] private UnitStabilizeOtherController m_StabilizeOtherController;
	[SerializeField] private UnitFiremanCarryController m_FiremanCarryController;
	[SerializeField] private UnitFallenDragController m_FallenDragController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private CharacterInventory m_CharacterInventory;
	[SerializeField] private Collider m_SelectionCollider;
	[SerializeField] private GameObject m_SelectionVisualRoot;
	[SerializeField] private bool m_DisableDirectInputForRts = true;
	[Header("Animator Variation")]
	[SerializeField, Range(0.85f, 1.15f)] private float m_MoveAnimatorSpeedMin = 0.97f;
	[SerializeField, Range(0.85f, 1.15f)] private float m_MoveAnimatorSpeedMax = 1.03f;
	[SerializeField] private float m_RuntimeMoveAnimatorSpeed = 1f;
	[SerializeField] private bool m_IsSelected;

	private static readonly List<RtsUnitMember> s_Instances = new List<RtsUnitMember>(128);
	private Coroutine m_PendingCommandCoroutine;
	private int m_PendingCommandVersion;
	#endregion

	#region Public Properties
	public static IReadOnlyList<RtsUnitMember> Instances => s_Instances;
	public CharacterInventory CharacterInventory => m_CharacterInventory;
	public bool IsSelected => m_IsSelected;
	public bool IsPlayerSelectable => m_Team != null && m_Team.Team == UnitTeamId.Player;
	public bool WantsReady => m_ReadyHands != null && m_ReadyHands.WantsReady;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_MagazineLoadingController == null)
			m_MagazineLoadingController = GetComponent<UnitMagazineLoadingController>();
		if (m_WeaponReloadController == null)
			m_WeaponReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_SelfStabilizationController == null)
			m_SelfStabilizationController = GetComponent<UnitSelfStabilizationController>();
		if (m_FallenDragController == null)
			m_FallenDragController = GetComponent<UnitFallenDragController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_CharacterInventory == null)
			m_CharacterInventory = GetComponent<CharacterInventory>();
		if (m_SelectionCollider == null)
			m_SelectionCollider = GetComponent<Collider>();

		m_RuntimeMoveAnimatorSpeed = UnityEngine.Random.Range(
			Mathf.Min(m_MoveAnimatorSpeedMin, m_MoveAnimatorSpeedMax),
			Mathf.Max(m_MoveAnimatorSpeedMin, m_MoveAnimatorSpeedMax));

		if (m_DisableDirectInputForRts)
			ApplyDirectInputState(false);
	}

	private void OnEnable()
	{
		if (!s_Instances.Contains(this))
			s_Instances.Add(this);
		SetSelected(false);
		ApplyAnimatorSpeedVariation();
	}

	private void OnDisable()
	{
		CancelPendingCommand();
		ResetAnimatorSpeed();
		s_Instances.Remove(this);
		SetSelected(false);
	}

	private void Update()
	{
		ApplyAnimatorSpeedVariation();
	}
	#endregion

	#region Public Methods
	public void SetSelected(bool _selected)
	{
		m_IsSelected = _selected;
		if (m_SelectionVisualRoot != null)
			m_SelectionVisualRoot.SetActive(_selected);
	}

	public void IssueMoveOrder(Vector3 _worldPosition, UnitClickToMove.MoveTier _moveTier)
	{
		ScheduleRtsCommand(() =>
		{
			UnitSelfStabilizationController selfStabilization = ResolveSelfStabilizationController();
			if (selfStabilization != null &&
			    (selfStabilization.IsSelfHealing || selfStabilization.IsHealPresentationActive))
				return;

			UnitStabilizeOtherController stabilizeOther = ResolveStabilizeOtherController();
			if (stabilizeOther != null &&
			    (stabilizeOther.IsStabilizingOther || stabilizeOther.IsHealPresentationActive))
				return;

			if (m_FallenDragController != null && m_FallenDragController.IsDragging)
				_moveTier = UnitClickToMove.MoveTier.Walk;

			if (_moveTier == UnitClickToMove.MoveTier.Run || _moveTier == UnitClickToMove.MoveTier.Sprint)
				m_MagazineLoadingController?.StopLoading();

			if (m_ClickToMove != null)
			{
				m_ClickToMove.IssueNavOrder(_worldPosition, _moveTier);
				return;
			}

			if (m_LocomotionDriver != null)
			{
				UnitNavLocomotionDriver.MoveTier navTier = _moveTier switch
				{
					UnitClickToMove.MoveTier.Run => UnitNavLocomotionDriver.MoveTier.Run,
					UnitClickToMove.MoveTier.Sprint => UnitNavLocomotionDriver.MoveTier.Sprint,
					_ => UnitNavLocomotionDriver.MoveTier.Walk
				};
				m_LocomotionDriver.IssueNavOrder(_worldPosition, navTier);
			}
		});
	}

	public void SetReadyWanted(bool _ready)
	{
		ScheduleRtsCommand(() =>
		{
			if (m_ReadyHands != null)
				m_ReadyHands.SetReadyWanted(_ready);
		});
	}

	public void RequestStance(LocomotionStance _stance)
	{
		if (_stance == LocomotionStance.Prone && !LocomotionProneFeature.Enabled)
			return;

		ScheduleRtsCommand(() =>
		{
			if (_stance == LocomotionStance.Prone)
				m_MagazineLoadingController?.StopLoading();

			if (m_Stance != null)
				m_Stance.RequestStance(_stance);
		});
	}

	public void HardStop()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_FallenDragController != null && m_FallenDragController.IsDragging)
			{
				m_FallenDragController.RequestReleaseDrag();
				return;
			}

			UnitSelfStabilizationController selfStabilization = ResolveSelfStabilizationController();
			selfStabilization?.StopSelfStabilization();

			UnitStabilizeOtherController stabilizeOther = ResolveStabilizeOtherController();
			stabilizeOther?.StopStabilizeOther();

			UnitFiremanCarryController firemanCarry = ResolveFiremanCarryController();
			firemanCarry?.RequestRelease();

			m_MagazineLoadingController?.StopLoading();
			m_WeaponReloadController?.StopReload();
			m_FireController?.StopFiring();

			if (m_ClickToMove != null)
			{
				m_ClickToMove.HardStop();
				return;
			}

			if (m_LocomotionDriver != null)
				m_LocomotionDriver.HardStop();
		});
	}

	public void StartFiring()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_FireController != null)
				m_FireController.StartFiring();
		});
	}

	public void StopFiring()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_FireController != null)
				m_FireController.StopFiring();
		});
	}

	public WeaponShotAttemptResult TryFireSingleShot()
	{
		WeaponShotAttemptResult result = WeaponShotAttemptResult.NoWeapon;
		ScheduleRtsCommand(() =>
		{
			if (m_FireController != null)
				result = m_FireController.TryFireSingleShot();
		});

		return result;
	}

	public void StartManualMagazineLoading()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_MagazineLoadingController == null)
				return;

			m_MagazineLoadingController.TryStartLoadingMagazineFromAmmoBoxes();
		});
	}

	public void StartWeaponReload()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_WeaponReloadController == null)
				return;

			m_WeaponReloadController.TryStartReload();
		});
	}

	/// <summary>Следующий доступный режим огня по <see cref="WeaponDefinition.AvailableFireModes"/>.</summary>
	public void CycleWeaponFireMode()
	{
		ScheduleRtsCommand(() =>
		{
			m_FireController?.ResetBurstStateForFireModeChange();

			if (m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			{
				Debug.LogWarning($"{name}: смена режима огня — нет состояния оружия.", this);
				return;
			}

			WeaponFireMode before = m_WeaponRuntime.RuntimeState.SelectedFireMode;
			if (!m_WeaponRuntime.TryCycleToNextFireMode())
			{
				Debug.Log($"{name}: режим огня не изменён (один доступный режим или нет экипированного оружия). Сейчас: {before}.", this);
				return;
			}

			WeaponFireMode after = m_WeaponRuntime.RuntimeState.SelectedFireMode;
			WeaponFireMode effectiveAfter = m_FireController != null
				? m_FireController.ResolveEffectiveFireMode()
				: after;
			string afterLabel = after == WeaponFireMode.Auto
				? $"{WeaponFireModeUtility.GetDisplayName(after)}→{WeaponFireModeUtility.GetDisplayName(effectiveAfter)}"
				: WeaponFireModeUtility.GetDisplayName(after);
			Debug.Log(
				$"{name}: режим огня {WeaponFireModeUtility.GetDisplayName(before)} → {afterLabel}.",
				this);
			PlayFireModeSwitchSound();
		});
	}

	/// <summary>Следующий режим прицеливания юнита: полное, быстрое, на вскидку, авто.</summary>
	public void CycleWeaponAimMode()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_WeaponRuntime == null)
			{
				Debug.LogWarning($"{name}: смена режима прицеливания — нет runtime оружия.", this);
				return;
			}

			WeaponAimMode before = m_WeaponRuntime.SelectedAimMode;
			if (!m_WeaponRuntime.TryCycleToNextAimMode(out WeaponAimMode after))
			{
				Debug.Log($"{name}: режим прицеливания не изменён. Сейчас: {before}.", this);
				return;
			}

			Debug.Log(
				$"{name}: режим прицеливания {WeaponAimModeUtility.GetDisplayName(before)} → {WeaponAimModeUtility.GetDisplayName(after)} " +
				$"(порог выстрела: {WeaponAimModeUtility.GetRequiredAimProgress01(after, 0f):P0}; в авто порог зависит от дистанции).",
				this);
			PlayFireModeSwitchSound();
		});
	}

	private void PlayFireModeSwitchSound()
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		AudioClip clip = weaponDefinition != null ? weaponDefinition.FireModeSwitchSound : null;
		if (clip == null)
			return;

		Vector3 position = transform.position + Vector3.up * 1.35f;
		if (m_UnitEquipment != null && m_UnitEquipment.MainWeaponRoot != null)
			position = m_UnitEquipment.MainWeaponRoot.position;

		float volume = weaponDefinition.FireModeSwitchSoundVolume;
		AudioSource.PlayClipAtPoint(clip, position, volume);
	}

	public bool TryGetCurrentStance(out LocomotionStance _stance)
	{
		if (m_Stance == null)
		{
			_stance = LocomotionStance.Standing;
			return false;
		}

		_stance = m_Stance.CurrentStance;
		return true;
	}

	public bool TryGetSelectionBounds(out Bounds _bounds)
	{
		if (m_SelectionCollider != null)
		{
			_bounds = m_SelectionCollider.bounds;
			return true;
		}

		_bounds = new Bounds(transform.position, Vector3.one);
		return true;
	}

	public void ApplyDirectInputState(bool _enabled)
	{
		if (m_ClickToMove != null)
			m_ClickToMove.SetDirectInputEnabled(_enabled);
		if (m_Stance != null)
			m_Stance.SetKeyboardInputEnabled(_enabled);
		if (m_ReadyHands != null)
			m_ReadyHands.SetKeyboardInputEnabled(_enabled);
	}
	#endregion

	#region Private Methods
	private UnitSelfStabilizationController ResolveSelfStabilizationController()
	{
		if (m_SelfStabilizationController == null)
			m_SelfStabilizationController = GetComponent<UnitSelfStabilizationController>();

		return m_SelfStabilizationController;
	}

	private UnitStabilizeOtherController ResolveStabilizeOtherController()
	{
		if (m_StabilizeOtherController == null)
			m_StabilizeOtherController = GetComponent<UnitStabilizeOtherController>();

		return m_StabilizeOtherController;
	}

	private UnitFiremanCarryController ResolveFiremanCarryController()
	{
		if (m_FiremanCarryController == null)
			m_FiremanCarryController = GetComponent<UnitFiremanCarryController>();

		return m_FiremanCarryController;
	}

	private void ScheduleRtsCommand(Action _command)
	{
		if (_command == null)
			return;

		m_PendingCommandVersion++;

		if (m_PendingCommandCoroutine != null)
			StopCoroutine(m_PendingCommandCoroutine);

		m_PendingCommandCoroutine = null;
		_command();
	}

	private void CancelPendingCommand()
	{
		m_PendingCommandVersion++;
		if (m_PendingCommandCoroutine == null)
			return;

		StopCoroutine(m_PendingCommandCoroutine);
		m_PendingCommandCoroutine = null;
	}

	private void ApplyAnimatorSpeedVariation()
	{
		if (m_Animator == null)
			return;

		float playbackSync = 1f;
		if (m_LocomotionDriver != null)
			playbackSync = m_LocomotionDriver.AnimatorPlaybackSpeedMultiplier;
		else if (m_ClickToMove != null)
			playbackSync = m_ClickToMove.AnimatorPlaybackSpeedMultiplier;

		m_Animator.speed = IsExecutingMoveOrder()
			? m_RuntimeMoveAnimatorSpeed * playbackSync
			: 1f;
	}

	private bool IsExecutingMoveOrder()
	{
		if (m_ClickToMove != null)
			return m_ClickToMove.HasMoveIntent;
		if (m_LocomotionDriver != null)
			return m_LocomotionDriver.HasMoveIntent;

		return false;
	}

	private void ResetAnimatorSpeed()
	{
		if (m_Animator != null)
			m_Animator.speed = 1f;
	}
	#endregion
}

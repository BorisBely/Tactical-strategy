using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Перезарядка: фаза смены магазина (<c>IsReloadingWeapon</c>) и отдельно фаза передёргивания затвора (<c>IsCyclingBolt</c>).
/// Патронник: выстрел только при <see cref="WeaponRuntimeState.HasRoundInChamber"/>; подача из магазина —
/// для оружия с <see cref="WeaponDefinition.HasBoltHoldOpenDelay"/> ивентом <c>AnimationEvent_ReloadBoltHoldOpenDelay</c> в конце клипа перезарядки,
/// иначе <c>AnimationEvent_FinishWeaponReload</c> в клипе передёргивания затвора (после вставки магазина Animator получает <c>IsCyclingBolt</c>).
/// Если магазин уже в оружии с патронами, но патронник пуст — <see cref="TryStartReload"/> запускает только затвор (<see cref="TryStartBoltCycleOnly"/>).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(54)]
public sealed class UnitWeaponReloadController : MonoBehaviour
{
	#region Constants
	public const string ParamIsReloadingWeapon = "IsReloadingWeapon";
	public const string ParamIsCyclingBolt = "IsCyclingBolt";
	public const string AimReloadLayerName = "Aim_Point_U90-D90";
	private static readonly int s_IsReloadingWeapon = Animator.StringToHash(ParamIsReloadingWeapon);
	private static readonly int s_IsCyclingBolt = Animator.StringToHash(ParamIsCyclingBolt);
	private static readonly int s_WeaponReady = Animator.StringToHash(UnitAnimatorWeaponMode.ParamWeaponReady);
	private static readonly int s_Stance = Animator.StringToHash(UnitAnimatorWeaponMode.ParamStance);
	private static readonly int s_AimRelaxedIdleStateHash = Animator.StringToHash("Stand_Relaxed_Idle");
	private static readonly int s_AimRelaxedReloadStateHash = Animator.StringToHash("Stand_Relaxed_Reload");
	private static readonly int s_AimReloadStateHash = Animator.StringToHash("Stand_Aim_Reload");
	/// <summary>Согласовано с <c>Stand_Relaxed_Reload.anim</c> / <c>Stand_Aim_Reload.anim</c> (30 fps, ~89 кадров).</summary>
	private const float c_ReloadClipDurationSeconds = 2.966667f;
	private const float c_ReloadClipSampleRate = 30f;
	/// <summary>UI install-only: начало фазы вставки (кадр 40 в DCC / в клипе перезарядки).</summary>
	private const int c_UiInstallOnlyReloadStartFrame = 40;
	#endregion

	#region Serialized Fields
	[SerializeField] private CharacterInventory m_CharacterInventory;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoadingController;
	[SerializeField] private InventoryScreenBindings m_InventoryBindings;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private Transform m_LeftHandAnchor;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponMalfunctionController m_MalfunctionController;
	[Tooltip("Для звуков перезарядки; если пусто — создаётся дочерний ReloadAudioSource_Auto.")]
	[SerializeField] private AudioSource m_ReloadAudioSource;
	[SerializeField, Min(0.01f)] private float m_ReloadSoundSpatialMinDistance = 1f;
	[SerializeField, Min(0.5f)] private float m_ReloadSoundSpatialMaxDistance = 45f;

	[Header("Bolt presentation")]
	[Tooltip("После FinishWeaponReload: дополнительно блокировать стрельбу и держать IsReloadBusy на это время, пока доигрывается анимация затвора. Сбрасывается раньше, если вызван AnimationEvent_BoltMotionPresentationFinished. 0 = не блокировать после финиша (точный контроль только ивентом BoltMotionPresentationFinished).")]
	[SerializeField, Min(0f)] private float m_BoltPresentationFireTailSeconds;

	[Header("Magazine visual transfer")]
	[Tooltip("Длительность сглаживания локальной позиции/поворота при переносе магазина между сокетом оружия и левой рукой.")]
	[SerializeField, Min(0.01f)] private float m_MagazineVisualTransferDuration = 0.12f;

	[Header("Debug")]
	[SerializeField] private bool m_IsReloadingWeapon;
	[SerializeField] private bool m_IsCyclingBolt;
	[SerializeField] private bool m_HasEjectedCurrentMagazine;
	[SerializeField] private int m_DebugSourceBagIndex = -1;
	[SerializeField] private int m_DebugFallbackMagazineBagIndex = -1;
	[SerializeField] private string m_DebugLastFailureReason;
	#endregion

	#region Private Fields
	private InventorySlotRuntimeData m_PendingReplacementMagazine;
	private GameObject m_LeftHandMagazineVisualInstance;
	private Coroutine m_MagazineVisualTransferCoroutine;
	private GameObject m_ActiveMagazineVisualTransferInstance;
	private Vector3 m_ActiveMagazineVisualTransferTargetLocalPosition;
	private Quaternion m_ActiveMagazineVisualTransferTargetLocalRotation = Quaternion.identity;
	private bool m_ShouldStartManualMagazineLoadingAfterReload;
	private bool m_ShouldStartReloadAfterMagazineLoading;
	private int m_PendingReloadPreferredBagIndex = -1;
	/// <summary>Сбрасывается при старте перезарядки; true только после успешного <see cref="AnimationEvent_InsertPendingMagazineIntoWeapon"/>.</summary>
	private bool m_MagazineInsertCompletedThisReload;
	/// <summary>После <see cref="AnimationEvent_FinishWeaponReload"/> логика и патронник уже готовы, но анимация затвора может ещё идти — стрельбу держим до <see cref="AnimationEvent_BoltMotionPresentationFinished"/>.</summary>
	private bool m_BoltPresentationSuppressesFire;
	private bool m_MalfunctionStripReinsertReloadActive;
	private bool m_UiMagazineModificationActive;
	private bool m_UiMagazineEjectOnly;
	/// <summary>UI-вставка в пустое оружие: только фаза insert (+ затвор при пустом патроннике), без извлечения.</summary>
	private bool m_UiMagazineInstallOnly;
	/// <summary>Только анимация на зеркальных юнитах пресета — без мутации сумки и без <see cref="UiMagazineModificationCompleted"/>.</summary>
	private bool m_UiMagazineMirrorAnimationOnly;
	private InventorySlotRuntimeData m_UiLastEjectedMagazine;
	private int m_AimReloadLayerIndex = -1;
	private int m_MagazineLoadingLayerIndex = -1;
	private bool m_WasAimReloadBusy;
	#endregion

	#region Public Properties
	public bool IsReloadingWeapon => m_IsReloadingWeapon;
	public bool IsCyclingBolt => m_IsCyclingBolt;
	public bool IsReloadBusy =>
		m_IsReloadingWeapon ||
		m_IsCyclingBolt ||
		m_BoltPresentationSuppressesFire;
	public bool IsMalfunctionStripReinsertReloadActive => m_MalfunctionStripReinsertReloadActive;
	public bool MagazineInsertCompletedThisReload => m_MagazineInsertCompletedThisReload;
	public bool IsUiMagazineModificationActive => m_UiMagazineModificationActive;
	#endregion

	#region Events
	/// <summary>UI-модификация магазина завершена (install или eject). Для eject-only в <paramref name="_ejectedMagazine"/> лежит снятый магазин.</summary>
	public event Action<InventorySlotRuntimeData> UiMagazineModificationCompleted;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_CharacterInventory == null)
			m_CharacterInventory = GetComponent<CharacterInventory>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_MagazineLoadingController == null)
			m_MagazineLoadingController = GetComponent<UnitMagazineLoadingController>();
		if (m_InventoryBindings == null)
			m_InventoryBindings = InventoryScreenBindings.Instance;
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_LeftHandAnchor == null && m_Animator != null && m_Animator.isHuman)
			m_LeftHandAnchor = m_Animator.GetBoneTransform(HumanBodyBones.LeftHand);
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_MalfunctionController == null)
			m_MalfunctionController = GetComponent<UnitWeaponMalfunctionController>();

		EnsureReloadAudioSource();
		ResolveAnimatorLayerIndices();
	}

	private void Update()
	{
		ApplyReloadAnimatorLayerWeightsIfBusy();
	}

	private void OnDisable()
	{
		if (m_MagazineLoadingController != null)
			m_MagazineLoadingController.LoadingStopped -= HandleMagazineLoadingStopped;

		CancelInvoke(nameof(ClearBoltPresentationSuppressFireOnly));
		StopReloadInternal(true);
	}

	private void OnEnable()
	{
		if (m_MagazineLoadingController == null)
			m_MagazineLoadingController = GetComponent<UnitMagazineLoadingController>();
		if (m_MagazineLoadingController != null)
			m_MagazineLoadingController.LoadingStopped += HandleMagazineLoadingStopped;
	}
	#endregion

	#region Public Methods
	public bool TryStartReload()
	{
		return TryStartReloadInternal(-1);
	}

	/// <summary>Только передёргивание затвора: магазин с патронами уже вставлен, патронник пуст.</summary>
	/// <summary>Передёргивание затвора в сценарии отказа (патронник может быть занят).</summary>
	public bool TryStartMalfunctionBoltRack()
	{
		m_DebugLastFailureReason = null;

		if (m_IsReloadingWeapon || m_IsCyclingBolt)
		{
			m_DebugLastFailureReason = "Already reloading or bolting";
			return false;
		}

		if (m_MalfunctionController == null || !m_MalfunctionController.IsMalfunctionBoltRecoveryContext)
		{
			m_DebugLastFailureReason = "No malfunction recovery";
			return false;
		}

		if (m_CharacterInventory == null || m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
		{
			m_DebugLastFailureReason = "Missing runtime references";
			return false;
		}

		if (m_BusyState != null && m_BusyState.IsBusy &&
		    (m_BusyState.Reasons & ~UnitBusyState.BusyReason.Reload) != 0)
		{
			m_DebugLastFailureReason = $"Unit is busy: {m_BusyState.Reasons}";
			return false;
		}

		CancelInvoke(nameof(ClearBoltPresentationSuppressFireOnly));
		m_IsCyclingBolt = true;
		m_BoltPresentationSuppressesFire = true;
		m_FireController?.StopFiring();
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, true);
		m_MalfunctionController?.NotifyBoltAnimStarting();
		SyncAnimatorState();
		RefreshInventoryUiIfActive();
		return true;
	}

	/// <summary>Тяжёлый клин: полный граф перезарядки без предварительного магазина в левой руке; тот же магазин уходит в pending на ивенте снятия.</summary>
	public bool TryStartMalfunctionStripReinsertReload()
	{
		m_DebugLastFailureReason = null;

		if (m_IsReloadingWeapon || m_IsCyclingBolt)
		{
			m_DebugLastFailureReason = "Already reloading or bolting";
			return false;
		}

		if (m_CharacterInventory == null || m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
		{
			m_DebugLastFailureReason = "Missing runtime references";
			return false;
		}

		if (m_BusyState != null && m_BusyState.IsBusy &&
		    (m_BusyState.Reasons & ~UnitBusyState.BusyReason.Reload) != 0)
		{
			m_DebugLastFailureReason = $"Unit is busy: {m_BusyState.Reasons}";
			return false;
		}

		CancelInvoke(nameof(ClearBoltPresentationSuppressFireOnly));
		m_MalfunctionStripReinsertReloadActive = true;
		m_IsReloadingWeapon = true;
		m_IsCyclingBolt = false;
		m_BoltPresentationSuppressesFire = false;
		m_HasEjectedCurrentMagazine = false;
		m_MagazineInsertCompletedThisReload = false;
		m_PendingReplacementMagazine = default;
		m_ShouldStartManualMagazineLoadingAfterReload = false;
		m_DebugSourceBagIndex = -1;
		m_DebugFallbackMagazineBagIndex = -1;
		m_FireController?.StopFiring();
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, true);
		TryAttachPendingMagazineVisualToLeftHand();
		SyncAnimatorState();
		RefreshInventoryUiIfActive();
		return true;
	}

	public void TryPlayBoltCycleSoundPublic()
	{
		TryPlayBoltCycleSound();
	}

	/// <summary>Завершить только цикл затвора отказа, не трогая полную перезарядку.</summary>
	public void NotifyMalfunctionBoltHandledEnd()
	{
		if (m_IsReloadingWeapon)
		{
			m_IsCyclingBolt = false;
			m_BoltPresentationSuppressesFire = false;
			SyncAnimatorState();
			return;
		}

		CancelInvoke(nameof(ClearBoltPresentationSuppressFireOnly));
		m_BoltPresentationSuppressesFire = false;
		m_IsCyclingBolt = false;
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, false);
		SyncAnimatorState();
	}

	/// <summary>Повторный затвор между снятием и вставкой магазина (тяжёлый клин, фаза B).</summary>
	public void RestartMalfunctionBoltCycleDuringStripReload()
	{
		if (!m_IsReloadingWeapon || !m_MalfunctionStripReinsertReloadActive)
			return;

		CancelInvoke(nameof(ClearBoltPresentationSuppressFireOnly));
		m_IsCyclingBolt = true;
		m_BoltPresentationSuppressesFire = true;
		m_MalfunctionController?.NotifyBoltAnimStarting();
		SyncAnimatorState();
	}

	public bool TryStartBoltCycleOnly()
	{
		m_DebugLastFailureReason = null;

		if (m_IsReloadingWeapon || m_IsCyclingBolt)
		{
			m_DebugLastFailureReason = "Already reloading or bolting";
			return false;
		}

		if (m_CharacterInventory == null || m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
		{
			m_DebugLastFailureReason = "Missing runtime references";
			return false;
		}

		WeaponRuntimeState rs = m_WeaponRuntime.RuntimeState;
		if (!rs.HasMagazine || !rs.HasAmmoInMagazine || rs.HasRoundInChamber)
		{
			m_DebugLastFailureReason = "Bolt cycle not applicable";
			return false;
		}

		if (m_BusyState != null && m_BusyState.IsBusy)
		{
			m_DebugLastFailureReason = $"Unit is busy: {m_BusyState.Reasons}";
			return false;
		}

		CancelInvoke(nameof(ClearBoltPresentationSuppressFireOnly));
		m_IsCyclingBolt = true;
		m_BoltPresentationSuppressesFire = true;
		m_FireController?.StopFiring();
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, true);
		SyncAnimatorState();
		RefreshInventoryUiIfActive();
		return true;
	}

	/// <summary>Установка магазина из UI модификации: магазин уже снят с источника (сумка/земля), данные вставляются на animation events.</summary>
	public bool TryStartUiMagazineInstall(InventorySlotRuntimeData _magazineFromSource, bool _mirrorAnimationOnly = false)
	{
		m_DebugLastFailureReason = null;

		if (_magazineFromSource.IsEmpty)
		{
			m_DebugLastFailureReason = "Empty magazine item";
			return false;
		}

		if (!TryValidateUiMagazineModificationStart())
			return false;

		WeaponRuntimeState runtimeState = m_WeaponRuntime.RuntimeState;
		if (runtimeState != null &&
		    !runtimeState.CanAcceptMagazineItem(_magazineFromSource))
		{
			m_DebugLastFailureReason = "Magazine incompatible with equipped weapon";
			return false;
		}

		CancelInvoke(nameof(ClearBoltPresentationSuppressFireOnly));
		m_UiMagazineModificationActive = true;
		m_UiMagazineEjectOnly = false;
		m_UiMagazineInstallOnly = runtimeState == null || !runtimeState.HasMagazine;
		m_UiMagazineMirrorAnimationOnly = _mirrorAnimationOnly;
		m_UiLastEjectedMagazine = default;
		m_IsReloadingWeapon = true;
		m_IsCyclingBolt = false;
		m_BoltPresentationSuppressesFire = false;
		m_HasEjectedCurrentMagazine = m_UiMagazineInstallOnly;
		m_MagazineInsertCompletedThisReload = false;
		m_PendingReplacementMagazine = _magazineFromSource;
		m_ShouldStartManualMagazineLoadingAfterReload = false;
		m_DebugSourceBagIndex = -1;
		m_DebugFallbackMagazineBagIndex = -1;
		m_FireController?.StopFiring();
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, true);
		TryAttachPendingMagazineVisualToLeftHand();
		SyncAnimatorState();
		if (m_UiMagazineInstallOnly)
			SnapAimLayerToReloadInsertPhaseForUiInstallOnly();
		RefreshInventoryUiIfActive();
		return true;
	}

	/// <summary>Извлечение магазина из UI модификации: снятие на animation event, без вставки и затвора.</summary>
	public bool TryStartUiMagazineEject(bool _mirrorAnimationOnly = false)
	{
		m_DebugLastFailureReason = null;

		if (!TryValidateUiMagazineModificationStart())
			return false;

		WeaponRuntimeState runtimeState = m_WeaponRuntime.RuntimeState;
		if (runtimeState == null || !runtimeState.HasMagazine)
		{
			m_DebugLastFailureReason = "No magazine to eject";
			return false;
		}

		CancelInvoke(nameof(ClearBoltPresentationSuppressFireOnly));
		m_UiMagazineModificationActive = true;
		m_UiMagazineEjectOnly = true;
		m_UiMagazineInstallOnly = false;
		m_UiMagazineMirrorAnimationOnly = _mirrorAnimationOnly;
		m_UiLastEjectedMagazine = default;
		m_IsReloadingWeapon = true;
		m_IsCyclingBolt = false;
		m_BoltPresentationSuppressesFire = false;
		m_HasEjectedCurrentMagazine = false;
		m_MagazineInsertCompletedThisReload = false;
		m_PendingReplacementMagazine = default;
		m_ShouldStartManualMagazineLoadingAfterReload = false;
		m_DebugSourceBagIndex = -1;
		m_DebugFallbackMagazineBagIndex = -1;
		m_FireController?.StopFiring();
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, true);
		StopMagazineVisualTransfer();
		ClearLeftHandMagazineVisual();
		SyncAnimatorState();
		RefreshInventoryUiIfActive();
		return true;
	}
	#endregion

	#region Private Methods
	private bool TryValidateUiMagazineModificationStart()
	{
		if (m_IsReloadingWeapon || m_IsCyclingBolt)
		{
			m_DebugLastFailureReason = "Already reloading or bolting";
			return false;
		}

		if (m_CharacterInventory == null || m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
		{
			m_DebugLastFailureReason = "Missing runtime references";
			return false;
		}

		if (m_BusyState != null && m_BusyState.IsBusy)
		{
			m_DebugLastFailureReason = $"Unit is busy: {m_BusyState.Reasons}";
			return false;
		}

		return true;
	}

	private bool TryStartReloadInternal(int _preferredBagIndex)
	{
		m_DebugLastFailureReason = null;

		if (m_IsReloadingWeapon || m_IsCyclingBolt)
		{
			m_DebugLastFailureReason = "Already reloading or bolting";
			return false;
		}

		if (m_CharacterInventory == null || m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
		{
			m_DebugLastFailureReason = "Missing runtime references";
			return false;
		}

		if (m_BusyState != null && m_BusyState.IsBusy)
		{
			m_DebugLastFailureReason = $"Unit is busy: {m_BusyState.Reasons}";
			return false;
		}

		if (_preferredBagIndex < 0 && ShouldUseBoltCycleOnlyInsteadOfFullReload())
			return TryStartBoltCycleOnly();

		int fallbackMagazineBagIndex = -1;
		bool hasReplacementMagazine = TryTakeBestReplacementMagazine(_preferredBagIndex, out int sourceBagIndex, out InventorySlotRuntimeData replacementMagazine);
		if (!hasReplacementMagazine && !TryPrepareFallbackManualLoading(out fallbackMagazineBagIndex))
		{
			if (_preferredBagIndex < 0 && TryStartMagazineLoadingThenReload())
				return true;

			m_DebugLastFailureReason = "No compatible magazine in bag";
			return false;
		}

		CancelInvoke(nameof(ClearBoltPresentationSuppressFireOnly));
		m_IsReloadingWeapon = true;
		m_IsCyclingBolt = false;
		m_BoltPresentationSuppressesFire = false;
		m_HasEjectedCurrentMagazine = false;
		m_MagazineInsertCompletedThisReload = false;
		m_DebugSourceBagIndex = hasReplacementMagazine ? sourceBagIndex : -1;
		m_PendingReplacementMagazine = hasReplacementMagazine ? replacementMagazine : default;
		m_ShouldStartManualMagazineLoadingAfterReload = !hasReplacementMagazine;
		m_DebugFallbackMagazineBagIndex = hasReplacementMagazine ? -1 : fallbackMagazineBagIndex;
		m_FireController?.StopFiring();
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, true);
		TryAttachPendingMagazineVisualToLeftHand();
		SyncAnimatorState();
		RefreshInventoryUiIfActive();
		return true;
	}

	public void StopReload()
	{
		StopReloadInternal(true);
	}

	/// <summary>
	/// Ивент на момент, когда старый магазин должен выйти из оружия.
	/// При пустом оружии просто ничего не делает.
	/// </summary>
	public void AnimationEvent_EjectCurrentWeaponMagazineToInventory()
	{
		if (!m_IsReloadingWeapon || m_HasEjectedCurrentMagazine || m_WeaponRuntime == null)
			return;

		if (m_UiMagazineInstallOnly)
			return;

		TryPlayReloadSoundFromWeaponDefinition(wd => wd.ReloadMagOutSound);

		if (m_MalfunctionStripReinsertReloadActive)
		{
			GameObject detachedMagazineVisual = TryDetachWeaponMagazineVisual();
			if (m_WeaponRuntime.TryEjectMagazine(out InventorySlotRuntimeData ejectedMagazine, _syncVisual: false))
			{
				m_PendingReplacementMagazine = ejectedMagazine;
				m_HasEjectedCurrentMagazine = true;
				PresentEjectedMagazineVisual(detachedMagazineVisual, ejectedMagazine);
			}
			else if (detachedMagazineVisual != null)
				Destroy(detachedMagazineVisual);

			EnsurePendingReplacementMagazineInLeftHand();
			RefreshInventoryUiIfActive();
			return;
		}

		GameObject detachedWeaponMagazineVisual = TryDetachWeaponMagazineVisual();
		if (m_WeaponRuntime.TryEjectMagazine(out InventorySlotRuntimeData ejectedMagazineNormal, _syncVisual: false))
		{
			if (m_UiMagazineModificationActive)
				m_UiLastEjectedMagazine = ejectedMagazineNormal;

			if (m_UiMagazineModificationActive && m_UiMagazineEjectOnly)
				PresentEjectedMagazineVisual(detachedWeaponMagazineVisual, ejectedMagazineNormal);
			else if (detachedWeaponMagazineVisual != null)
				Destroy(detachedWeaponMagazineVisual);

			if (m_CharacterInventory != null &&
			    !m_UiMagazineMirrorAnimationOnly &&
			    (!m_UiMagazineModificationActive || !m_UiMagazineEjectOnly || WeaponMagazineModificationApplier.ShouldAddUiEjectedMagazineToBag))
			{
				int bagIndexBeforeAdd = m_CharacterInventory.BagCount;
				if (m_CharacterInventory.TryAdd(ejectedMagazineNormal) && m_ShouldStartManualMagazineLoadingAfterReload)
					m_DebugFallbackMagazineBagIndex = bagIndexBeforeAdd;
			}

		}
		else if (detachedWeaponMagazineVisual != null)
			Destroy(detachedWeaponMagazineVisual);

		m_HasEjectedCurrentMagazine = true;
		EnsurePendingReplacementMagazineInLeftHand();
		RefreshInventoryUiIfActive();

		if (m_UiMagazineModificationActive && m_UiMagazineEjectOnly)
		{
			if (!m_UiLastEjectedMagazine.IsEmpty)
			{
				SnapAimLayerOutOfReloadAfterUiEjectOnly();
				FinalizeReloadSequenceAndMaybeChainManualLoad();
			}
			else
				StopReloadInternal(false);
		}
	}

	/// <summary>
	/// Ивент на момент вставки нового магазина в оружие.
	/// Если сменного магазина нет, но после перезарядки планируется <see cref="m_ShouldStartManualMagazineLoadingAfterReload"/> (патроны из коробок в магазин в сумке) — на этом ивенте завершаем фазу перезарядки и запускаем зарядку.
	/// </summary>
	public void AnimationEvent_InsertPendingMagazineIntoWeapon()
	{
		if (!m_IsReloadingWeapon || m_WeaponRuntime == null)
			return;

		if (m_PendingReplacementMagazine.IsEmpty)
		{
			if (m_UiMagazineModificationActive && m_UiMagazineEjectOnly)
				return;

			if (m_ShouldStartManualMagazineLoadingAfterReload)
				FinalizeReloadSequenceAndMaybeChainManualLoad();
			return;
		}

		EnsurePendingReplacementMagazineInLeftHand();

		ItemDefinition insertedMagazineDefinition = m_PendingReplacementMagazine.Definition;
		GameObject handMagazineVisual = m_LeftHandMagazineVisualInstance;
		m_LeftHandMagazineVisualInstance = null;

		if (!m_WeaponRuntime.TryInsertMagazine(m_PendingReplacementMagazine, _syncVisual: handMagazineVisual == null))
		{
			m_LeftHandMagazineVisualInstance = handMagazineVisual;
			StopReloadInternal(true);
			return;
		}

		m_MagazineInsertCompletedThisReload = true;
		TryPlayReloadSoundFromWeaponDefinition(wd => wd.ReloadMagInSound);

		if (m_MalfunctionStripReinsertReloadActive && m_MalfunctionController != null)
			m_MalfunctionController.OnMalfunctionStripReloadInsertComplete();

		m_PendingReplacementMagazine = default;

		if (handMagazineVisual != null)
			TransferMagazineVisualToWeaponSocket(handMagazineVisual, insertedMagazineDefinition);
		else
			m_WeaponRuntime.SyncInsertedMagazineVisualFromState();

		RefreshInventoryUiIfActive();

		WeaponDefinition weaponDefinition = m_WeaponRuntime.CurrentWeaponDefinition;
		if (ShouldSkipBoltCycleAfterUiMagazineInstall())
		{
			FinalizeUiMagazineInstallOnlyWithoutBolt();
			return;
		}

		if (weaponDefinition != null && !weaponDefinition.HasBoltHoldOpenDelay)
		{
			m_BoltPresentationSuppressesFire = true;
			m_IsReloadingWeapon = false;
			m_IsCyclingBolt = true;
			SyncAnimatorState();
		}
	}

	/// <summary>
	/// Конец клипа перезарядки при удержании затвора: звук задержки и досыл патрона в патронник.
	/// Срабатывает только после успешного <see cref="AnimationEvent_InsertPendingMagazineIntoWeapon"/>, чтобы ивент не срабатывал на этапе извлечения магазина.
	/// </summary>
	public void AnimationEvent_ReloadBoltHoldOpenDelay()
	{
		if (!m_IsReloadingWeapon || m_WeaponRuntime == null)
			return;

		WeaponDefinition weaponDefinition = m_WeaponRuntime.CurrentWeaponDefinition;
		if (weaponDefinition == null || !weaponDefinition.HasBoltHoldOpenDelay)
			return;

		if (!m_MagazineInsertCompletedThisReload)
			return;

		if (ShouldSkipBoltCycleAfterUiMagazineInstall())
		{
			FinalizeUiMagazineInstallOnlyWithoutBolt();
			return;
		}

		TryPlayReloadSoundFromWeaponDefinition(wd => wd.ReloadBoltHoldOpenDelaySound);
		m_WeaponRuntime.TryChamberRoundFromMagazine();
		FinalizeReloadSequenceAndMaybeChainManualLoad();
	}

	/// <summary>
	/// Конец досыла: клип передёргивания затвора (<c>IsCyclingBolt</c>) или legacy один клип при <c>IsReloadingWeapon</c>.
	/// Звук — только <see cref="WeaponDefinition.BoltCycleSound"/>.
	/// Хвост анимации: см. <see cref="m_BoltPresentationFireTailSeconds"/> и опционально <see cref="AnimationEvent_BoltMotionPresentationFinished"/>.
	/// </summary>
	public void AnimationEvent_FinishWeaponReload()
	{
		if (!m_IsReloadingWeapon && !m_IsCyclingBolt)
			return;

		if (m_MalfunctionController != null && m_MalfunctionController.TryConsumeBoltFinishEvent())
			return;

		if (m_IsReloadingWeapon && m_ShouldStartManualMagazineLoadingAfterReload &&
			m_PendingReplacementMagazine.IsEmpty && !m_MagazineInsertCompletedThisReload)
		{
			FinalizeReloadSequenceAndMaybeChainManualLoad();
			return;
		}

		bool holdFireDuringBoltTail = m_BoltPresentationSuppressesFire;
		TryPlayBoltCycleSound();
		if (!m_UiMagazineModificationActive || !m_UiMagazineEjectOnly)
			m_WeaponRuntime?.TryChamberRoundFromMagazine();
		FinalizeReloadSequenceAndMaybeChainManualLoad();

		if (holdFireDuringBoltTail && m_BoltPresentationFireTailSeconds > 0f)
		{
			m_BoltPresentationSuppressesFire = true;
			CancelInvoke(nameof(ClearBoltPresentationSuppressFireOnly));
			Invoke(nameof(ClearBoltPresentationSuppressFireOnly), m_BoltPresentationFireTailSeconds);
		}
	}

	/// <summary>
	/// Конец клипа затвора: снять блок стрельбы раньше таймера. Необязательно, если задан <see cref="m_BoltPresentationFireTailSeconds"/>.
	/// </summary>
	public void AnimationEvent_BoltMotionPresentationFinished()
	{
		CancelInvoke(nameof(ClearBoltPresentationSuppressFireOnly));
		m_BoltPresentationSuppressesFire = false;
	}

	private void ClearBoltPresentationSuppressFireOnly()
	{
		m_BoltPresentationSuppressesFire = false;
	}

	private void FinalizeReloadSequenceAndMaybeChainManualLoad()
	{
		bool shouldStartManualMagazineLoading = m_ShouldStartManualMagazineLoadingAfterReload;
		int fallbackMagazineBagIndex = m_DebugFallbackMagazineBagIndex;
		bool wasUiMagazineModification = m_UiMagazineModificationActive;
		bool wasMirrorAnimationOnly = m_UiMagazineMirrorAnimationOnly;
		InventorySlotRuntimeData uiEjectedMagazine = m_UiLastEjectedMagazine;
		StopReloadInternal(false);

		if (wasUiMagazineModification && !wasMirrorAnimationOnly)
			UiMagazineModificationCompleted?.Invoke(uiEjectedMagazine);

		if (shouldStartManualMagazineLoading && m_MagazineLoadingController != null &&
			m_MagazineLoadingController.TryStartLoadingMagazineFromAmmoBoxes(fallbackMagazineBagIndex))
		{
			m_ShouldStartReloadAfterMagazineLoading = true;
			m_PendingReloadPreferredBagIndex = fallbackMagazineBagIndex;
		}
	}

	private bool ShouldUseBoltCycleOnlyInsteadOfFullReload()
	{
		if (m_WeaponRuntime?.RuntimeState == null)
			return false;

		WeaponRuntimeState rs = m_WeaponRuntime.RuntimeState;
		return rs.HasMagazine && rs.HasAmmoInMagazine && !rs.HasRoundInChamber;
	}
	private bool TryTakeBestReplacementMagazine(int _preferredBagIndex, out int _bagIndex, out InventorySlotRuntimeData _magazineItem)
	{
		_bagIndex = -1;
		_magazineItem = default;

		if (m_CharacterInventory == null || m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			return false;

		if (TryTakeSpecificLoadedMagazine(_preferredBagIndex, out _magazineItem))
		{
			_bagIndex = _preferredBagIndex;
			return true;
		}

		int bestAmmoCount = -1;
		bool bestIsFull = false;

		for (int i = 0; i < m_CharacterInventory.BagCount; i++)
		{
			InventorySlotRuntimeData candidate = m_CharacterInventory.BagItems[i];
			if (!m_WeaponRuntime.RuntimeState.CanAcceptMagazineItem(candidate))
				continue;

			MagazineRuntimeState candidateState = candidate.InstanceState != null ? candidate.InstanceState.MagazineState : null;
			MagazineDefinition candidateDefinition = candidate.Definition != null ? candidate.Definition.MagazineDefinition : null;
			if (candidateState == null || candidateDefinition == null)
				continue;
			if (candidateState.CurrentAmmoCount <= 0)
				continue;

			bool isFull = candidateState.CurrentAmmoCount >= candidateDefinition.Capacity;
			int ammoCount = candidateState.CurrentAmmoCount;

			if (_bagIndex >= 0)
			{
				if (bestIsFull != isFull && !isFull)
					continue;
				if (bestIsFull == isFull && ammoCount <= bestAmmoCount)
					continue;
				if (!bestIsFull && isFull)
				{
					// full magazine always wins over partial
				}
			}

			bestIsFull = isFull;
			bestAmmoCount = ammoCount;
			_bagIndex = i;
			_magazineItem = candidate;
		}

		if (_bagIndex < 0 || _magazineItem.IsEmpty)
			return false;

		return m_CharacterInventory.TryRemoveBagAt(_bagIndex, out _);
	}

	private bool TryTakeSpecificLoadedMagazine(int _bagIndex, out InventorySlotRuntimeData _magazineItem)
	{
		_magazineItem = default;

		if (m_CharacterInventory == null || _bagIndex < 0 || _bagIndex >= m_CharacterInventory.BagCount)
			return false;

		InventorySlotRuntimeData candidate = m_CharacterInventory.BagItems[_bagIndex];
		if (!m_WeaponRuntime.RuntimeState.CanAcceptMagazineItem(candidate))
			return false;

		MagazineRuntimeState candidateState = candidate.InstanceState != null ? candidate.InstanceState.MagazineState : null;
		if (candidateState == null || candidateState.CurrentAmmoCount <= 0)
			return false;

		if (!m_CharacterInventory.TryRemoveBagAt(_bagIndex, out _))
			return false;

		_magazineItem = candidate;
		return true;
	}

	private bool TryPrepareFallbackManualLoading(out int _fallbackMagazineBagIndex)
	{
		_fallbackMagazineBagIndex = -1;

		if (m_WeaponRuntime == null || m_WeaponRuntime.CurrentMagazine == null || m_CharacterInventory == null)
			return false;

		MagazineRuntimeState currentMagazine = m_WeaponRuntime.CurrentMagazine;
		if (currentMagazine.Definition == null)
			return false;
		if (currentMagazine.CurrentAmmoCount >= currentMagazine.Definition.Capacity)
			return false;

		CaliberType caliber = currentMagazine.Definition.SupportedCaliber;
		if (caliber == CaliberType.None)
			return false;

		for (int i = 0; i < m_CharacterInventory.BagCount; i++)
		{
			InventorySlotRuntimeData item = m_CharacterInventory.BagItems[i];
			AmmoContainerRuntimeState ammoContainerState = item.InstanceState != null ? item.InstanceState.AmmoContainerState : null;
			AmmoDefinition ammoDefinition = item.Definition != null ? item.Definition.AmmoDefinition : null;
			if (ammoContainerState == null || ammoDefinition == null)
				continue;
			if (!ammoContainerState.HasAmmo)
				continue;
			if (ammoDefinition.Caliber != caliber)
				continue;

			_fallbackMagazineBagIndex = -1;
			return true;
		}

		return false;
	}

	private bool TryStartMagazineLoadingThenReload()
	{
		if (m_MagazineLoadingController == null || m_CharacterInventory == null || m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			return false;

		if (!TryFindBestMagazineToLoadBeforeReload(out int bagIndex))
			return false;

		m_ShouldStartReloadAfterMagazineLoading = true;
		m_PendingReloadPreferredBagIndex = bagIndex;

		if (m_MagazineLoadingController.TryStartLoadingMagazineFromAmmoBoxes(bagIndex))
			return true;

		m_ShouldStartReloadAfterMagazineLoading = false;
		m_PendingReloadPreferredBagIndex = -1;
		return false;
	}

	private bool TryFindBestMagazineToLoadBeforeReload(out int _bagIndex)
	{
		_bagIndex = -1;

		if (m_CharacterInventory == null || m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			return false;

		int bestAmmoCount = -1;
		IReadOnlyList<InventorySlotRuntimeData> bagItems = m_CharacterInventory.BagItems;

		for (int i = 0; i < bagItems.Count; i++)
		{
			InventorySlotRuntimeData candidate = bagItems[i];
			if (!m_WeaponRuntime.RuntimeState.CanAcceptMagazineItem(candidate))
				continue;

			MagazineRuntimeState candidateState = candidate.InstanceState != null ? candidate.InstanceState.MagazineState : null;
			MagazineDefinition candidateDefinition = candidate.Definition != null ? candidate.Definition.MagazineDefinition : null;
			if (candidateState == null || candidateDefinition == null)
				continue;
			if (candidateState.CurrentAmmoCount >= candidateDefinition.Capacity)
				continue;
			if (!HasAmmoBoxForCaliber(candidateDefinition.SupportedCaliber))
				continue;
			if (candidateState.CurrentAmmoCount <= bestAmmoCount)
				continue;

			bestAmmoCount = candidateState.CurrentAmmoCount;
			_bagIndex = i;
		}

		return _bagIndex >= 0;
	}

	private bool HasAmmoBoxForCaliber(CaliberType _caliber)
	{
		if (m_CharacterInventory == null)
			return false;

		for (int i = 0; i < m_CharacterInventory.BagCount; i++)
		{
			InventorySlotRuntimeData item = m_CharacterInventory.BagItems[i];
			AmmoContainerRuntimeState ammoContainerState = item.InstanceState != null ? item.InstanceState.AmmoContainerState : null;
			AmmoDefinition ammoDefinition = item.Definition != null ? item.Definition.AmmoDefinition : null;
			if (ammoContainerState == null || ammoDefinition == null)
				continue;
			if (!ammoContainerState.HasAmmo)
				continue;
			if (ammoDefinition.Caliber != _caliber)
				continue;

			return true;
		}

		return false;
	}

	private void RefreshInventoryUiIfActive()
	{
		if (m_InventoryBindings == null)
			m_InventoryBindings = InventoryScreenBindings.Instance;
		if (m_InventoryBindings == null)
			return;
		if (m_InventoryBindings.ActiveCharacterInventory != m_CharacterInventory)
			return;

		m_InventoryBindings.RefreshActiveCharacterPanel();
	}

	private void StopReloadInternal(bool _restorePendingMagazineToBag)
	{
		CancelInvoke(nameof(ClearBoltPresentationSuppressFireOnly));
		m_BoltPresentationSuppressesFire = false;
		m_MalfunctionStripReinsertReloadActive = false;

		if (_restorePendingMagazineToBag && !m_PendingReplacementMagazine.IsEmpty && m_CharacterInventory != null)
			m_CharacterInventory.TryAdd(m_PendingReplacementMagazine);

		m_WasAimReloadBusy = false;
		m_UiMagazineModificationActive = false;
		m_UiMagazineEjectOnly = false;
		m_UiMagazineInstallOnly = false;
		m_UiMagazineMirrorAnimationOnly = false;
		m_UiLastEjectedMagazine = default;
		m_PendingReplacementMagazine = default;
		m_IsReloadingWeapon = false;
		m_IsCyclingBolt = false;
		m_HasEjectedCurrentMagazine = false;
		m_MagazineInsertCompletedThisReload = false;
		m_ShouldStartManualMagazineLoadingAfterReload = false;
		m_ShouldStartReloadAfterMagazineLoading = false;
		m_PendingReloadPreferredBagIndex = -1;
		m_DebugSourceBagIndex = -1;
		m_DebugFallbackMagazineBagIndex = -1;
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, false);
		StopMagazineVisualTransfer();
		ClearLeftHandMagazineVisual();
		SnapEquippedMagazineVisualToSocketOrigin();
		SyncAnimatorState();
		RefreshInventoryUiIfActive();
	}

	private void HandleMagazineLoadingStopped(int _bagIndex, bool _completedWithUsableMagazine)
	{
		if (!m_ShouldStartReloadAfterMagazineLoading)
			return;

		int preferredBagIndex = _bagIndex >= 0 ? _bagIndex : m_PendingReloadPreferredBagIndex;
		m_ShouldStartReloadAfterMagazineLoading = false;
		m_PendingReloadPreferredBagIndex = -1;

		if (!_completedWithUsableMagazine)
			return;

		m_MagazineLoadingController?.CompleteLoadingPresentationNow();
		TryStartReloadInternal(preferredBagIndex);
	}

	private void SyncAnimatorState()
	{
		bool reloadBusy = IsReloadBusy;
		bool enteringReload = reloadBusy && !m_WasAimReloadBusy;

		// До SetBool и поднятия веса aim-слоя: иначе 1 кадр виден Stand_Aim_Pitch_Blend (дефолт слоя).
		if (enteringReload)
			SnapAimLayerToRelaxedIdleIfNotReady();

		if (m_Animator != null)
		{
			m_Animator.SetBool(s_IsReloadingWeapon, m_IsReloadingWeapon);
			m_Animator.SetBool(s_IsCyclingBolt, m_IsCyclingBolt);
		}

		ApplyReloadAnimatorLayerWeightsIfBusy();
		m_WasAimReloadBusy = reloadBusy;
	}

	private void ResolveAnimatorLayerIndices()
	{
		if (m_Animator == null)
		{
			m_AimReloadLayerIndex = -1;
			m_MagazineLoadingLayerIndex = -1;
			return;
		}

		m_AimReloadLayerIndex = m_Animator.GetLayerIndex(AimReloadLayerName);
		m_MagazineLoadingLayerIndex = m_Animator.GetLayerIndex(UnitMagazineLoadingController.MagazineLoadingHandsLayerName);
	}

	/// <summary>
	/// До <see cref="UnitWeaponAiming"/> (55): snap relaxed idle и вес aim-слоя, чтобы не мелькал pitch-blend.
	/// </summary>
	private void ApplyReloadAnimatorLayerWeightsIfBusy()
	{
		if (m_Animator == null || !IsReloadBusy)
			return;

		if (m_AimReloadLayerIndex < 0 || m_MagazineLoadingLayerIndex < 0)
			ResolveAnimatorLayerIndices();

		if (m_AimReloadLayerIndex >= 0)
			m_Animator.SetLayerWeight(m_AimReloadLayerIndex, 1f);

		if (m_MagazineLoadingLayerIndex >= 0)
			m_Animator.SetLayerWeight(m_MagazineLoadingLayerIndex, 0f);
	}

	/// <summary>
	/// Перед relaxed-перезарядкой aim-слой должен быть в <c>Stand_Relaxed_Idle</c>, иначе при весе 1 виден рывок из pitch-blend.
	/// </summary>
	private void SnapAimLayerToRelaxedIdleIfNotReady()
	{
		if (m_Animator == null || m_Animator.GetBool(s_WeaponReady))
			return;

		if (m_AimReloadLayerIndex < 0)
			ResolveAnimatorLayerIndices();
		if (m_AimReloadLayerIndex < 0)
			return;

		if (m_Animator.GetInteger(s_Stance) != (int)LocomotionStance.Standing)
			return;

		AnimatorStateInfo stateInfo = m_Animator.GetCurrentAnimatorStateInfo(m_AimReloadLayerIndex);
		if (stateInfo.shortNameHash == s_AimRelaxedIdleStateHash ||
		    stateInfo.shortNameHash == Animator.StringToHash("Stand_Relaxed_Reload") ||
		    stateInfo.shortNameHash == Animator.StringToHash("Stand_Relaxed__CyclingBolt"))
			return;

		m_Animator.Play(s_AimRelaxedIdleStateHash, m_AimReloadLayerIndex, 0f);
	}

	/// <summary>
	/// UI eject-only: после ивента извлечения сразу выходим из клипа перезарядки, не доигрывая вставку/затвор.
	/// </summary>
	private void SnapAimLayerOutOfReloadAfterUiEjectOnly()
	{
		SnapAimLayerToRelaxedIdleAfterUiMagazineModification();
	}

	/// <summary>
	/// UI install-only: начать клип перезарядки с кадра <see cref="c_UiInstallOnlyReloadStartFrame"/> (фаза вставки).
	/// </summary>
	private void SnapAimLayerToReloadInsertPhaseForUiInstallOnly()
	{
		if (m_Animator == null)
			return;

		if (m_AimReloadLayerIndex < 0)
			ResolveAnimatorLayerIndices();
		if (m_AimReloadLayerIndex < 0)
			return;

		if (m_Animator.GetInteger(s_Stance) != (int)LocomotionStance.Standing)
			return;

		float startSeconds = c_UiInstallOnlyReloadStartFrame / c_ReloadClipSampleRate;
		float normalizedTime = Mathf.Clamp01(startSeconds / c_ReloadClipDurationSeconds);
		int reloadStateHash = m_Animator.GetBool(s_WeaponReady) ? s_AimReloadStateHash : s_AimRelaxedReloadStateHash;
		m_Animator.Play(reloadStateHash, m_AimReloadLayerIndex, normalizedTime);
	}

	private void SnapAimLayerToRelaxedIdleAfterUiMagazineModification()
	{
		if (m_Animator == null || m_Animator.GetBool(s_WeaponReady))
			return;

		if (m_AimReloadLayerIndex < 0)
			ResolveAnimatorLayerIndices();
		if (m_AimReloadLayerIndex < 0)
			return;

		if (m_Animator.GetInteger(s_Stance) != (int)LocomotionStance.Standing)
			return;

		m_Animator.Play(s_AimRelaxedIdleStateHash, m_AimReloadLayerIndex, 0f);
	}

	private void FinalizeUiMagazineInstallOnlyWithoutBolt()
	{
		SnapAimLayerToRelaxedIdleAfterUiMagazineModification();
		FinalizeReloadSequenceAndMaybeChainManualLoad();
	}

	private bool ShouldSkipBoltCycleAfterUiMagazineInstall()
	{
		return m_UiMagazineInstallOnly &&
		       m_WeaponRuntime?.RuntimeState != null &&
		       m_WeaponRuntime.RuntimeState.HasRoundInChamber;
	}

	private void TryPlayBoltCycleSound()
	{
		TryPlayReloadSoundFromWeaponDefinition(wd => wd.BoltCycleSound);
	}

	private bool ShouldDeferReplacementMagazineHandVisualUntilEject()
	{
		return m_WeaponRuntime?.RuntimeState != null && m_WeaponRuntime.RuntimeState.HasMagazine;
	}

	private void TryAttachPendingMagazineVisualToLeftHand()
	{
		if (m_PendingReplacementMagazine.IsEmpty)
			return;

		if (!m_HasEjectedCurrentMagazine && ShouldDeferReplacementMagazineHandVisualUntilEject())
			return;

		EnsurePendingReplacementMagazineInLeftHand();
	}

	private void EnsurePendingReplacementMagazineInLeftHand()
	{
		if (m_PendingReplacementMagazine.IsEmpty || m_LeftHandAnchor == null)
			return;

		if (m_LeftHandMagazineVisualInstance != null)
			return;

		AttachMagazineVisualToLeftHand(m_PendingReplacementMagazine);
	}

	private GameObject TryDetachWeaponMagazineVisual()
	{
		EquippedWeapon equippedWeapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		return equippedWeapon != null ? equippedWeapon.TryDetachInsertedMagazineVisual() : null;
	}

	private void PresentEjectedMagazineVisual(GameObject _detachedVisual, InventorySlotRuntimeData _magazineItem)
	{
		if (_magazineItem.IsEmpty)
		{
			if (_detachedVisual != null)
				Destroy(_detachedVisual);
			return;
		}

		if (_detachedVisual != null)
		{
			if (m_LeftHandMagazineVisualInstance != null)
			{
				Destroy(_detachedVisual);
				EnsurePendingReplacementMagazineInLeftHand();
				return;
			}

			TransferMagazineVisualToLeftHand(_detachedVisual);
			return;
		}

		AttachMagazineVisualToLeftHand(_magazineItem);
	}

	private void AttachMagazineVisualToLeftHand(InventorySlotRuntimeData _magazineItem)
	{
		if (_magazineItem.IsEmpty || m_LeftHandAnchor == null)
			return;

		ItemDefinition magazineDefinition = _magazineItem.Definition;
		if (magazineDefinition == null || magazineDefinition.EquippedVisualPrefab == null)
			return;

		StopMagazineVisualTransfer();
		ClearLeftHandMagazineVisual();

		m_LeftHandMagazineVisualInstance = Instantiate(magazineDefinition.EquippedVisualPrefab, m_LeftHandAnchor);
		m_LeftHandMagazineVisualInstance.transform.localPosition = magazineDefinition.RightHandLocalPosition;
		m_LeftHandMagazineVisualInstance.transform.localRotation = magazineDefinition.RightHandLocalRotation;
		DisablePhysicsOnVisual(m_LeftHandMagazineVisualInstance);
	}

	private void TransferMagazineVisualToLeftHand(GameObject _instance)
	{
		if (_instance == null || m_LeftHandAnchor == null)
			return;

		StopMagazineVisualTransfer();
		ClearLeftHandMagazineVisual();
		DisablePhysicsOnVisual(_instance);
		m_LeftHandMagazineVisualInstance = _instance;
		_instance.transform.SetParent(m_LeftHandAnchor, true);
	}

	private void TransferMagazineVisualToWeaponSocket(GameObject _instance, ItemDefinition _magazineDefinition)
	{
		EquippedWeapon equippedWeapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Transform magazineSocket = equippedWeapon != null ? equippedWeapon.MagazineSocketTransform : null;
		if (_instance == null || magazineSocket == null)
		{
			if (_instance != null)
				Destroy(_instance);
			return;
		}

		StopMagazineVisualTransfer();
		equippedWeapon.AcceptTransferredMagazineVisual(_instance, _magazineDefinition);
		DisablePhysicsOnVisual(_instance);
		_instance.transform.SetParent(magazineSocket, true);

		Vector3 targetLocalPosition = Vector3.zero;
		Quaternion targetLocalRotation = Quaternion.identity;
		m_ActiveMagazineVisualTransferInstance = _instance;
		m_ActiveMagazineVisualTransferTargetLocalPosition = targetLocalPosition;
		m_ActiveMagazineVisualTransferTargetLocalRotation = targetLocalRotation;
		m_MagazineVisualTransferCoroutine = StartCoroutine(AnimateMagazineVisualLocalTransform(
			_instance,
			targetLocalPosition,
			targetLocalRotation));
	}

	private IEnumerator AnimateMagazineVisualLocalTransform(
		GameObject _instance,
		Vector3 _targetLocalPosition,
		Quaternion _targetLocalRotation)
	{
		if (_instance == null)
			yield break;

		Vector3 startLocalPosition = _instance.transform.localPosition;
		Quaternion startLocalRotation = _instance.transform.localRotation;
		float duration = m_MagazineVisualTransferDuration;
		float elapsed = 0f;

		while (elapsed < duration)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / duration);
			float smoothT = Mathf.SmoothStep(0f, 1f, t);
			_instance.transform.localPosition = Vector3.Lerp(startLocalPosition, _targetLocalPosition, smoothT);
			_instance.transform.localRotation = Quaternion.Slerp(startLocalRotation, _targetLocalRotation, smoothT);
			yield return null;
		}

		_instance.transform.localPosition = _targetLocalPosition;
		_instance.transform.localRotation = _targetLocalRotation;
		m_MagazineVisualTransferCoroutine = null;
		m_ActiveMagazineVisualTransferInstance = null;
	}

	private void StopMagazineVisualTransfer()
	{
		if (m_MagazineVisualTransferCoroutine != null)
		{
			StopCoroutine(m_MagazineVisualTransferCoroutine);
			m_MagazineVisualTransferCoroutine = null;
		}

		FinalizeActiveMagazineVisualTransferSnap();
	}

	private void FinalizeActiveMagazineVisualTransferSnap()
	{
		if (m_ActiveMagazineVisualTransferInstance == null)
			return;

		GameObject instance = m_ActiveMagazineVisualTransferInstance;
		m_ActiveMagazineVisualTransferInstance = null;

		if (instance == null)
			return;

		EquippedWeapon equippedWeapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Transform magazineSocket = equippedWeapon != null ? equippedWeapon.MagazineSocketTransform : null;
		if (magazineSocket != null && instance.transform.parent != magazineSocket)
			instance.transform.SetParent(magazineSocket, false);

		instance.transform.localPosition = m_ActiveMagazineVisualTransferTargetLocalPosition;
		instance.transform.localRotation = m_ActiveMagazineVisualTransferTargetLocalRotation;
	}

	private void SnapEquippedMagazineVisualToSocketOrigin()
	{
		EquippedWeapon equippedWeapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		equippedWeapon?.SnapInsertedMagazineVisualToSocketOrigin();
	}

	private void ClearLeftHandMagazineVisual()
	{
		StopMagazineVisualTransfer();

		if (m_LeftHandMagazineVisualInstance == null)
			return;

		Destroy(m_LeftHandMagazineVisualInstance);
		m_LeftHandMagazineVisualInstance = null;
	}

	private static void DisablePhysicsOnVisual(GameObject _root)
	{
		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			bodies[i].isKinematic = true;
			bodies[i].detectCollisions = false;
		}

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
			colliders[i].enabled = false;

		WorldPickupItem[] pickups = _root.GetComponentsInChildren<WorldPickupItem>(true);
		for (int i = 0; i < pickups.Length; i++)
			pickups[i].enabled = false;
	}

	private void EnsureReloadAudioSource()
	{
		if (m_ReloadAudioSource != null && m_ReloadAudioSource.transform != transform)
		{
			ConfigureReloadAudioSource(m_ReloadAudioSource);
			return;
		}

		const string c_ReloadAudioChildName = "ReloadAudioSource_Auto";
		Transform child = transform.Find(c_ReloadAudioChildName);
		if (child == null)
		{
			GameObject go = new GameObject(c_ReloadAudioChildName);
			go.transform.SetParent(transform, false);
			child = go.transform;
		}

		if (!child.TryGetComponent(out m_ReloadAudioSource))
			m_ReloadAudioSource = child.gameObject.AddComponent<AudioSource>();

		m_ReloadAudioSource.playOnAwake = false;
		ConfigureReloadAudioSource(m_ReloadAudioSource);
	}

	private void ConfigureReloadAudioSource(AudioSource _source)
	{
		if (_source == null)
			return;

		_source.spatialBlend = 1f;
		_source.minDistance = m_ReloadSoundSpatialMinDistance;
		_source.maxDistance = m_ReloadSoundSpatialMaxDistance;
		_source.rolloffMode = AudioRolloffMode.Linear;
		_source.dopplerLevel = 0f;
	}

	private void TryPlayReloadSoundFromWeaponDefinition(Func<WeaponDefinition, AudioClip> _pickClip)
	{
		if (m_ReloadAudioSource == null)
			EnsureReloadAudioSource();
		if (m_ReloadAudioSource == null)
			return;

		WeaponDefinition weaponDefinition = ResolveWeaponDefinitionForReloadAudio();
		if (weaponDefinition == null)
			return;

		AudioClip clip = _pickClip(weaponDefinition);
		if (clip == null)
			return;

		Vector3 pos = transform.position;
		if (m_Equipment != null && m_Equipment.EquippedWeapon != null && m_Equipment.EquippedWeapon.BarrelTransform != null)
			pos = m_Equipment.EquippedWeapon.BarrelTransform.position;

		m_ReloadAudioSource.transform.position = pos;
		m_ReloadAudioSource.PlayOneShot(clip, weaponDefinition.ReloadSoundsVolume);
	}

	private WeaponDefinition ResolveWeaponDefinitionForReloadAudio()
	{
		if (m_WeaponRuntime != null && m_WeaponRuntime.CurrentWeaponDefinition != null)
			return m_WeaponRuntime.CurrentWeaponDefinition;

		if (m_Equipment != null && m_Equipment.EquippedDefinition != null && m_Equipment.EquippedDefinition.WeaponDefinition != null)
			return m_Equipment.EquippedDefinition.WeaponDefinition;

		return null;
	}
	#endregion
}

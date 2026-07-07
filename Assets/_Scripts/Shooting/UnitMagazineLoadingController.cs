using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ручная зарядка магазина в сумке патронами из коробок того же калибра.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(61)]
public sealed class UnitMagazineLoadingController : MonoBehaviour
{
	#region Events
	public event Action<int, bool> LoadingStopped;
	#endregion

	#region Constants
	public const string ParamIsLoadingMagazine = "IsLoadingMagazine";
	public const string MagazineLoadingHandsLayerName = "Magazine_Loading_Hands";
	private static readonly int s_IsLoadingMagazine = Animator.StringToHash(ParamIsLoadingMagazine);
	#endregion

	#region Serialized Fields
	[Tooltip("Инвентарь юнита с магазинами и коробками патронов.")]
	[SerializeField] private CharacterInventory m_CharacterInventory;
	[Tooltip("Занятость юнита на время ручной зарядки.")]
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private UnitWeaponReloadController m_WeaponReloadController;
	[Tooltip("Для обновления UI, если инвентарь этого юнита сейчас открыт.")]
	[SerializeField] private InventoryScreenBindings m_InventoryBindings;
	[Tooltip("Animator юнита. На нём можно завести bool-параметр IsLoadingMagazine для loop-анимации зарядки.")]
	[SerializeField] private Animator m_Animator;
	[Tooltip("Якорь левой руки для временного визуала магазина во время зарядки. Если пусто, пробуем взять кость LeftHand у humanoid Animator.")]
	[SerializeField] private Transform m_LeftHandAnchor;
	[Tooltip("Звук вставки патрона; если пусто — дочерний MagazineRoundLoadAudio_Auto.")]
	[SerializeField] private AudioSource m_RoundLoadAudioSource;
	[SerializeField, Min(0.01f)] private float m_RoundLoadSoundSpatialMinDistance = 1f;
	[SerializeField, Min(0.5f)] private float m_RoundLoadSoundSpatialMaxDistance = 45f;

	[Header("Animator")]
	[Tooltip("Плавное включение/выключение слоя Magazine_Loading_Hands (сек).")]
	[SerializeField, Min(0.02f)] private float m_LayerWeightFadeSeconds = 0.28f;

	[Header("Debug")]
	[SerializeField] private bool m_IsLoadingMagazine;
	[SerializeField] private int m_DebugTargetMagazineBagIndex = -1;
	[SerializeField] private string m_DebugLastFailureReason;
	[SerializeField] private int m_DebugLoadedRoundsThisSession;
	#endregion

	#region Private Fields
	private GameObject m_LeftHandMagazineVisualInstance;
	private int[] m_RoundLoadSoundShufflePermutation;
	private int m_RoundLoadSoundShuffleCursor;
	private int m_MagazineLoadingLayerIndex = -1;
	private float m_SmoothedLayerWeight;
	private Coroutine m_FinishPresentationAfterFadeCoroutine;
	#endregion

	#region Public Properties
	public bool IsLoadingMagazine => m_IsLoadingMagazine;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_CharacterInventory == null)
			m_CharacterInventory = GetComponent<CharacterInventory>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_WeaponReloadController == null)
			m_WeaponReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_InventoryBindings == null)
			m_InventoryBindings = InventoryScreenBindings.Instance;
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_LeftHandAnchor == null && m_Animator != null && m_Animator.isHuman)
			m_LeftHandAnchor = m_Animator.GetBoneTransform(HumanBodyBones.LeftHand);

		ResolveMagazineLoadingLayerIndex();
		EnsureRoundLoadAudioSource();
	}

	private void OnEnable()
	{
		m_SmoothedLayerWeight = m_IsLoadingMagazine ? 1f : 0f;
		SyncAnimatorState();
		ApplyMagazineLoadingLayerWeightImmediate(m_SmoothedLayerWeight);
	}

	private void LateUpdate()
	{
		SyncMagazineLoadingLayerWeightIfAllowed();
	}

	private void OnDisable()
	{
		StopLoadingInternal(false, true);
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Немедленно вернуть визуал основного оружия и убрать магазин с левой руки
	/// (например перед перезарядкой оружия после зарядки магазина в сумке).
	/// </summary>
	public void CompleteLoadingPresentationNow()
	{
		FinishLoadingPresentationImmediately();
	}

	public bool TryStartLoadingMagazineFromAmmoBoxes()
	{
		return TryStartLoadingMagazineFromAmmoBoxes(-1);
	}

	public bool TryStartLoadingMagazineFromAmmoBoxes(int _preferredBagIndex)
	{
		m_DebugLastFailureReason = null;

		if (m_IsLoadingMagazine)
		{
			m_DebugLastFailureReason = "Already loading";
			return false;
		}

		if (m_CharacterInventory == null)
		{
			m_DebugLastFailureReason = "Missing inventory";
			return false;
		}

		if (m_BusyState != null && m_BusyState.IsBusy)
		{
			m_DebugLastFailureReason = $"Unit is busy: {m_BusyState.Reasons}";
			return false;
		}

		if (!TryFindBestMagazineToLoad(_preferredBagIndex, out int targetMagazineIndex, out MagazineRuntimeState targetMagazineState))
		{
			m_DebugLastFailureReason = "No compatible magazine to load";
			return false;
		}

		if (!TryFindBestAmmoBoxIndex(targetMagazineState.Definition.SupportedCaliber, out _))
		{
			m_DebugLastFailureReason = "No ammo box with matching caliber";
			return false;
		}

		m_IsLoadingMagazine = true;
		m_DebugTargetMagazineBagIndex = targetMagazineIndex;
		m_DebugLoadedRoundsThisSession = 0;
		CancelFinishPresentationAfterFadeCoroutine();
		PrepareRoundLoadSoundShuffle(targetMagazineState.Definition);
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, true);
		m_UnitEquipment?.SetMainWeaponVisualActive(false);
		AttachCurrentLoadingMagazineVisualToLeftHand();
		SyncAnimatorState();
		RefreshInventoryUiIfActive();
		return true;
	}

	public void StopLoading()
	{
		StopLoadingInternal(false, false);
	}

	/// <summary>
	/// Вызывается анимационным ивентом в момент вставки одного патрона в магазин.
	/// </summary>
	public void AnimationEvent_LoadOneRoundIntoMagazine()
	{
		if (!m_IsLoadingMagazine)
			return;

		if (!TryFindCurrentLoadingMagazine(out MagazineRuntimeState targetMagazineState))
		{
			StopLoadingInternal(false);
			return;
		}

		if (!TryFindBestAmmoBoxIndex(targetMagazineState.Definition.SupportedCaliber, out int ammoBoxIndex))
		{
			StopLoadingInternal(m_DebugLoadedRoundsThisSession > 0);
			return;
		}

		if (!TryConsumeRoundFromAmmoBox(ammoBoxIndex, out AmmoDefinition ammoDefinition))
		{
			StopLoadingInternal(m_DebugLoadedRoundsThisSession > 0);
			return;
		}

		if (!targetMagazineState.TryLoadRound(ammoDefinition))
		{
			StopLoadingInternal(false);
			return;
		}

		TryPlayRoundLoadSound(targetMagazineState.Definition);

		m_DebugLoadedRoundsThisSession++;
		RefreshInventoryUiIfActive();

		if (targetMagazineState.CurrentAmmoCount >= targetMagazineState.Definition.Capacity ||
			!HasAmmoBoxForCaliber(targetMagazineState.Definition.SupportedCaliber))
		{
			StopLoadingInternal(true);
		}
	}

	/// <summary>
	/// Опциональный ивент на конец цикла анимации, если нужно корректно выйти из loop, когда заряжать уже нечего.
	/// </summary>
	public void AnimationEvent_FinishMagazineLoadingLoopIfDone()
	{
		if (!m_IsLoadingMagazine)
			return;

		if (!TryFindCurrentLoadingMagazine(out MagazineRuntimeState targetMagazineState))
		{
			StopLoadingInternal(false);
			return;
		}

		if (targetMagazineState.CurrentAmmoCount >= targetMagazineState.Definition.Capacity ||
			!HasAmmoBoxForCaliber(targetMagazineState.Definition.SupportedCaliber))
		{
			StopLoadingInternal(true);
		}
	}
	#endregion

	#region Private Methods
	private bool TryFindBestMagazineToLoad(int _preferredBagIndex, out int _bagIndex, out MagazineRuntimeState _magazineState)
	{
		_bagIndex = -1;
		_magazineState = null;

		if (m_CharacterInventory == null)
			return false;

		if (TryGetLoadableMagazineStateAt(_preferredBagIndex, out _magazineState))
		{
			_bagIndex = _preferredBagIndex;
			return true;
		}

		IReadOnlyList<InventorySlotRuntimeData> bagItems = m_CharacterInventory.BagItems;
		int bestAmmoCount = int.MaxValue;

		for (int i = 0; i < bagItems.Count; i++)
		{
			InventorySlotRuntimeData item = bagItems[i];
			MagazineRuntimeState magazineState = item.InstanceState != null ? item.InstanceState.MagazineState : null;
			MagazineDefinition magazineDefinition = item.Definition != null ? item.Definition.MagazineDefinition : null;
			if (magazineState == null || magazineDefinition == null)
				continue;
			if (magazineState.Definition == null)
				magazineState.Configure(magazineDefinition, magazineState.LoadedAmmoDefinition, magazineState.CurrentAmmoCount);
			if (magazineState.Definition == null)
				continue;
			if (magazineState.CurrentAmmoCount >= magazineState.Definition.Capacity)
				continue;
			if (!HasAmmoBoxForCaliber(magazineState.Definition.SupportedCaliber))
				continue;
			if (magazineState.CurrentAmmoCount >= bestAmmoCount)
				continue;

			bestAmmoCount = magazineState.CurrentAmmoCount;
			_bagIndex = i;
			_magazineState = magazineState;
		}

		return _bagIndex >= 0 && _magazineState != null;
	}

	private bool TryGetLoadableMagazineStateAt(int _bagIndex, out MagazineRuntimeState _magazineState)
	{
		_magazineState = null;

		if (m_CharacterInventory == null || _bagIndex < 0 || _bagIndex >= m_CharacterInventory.BagCount)
			return false;

		InventorySlotRuntimeData item = m_CharacterInventory.BagItems[_bagIndex];
		MagazineRuntimeState magazineState = item.InstanceState != null ? item.InstanceState.MagazineState : null;
		MagazineDefinition magazineDefinition = item.Definition != null ? item.Definition.MagazineDefinition : null;
		if (magazineState == null || magazineDefinition == null)
			return false;
		if (magazineState.Definition == null)
			magazineState.Configure(magazineDefinition, magazineState.LoadedAmmoDefinition, magazineState.CurrentAmmoCount);
		if (magazineState.Definition == null)
			return false;
		if (magazineState.CurrentAmmoCount >= magazineState.Definition.Capacity)
			return false;
		if (!HasAmmoBoxForCaliber(magazineState.Definition.SupportedCaliber))
			return false;

		_magazineState = magazineState;
		return true;
	}

	private bool TryFindCurrentLoadingMagazine(out MagazineRuntimeState _magazineState)
	{
		_magazineState = null;

		if (m_CharacterInventory == null || m_DebugTargetMagazineBagIndex < 0 || m_DebugTargetMagazineBagIndex >= m_CharacterInventory.BagCount)
			return false;

		InventorySlotRuntimeData item = m_CharacterInventory.BagItems[m_DebugTargetMagazineBagIndex];
		_magazineState = item.InstanceState != null ? item.InstanceState.MagazineState : null;
		return _magazineState != null && _magazineState.Definition != null;
	}

	private bool HasAmmoBoxForCaliber(CaliberType _caliber)
	{
		return TryFindBestAmmoBoxIndex(_caliber, out _);
	}

	private bool TryFindBestAmmoBoxIndex(CaliberType _caliber, out int _bagIndex)
	{
		_bagIndex = -1;

		if (m_CharacterInventory == null)
			return false;

		IReadOnlyList<InventorySlotRuntimeData> bagItems = m_CharacterInventory.BagItems;
		int bestAmmoCount = -1;

		for (int i = 0; i < bagItems.Count; i++)
		{
			InventorySlotRuntimeData item = bagItems[i];
			AmmoContainerRuntimeState ammoContainerState = item.InstanceState != null ? item.InstanceState.AmmoContainerState : null;
			AmmoDefinition ammoDefinition = item.Definition != null ? item.Definition.AmmoDefinition : null;
			if (ammoContainerState == null || ammoDefinition == null)
				continue;
			if (!ammoContainerState.HasAmmo)
				continue;
			if (ammoDefinition.Caliber != _caliber)
				continue;
			if (ammoContainerState.CurrentAmmoCount <= bestAmmoCount)
				continue;

			bestAmmoCount = ammoContainerState.CurrentAmmoCount;
			_bagIndex = i;
		}

		return _bagIndex >= 0;
	}

	private bool TryConsumeRoundFromAmmoBox(int _bagIndex, out AmmoDefinition _ammoDefinition)
	{
		_ammoDefinition = null;

		if (m_CharacterInventory == null || _bagIndex < 0 || _bagIndex >= m_CharacterInventory.BagCount)
			return false;

		InventorySlotRuntimeData bagItem = m_CharacterInventory.BagItems[_bagIndex];
		AmmoContainerRuntimeState ammoContainerState = bagItem.InstanceState != null ? bagItem.InstanceState.AmmoContainerState : null;
		if (ammoContainerState == null || !ammoContainerState.HasAmmo)
			return false;

		_ammoDefinition = ammoContainerState.AmmoDefinition;
		if (!ammoContainerState.TryConsumeRound())
			return false;

		if (!ammoContainerState.HasAmmo)
		{
			bool removed = m_CharacterInventory.TryRemoveBagAt(_bagIndex, out _);
			if (removed && _bagIndex < m_DebugTargetMagazineBagIndex)
				m_DebugTargetMagazineBagIndex--;
			return removed;
		}

		return m_CharacterInventory.TrySetBagItemAt(_bagIndex, bagItem);
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

	private void StopLoadingInternal(bool _completedNaturally, bool _immediatePresentation = false)
	{
		int stoppedBagIndex = m_DebugTargetMagazineBagIndex;
		bool hasAmmoAfterStop = false;
		if (m_CharacterInventory != null &&
		    stoppedBagIndex >= 0 &&
		    stoppedBagIndex < m_CharacterInventory.BagCount)
		{
			InventorySlotRuntimeData bagItem = m_CharacterInventory.BagItems[stoppedBagIndex];
			MagazineRuntimeState magazineState = bagItem.InstanceState != null ? bagItem.InstanceState.MagazineState : null;
			hasAmmoAfterStop = magazineState != null && magazineState.CurrentAmmoCount > 0;
		}

		m_IsLoadingMagazine = false;
		m_DebugTargetMagazineBagIndex = -1;
		m_RoundLoadSoundShufflePermutation = null;
		m_RoundLoadSoundShuffleCursor = 0;
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, false);
		CancelFinishPresentationAfterFadeCoroutine();
		SyncAnimatorState();
		RefreshInventoryUiIfActive();

		if (_immediatePresentation)
			FinishLoadingPresentationImmediately();
		else
			m_FinishPresentationAfterFadeCoroutine = StartCoroutine(FinishLoadingPresentationAfterLayerFade());

		bool completedNaturally = _completedNaturally && hasAmmoAfterStop;
		if (isActiveAndEnabled && gameObject.activeInHierarchy)
			StartCoroutine(InvokeLoadingStoppedNextFrame(stoppedBagIndex, completedNaturally));
		else
			LoadingStopped?.Invoke(stoppedBagIndex, completedNaturally);
	}

	private IEnumerator InvokeLoadingStoppedNextFrame(int _bagIndex, bool _completedNaturally)
	{
		yield return null;
		LoadingStopped?.Invoke(_bagIndex, _completedNaturally);
	}

	private void SyncAnimatorState()
	{
		if (m_Animator == null)
			return;

		m_Animator.SetBool(s_IsLoadingMagazine, m_IsLoadingMagazine);
		SyncMagazineLoadingLayerWeightIfAllowed();
	}

	private void SyncMagazineLoadingLayerWeightIfAllowed()
	{
		if (m_Animator == null)
			return;

		if (m_MagazineLoadingLayerIndex < 0)
			ResolveMagazineLoadingLayerIndex();
		if (m_MagazineLoadingLayerIndex < 0)
			return;

		float targetWeight = m_IsLoadingMagazine ? 1f : 0f;
		float fadeSeconds = Mathf.Max(0.02f, m_LayerWeightFadeSeconds);
		m_SmoothedLayerWeight = Mathf.MoveTowards(m_SmoothedLayerWeight, targetWeight, Time.deltaTime / fadeSeconds);

		bool weaponReloadOwnsLayers = m_WeaponReloadController != null && m_WeaponReloadController.IsReloadBusy;
		if (!weaponReloadOwnsLayers)
			m_Animator.SetLayerWeight(m_MagazineLoadingLayerIndex, m_SmoothedLayerWeight);
	}

	private void ApplyMagazineLoadingLayerWeightImmediate(float _weight)
	{
		if (m_Animator == null)
			return;

		if (m_MagazineLoadingLayerIndex < 0)
			ResolveMagazineLoadingLayerIndex();
		if (m_MagazineLoadingLayerIndex < 0)
			return;

		m_SmoothedLayerWeight = _weight;
		m_Animator.SetLayerWeight(m_MagazineLoadingLayerIndex, m_SmoothedLayerWeight);
	}

	private IEnumerator FinishLoadingPresentationAfterLayerFade()
	{
		const float c_WeightEpsilon = 0.02f;
		while (m_SmoothedLayerWeight > c_WeightEpsilon)
			yield return null;

		m_UnitEquipment?.SetMainWeaponVisualActive(true);
		ClearLeftHandMagazineVisual();
		m_FinishPresentationAfterFadeCoroutine = null;
	}

	private void CancelFinishPresentationAfterFadeCoroutine()
	{
		if (m_FinishPresentationAfterFadeCoroutine == null)
			return;

		StopCoroutine(m_FinishPresentationAfterFadeCoroutine);
		m_FinishPresentationAfterFadeCoroutine = null;
	}

	private void FinishLoadingPresentationImmediately()
	{
		CancelFinishPresentationAfterFadeCoroutine();
		m_UnitEquipment?.SetMainWeaponVisualActive(true);
		ClearLeftHandMagazineVisual();
		ApplyMagazineLoadingLayerWeightImmediate(0f);
	}

	private void ResolveMagazineLoadingLayerIndex()
	{
		if (m_Animator == null)
		{
			m_MagazineLoadingLayerIndex = -1;
			return;
		}

		m_MagazineLoadingLayerIndex = m_Animator.GetLayerIndex(MagazineLoadingHandsLayerName);
	}

	private void AttachCurrentLoadingMagazineVisualToLeftHand()
	{
		ClearLeftHandMagazineVisual();

		if (m_LeftHandAnchor == null || m_CharacterInventory == null)
			return;
		if (m_DebugTargetMagazineBagIndex < 0 || m_DebugTargetMagazineBagIndex >= m_CharacterInventory.BagCount)
			return;

		InventorySlotRuntimeData magazineItem = m_CharacterInventory.BagItems[m_DebugTargetMagazineBagIndex];
		ItemDefinition magazineDefinition = magazineItem.Definition;
		if (magazineDefinition == null || magazineDefinition.EquippedVisualPrefab == null)
			return;

		m_LeftHandMagazineVisualInstance = Instantiate(magazineDefinition.EquippedVisualPrefab, m_LeftHandAnchor);
		m_LeftHandMagazineVisualInstance.transform.localPosition = magazineDefinition.RightHandLocalPosition;
		m_LeftHandMagazineVisualInstance.transform.localRotation = magazineDefinition.RightHandLocalRotation;
		DisablePhysicsOnLoadingVisual(m_LeftHandMagazineVisualInstance);
	}

	private void ClearLeftHandMagazineVisual()
	{
		if (m_LeftHandMagazineVisualInstance == null)
			return;

		Destroy(m_LeftHandMagazineVisualInstance);
		m_LeftHandMagazineVisualInstance = null;
	}
	private void EnsureRoundLoadAudioSource()
	{
		if (m_RoundLoadAudioSource != null && m_RoundLoadAudioSource.transform != transform)
		{
			ConfigureRoundLoadAudioSource(m_RoundLoadAudioSource);
			return;
		}

		const string c_Name = "MagazineRoundLoadAudio_Auto";
		Transform child = transform.Find(c_Name);
		if (child == null)
		{
			GameObject go = new GameObject(c_Name);
			go.transform.SetParent(transform, false);
			child = go.transform;
		}

		if (!child.TryGetComponent(out m_RoundLoadAudioSource))
			m_RoundLoadAudioSource = child.gameObject.AddComponent<AudioSource>();

		m_RoundLoadAudioSource.playOnAwake = false;
		ConfigureRoundLoadAudioSource(m_RoundLoadAudioSource);
	}

	private void ConfigureRoundLoadAudioSource(AudioSource _source)
	{
		if (_source == null)
			return;

		UnitNonFireAudioUtility.ConfigureSpatial(_source, m_RoundLoadSoundSpatialMaxDistance);
	}

	private void PrepareRoundLoadSoundShuffle(MagazineDefinition _definition)
	{
		m_RoundLoadSoundShufflePermutation = null;
		m_RoundLoadSoundShuffleCursor = 0;

		if (_definition == null)
			return;

		AudioClip[] clips = _definition.RoundLoadSounds;
		if (clips == null || clips.Length == 0)
			return;

		int count = 0;
		for (int i = 0; i < clips.Length; i++)
		{
			if (clips[i] != null)
				count++;
		}

		if (count == 0)
			return;

		m_RoundLoadSoundShufflePermutation = new int[count];
		int w = 0;
		for (int i = 0; i < clips.Length; i++)
		{
			if (clips[i] != null)
				m_RoundLoadSoundShufflePermutation[w++] = i;
		}

		ShuffleIntArrayInPlace(m_RoundLoadSoundShufflePermutation);
	}

	private void TryPlayRoundLoadSound(MagazineDefinition _definition)
	{
		if (_definition == null || m_RoundLoadAudioSource == null)
			return;

		if (m_RoundLoadAudioSource.transform == transform)
			EnsureRoundLoadAudioSource();
		if (m_RoundLoadAudioSource == null || m_RoundLoadAudioSource.transform == transform)
			return;

		AudioClip[] clips = _definition.RoundLoadSounds;
		if (clips == null || clips.Length == 0 || m_RoundLoadSoundShufflePermutation == null
			|| m_RoundLoadSoundShufflePermutation.Length == 0)
			return;

		if (m_RoundLoadSoundShuffleCursor >= m_RoundLoadSoundShufflePermutation.Length)
		{
			PrepareRoundLoadSoundShuffle(_definition);
			if (m_RoundLoadSoundShufflePermutation == null || m_RoundLoadSoundShufflePermutation.Length == 0)
				return;
		}

		int clipIndex = m_RoundLoadSoundShufflePermutation[m_RoundLoadSoundShuffleCursor++];
		AudioClip clip = clips[clipIndex];
		if (clip == null)
			return;

		Vector3 pos = m_LeftHandAnchor != null ? m_LeftHandAnchor.position : transform.position;
		m_RoundLoadAudioSource.transform.position = pos;
		m_RoundLoadAudioSource.PlayOneShot(
			clip,
			UnitNonFireAudioUtility.ScaleVolume(_definition.RoundLoadSoundsVolume));
	}

	private static void ShuffleIntArrayInPlace(int[] _indices)
	{
		if (_indices == null || _indices.Length <= 1)
			return;

		for (int i = _indices.Length - 1; i > 0; i--)
		{
			int j = UnityEngine.Random.Range(0, i + 1);
			(_indices[i], _indices[j]) = (_indices[j], _indices[i]);
		}
	}

	private static void DisablePhysicsOnLoadingVisual(GameObject _root)
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
	#endregion
}

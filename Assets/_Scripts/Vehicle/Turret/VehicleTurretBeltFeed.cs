using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(52)]
public sealed class VehicleTurretBeltFeed : MonoBehaviour
{
	#region Nested Types
	[System.Serializable]
	private struct BeltSlot
	{
		public Vector3 LocalPosition;
		public Quaternion LocalRotation;
	}

	private class BeltRound
	{
		public Transform Transform;
		public int CurrentSlot;
		public int TargetSlot;
		public float Progress;
		public bool Visible;
		public Vector3 InertiaOvershoot;
		public float WobbleTimer;
		public float WobbleDuration;
		public float WobblePitch;
		public float WobbleRoll;
		public Vector3 JitterOffset;
		public float JitterTimer;
		public float JitterDuration;
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private VehicleTurretGunnerBridge m_Bridge;
	[SerializeField] private VehicleTurretHierarchyBinder m_Hierarchy;
	[SerializeField] private VehicleTurretEquipmentController m_Equipment;
	[SerializeField] private VehicleInventory m_Inventory;
	[SerializeField] private VehicleTurretReloadController m_ReloadController;

	[Header("Belt Template")]
	[Tooltip("M2: parent object containing 12_7 template children.")]
	[SerializeField] private Transform m_BulletBeltRoot;
	[Tooltip("MK19: parent object containing 40mm grenade.001 template children.")]
	[SerializeField] private Transform m_Mk19BeltRoot;
	[SerializeField] private int m_SlotCount = 8;

	[Header("Baked Slots (optional)")]
	[Tooltip("Slot poses used when belt root has no template children. Bake from scene via context menu.")]
	[SerializeField] private BeltSlot[] m_M2BakedSlots;
	[SerializeField] private BeltSlot[] m_Mk19BakedSlots;

	[Header("Round Prefabs (optional)")]
	[Tooltip("Pooled round mesh when belt root templates are removed.")]
	[SerializeField] private GameObject m_M2RoundPrefab;
	[SerializeField] private GameObject m_Mk19RoundPrefab;

	[Header("Pool")]
	[SerializeField] private int m_PoolCapacity = 10;
	[SerializeField] private int m_VisibleRounds = 7;

	[Header("Movement")]
	[Tooltip("Fire interval in seconds (= 60 / RPM).")]
	[SerializeField, Min(0.01f)] private float m_FireInterval = 0.124f;

	[Header("Wave")]
	[SerializeField, Min(0f)] private float m_WaveAmplitude = 0.012f;
	[SerializeField, Min(0.01f)] private float m_WaveDuration = 0.15f;
	[SerializeField, Min(0f)] private float m_WaveSpeed = 1f;

	[Header("Propagation Delay")]
	[SerializeField, Min(0f)] private float m_PropagationDelayPerSlot = 0.005f;

	[Header("Inertia")]
	[SerializeField, Min(0f)] private float m_InertiaOvershootDistance = 0.002f;
	[SerializeField, Min(0.01f)] private float m_InertiaReturnDuration = 0.08f;

	[Header("Wobble")]
	[SerializeField, Min(0f)] private float m_WobbleMaxAngle = 2f;
	[SerializeField, Min(0.01f)] private float m_WobbleDuration = 0.2f;

	[Header("Micro Jitter")]
	[SerializeField, Min(0f)] private float m_JitterMaxDistance = 0.002f;
	[SerializeField, Min(0.01f)] private float m_JitterDuration = 0.08f;

	[Header("Stretch")]
	[SerializeField, Range(0f, 0.05f)] private float m_StretchFactor = 0.02f;

	[Header("Magazine Box")]
	[SerializeField, Min(0f)] private float m_MagKickDistance = 0.0005f;
	[SerializeField, Min(0.01f)] private float m_MagKickDuration = 0.06f;

	[Header("Idle")]
	[SerializeField, Range(0f, 0.002f)] private float m_IdleAmplitude = 0.0005f;
	[SerializeField, Min(1f)] private float m_IdleInterval = 3f;

	[Header("LOD")]
	[Tooltip("Max distance from camera at which the belt is visible.")]
	[SerializeField, Min(1f)] private float m_BeltLodMaxDistance = 25f;
	[Tooltip("How often to re-check camera distance (seconds).")]
	[SerializeField, Min(0.1f)] private float m_LodCheckInterval = 0.5f;
	#endregion

	#region Private Fields
	private BeltSlot[] m_Slots;
	private BeltRound[] m_Rounds;
	private Transform m_PoolRoot;
	private int m_PoolIndex;
	private bool m_Subscribed;
	private bool m_HadAmmoLastFrame;
	private TurretWeaponVariant m_LastVariant;
	private TurretWeaponVariant m_PoolVariant = TurretWeaponVariant.None;
	private float m_WaveTimer;
	private float m_QueueVibrationTimer;
	private float m_QueueVibrationAmplitude;
	private float m_IdleTimer;
	private float m_IdlePhase;
	private Transform m_MagTransform;
	private Vector3 m_MagRestLocalPos;
	private float m_MagKickTimer;
	private Vector3 m_MagKickOffset;
	private Camera m_CachedCamera;
	private float m_NextLodCheckTime;
	private bool m_IsLodVisible = true;
	private bool m_ReloadBeltSuppressed;
	private int m_VisualAmmoOverride = -1;
	#endregion

	#region Public Methods — Reload Belt
	public void HideBeltForReload()
	{
		m_ReloadBeltSuppressed = true;
		m_VisualAmmoOverride = -1;
		HideAllVisibleRounds();
	}

	public void ShowBeltForReload(int _visualAmmoCount)
	{
		m_ReloadBeltSuppressed = false;
		m_VisualAmmoOverride = Mathf.Max(0, _visualAmmoCount);
		RebuildBeltVisual();
	}

	public void ClearReloadBeltVisualOverride()
	{
		m_ReloadBeltSuppressed = false;
		m_VisualAmmoOverride = -1;
		if (GetAmmoRemaining() > 0)
			RebuildBeltVisual();
		else
			HideAllVisibleRounds();
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Bridge == null) TryGetComponent(out m_Bridge);
		if (m_Hierarchy == null) TryGetComponent(out m_Hierarchy);
		if (m_Equipment == null) TryGetComponent(out m_Equipment);
		if (m_Inventory == null) TryGetComponent(out m_Inventory);
		if (m_ReloadController == null) TryGetComponent(out m_ReloadController);
		m_Hierarchy?.EnsureBound();
		ResolveBeltRootsIfNeeded();
		SuppressAllBeltTemplateVisuals();
	}

	private void Start()
	{
		TurretWeaponVariant variant = ResolveActiveVariant();
		m_LastVariant = variant;
		InitializeSlots();
		EnsurePoolForVariant(variant);
		InitializeRounds();
		SaveMagRestPose();
	}

	private void OnEnable()
	{
		TrySubscribe();
		if (m_Inventory != null)
			m_Inventory.InventoryChanged += HandleInventoryChanged;
	}

	private void OnDisable()
	{
		TryUnsubscribe();
		if (m_Inventory != null)
			m_Inventory.InventoryChanged -= HandleInventoryChanged;
		FullCleanup();
	}

	private void Update()
	{
		if (m_Inventory == null || !m_Inventory.HasTurretWeapon)
		{
			TryUnsubscribe();
			HideAllVisibleRounds();
			m_LastVariant = TurretWeaponVariant.None;
			return;
		}

		if (m_Bridge == null || !m_Bridge.HasBoundGunner)
		{
			if (m_Subscribed)
				TryUnsubscribe();
			return;
		}

		TrySubscribe();

		TurretWeaponVariant currentVariant = m_Equipment?.ActiveWeaponItem?.TurretWeaponVariant ?? TurretWeaponVariant.None;
		if (currentVariant != m_LastVariant)
		{
			m_LastVariant = currentVariant;
			InitializeSlots();
			UpdateMagTransformForVariant();
			EnsurePoolForVariant(currentVariant);
			SuppressAllBeltTemplateVisuals();
			if (!m_ReloadBeltSuppressed)
				RebuildBeltVisual();
		}

		if (m_ReloadBeltSuppressed)
			return;

		if (m_Rounds == null || m_Slots == null)
			return;

		int ammo = GetEffectiveAmmoRemaining();
		if (ammo <= 0 && CountVisibleRounds() > 0 && m_VisualAmmoOverride < 0)
		{
			HideAllVisibleRounds();
		}
		else if (ammo > 0 && CountVisibleRounds() == 0)
		{
			RebuildBeltVisual();
		}
		m_HadAmmoLastFrame = ammo > 0;

		float dt = Time.deltaTime;
		UpdateLod();

		if (!m_IsLodVisible)
			return;

		UpdateMovement(dt);
		UpdateWaveAnimation(dt);
		UpdateInertia(dt);
		UpdateWobble(dt);
		UpdateJitter(dt);
		UpdateMagKick(dt);
		UpdateIdle(dt);

		ApplyAllRounds();
		ApplyMagPose();
	}

	private void HandleInventoryChanged(VehicleInventory _)
	{
		if (m_Inventory == null || !m_Inventory.HasTurretWeapon)
		{
			FullCleanup();
			return;
		}

		m_LastVariant = TurretWeaponVariant.None;
	}
	#endregion

	#region Subscription
	private void TrySubscribe()
	{
		if (m_Subscribed || m_Bridge == null || !m_Bridge.HasBoundGunner)
			return;
		var fc = m_Bridge.BoundGunner.GetComponent<UnitWeaponFireController>();
		if (fc == null)
			return;
		fc.ShotFired += HandleShotFired;
		m_Subscribed = true;
	}

	private void TryUnsubscribe()
	{
		if (!m_Subscribed)
			return;
		if (m_Bridge != null && m_Bridge.HasBoundGunner)
			m_Bridge.BoundGunner.GetComponent<UnitWeaponFireController>().ShotFired -= HandleShotFired;
		m_Subscribed = false;
	}
	#endregion

	#region Initialization
	private TurretWeaponVariant ResolveActiveVariant()
	{
		TurretWeaponVariant variant = m_Equipment?.ActiveWeaponItem?.TurretWeaponVariant ?? TurretWeaponVariant.None;
		if (variant != TurretWeaponVariant.None)
			return variant;

		if (m_Inventory != null && m_Inventory.HasTurretWeapon && !m_Inventory.TurretWeapon.IsEmpty)
		{
			variant = m_Inventory.TurretWeapon.Definition?.TurretWeaponVariant ?? TurretWeaponVariant.None;
			if (variant != TurretWeaponVariant.None)
				return variant;
		}

		return TurretWeaponVariant.Browning127;
	}

	private Transform GetBeltRootForVariant(TurretWeaponVariant _variant)
	{
		return _variant == TurretWeaponVariant.Mk19 ? m_Mk19BeltRoot : m_BulletBeltRoot;
	}

	private Transform GetActiveBeltRoot()
	{
		return GetBeltRootForVariant(ResolveActiveVariant());
	}

	private void ResolveBeltRootsIfNeeded()
	{
		if (m_BulletBeltRoot == null && m_Hierarchy?.Gun127 != null)
			m_BulletBeltRoot = FindDeepChild(m_Hierarchy.Gun127, "BulletBelt");
		if (m_Mk19BeltRoot == null && m_Hierarchy?.Mk19 != null)
		{
			m_Mk19BeltRoot = FindDeepChild(m_Hierarchy.Mk19, "belt");
			if (m_Mk19BeltRoot == null)
				m_Mk19BeltRoot = FindDeepChild(m_Hierarchy.Mk19, "MK19_1");
		}
	}

	private void SuppressAllBeltTemplateVisuals()
	{
		SuppressBeltTemplateVisuals(m_BulletBeltRoot);
		SuppressBeltTemplateVisuals(m_Mk19BeltRoot);
	}

	private static bool IsMk19NonBeltChild(Transform _child)
	{
		if (_child == null)
			return true;

		string name = _child.name;
		return name == VehicleTurretCombatSockets.Mk19HandleName
		       || name == VehicleTurretCombatSockets.Mk19BoltVisualName
		       || name == EquippedWeapon.MuzzleExitTransformName
		       || name == VehicleTurretCombatSockets.Mk19ShellEjectName
		       || name == VehicleTurretReloadController.LeftHandIkNotReadyHandleName
		       || name == VehicleTurretReloadController.RightHandIkNotReadyHandleName;
	}

	private static void SuppressBeltTemplateVisuals(Transform _beltRoot)
	{
		if (_beltRoot == null)
			return;

		for (int i = 0; i < _beltRoot.childCount; i++)
		{
			Transform child = _beltRoot.GetChild(i);
			if (child == null || IsMk19NonBeltChild(child))
				continue;

			child.gameObject.SetActive(false);
			MeshRenderer[] renderers = child.GetComponentsInChildren<MeshRenderer>(true);
			for (int r = 0; r < renderers.Length; r++)
			{
				if (renderers[r] != null)
					renderers[r].enabled = false;
			}
		}
	}

	private static Transform FindDeepChild(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;

		Transform[] all = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != null && all[i].name == _name)
				return all[i];
		}

		return null;
	}

	private static GameObject FindBeltRoundTemplate(Transform _beltRoot, TurretWeaponVariant _variant)
	{
		if (_beltRoot == null || _beltRoot.childCount == 0)
			return null;

		string prefix = _variant == TurretWeaponVariant.Mk19 ? "40mm" : "12_7";
		for (int i = 0; i < _beltRoot.childCount; i++)
		{
			Transform child = _beltRoot.GetChild(i);
			if (child.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
				return child.gameObject;
		}

		return _beltRoot.GetChild(0).gameObject;
	}

	private GameObject GetRoundPrefab(TurretWeaponVariant _variant)
	{
		return _variant == TurretWeaponVariant.Mk19 ? m_Mk19RoundPrefab : m_M2RoundPrefab;
	}

	private BeltSlot[] GetBakedSlots(TurretWeaponVariant _variant)
	{
		return _variant == TurretWeaponVariant.Mk19 ? m_Mk19BakedSlots : m_M2BakedSlots;
	}

	private bool TryCaptureSlotsFromChildren(Transform _beltRoot, out BeltSlot[] _slots)
	{
		_slots = null;
		if (_beltRoot == null || _beltRoot.childCount == 0)
			return false;

		string prefix = InferRuntimeTemplatePrefix(_beltRoot);
		var captured = new System.Collections.Generic.List<BeltSlot>(_beltRoot.childCount);
		for (int i = 0; i < _beltRoot.childCount; i++)
		{
			Transform child = _beltRoot.GetChild(i);
			if (child == null || IsMk19NonBeltChild(child))
				continue;
			if (prefix != null &&
			    !child.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
				continue;

			captured.Add(new BeltSlot
			{
				LocalPosition = child.localPosition,
				LocalRotation = child.localRotation
			});
		}

		if (captured.Count == 0)
			return false;

		_slots = captured.ToArray();
		if (prefix == "40mm")
			_slots = BuildMk19FeedOrderedSlots(_slots);

		m_SlotCount = _slots.Length;
		SuppressBeltTemplateVisuals(_beltRoot);
		return true;
	}

	private bool TryApplyBakedSlots(TurretWeaponVariant _variant)
	{
		BeltSlot[] baked = GetBakedSlots(_variant);
		if (baked == null || baked.Length == 0)
			return false;

		// Prefer live template children when present — baked data can go stale after hierarchy restores.
		Transform beltRoot = GetBeltRootForVariant(_variant);
		int templateCount = CountBeltTemplateChildren(beltRoot, _variant);
		if (templateCount > 0)
		{
			// Always rebuild MK19 from live templates so feed order/poses stay correct.
			if (_variant == TurretWeaponVariant.Mk19)
				return false;

			if (templateCount != baked.Length)
				return false;
		}

		m_Slots = new BeltSlot[baked.Length];
		for (int i = 0; i < baked.Length; i++)
			m_Slots[i] = baked[i];
		m_SlotCount = baked.Length;
		return true;
	}

	/// <summary>
	/// Belt feed expects slot 0 = magazine side, last = chamber/feed.
	/// MK19 mesh templates are authored chamber→mag; reverse and optionally densify the chain.
	/// </summary>
	private static BeltSlot[] BuildMk19FeedOrderedSlots(BeltSlot[] _captured)
	{
		if (_captured == null || _captured.Length == 0)
			return _captured;

		BeltSlot[] ordered = (BeltSlot[])_captured.Clone();
		if (ordered.Length >= 2)
		{
			float firstDist = ordered[0].LocalPosition.sqrMagnitude;
			float lastDist = ordered[ordered.Length - 1].LocalPosition.sqrMagnitude;
			// Closer to belt origin ≈ chamber/receiver; farther ≈ hanging mag side.
			if (firstDist < lastDist)
				System.Array.Reverse(ordered);
		}

		const int desiredCount = 6;
		if (ordered.Length >= desiredCount)
			return ordered;

		return DensifyBeltSlots(ordered, desiredCount);
	}

	private static BeltSlot[] DensifyBeltSlots(BeltSlot[] _source, int _desiredCount)
	{
		if (_source == null || _source.Length == 0 || _desiredCount <= _source.Length)
			return _source;

		var result = new System.Collections.Generic.List<BeltSlot>(_desiredCount);
		int segments = _source.Length - 1;
		if (segments <= 0)
		{
			for (int i = 0; i < _desiredCount; i++)
				result.Add(_source[0]);
			return result.ToArray();
		}

		// Extrapolate past magazine end (index 0) so the hanging belt looks longer.
		int extraMag = _desiredCount - _source.Length;
		Vector3 magStep = _source[0].LocalPosition - _source[1].LocalPosition;
		Quaternion magRot = _source[0].LocalRotation;
		for (int e = extraMag; e >= 1; e--)
		{
			result.Add(new BeltSlot
			{
				LocalPosition = _source[0].LocalPosition + magStep * e,
				LocalRotation = magRot
			});
		}

		for (int i = 0; i < _source.Length; i++)
			result.Add(_source[i]);

		return result.ToArray();
	}

	private static int CountBeltTemplateChildren(Transform _beltRoot, TurretWeaponVariant _variant)
	{
		if (_beltRoot == null)
			return 0;

		string prefix = _variant == TurretWeaponVariant.Mk19 ? "40mm" : "12_7";
		int count = 0;
		for (int i = 0; i < _beltRoot.childCount; i++)
		{
			Transform child = _beltRoot.GetChild(i);
			if (child == null || IsMk19NonBeltChild(child))
				continue;
			if (child.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
				count++;
		}

		return count;
	}

	private static string InferRuntimeTemplatePrefix(Transform _beltRoot)
	{
		if (_beltRoot == null)
			return null;

		string name = _beltRoot.name;
		if (name.IndexOf("Bullet", System.StringComparison.OrdinalIgnoreCase) >= 0)
			return "12_7";
		if (name.IndexOf("MK19", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
		    name.IndexOf("belt", System.StringComparison.OrdinalIgnoreCase) >= 0)
			return "40mm";

		return null;
	}

	private void EnsurePoolForVariant(TurretWeaponVariant _variant)
	{
		if (_variant == TurretWeaponVariant.None)
			_variant = TurretWeaponVariant.Browning127;

		if (m_PoolVariant == _variant && m_Rounds != null && m_Rounds.Length == m_PoolCapacity)
			return;

		DestroyPoolObjects();
		m_PoolVariant = _variant;
		InitializePool();
	}

	private void DestroyPoolObjects()
	{
		if (m_Rounds != null)
			HideAllVisibleRounds();

		if (m_PoolRoot != null)
		{
			if (Application.isPlaying)
				Destroy(m_PoolRoot.gameObject);
			else
				DestroyImmediate(m_PoolRoot.gameObject);
			m_PoolRoot = null;
		}

		m_Rounds = null;
		m_PoolIndex = 0;
	}

	private void InitializeSlots()
	{
		TurretWeaponVariant variant = ResolveActiveVariant();
		if (TryApplyBakedSlots(variant))
			return;

		Transform beltRoot = GetActiveBeltRoot();
		if (TryCaptureSlotsFromChildren(beltRoot, out BeltSlot[] fromChildren))
		{
			m_Slots = fromChildren;
			return;
		}

		if (TryCaptureSlotsFromChildren(GetBeltRootForVariant(TurretWeaponVariant.Browning127), out fromChildren))
		{
			m_Slots = fromChildren;
			return;
		}

		if (TryCaptureSlotsFromChildren(GetBeltRootForVariant(TurretWeaponVariant.Mk19), out fromChildren))
			m_Slots = fromChildren;
	}

	private void InitializePool()
	{
		m_PoolRoot = new GameObject("BeltRoundPool").transform;
		m_PoolRoot.SetParent(transform, false);
		m_PoolRoot.gameObject.SetActive(false);

		m_Rounds = new BeltRound[m_PoolCapacity];

		Transform beltRoot = GetBeltRootForVariant(m_PoolVariant);
		GameObject template = GetRoundPrefab(m_PoolVariant);
		if (template == null)
			template = FindBeltRoundTemplate(beltRoot, m_PoolVariant);

		for (int i = 0; i < m_PoolCapacity; i++)
		{
			GameObject go;
			if (template != null)
			{
				go = Instantiate(template, m_PoolRoot);
				go.name = $"BeltRound_{i}";
				EnsureRoundRenderersEnabled(go.transform);
			}
			else
			{
				go = new GameObject($"BeltRound_{i}");
				go.transform.SetParent(m_PoolRoot, false);
			}

			m_Rounds[i] = new BeltRound
			{
				Transform = go.transform,
				CurrentSlot = -1,
				TargetSlot = -1,
				Progress = 1f,
				Visible = false
			};
			go.SetActive(false);
		}

		m_PoolIndex = 0;
	}

	private int GetEffectiveAmmoRemaining()
	{
		if (m_VisualAmmoOverride >= 0)
			return m_VisualAmmoOverride;
		return GetAmmoRemaining();
	}

	private void InitializeRounds()
	{
		if (m_Rounds == null || m_Slots == null || m_Slots.Length == 0)
			return;

		int ammoRemaining = GetEffectiveAmmoRemaining();
		if (ammoRemaining <= 0)
			return;

		Transform beltRoot = GetActiveBeltRoot();
		if (beltRoot == null)
			return;

		int visibleCount = Mathf.Min(m_VisibleRounds, m_SlotCount, m_PoolCapacity);
		visibleCount = Mathf.Min(visibleCount, ammoRemaining);

		for (int i = 0; i < visibleCount; i++)
		{
			BeltRound round = m_Rounds[i];
			int slotIndex = m_SlotCount - visibleCount + i;

			round.CurrentSlot = slotIndex;
			round.TargetSlot = slotIndex;
			round.Progress = 1f;
			round.Visible = true;
			round.InertiaOvershoot = Vector3.zero;
			round.JitterOffset = Vector3.zero;

			// Parent first with local identity, then apply belt-local slot pose.
			// Previous order (set local while under pool, then SetParent worldStays)
			// placed MK19 grenades in the wrong space under MK19_1/belt.
			round.Transform.SetParent(beltRoot, false);
			BeltSlot slot = m_Slots[slotIndex];
			round.Transform.localPosition = slot.LocalPosition;
			round.Transform.localRotation = slot.LocalRotation;
			round.Transform.localScale = Vector3.one;
			EnsureRoundRenderersEnabled(round.Transform);
			round.Transform.gameObject.SetActive(true);
		}
		m_PoolIndex = visibleCount;
	}

	private static void EnsureRoundRenderersEnabled(Transform _round)
	{
		if (_round == null)
			return;

		MeshRenderer[] renderers = _round.GetComponentsInChildren<MeshRenderer>(true);
		for (int i = 0; i < renderers.Length; i++)
		{
			if (renderers[i] != null)
				renderers[i].enabled = true;
		}
	}

	private int GetAmmoRemaining()
	{
		WeaponRuntimeState state = m_Equipment != null ? m_Equipment.ActiveWeaponRuntimeState : null;
		if (state == null || !state.HasMagazine)
			return 0;
		return state.CurrentMagazine?.CurrentAmmoCount ?? 0;
	}

	private int CountVisibleRounds()
	{
		if (m_Rounds == null)
			return 0;

		int count = 0;
		for (int i = 0; i < m_Rounds.Length; i++)
		{
			if (m_Rounds[i] != null && m_Rounds[i].Visible)
				count++;
		}
		return count;
	}

	private void SaveMagRestPose()
	{
		TurretWeaponVariant variant = m_Equipment?.ActiveWeaponItem?.TurretWeaponVariant ?? TurretWeaponVariant.None;
		bool isMk19 = variant == TurretWeaponVariant.Mk19;
		m_MagTransform = isMk19 ? m_Hierarchy?.MagMk19 : m_Hierarchy?.Mag127;
		if (m_MagTransform != null)
			m_MagRestLocalPos = m_MagTransform.localPosition;
	}

	private void UpdateMagTransformForVariant()
	{
		if (m_Equipment == null)
			return;
		ItemDefinition activeWeapon = m_Equipment.ActiveWeaponItem;
		bool isMk19 = activeWeapon != null && activeWeapon.TurretWeaponVariant == TurretWeaponVariant.Mk19;
		m_MagTransform = isMk19 ? m_Hierarchy?.MagMk19 : m_Hierarchy?.Mag127;
		if (m_MagTransform != null && (m_ReloadController == null || !m_ReloadController.IsReloading))
			m_MagRestLocalPos = m_MagTransform.localPosition;
	}
	#endregion

	#region Shot Handling
	private void HandleShotFired(AmmoDefinition _ammo)
	{
		if (m_Slots == null || m_Slots.Length == 0 || m_Rounds == null)
			return;
		if (m_Inventory == null || !m_Inventory.HasTurretWeapon)
			return;
		if (m_Bridge == null || !m_Bridge.HasBoundGunner)
			return;

		int ammoAfterShot = GetAmmoRemaining();
		if (ammoAfterShot <= 0)
		{
			HideAllVisibleRounds();
			return;
		}

		ConsumeLastRound();
		ShiftAllRounds();

		int visibleCount = CountVisibleRounds();
		if (ammoAfterShot > visibleCount)
			SpawnNewRoundAtMagazine();

		TriggerMagazineKick();
		TriggerWaveImpulse();
		TriggerWobbleAndJitter();
	}

	private void HideAllVisibleRounds()
	{
		if (m_Rounds == null)
			return;

		for (int i = 0; i < m_Rounds.Length; i++)
		{
			BeltRound r = m_Rounds[i];
			if (r == null || !r.Visible)
				continue;
			r.Visible = false;
			r.CurrentSlot = -1;
			r.TargetSlot = -1;
			r.Transform.gameObject.SetActive(false);
			r.Transform.SetParent(m_PoolRoot, false);
		}
		m_PoolIndex = 0;
	}

	private void ConsumeLastRound()
	{
		for (int i = 0; i < m_Rounds.Length; i++)
		{
			BeltRound round = m_Rounds[i];
			if (!round.Visible || round.CurrentSlot != m_SlotCount - 1)
				continue;

			round.Visible = false;
			round.Transform.gameObject.SetActive(false);
			round.CurrentSlot = -1;
			round.TargetSlot = -1;
			break;
		}
	}

	private void ShiftAllRounds()
	{
		for (int i = 0; i < m_Rounds.Length; i++)
		{
			BeltRound round = m_Rounds[i];
			if (!round.Visible)
				continue;
			if (round.CurrentSlot < 0 || round.CurrentSlot >= m_SlotCount - 1)
				continue;

			round.TargetSlot = round.CurrentSlot + 1;
			round.Progress = 0f;
		}
	}

	private void SpawnNewRoundAtMagazine()
	{
		BeltRound free = GetFreeRound();
		if (free == null)
			return;

		Transform mag = m_MagTransform;
		if (mag != null)
		{
			free.Transform.SetParent(mag, false);
			free.Transform.localPosition = Vector3.zero;
			free.Transform.localRotation = Quaternion.identity;
		}

		free.Transform.gameObject.SetActive(true);

		free.Transform.SetParent(GetActiveBeltRoot(), false);

		BeltSlot slot0 = m_Slots[0];
		free.Transform.localPosition = slot0.LocalPosition;
		free.Transform.localRotation = slot0.LocalRotation;
		free.Transform.localScale = Vector3.one;
		EnsureRoundRenderersEnabled(free.Transform);

		free.CurrentSlot = 0;
		free.TargetSlot = 0;
		free.Progress = 1f;
		free.Visible = true;
	}

	private void TriggerMagazineKick()
	{
		m_MagKickTimer = m_MagKickDuration;
		m_MagKickOffset = new Vector3(
			Random.Range(-m_MagKickDistance, m_MagKickDistance),
			Random.Range(-m_MagKickDistance, m_MagKickDistance),
			Random.Range(-m_MagKickDistance * 0.5f, m_MagKickDistance * 0.5f));
	}

	private void TriggerWaveImpulse()
	{
		m_WaveTimer = m_WaveDuration;
		m_QueueVibrationAmplitude += m_WaveAmplitude * 0.3f;
		m_QueueVibrationAmplitude = Mathf.Min(m_QueueVibrationAmplitude, m_WaveAmplitude * 1.5f);
	}

	private void TriggerWobbleAndJitter()
	{
		for (int i = 0; i < m_Rounds.Length; i++)
		{
			BeltRound round = m_Rounds[i];
			if (!round.Visible)
				continue;

			float slotFactor = (float)round.CurrentSlot / (m_SlotCount - 1);
			round.WobbleTimer = m_WobbleDuration;
			round.WobbleDuration = m_WobbleDuration;
			round.WobblePitch = Random.Range(-m_WobbleMaxAngle, m_WobbleMaxAngle) * slotFactor;
			round.WobbleRoll = Random.Range(-m_WobbleMaxAngle, m_WobbleMaxAngle) * slotFactor;

			round.JitterTimer = m_JitterDuration;
			round.JitterDuration = m_JitterDuration;
			float mag = m_JitterMaxDistance * (0.5f + slotFactor * 0.5f);
			round.JitterOffset = new Vector3(
				Random.Range(-mag, mag),
				Random.Range(-mag, mag),
				Random.Range(-mag * 0.5f, mag * 0.5f));
		}
	}
	#endregion

	#region Pool
	private BeltRound GetFreeRound()
	{
		for (int attempt = 0; attempt < m_PoolCapacity; attempt++)
		{
			m_PoolIndex = (m_PoolIndex + 1) % m_PoolCapacity;
			BeltRound round = m_Rounds[m_PoolIndex];
			if (!round.Visible)
				return round;
		}
		return null;
	}
	#endregion

	#region Update — LOD
	private void UpdateLod()
	{
		float time = Time.time;
		if (time < m_NextLodCheckTime)
			return;
		m_NextLodCheckTime = time + m_LodCheckInterval;

		if (m_CachedCamera == null)
			m_CachedCamera = Camera.main;
		if (m_CachedCamera == null)
			return;

		float sqrDist = (transform.position - m_CachedCamera.transform.position).sqrMagnitude;
		bool shouldBeVisible = sqrDist <= m_BeltLodMaxDistance * m_BeltLodMaxDistance;

		if (shouldBeVisible == m_IsLodVisible)
			return;

		m_IsLodVisible = shouldBeVisible;

		if (m_IsLodVisible)
		{
			TrySubscribe();
			RebuildBeltVisual();
		}
		else
		{
			TryUnsubscribe();
			HideAllRounds();
		}
	}

	private void RebuildBeltVisual()
	{
		if (m_Rounds == null || m_Slots == null)
			return;

		for (int i = 0; i < m_Rounds.Length; i++)
		{
			BeltRound r = m_Rounds[i];
			if (r == null || r.Transform == null)
				continue;
			r.Visible = false;
			r.CurrentSlot = -1;
			r.TargetSlot = -1;
			r.Progress = 1f;
			r.InertiaOvershoot = Vector3.zero;
			r.JitterOffset = Vector3.zero;
			r.Transform.gameObject.SetActive(false);
			r.Transform.SetParent(m_PoolRoot, false);
		}
		m_PoolIndex = 0;
		m_WaveTimer = 0f;
		m_QueueVibrationAmplitude = 0f;
		InitializeRounds();
	}

	private void HideAllRounds()
	{
		if (m_Rounds == null)
			return;

		for (int i = 0; i < m_Rounds.Length; i++)
		{
			BeltRound r = m_Rounds[i];
			if (r == null || r.Transform == null)
				continue;
			r.Transform.gameObject.SetActive(false);
		}
	}

	private void FullCleanup()
	{
		TryUnsubscribe();
		DestroyPoolObjects();
		m_Slots = null;
		m_PoolVariant = TurretWeaponVariant.None;
		m_WaveTimer = 0f;
		m_QueueVibrationAmplitude = 0f;
		m_MagTransform = null;
		m_ReloadBeltSuppressed = false;
		m_VisualAmmoOverride = -1;
	}
	#endregion

	#region Update — Movement
	private void UpdateMovement(float _dt)
	{
		for (int i = 0; i < m_Rounds.Length; i++)
		{
			BeltRound round = m_Rounds[i];
			if (!round.Visible)
				continue;
			if (round.CurrentSlot == round.TargetSlot)
				continue;

			round.Progress += _dt / m_FireInterval;
			if (round.Progress >= 1f)
			{
				round.Progress = 1f;
				round.CurrentSlot = round.TargetSlot;
				TriggerInertia(round);
			}
		}
	}

	private float GetMovementProgress(BeltRound _round)
	{
		return Mathf.SmoothStep(0f, 1f, _round.Progress);
	}
	#endregion

	#region Layer 1 — Wave
	private void UpdateWaveAnimation(float _dt)
	{
		if (m_WaveTimer > 0f)
			m_WaveTimer -= _dt;

		m_QueueVibrationAmplitude = Mathf.MoveTowards(m_QueueVibrationAmplitude, 0f, m_WaveAmplitude * m_WaveSpeed * _dt);
	}
	#endregion

	#region Layer 3 — Inertia
	private void TriggerInertia(BeltRound _round)
	{
		if (_round.CurrentSlot <= 0)
			return;
		Vector3 dirToPrev = (m_Slots[_round.CurrentSlot - 1].LocalPosition - m_Slots[_round.CurrentSlot].LocalPosition).normalized;
		_round.InertiaOvershoot = dirToPrev * m_InertiaOvershootDistance;
	}

	private void UpdateInertia(float _dt)
	{
		for (int i = 0; i < m_Rounds.Length; i++)
		{
			BeltRound round = m_Rounds[i];
			if (!round.Visible)
				continue;
			round.InertiaOvershoot = Vector3.MoveTowards(round.InertiaOvershoot, Vector3.zero, (m_InertiaOvershootDistance / Mathf.Max(0.001f, m_InertiaReturnDuration)) * _dt);
		}
	}
	#endregion

	#region Layer 4-5 — Wobble + Jitter
	private void UpdateWobble(float _dt)
	{
		for (int i = 0; i < m_Rounds.Length; i++)
		{
			BeltRound round = m_Rounds[i];
			if (!round.Visible)
				continue;
			if (round.WobbleTimer <= 0f)
				continue;

			round.WobbleTimer -= _dt;
			float t = round.WobbleTimer > 0f ? Mathf.Clamp01(round.WobbleTimer / Mathf.Max(0.001f, round.WobbleDuration)) : 0f;
			round.WobblePitch = Mathf.Lerp(0f, round.WobblePitch, t);
			round.WobbleRoll = Mathf.Lerp(0f, round.WobbleRoll, t);
		}
	}

	private void UpdateJitter(float _dt)
	{
		for (int i = 0; i < m_Rounds.Length; i++)
		{
			BeltRound round = m_Rounds[i];
			if (!round.Visible)
				continue;
			if (round.JitterTimer <= 0f)
				continue;

			round.JitterTimer -= _dt;
			float t = round.JitterTimer > 0f ? Mathf.Clamp01(round.JitterTimer / Mathf.Max(0.001f, round.JitterDuration)) : 0f;
			round.JitterOffset *= t;
		}
	}
	#endregion

	#region Layer 7 — Queue Vibration
	private float GetQueueVibrationOffset()
	{
		if (m_QueueVibrationAmplitude < 0.0001f)
			return 0f;
		float noise = Mathf.PerlinNoise(Time.time * 30f, 0f) * 2f - 1f;
		return noise * m_QueueVibrationAmplitude;
	}
	#endregion

	#region Idle
	private void UpdateIdle(float _dt)
	{
		m_IdleTimer += _dt;
		if (m_IdleTimer >= m_IdleInterval)
		{
			m_IdleTimer = 0f;
			m_IdlePhase = Random.Range(0f, 1f);
		}

		m_IdlePhase += _dt * 0.01f;
	}
	#endregion

	#region Magazine Kick
	private void UpdateMagKick(float _dt)
	{
		if (m_MagKickTimer > 0f)
			m_MagKickTimer -= _dt;
		m_MagKickOffset = Vector3.MoveTowards(m_MagKickOffset, Vector3.zero, m_MagKickDistance * 2f * _dt);
	}

	private void ApplyMagPose()
	{
		if (m_MagTransform == null)
			return;
		if (m_ReloadController != null && m_ReloadController.IsReloading)
			return;
		m_MagTransform.localPosition = m_MagRestLocalPos + m_MagKickOffset;
	}
	#endregion

	#region Apply
	private void ApplyAllRounds()
	{
		for (int i = 0; i < m_Rounds.Length; i++)
		{
			BeltRound round = m_Rounds[i];
			if (!round.Visible)
				continue;

			float propDelay = round.CurrentSlot * m_PropagationDelayPerSlot;
			float t = GetMovementProgress(round);
			float delayedT = Mathf.Clamp01((t - propDelay / m_FireInterval));

			int fromSlot = round.CurrentSlot;
			int toSlot = round.TargetSlot;
			if (fromSlot < 0 || fromSlot >= m_SlotCount || toSlot < 0 || toSlot >= m_SlotCount)
				continue;

			BeltSlot from = m_Slots[fromSlot];
			BeltSlot to = m_Slots[toSlot];

			Vector3 basePos = Vector3.Lerp(from.LocalPosition, to.LocalPosition, delayedT);
			Quaternion baseRot = Quaternion.Slerp(from.LocalRotation, to.LocalRotation, delayedT);

			// Layer 6: stretch
			if (delayedT > 0f && delayedT < 1f)
			{
				Vector3 forward = (to.LocalPosition - from.LocalPosition).normalized;
				float stretch = Mathf.Sin(delayedT * Mathf.PI) * m_StretchFactor;
				basePos += forward * stretch;
			}

			float waveAmplitude = m_PoolVariant == TurretWeaponVariant.Mk19
				? m_WaveAmplitude * 0.35f
				: m_WaveAmplitude;
			float waveT = Mathf.Clamp01(m_WaveTimer / Mathf.Max(0.001f, m_WaveDuration));
			if (waveT > 0f && m_SlotCount > 1)
			{
				float slotFactor = (float)(m_SlotCount - 1 - round.CurrentSlot) / (m_SlotCount - 1);
				float waveStrength = Mathf.Lerp(0.2f, 1f, slotFactor);
				float waveOffset = Mathf.Sin(waveT * Mathf.PI * 2f + slotFactor * 2f) * waveAmplitude * waveStrength;
				basePos.y += waveOffset;
			}

			// Layer 7: queue vibration
			float qvib = GetQueueVibrationOffset();
			basePos.x += qvib * 0.5f;
			basePos.y += qvib;

			// Layer 3: inertia
			basePos += round.InertiaOvershoot;

			// Layer 5: jitter
			basePos += round.JitterOffset;

			// Layer 4: wobble
			Quaternion wobbleRot = Quaternion.Euler(round.WobblePitch, 0f, round.WobbleRoll);

			// Layer 8: idle
			float idleX = Mathf.Sin(Time.time * 0.3f + round.CurrentSlot) * m_IdleAmplitude;
			float idleY = Mathf.Cos(Time.time * 0.2f + round.CurrentSlot * 2f) * m_IdleAmplitude;
			basePos.x += idleX;
			basePos.y += idleY;

			round.Transform.localPosition = basePos;
			round.Transform.localRotation = baseRot * wobbleRot;
		}
	}
	#endregion

#if UNITY_EDITOR
	public struct EditorBeltBakeResult
	{
		public int M2SlotCount;
		public int Mk19SlotCount;
		public bool RemovedTemplateChildren;
	}

	[ContextMenu("Bake Belt Slots From Scene Templates")]
	public EditorBeltBakeResult EditorBakeBeltSlotsFromSceneTemplates(bool _removeTemplateChildren = false)
	{
		ResolveRefsIfNeeded();
		ResolveBeltRootsIfNeeded();

		m_M2BakedSlots = CaptureSlotsForBake(m_BulletBeltRoot);
		m_Mk19BakedSlots = CaptureSlotsForBake(m_Mk19BeltRoot);

		int m2Count = m_M2BakedSlots?.Length ?? 0;
		int mk19Count = m_Mk19BakedSlots?.Length ?? 0;
		if (m2Count > 0 || mk19Count > 0)
			m_SlotCount = Mathf.Max(m2Count, mk19Count);

		if (_removeTemplateChildren)
		{
			RemoveBeltTemplateChildren(m_BulletBeltRoot);
			RemoveBeltTemplateChildren(m_Mk19BeltRoot);
		}

		UnityEditor.EditorUtility.SetDirty(this);
		return new EditorBeltBakeResult
		{
			M2SlotCount = m2Count,
			Mk19SlotCount = mk19Count,
			RemovedTemplateChildren = _removeTemplateChildren
		};
	}

	private void ResolveRefsIfNeeded()
	{
		if (m_Bridge == null) TryGetComponent(out m_Bridge);
		if (m_Hierarchy == null) TryGetComponent(out m_Hierarchy);
		if (m_Equipment == null) TryGetComponent(out m_Equipment);
		if (m_Inventory == null) TryGetComponent(out m_Inventory);
		if (m_ReloadController == null) TryGetComponent(out m_ReloadController);
		m_Hierarchy?.EnsureBound();
	}

	private BeltSlot[] CaptureSlotsForBake(Transform _beltRoot)
	{
		if (_beltRoot == null || _beltRoot.childCount == 0)
			return null;

		string prefix = InferTemplatePrefix(_beltRoot);
		var slots = new System.Collections.Generic.List<BeltSlot>(_beltRoot.childCount);
		for (int i = 0; i < _beltRoot.childCount; i++)
		{
			Transform child = _beltRoot.GetChild(i);
			if (prefix != null &&
			    !child.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
				continue;

			slots.Add(new BeltSlot
			{
				LocalPosition = child.localPosition,
				LocalRotation = child.localRotation
			});
		}

		return slots.Count > 0 ? slots.ToArray() : null;
	}

	private static string InferTemplatePrefix(Transform _beltRoot)
	{
		if (_beltRoot == null)
			return null;

		string name = _beltRoot.name;
		if (name.IndexOf("Bullet", System.StringComparison.OrdinalIgnoreCase) >= 0)
			return "12_7";
		if (name.IndexOf("MK19", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
		    name.IndexOf("belt", System.StringComparison.OrdinalIgnoreCase) >= 0)
			return "40mm";

		return null;
	}

	private static void RemoveBeltTemplateChildren(Transform _beltRoot)
	{
		if (_beltRoot == null)
			return;

		for (int i = _beltRoot.childCount - 1; i >= 0; i--)
			UnityEngine.Object.DestroyImmediate(_beltRoot.GetChild(i).gameObject);
	}

	public void ResolveRefsIfNeededEditor() => ResolveRefsIfNeeded();

	public void ResolveBeltRootsIfNeededEditor() => ResolveBeltRootsIfNeeded();

	public Transform BulletBeltRootEditor => m_BulletBeltRoot;

	public Transform Mk19BeltRootEditor => m_Mk19BeltRoot;

	public void AssignRoundPrefabsEditor(GameObject _m2RoundPrefab, GameObject _mk19RoundPrefab)
	{
		m_M2RoundPrefab = _m2RoundPrefab;
		m_Mk19RoundPrefab = _mk19RoundPrefab;
	}
#endif
}

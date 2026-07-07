using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Триггер на персонаже: объекты с <see cref="WorldPickupItem"/> попадают в панель «земля»
/// через <see cref="InventoryScreenBindings"/> (ссылки на Canvas на юните не нужны).
/// Выход из зоны (когда ни один коллайдер лута не пересекается) убирает строку из UI.
/// Для срабатывания OnTriggerEnter при движении через NavMesh без Rigidbody на юните Unity не шлёт события,
/// если оба коллайдера «статичны» — в <see cref="Awake"/> добавляется кинематический <see cref="Rigidbody"/>.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class InventoryPickupZone : MonoBehaviour
{
	#region Constants
	private const int c_OverlapBufferSize = 48;
	#endregion

	#region Serialized Fields
	[Tooltip("Если задано — подбирать только объекты на этих слоях.")]
	[SerializeField] private LayerMask m_ItemLayerMask;
	[SerializeField] private bool m_UseLayerMask;
	#endregion

	#region Private Fields
	private static readonly Collider[] s_OverlapBuffer = new Collider[c_OverlapBufferSize];

	/// <summary>Сколько коллайдеров лута сейчас внутри зоны (несколько коллайдеров на одном объекте — один предмет).</summary>
	private readonly Dictionary<WorldPickupItem, int> m_OverlapRefCount = new Dictionary<WorldPickupItem, int>();
	#endregion

	#region Unity Lifecycle
	private void Reset()
	{
		var c = GetComponent<Collider>();
		c.isTrigger = true;
		EnsureKinematicRigidbodyForTriggers();
	}

	private void Awake()
	{
		EnsureKinematicRigidbodyForTriggers();
	}
	#endregion

	#region Public Methods
	/// <summary>Зарегистрировать лут, появившийся внутри зоны без OnTriggerEnter (выброс из инвентаря и т.п.).</summary>
	public void RegisterPickupOverlap(WorldPickupItem _pickup)
	{
		if (_pickup == null)
			return;

		if (!m_OverlapRefCount.TryGetValue(_pickup, out int count))
			count = 0;
		m_OverlapRefCount[_pickup] = count + 1;
	}

	/// <summary>Очистить панель «земля» и снова заполнить по объектам, сейчас пересекающим эту зону (вызывать при открытии инвентаря).</summary>
	public void RepopulateGroundPanelFromCurrentOverlaps()
	{
		if (InventoryExchangeController.Instance.IsActive)
			return;

		InventoryPanelView groundPanel = InventoryScreenBindings.Instance != null
			? InventoryScreenBindings.Instance.GroundPanel
			: null;
		if (groundPanel == null)
			return;

		SyncOverlapsFromPhysics();
		PurgeDestroyedOverlaps();

		groundPanel.ClearAllSlots();

		WorldPickupItem[] snapshot = new WorldPickupItem[m_OverlapRefCount.Count];
		m_OverlapRefCount.Keys.CopyTo(snapshot, 0);
		for (int i = 0; i < snapshot.Length; i++)
		{
			WorldPickupItem pickup = snapshot[i];
			if (pickup == null)
				continue;

			InventorySlotRuntimeData data = pickup.BuildSlotData();
			if (groundPanel.TryAdd(data))
				pickup.RegisterListedInGroundUi();
		}

		groundPanel.RebuildContentLayout();

		RuntimeInventoryModificationCoordinator modificationCoordinator = RuntimeInventoryModificationCoordinator.Instance;
		if (modificationCoordinator != null)
			modificationCoordinator.OnGroundPanelRepopulated();
	}
	#endregion

	#region Private Methods
	/// <summary>
	/// Один из участников триггерной пары должен иметь Rigidbody, иначе Enter/Exit не вызываются
	/// (типично: юнит на NavMesh без тела, лут только с MeshCollider).
	/// </summary>
	private void EnsureKinematicRigidbodyForTriggers()
	{
		if (!TryGetComponent(out Rigidbody rb))
		{
			rb = gameObject.AddComponent<Rigidbody>();
			rb.isKinematic = true;
			rb.useGravity = false;
			return;
		}

		if (rb.isKinematic)
			rb.useGravity = false;
	}

	private void OnTriggerEnter(Collider _other)
	{
		bool hasPickup = TryGetPickup(_other, out WorldPickupItem detectedPickup);

		if (m_UseLayerMask && m_ItemLayerMask != 0)
		{
			int bit = 1 << _other.gameObject.layer;
			if ((m_ItemLayerMask.value & bit) == 0)
				return;
		}

		if (!hasPickup)
			return;

		WorldPickupItem pickup = detectedPickup;

		if (!m_OverlapRefCount.TryGetValue(pickup, out int count))
			count = 0;
		count++;
		m_OverlapRefCount[pickup] = count;

		if (count > 1)
			return;

		if (InventoryExchangeController.Instance.IsActive)
			return;

		if (InventoryScreenBindings.Instance == null)
		{
			Debug.LogWarning(
				$"{nameof(InventoryPickupZone)}: в сцене нет активного {nameof(InventoryScreenBindings)} — панель «земля» недоступна.",
				this);
			return;
		}

		InventoryPanelView groundPanel = InventoryScreenBindings.Instance.GroundPanel;
		if (groundPanel == null)
		{
			Debug.LogWarning(
				$"{nameof(InventoryPickupZone)}: {nameof(InventoryScreenBindings.GroundPanel)} == null. Назначьте " +
				$"в {nameof(RtsUnitSelectionManager)} поле Ground Panel (колонка «земля», не рюкзак).",
				this);
			return;
		}

		if (pickup.IsListedInGroundUi)
			return;

		InventorySlotRuntimeData data = pickup.BuildSlotData();
		if (data.IsEmpty)
		{
			Debug.LogWarning(
				$"{nameof(InventoryPickupZone)}: у '{pickup.name}' пустые данные слота (проверьте {nameof(ItemDefinition)} на {nameof(WorldPickupItem)}).",
				pickup);
			return;
		}

		bool ok = groundPanel.TryAdd(data);
		if (!ok)
		{
			Debug.LogWarning(
				$"{nameof(InventoryPickupZone)}: {nameof(InventoryPanelView.TryAdd)} не удалось для '{pickup.name}'. " +
				$"На панели «земля» должны быть назначены Slot Prefab и Slots Container (или в контейнере есть пустые ячейки).",
				groundPanel);
			return;
		}

		pickup.RegisterListedInGroundUi();
		groundPanel.RebuildContentLayout();
		RuntimeInventoryModificationCoordinator.Instance?.OnGroundPanelRepopulated();
	}

	private void OnTriggerExit(Collider _other)
	{
		if (!TryGetPickup(_other, out WorldPickupItem pickup))
			return;

		if (!m_OverlapRefCount.TryGetValue(pickup, out int count))
			return;

		count--;
		if (count > 0)
		{
			m_OverlapRefCount[pickup] = count;
			return;
		}

		m_OverlapRefCount.Remove(pickup);

		if (InventoryExchangeController.Instance.IsActive)
			return;

		InventoryPanelView groundPanel = InventoryScreenBindings.Instance != null
			? InventoryScreenBindings.Instance.GroundPanel
			: null;
		if (groundPanel != null)
			groundPanel.TryRemoveGroundListingForPickup(pickup);
	}

	private static bool TryGetPickup(Collider _col, out WorldPickupItem _pickup)
	{
		_pickup = _col.GetComponent<WorldPickupItem>();
		if (_pickup == null)
			_pickup = _col.GetComponentInParent<WorldPickupItem>();
		return _pickup != null;
	}

	private void PurgeDestroyedOverlaps()
	{
		List<WorldPickupItem> toRemove = null;
		foreach (WorldPickupItem p in m_OverlapRefCount.Keys)
		{
			if (p == null)
				(toRemove ??= new List<WorldPickupItem>()).Add(p);
		}

		if (toRemove == null)
			return;
		for (int i = 0; i < toRemove.Count; i++)
			m_OverlapRefCount.Remove(toRemove[i]);
	}

	/// <summary>
	/// Перестроить учёт пересечений по физике: выброшенный лут часто появляется уже внутри триггера,
	/// и Unity не вызывает OnTriggerEnter для таких объектов.
	/// </summary>
	private void SyncOverlapsFromPhysics()
	{
		if (!TryGetComponent(out SphereCollider sphereCollider))
			return;

		Vector3 center = transform.TransformPoint(sphereCollider.center);
		float radius = GetWorldSphereRadius(sphereCollider);
		int hitCount = Physics.OverlapSphereNonAlloc(
			center,
			radius,
			s_OverlapBuffer,
			GetOverlapLayerMask(),
			QueryTriggerInteraction.Collide);

		m_OverlapRefCount.Clear();
		for (int i = 0; i < hitCount; i++)
		{
			Collider hit = s_OverlapBuffer[i];
			if (hit == null || hit == sphereCollider)
				continue;

			if (!TryGetPickup(hit, out WorldPickupItem pickup))
				continue;

			if (!m_OverlapRefCount.TryGetValue(pickup, out int count))
				count = 0;
			m_OverlapRefCount[pickup] = count + 1;
		}
	}

	private int GetOverlapLayerMask()
	{
		if (m_UseLayerMask && m_ItemLayerMask.value != 0)
			return m_ItemLayerMask.value;

		return Physics.AllLayers;
	}

	private static float GetWorldSphereRadius(SphereCollider _sphereCollider)
	{
		Vector3 lossyScale = _sphereCollider.transform.lossyScale;
		float maxAxis = Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z));
		return _sphereCollider.radius * maxAxis;
	}
	#endregion
}

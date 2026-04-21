using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Вешается на корень префаба оружия в руке и на тот же визуал в мире. Якоря геймплея (<see cref="m_Barrel"/>, <see cref="m_SightPivot"/>) не родитель мешей модулей.
/// Постоянное состояние экземпляра (магазин, патронник, износ, список модулей в инвентаре) живёт в <see cref="WeaponRuntimeState"/> внутри <see cref="ItemInstanceState"/> —
/// этот компонент уничтожается вместе с визуалом при снятии. В инспекторе задаётся только пресет <see cref="m_EquippedAttachments"/> (и сокеты).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(60)]
public sealed class EquippedWeapon : MonoBehaviour
{
	#region Constants
	private const int c_RailSocketCount = 4;
	#endregion

	#region Serialized Fields — уже используются геймплеем
	[Header("Геймплей: ствол и линия выстрела")]
	[Tooltip("Пустышка на конце дула: позиция и forward — линия выстрела. Если пусто — для геймплея берётся transform этого компонента (часто рукоять, не дуло).")]
	[SerializeField] private Transform m_Barrel;

	[Header("Геймплей: гильза")]
	[Tooltip("Точка выброса гильзы: position и forward — направление выброса. Если пусто — позиция от Barrel, направление −Barrel.right.")]
	[SerializeField] private Transform m_ShellEject;

	[Header("Геймплей: прицел для зрения (не визуал модуля)")]
	[Tooltip("Пустышка прицела: <c>UnitVision</c> берёт отсюда конус FOV и LOS при оружии на готове (ось = forward).")]
	[SerializeField] private Transform m_SightPivot;
	#endregion

	#region Serialized Fields — магазин (есть логика визуала)
	[Header("Магазин")]
	[Tooltip("Точка, куда крепится отдельный visual вставленного магазина. Если пусто, визуал магазина в оружии не создаётся.")]
	[SerializeField] private Transform m_MagazineSocket;
	#endregion

	#region Serialized Fields — сокеты визуала модулей (родитель префаба модуля)
	[Header("Модули: визуал (не Barrel / не Sight Pivot)")]
	[Tooltip("Дуло: глушитель, ДТК, пламегаситель. Не совмещать с геймплейным Barrel.")]
	[SerializeField] private Transform m_MuzzleModuleVisualSocket;
	[Tooltip("Коллиматор / оптика. Не совмещать с Sight Pivot (конус зрения / LOS).")]
	[SerializeField] private Transform m_OpticModuleVisualSocket;
	[Tooltip("Приклад (слот Stock).")]
	[SerializeField] private Transform m_StockSocket;
	[Tooltip("Рукоятка / упор под стволом (слот UnderBarrel).")]
	[SerializeField] private Transform m_UnderBarrelSocket;
	[Tooltip("До четырёх слотов планки Rail: ЛЦУ, фонарь и т.д. Индексы 0..3. Пустые элементы — не используются.")]
	[SerializeField] private Transform[] m_RailSockets = new Transform[c_RailSocketCount];
	[Tooltip("Параллельно WeaponDefinition.AttachmentSlots. На префабе лута должен совпадать с WorldPickupItem.EquippedAttachments. Копируется в WeaponRuntimeState, пока там пусто (если на WorldPickupItem список пуст); иначе подставляется для визуала.")]
	[SerializeField] private WeaponAttachmentDefinition[] m_EquippedAttachments;
	#endregion

	#region Serialized Fields — прочее
	[Header("Визуал отдачи")]
	[Tooltip("Необязательно: отдельный узел для kick. Если пусто — UnitWeaponVisualRecoilKick крутит корень оружия целиком (после позы аниматора накладывается отдача).")]
	[SerializeField] private Transform m_VisualRecoilKickPivot;

	[Header("Отладка")]
	[Tooltip("Луч из пустышки Barrel (только если она назначена). В Game view включи Gizmos на вкладке Game.")]
	[SerializeField] private bool m_DrawBarrelDebugRay;
	[SerializeField, Min(0.01f)] private float m_BarrelDebugRayLength = 4f;
	[SerializeField] private Color m_BarrelDebugRayColor = new Color(0f, 0.92f, 1f, 1f);
	#endregion

	#region Public Properties
	/// <summary>Точка выстрела: позиция и <c>forward</c> — направление ствола.</summary>
	public Transform BarrelTransform => m_Barrel != null ? m_Barrel : transform;

	/// <summary>Точка выброса гильзы; null — эвристика от ствола.</summary>
	public Transform ShellEjectTransform => m_ShellEject;

	/// <summary>Прицел для конуса зрения; null если не задан.</summary>
	public Transform SightPivotTransform => m_SightPivot;

	/// <summary>Сокет визуала магазина; null если не настроен.</summary>
	public Transform MagazineSocketTransform => m_MagazineSocket;

	/// <summary>Сокет визуала на дуле; null если не настроен.</summary>
	public Transform MuzzleModuleVisualSocket => m_MuzzleModuleVisualSocket;

	/// <summary>Сокет визуала прицела; null если не настроен.</summary>
	public Transform OpticModuleVisualSocket => m_OpticModuleVisualSocket;

	/// <summary>Сокет приклада; null если не настроен.</summary>
	public Transform StockSocketTransform => m_StockSocket;

	/// <summary>Сокет рукоятки (under barrel); null если не настроен.</summary>
	public Transform UnderBarrelSocketTransform => m_UnderBarrelSocket;

	/// <summary>Количество слотов планки (фиксировано 4).</summary>
	public static int RailSocketCount => c_RailSocketCount;

	/// <summary>Узел для процедурной отдачи визуала; null — использовать корень инстанса.</summary>
	public Transform VisualRecoilKickPivot => m_VisualRecoilKickPivot;
	#endregion

	#region Public Methods — сокеты планки
	/// <summary>Сокет планки по индексу 0..3; null если не задан или инекс вне диапазона.</summary>
	public Transform GetRailSocketTransform(int _index)
	{
		if (_index < 0 || _index >= c_RailSocketCount || m_RailSockets == null || _index >= m_RailSockets.Length)
			return null;

		return m_RailSockets[_index];
	}
	#endregion

	#region Private Fields
	private GameObject m_InsertedMagazineVisualInstance;
	private ItemDefinition m_CurrentMagazineVisualDefinition;
	private readonly List<GameObject> m_AttachmentVisualInstances = new List<GameObject>(8);
	#endregion

	#region Public Methods
	public void SetInsertedMagazineVisual(ItemDefinition _magazineDefinition)
	{
		if (m_MagazineSocket == null)
		{
			ClearInsertedMagazineVisual();
			return;
		}

		if (_magazineDefinition == null || _magazineDefinition.EquippedVisualPrefab == null)
		{
			ClearInsertedMagazineVisual();
			return;
		}

		if (m_InsertedMagazineVisualInstance != null && ReferenceEquals(m_CurrentMagazineVisualDefinition, _magazineDefinition))
			return;

		ClearInsertedMagazineVisual();
		m_InsertedMagazineVisualInstance = Instantiate(_magazineDefinition.EquippedVisualPrefab, m_MagazineSocket);
		m_InsertedMagazineVisualInstance.transform.localPosition = Vector3.zero;
		m_InsertedMagazineVisualInstance.transform.localRotation = Quaternion.identity;
		m_CurrentMagazineVisualDefinition = _magazineDefinition;
		DisablePhysicsOnEquippedVisual(m_InsertedMagazineVisualInstance);
	}

	public void ClearInsertedMagazineVisual()
	{
		m_CurrentMagazineVisualDefinition = null;
		if (m_InsertedMagazineVisualInstance == null)
			return;

		Destroy(m_InsertedMagazineVisualInstance);
		m_InsertedMagazineVisualInstance = null;
	}

	/// <summary>
	/// Синхронизирует меши модулей с <see cref="WeaponDefinition.AttachmentSlots"/> и параллельным массивом <paramref name="_equipped"/>.
	/// Индекс слота = индекс в <paramref name="_equipped"/>; визуал вешается только на сокеты визуала (не Barrel / Sight Pivot).
	/// </summary>
	public void SyncAttachmentVisuals(WeaponDefinition _weapon, WeaponAttachmentDefinition[] _equipped)
	{
		ClearAttachmentVisualsInternal();

		if (_weapon == null)
			return;

		WeaponAttachmentSlotDefinition[] slots = _weapon.AttachmentSlots;
		if (slots == null || slots.Length == 0)
			return;

		bool[] usedEquipped = _equipped != null && _equipped.Length > 0 ? new bool[_equipped.Length] : null;

		int railVisualIndex = 0;
		for (int i = 0; i < slots.Length; i++)
		{
			WeaponAttachmentSlotType slotType = slots[i].SlotType;
			WeaponAttachmentDefinition def = ResolveEquippedForWeaponSlot(_equipped, usedEquipped, i, slotType);
			if (def == null)
			{
				if (slotType == WeaponAttachmentSlotType.Rail)
					railVisualIndex++;
				continue;
			}

			Transform parent = ResolveAttachmentVisualSocket(slotType, ref railVisualIndex);
			GameObject prefab = def.EquippedVisualPrefab;
			if (parent == null || prefab == null)
				continue;

			// На префабе уже может быть зашитый меш под сокетом — не дублировать и не вызывать Instantiate в OnValidate.
			if (parent.childCount > 0)
				continue;

			GameObject inst = Instantiate(prefab, parent);
			inst.transform.localPosition = Vector3.zero;
			inst.transform.localRotation = Quaternion.identity;
			DisablePhysicsOnEquippedVisual(inst);
			m_AttachmentVisualInstances.Add(inst);
		}
	}

	/// <summary>Удаляет все инстансы визуала модулей (магазин не трогает).</summary>
	public void ClearAttachmentVisuals()
	{
		ClearAttachmentVisualsInternal();
	}

	/// <summary>Копирует пресет с префаба в состояние экземпляра, если в <paramref name="_weaponState"/> ещё нет ни одного модуля (лут на сцене).</summary>
	public void TryCopyEquippedAttachmentsPresetToWeaponStateIfEmpty(WeaponRuntimeState _weaponState)
	{
		if (_weaponState == null || m_EquippedAttachments == null || m_EquippedAttachments.Length == 0)
			return;

		if (HasAnyNonNullAttachment(_weaponState.EquippedAttachments))
			return;

		_weaponState.SetEquippedAttachments(m_EquippedAttachments);
	}

	/// <summary>Визуал модулей: сначала из <paramref name="_weaponState"/>, иначе пресет с этого префаба.</summary>
	public void RefreshAttachmentVisualsFromState(WeaponDefinition _weapon, WeaponRuntimeState _weaponState)
	{
		if (_weapon == null)
		{
			ClearAttachmentVisuals();
			return;
		}

		WeaponAttachmentDefinition[] fromState = _weaponState != null ? _weaponState.EquippedAttachments : null;
		WeaponAttachmentDefinition[] use = HasAnyNonNullAttachment(fromState) ? fromState : m_EquippedAttachments;
		if (HasAnyNonNullAttachment(use))
			SyncAttachmentVisuals(_weapon, use);
		else
			ClearAttachmentVisuals();
	}

	private static bool HasAnyNonNullAttachment(WeaponAttachmentDefinition[] _attachments)
	{
		if (_attachments == null)
			return false;

		for (int i = 0; i < _attachments.Length; i++)
		{
			if (_attachments[i] != null)
				return true;
		}

		return false;
	}
	#endregion

	#region Unity Lifecycle
	private void LateUpdate()
	{
		if (!m_DrawBarrelDebugRay || m_Barrel == null || !Application.isPlaying)
			return;

		Transform b = m_Barrel;
		Debug.DrawRay(b.position, b.forward * m_BarrelDebugRayLength, m_BarrelDebugRayColor);
	}

	private void OnDrawGizmos()
	{
		if (!m_DrawBarrelDebugRay || m_Barrel == null)
			return;

		Gizmos.color = m_BarrelDebugRayColor;
		Vector3 start = m_Barrel.position;
		Vector3 end = start + m_Barrel.forward * m_BarrelDebugRayLength;
		Gizmos.DrawLine(start, end);
	}

	private void OnDestroy()
	{
		ClearInsertedMagazineVisual();
		ClearAttachmentVisualsInternal();
	}
	#endregion

	#region Private Methods
	/// <summary>
	/// Сначала параллельный индекс, если тип слота совпадает с <see cref="WeaponAttachmentDefinition.RequiredSlot"/>;
	/// иначе любой неиспользованный модуль с подходящим RequiredSlot (порядок в массиве — для нескольких Rail).
	/// </summary>
	private static WeaponAttachmentDefinition ResolveEquippedForWeaponSlot(
		WeaponAttachmentDefinition[] _equipped,
		bool[] _used,
		int _slotIndex,
		WeaponAttachmentSlotType _slotType)
	{
		if (_equipped == null || _equipped.Length == 0)
			return null;

		if (_slotIndex < _equipped.Length &&
		    !_used[_slotIndex] &&
		    _equipped[_slotIndex] != null &&
		    _equipped[_slotIndex].RequiredSlot == _slotType)
		{
			_used[_slotIndex] = true;
			return _equipped[_slotIndex];
		}

		for (int j = 0; j < _equipped.Length; j++)
		{
			if (_used[j] || _equipped[j] == null)
				continue;
			if (_equipped[j].RequiredSlot != _slotType)
				continue;

			_used[j] = true;
			return _equipped[j];
		}

		return null;
	}

	private Transform ResolveAttachmentVisualSocket(WeaponAttachmentSlotType _slotType, ref int _railVisualIndex)
	{
		switch (_slotType)
		{
			case WeaponAttachmentSlotType.Muzzle:
				return m_MuzzleModuleVisualSocket;
			case WeaponAttachmentSlotType.Optic:
				return m_OpticModuleVisualSocket;
			case WeaponAttachmentSlotType.UnderBarrel:
				return m_UnderBarrelSocket;
			case WeaponAttachmentSlotType.Stock:
				return m_StockSocket;
			case WeaponAttachmentSlotType.Rail:
			{
				Transform rail = GetRailSocketTransform(_railVisualIndex);
				_railVisualIndex++;
				return rail;
			}
			default:
				return null;
		}
	}

	private void ClearAttachmentVisualsInternal()
	{
		for (int i = 0; i < m_AttachmentVisualInstances.Count; i++)
		{
			if (m_AttachmentVisualInstances[i] != null)
				Destroy(m_AttachmentVisualInstances[i]);
		}

		m_AttachmentVisualInstances.Clear();
	}

	private static void DisablePhysicsOnEquippedVisual(GameObject _root)
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

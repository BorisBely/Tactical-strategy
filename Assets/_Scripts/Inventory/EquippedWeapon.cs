using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
	private const int c_RailSocketCount = 3;
	public const string MuzzleExitTransformName = "MuzzleExit";
	#endregion

	#region Serialized Fields — уже используются геймплеем
	[Header("Геймплей: ствол и линия выстрела")]
	[Tooltip("Пустышка на конце дула: позиция и forward — линия выстрела. На M4 обычно тот же Transform, что MuzzleModuleVisualSocket. Если пусто — используется MuzzleModuleVisualSocket.")]
	[SerializeField] private Transform m_Barrel;

	[Header("Геймплей: гильза")]
	[Tooltip("Точка выброса гильзы: position и forward — направление выброса. Если пусто — позиция от Barrel, направление −Barrel.right.")]
	[SerializeField] private Transform m_ShellEject;

	[Header("Геймплей: прицел для зрения (не визуал модуля)")]
	[Tooltip("Пустышка прицела: UnitVision берёт отсюда конус FOV и LOS. На M4 обычно тот же Transform, что OpticModuleVisualSocket. Если пусто — используется OpticModuleVisualSocket.")]
	[SerializeField] private Transform m_SightPivot;
	#endregion

	#region Serialized Fields — магазин (есть логика визуала)
	[Header("Магазин")]
	[Tooltip("Точка, куда крепится отдельный visual вставленного магазина. Если пусто, визуал магазина в оружии не создаётся.")]
	[SerializeField] private Transform m_MagazineSocket;
	[Tooltip("Точка для второго/бокового магазина (напр. M249 — боковой приёмник под AR-магазины). Если пусто, второй визуал не создаётся.")]
	[SerializeField] private Transform m_SecondaryMagazineSocket;
	#endregion

	#region Serialized Fields — сокеты визуала модулей (родитель префаба модуля)
	[Header("Модули: визуал (не Barrel / не Sight Pivot)")]
	[Tooltip("Дуло: глушитель, ДТК, пламегаситель. Не совмещать с геймплейным Barrel.")]
	[SerializeField] private Transform m_MuzzleModuleVisualSocket;
	[Tooltip("Коллиматор / оптика. Не совмещать с Sight Pivot (конус зрения / LOS).")]
	[SerializeField] private Transform m_OpticModuleVisualSocket;
	[Tooltip("Боковая планка (АК): прицелы Side rail. Пусто — слот не используется на этом оружии.")]
	[SerializeField] private Transform m_SideRailModuleVisualSocket;
	[Tooltip("Приклад (слот Stock).")]
	[SerializeField] private Transform m_StockSocket;
	[Tooltip("Рукоятка / упор под стволом (слот UnderBarrel).")]
	[SerializeField] private Transform m_UnderBarrelSocket;
	[Tooltip("До трёх слотов планки Rail: ЛЦУ, фонарь, накладки и т.д. Индексы 0..2. Пустые элементы — не используются.")]
	[SerializeField] private Transform[] m_RailSockets = new Transform[c_RailSocketCount];
	[Tooltip("Параллельно WeaponDefinition.AttachmentSlots. На префабе лута должен совпадать с WorldPickupItem.EquippedAttachments. Копируется в WeaponRuntimeState, пока там пусто (если на WorldPickupItem список пуст); иначе подставляется для визуала.")]
	[SerializeField] private WeaponAttachmentDefinition[] m_EquippedAttachments;
	#endregion

	#region Serialized Fields — дефолтные детали
	[Header("Дефолтные детали")]
	[Tooltip("Дефолтный прицел/целик/мушка, которые нужно выключать при установленном Optic-модуле и возвращать при снятии.")]
	[SerializeField] private GameObject[] m_DefaultOpticVisuals;
	[Tooltip("Дефолтный приклад, который нужно выключать при установленном Stock-модуле и возвращать при снятии.")]
	[SerializeField] private GameObject[] m_DefaultStockVisuals;
	[Tooltip("Визуал пикатинни-планки под прицел. Скрывается при установленном модуле в SideRail.")]
	[SerializeField] private GameObject[] m_OpticRailMountVisuals;
	[Tooltip("Визуал боковой планки. Скрывается при установленном модуле в Optic.")]
	[SerializeField] private GameObject[] m_SideRailMountVisuals;
	#endregion

	#region Serialized Fields — прочее
	[Header("Визуал отдачи")]
	[Tooltip("Необязательно: отдельный узел для kick. Если пусто — UnitWeaponVisualRecoilKick крутит корень оружия целиком (после позы аниматора накладывается отдача).")]
	[SerializeField] private Transform m_VisualRecoilKickPivot;

	[Header("Визуал затвора / dust cover")]
	[Tooltip("Слайд / затворная рама. Пусто = визуал затвора выключен для этого оружия (настраивается вручную).")]
	[SerializeField] private Transform m_BoltCarrier;
	[Tooltip("Локальное смещение BoltCarrier в полностью открытом положении относительно rest (обычно только −Z).")]
	[SerializeField] private Vector3 m_BoltOpenLocalOffset = new Vector3(0f, 0f, -0.08f);
	[Tooltip("Болтовая рукоятка: локальный euler открытого положения относительно rest (Mosin: Z=80). (0,0,0) = только линейный ход.")]
	[SerializeField] private Vector3 m_BoltHandleOpenLocalEulerAngles = Vector3.zero;
	[Tooltip("Болтовой цикл: сначала поворот рукоятки, потом ход. Доля 0..1 фазы поворота на открытие/закрытие.")]
	[SerializeField, Range(0.05f, 0.45f)] private float m_BoltHandleRotatePhaseNormalized = 0.25f;
	[Tooltip("Длительность цикла rest→open→rest при очереди / FullAuto (под макс. скорострельность), сек.")]
	[SerializeField, Min(0.02f)] private float m_BoltCycleSeconds = 0.085f;
	[Tooltip("Длительность цикла при одиночном / SemiAuto и при передёргивании затвора (заметнее, чем Auto), сек.")]
	[SerializeField, Min(0.02f)] private float m_BoltCycleSecondsSingleShot = 0.16f;
	[Tooltip("Длительность болтового передёргивания (рукоятка), сек. 0 = использовать Bolt Cycle Seconds Single Shot.")]
	[SerializeField, Min(0f)] private float m_BoltActionCycleSeconds = 0.55f;
	[Tooltip("Доля цикла (0..1), на которой затвор полностью открыт и спавнится физическая гильза.")]
	[SerializeField, Range(0.15f, 0.85f)] private float m_BoltShellEjectNormalizedTime = 0.5f;
	[Tooltip("Transform dust cover (у Synty M4 pivot уже на шарнире — сам DustGuard). Пусто = нет крышки.")]
	[SerializeField] private Transform m_DustCoverHinge;
	[Tooltip("Угол ЗАКРЫТИЯ от rest меша (градусы). Rest = открыто (0). M4: Z = -160.")]
	[FormerlySerializedAs("m_DustCoverOpenDegrees")]
	[SerializeField] private float m_DustCoverClosedDegrees = -160f;
	[Tooltip("Локальная ось шарнира dust cover (обычно вдоль ствола).")]
	[SerializeField] private Vector3 m_DustCoverHingeAxis = Vector3.forward;
	[Tooltip("Длительность lerp open/close dust cover (сек). Дальше камеры — мгновенный snap.")]
	[SerializeField, Min(0.01f)] private float m_DustCoverTweenSeconds = 0.12f;

	[Header("LMG belt reload")]
	[Tooltip("Шарнир верхней крышки пулемёта (SM_Wep_MachineGun_USA_Top_01 / SM_Wep_MachineGun_Bandit_Top_01). Пусто — поиск по имени в Awake.")]
	[SerializeField] private Transform m_LmgTopCoverHinge;
	[Tooltip("Визуал ленты (SM_Wep_MachineGun_USA_Belt_01 / SM_Wep_MachineGun_Bandit_Belt_01). Пусто — поиск по имени в Awake.")]
	[SerializeField] private GameObject m_LmgBeltMeshVisual;
	[Tooltip("Угол открытия крышки LMG по локальной оси X (градусы).")]
	[SerializeField] private float m_LmgCoverOpenDegrees = 110f;
	[Tooltip("Длительность анимации открытия/закрытия крышки LMG (сек).")]
	[SerializeField, Min(0.01f)] private float m_LmgCoverTweenSeconds = 0.30f;

	[Header("Отладка")]
	[Tooltip("Луч из BarrelTransform (Barrel или MuzzleModuleVisualSocket). В Game view включи Gizmos на вкладке Game.")]
	[SerializeField] private bool m_DrawBarrelDebugRay;
	[SerializeField, Min(0.01f)] private float m_BarrelDebugRayLength = 4f;
	[SerializeField] private Color m_BarrelDebugRayColor = new Color(0f, 0.92f, 1f, 1f);
	#endregion

	#region Public Properties
	/// <summary>Точка выстрела: позиция и <c>forward</c> — направление ствола.</summary>
	public Transform BarrelTransform => m_Barrel != null ? m_Barrel : (m_MuzzleModuleVisualSocket != null ? m_MuzzleModuleVisualSocket : transform);

	/// <summary>
	/// Точка вылета пули/звука/VFX: <see cref="MuzzleExitTransformName"/> на визуале дульного модуля, иначе <see cref="BarrelTransform"/>.
	/// </summary>
	public Transform FireOriginTransform => ResolveFireOriginTransform();

	/// <summary>Точка выброса гильзы; null — эвристика от ствола.</summary>
	public Transform ShellEjectTransform => m_ShellEject;

	/// <summary>Прицел для конуса зрения; null если не задан.</summary>
	public Transform SightPivotTransform => m_SightPivot != null ? m_SightPivot : m_OpticModuleVisualSocket;

	/// <summary>Сокет визуала магазина; null если не настроен.</summary>
	public Transform MagazineSocketTransform => m_MagazineSocket;

	/// <summary>Сокет визуала второго/бокового магазина; null если не настроен.</summary>
	public Transform SecondaryMagazineSocketTransform => m_SecondaryMagazineSocket;

	/// <summary>Сокет визуала на дуле; null если не настроен.</summary>
	public Transform MuzzleModuleVisualSocket => m_MuzzleModuleVisualSocket;

	/// <summary>Сокет визуала прицела; null если не настроен.</summary>
	public Transform OpticModuleVisualSocket => m_OpticModuleVisualSocket;

	/// <summary>Сокет боковой планки; null если не настроен.</summary>
	public Transform SideRailModuleVisualSocket => m_SideRailModuleVisualSocket;

	/// <summary>Сокет приклада; null если не настроен.</summary>
	public Transform StockSocketTransform => m_StockSocket;

	/// <summary>Сокет рукоятки (under barrel); null если не настроен.</summary>
	public Transform UnderBarrelSocketTransform => m_UnderBarrelSocket;

	/// <summary>Количество слотов планки (фиксировано 3).</summary>
	public static int RailSocketCount => c_RailSocketCount;

	/// <summary>Узел для процедурной отдачи визуала; null — использовать корень инстанса.</summary>
	public Transform VisualRecoilKickPivot => m_VisualRecoilKickPivot;

	/// <summary>Слайд / затворная рама; null — нет процедурного цикла затвора.</summary>
	public Transform BoltCarrierTransform => m_BoltCarrier;

	public Vector3 BoltOpenLocalOffset => m_BoltOpenLocalOffset;
	public Vector3 BoltHandleOpenLocalEulerAngles => m_BoltHandleOpenLocalEulerAngles;
	public float BoltHandleRotatePhaseNormalized => m_BoltHandleRotatePhaseNormalized;
	public float BoltCycleSeconds => m_BoltCycleSeconds;
	public float BoltCycleSecondsSingleShot => m_BoltCycleSecondsSingleShot;
	public float BoltActionCycleSeconds => m_BoltActionCycleSeconds;
	public float BoltShellEjectNormalizedTime => m_BoltShellEjectNormalizedTime;

	/// <summary>Шарнир dust cover; null — нет крышки (AK и т.п.).</summary>
	public Transform DustCoverHingeTransform => m_DustCoverHinge;

	/// <summary>Угол закрытия от rest меша (rest = открыто у Synty M4).</summary>
	public float DustCoverClosedDegrees => m_DustCoverClosedDegrees;
	public Vector3 DustCoverHingeAxis => m_DustCoverHingeAxis;
	public float DustCoverTweenSeconds => m_DustCoverTweenSeconds;

	public Transform LmgTopCoverHinge => m_LmgTopCoverHinge;
	public GameObject LmgBeltMeshVisual => m_LmgBeltMeshVisual;
	public float LmgCoverOpenDegrees => m_LmgCoverOpenDegrees;

	/// <summary>Пресет модулей с префаба оружия (стандартная комплектация).</summary>
	public WeaponAttachmentDefinition[] PresetEquippedAttachments => m_EquippedAttachments;

	/// <summary>Инстанс визуала рукоятки (UnderBarrel + Foregrip), если установлен.</summary>
	public Transform UnderBarrelForegripVisualRoot =>
		m_UnderBarrelForegripVisualInstance != null ? m_UnderBarrelForegripVisualInstance.transform : null;
	#endregion

	#region Public Methods — сокеты планки
	/// <summary>Сокет планки по индексу 0..2; null если не задан или индекс вне диапазона.</summary>
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
	private GameObject m_InsertedSecondaryMagazineVisualInstance;
	private ItemDefinition m_CurrentSecondaryMagazineVisualDefinition;
	private readonly List<GameObject> m_AttachmentVisualInstances = new List<GameObject>(8);
	private GameObject m_UnderBarrelForegripVisualInstance;
	private GameObject m_MuzzleAttachmentVisualInstance;
	private Quaternion m_LmgCoverInitialLocalRotation;
	private Vector3 m_LmgCoverInitialLocalEulerAngles;
	private Coroutine m_LmgCoverTweenCoroutine;
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

		if (_magazineDefinition.MagazineDefinition != null && _magazineDefinition.MagazineDefinition.IsNonRemovable)
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

	/// <summary>Снимает визуал магазина с сокета без уничтожения (для переноса в руку при перезарядке).</summary>
	public GameObject TryDetachInsertedMagazineVisual()
	{
		GameObject instance = m_InsertedMagazineVisualInstance;
		m_InsertedMagazineVisualInstance = null;
		m_CurrentMagazineVisualDefinition = null;
		if (instance == null)
			return null;

		instance.transform.SetParent(null, true);
		return instance;
	}

	public GameObject TryDetachInsertedSecondaryMagazineVisual()
	{
		GameObject instance = m_InsertedSecondaryMagazineVisualInstance;
		m_InsertedSecondaryMagazineVisualInstance = null;
		m_CurrentSecondaryMagazineVisualDefinition = null;
		if (instance == null)
			return null;

		instance.transform.SetParent(null, true);
		return instance;
	}

	/// <summary>Регистрирует уже существующий инстанс как визуал магазина в сокете (после переноса из руки).</summary>
	public void AcceptTransferredMagazineVisual(GameObject _instance, ItemDefinition _magazineDefinition)
	{
		if (_instance == null || m_MagazineSocket == null)
		{
			ClearInsertedMagazineVisual();
			return;
		}

		if (m_InsertedMagazineVisualInstance != null && m_InsertedMagazineVisualInstance != _instance)
			Destroy(m_InsertedMagazineVisualInstance);

		m_InsertedMagazineVisualInstance = _instance;
		m_CurrentMagazineVisualDefinition = _magazineDefinition;
	}

	public void AcceptTransferredSecondaryMagazineVisual(GameObject _instance, ItemDefinition _magazineDefinition)
	{
		if (_instance == null || m_SecondaryMagazineSocket == null)
		{
			ClearSecondaryMagazineVisual();
			return;
		}

		if (m_InsertedSecondaryMagazineVisualInstance != null && m_InsertedSecondaryMagazineVisualInstance != _instance)
			Destroy(m_InsertedSecondaryMagazineVisualInstance);

		m_InsertedSecondaryMagazineVisualInstance = _instance;
		m_CurrentSecondaryMagazineVisualDefinition = _magazineDefinition;
	}

	/// <summary>Выравнивает визуал магазина в сокете (после прерванного переноса из руки).</summary>
	public void SnapInsertedMagazineVisualToSocketOrigin()
	{
		if (m_InsertedMagazineVisualInstance == null || m_MagazineSocket == null)
			return;

		Transform visualTransform = m_InsertedMagazineVisualInstance.transform;
		if (visualTransform.parent != m_MagazineSocket)
			visualTransform.SetParent(m_MagazineSocket, false);

		visualTransform.localPosition = Vector3.zero;
		visualTransform.localRotation = Quaternion.identity;
	}

	public void OpenLmgCover()
	{
		if (m_LmgTopCoverHinge == null)
			return;

		Quaternion target = m_LmgCoverInitialLocalRotation * Quaternion.Euler(m_LmgCoverOpenDegrees, 0f, 0f);
		StartLmgCoverTween(target);
	}

	public void CloseLmgCover()
	{
		if (m_LmgTopCoverHinge == null)
			return;

		StartLmgCoverTween(m_LmgCoverInitialLocalRotation);
	}

	private void StartLmgCoverTween(Quaternion _target)
	{
		if (m_LmgCoverTweenCoroutine != null)
			StopCoroutine(m_LmgCoverTweenCoroutine);
		m_LmgCoverTweenCoroutine = StartCoroutine(AnimateLmgCover(_target));
	}

	private System.Collections.IEnumerator AnimateLmgCover(Quaternion _target)
	{
		Quaternion start = m_LmgTopCoverHinge.localRotation;
		float elapsed = 0f;
		while (elapsed < m_LmgCoverTweenSeconds)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / m_LmgCoverTweenSeconds);
			m_LmgTopCoverHinge.localRotation = Quaternion.Slerp(start, _target, t);
			yield return null;
		}

		m_LmgTopCoverHinge.localRotation = _target;
		m_LmgCoverTweenCoroutine = null;
	}

	public void ShowLmgBelt(bool _visible)
	{
		if (m_LmgBeltMeshVisual != null)
			m_LmgBeltMeshVisual.SetActive(_visible);
	}

	public void ClearInsertedMagazineVisual()
	{
		m_CurrentMagazineVisualDefinition = null;
		if (m_InsertedMagazineVisualInstance == null)
			return;

		Destroy(m_InsertedMagazineVisualInstance);
		m_InsertedMagazineVisualInstance = null;
	}

	public void SetSecondaryMagazineVisual(ItemDefinition _magazineDefinition)
	{
		if (m_SecondaryMagazineSocket == null)
		{
			ClearSecondaryMagazineVisual();
			return;
		}

		if (_magazineDefinition == null || _magazineDefinition.EquippedVisualPrefab == null)
		{
			ClearSecondaryMagazineVisual();
			return;
		}

		if (_magazineDefinition.MagazineDefinition != null && _magazineDefinition.MagazineDefinition.IsNonRemovable)
		{
			ClearSecondaryMagazineVisual();
			return;
		}

		if (m_InsertedSecondaryMagazineVisualInstance != null && ReferenceEquals(m_CurrentSecondaryMagazineVisualDefinition, _magazineDefinition))
			return;

		ClearSecondaryMagazineVisual();
		m_InsertedSecondaryMagazineVisualInstance = Instantiate(_magazineDefinition.EquippedVisualPrefab, m_SecondaryMagazineSocket);
		m_InsertedSecondaryMagazineVisualInstance.transform.localPosition = Vector3.zero;
		m_InsertedSecondaryMagazineVisualInstance.transform.localRotation = Quaternion.identity;
		m_CurrentSecondaryMagazineVisualDefinition = _magazineDefinition;
	}

	public void ClearSecondaryMagazineVisual()
	{
		m_CurrentSecondaryMagazineVisualDefinition = null;
		if (m_InsertedSecondaryMagazineVisualInstance == null)
			return;

		Destroy(m_InsertedSecondaryMagazineVisualInstance);
		m_InsertedSecondaryMagazineVisualInstance = null;
	}

	public void ClearAllMagazineVisuals()
	{
		ClearInsertedMagazineVisual();
		ClearSecondaryMagazineVisual();
	}

	/// <summary>
	/// Синхронизирует меши модулей с <see cref="WeaponDefinition.AttachmentSlots"/> и параллельным массивом <paramref name="_equipped"/>.
	/// Индекс слота = индекс в <paramref name="_equipped"/>; визуал вешается только на сокеты визуала (не Barrel / Sight Pivot).
	/// </summary>
	public void SyncAttachmentVisuals(
		WeaponDefinition _weapon,
		WeaponAttachmentDefinition[] _equipped,
		ItemDefinition[] _equippedItems = null)
	{
		ClearAttachmentVisualsInternal();

		if (_weapon == null)
		{
			RefreshDefaultPartVisibility(false, false);
			RefreshOpticMountVisibility(false, false);
			return;
		}

		WeaponAttachmentSlotDefinition[] slots = _weapon.AttachmentSlots;
		if (slots == null || slots.Length == 0)
		{
			RefreshDefaultPartVisibility(false, false);
			RefreshOpticMountVisibility(false, false);
			return;
		}

		bool hasOpticModule = false;
		bool hasSideRailModule = false;
		bool hasStockModule = false;
		int railVisualIndex = 0;
		for (int i = 0; i < slots.Length; i++)
		{
			WeaponAttachmentSlotType slotType = slots[i].SlotType;
			int railSocketIndex = slotType == WeaponAttachmentSlotType.Rail ? railVisualIndex : -1;
			WeaponAttachmentDefinition def = ResolveEquippedForWeaponSlot(
				_equipped,
				i,
				slotType,
				railSocketIndex,
				out int equippedSourceIndex);
			if (def == null)
			{
				if (slotType == WeaponAttachmentSlotType.Rail)
					railVisualIndex++;
				continue;
			}

			if (slotType == WeaponAttachmentSlotType.SideRail)
				hasSideRailModule = true;
			else if (slotType == WeaponAttachmentSlotType.Optic || def.AttachmentType == WeaponAttachmentType.Optic)
				hasOpticModule = true;
			else if (slotType == WeaponAttachmentSlotType.Stock)
				hasStockModule = true;

			Transform parent = ResolveAttachmentVisualSocket(slotType, ref railVisualIndex);
			GameObject prefab = ResolveAttachmentVisualPrefab(def, _equippedItems, equippedSourceIndex);
			if (parent == null || prefab == null)
				continue;

			GameObject inst = Instantiate(prefab, parent);
			inst.transform.localPosition = Vector3.zero;
			inst.transform.localRotation = Quaternion.identity;
			DisablePhysicsOnEquippedVisual(inst);
			m_AttachmentVisualInstances.Add(inst);

			if (slotType == WeaponAttachmentSlotType.Muzzle)
				m_MuzzleAttachmentVisualInstance = inst;

			if (slotType == WeaponAttachmentSlotType.UnderBarrel &&
			    (def.AttachmentType == WeaponAttachmentType.Foregrip || def.AttachmentType == WeaponAttachmentType.Bipod))
				m_UnderBarrelForegripVisualInstance = inst;
		}

		RefreshDefaultPartVisibility(hasOpticModule || hasSideRailModule, hasStockModule);
		RefreshOpticMountVisibility(hasOpticModule, hasSideRailModule);
	}

	/// <summary>
	/// Цель IK левой кисти: сначала на установленной рукоятке, иначе на корне оружия.
	/// If the foregrip has Ready but no NotReady empty, fall through to the weapon-body NotReady
	/// (do not reuse grip Ready — that makes not-ready identical to ready).
	/// </summary>
	public Transform ResolveLeftHandIkTargetTransform(string _childName)
	{
		if (string.IsNullOrWhiteSpace(_childName))
			return null;

		if (m_UnderBarrelForegripVisualInstance != null)
		{
			Transform onForegrip = FindChildRecursive(m_UnderBarrelForegripVisualInstance.transform, _childName);
			if (onForegrip != null)
				return onForegrip;
		}

		return FindChildRecursive(transform, _childName);
	}

	/// <summary>Цель IK правой кисти на инстансе оружия. Иначе null.</summary>
	public Transform ResolveRightHandIkTargetTransform(string _childName)
	{
		if (string.IsNullOrWhiteSpace(_childName))
			return null;

		return FindChildRecursive(transform, _childName);
	}

	/// <summary>Удаляет все инстансы визуала модулей (магазин не трогает).</summary>
	public void ClearAttachmentVisuals()
	{
		ClearAttachmentVisualsInternal();
		RefreshDefaultPartVisibility(false, false);
		RefreshOpticMountVisibility(false, false);
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
		ItemDefinition[] itemFromState = _weaponState != null ? _weaponState.EquippedAttachmentItems : null;
		WeaponAttachmentDefinition[] use = HasAnyNonNullAttachment(fromState) ? fromState : m_EquippedAttachments;
		if (HasAnyNonNullAttachment(use))
			SyncAttachmentVisuals(_weapon, use, itemFromState);
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
	private void Awake()
	{
		if (m_LmgTopCoverHinge == null)
			m_LmgTopCoverHinge = FindChildRecursive(transform, "SM_Wep_MachineGun_USA_Top_01") ?? FindChildRecursive(transform, "SM_Wep_MachineGun_Bandit_Top_01");
		if (m_LmgBeltMeshVisual == null)
		{
			Transform belt = FindChildRecursive(transform, "SM_Wep_MachineGun_USA_Belt_01") ?? FindChildRecursive(transform, "SM_Wep_MachineGun_Bandit_Belt_01");
			if (belt != null)
				m_LmgBeltMeshVisual = belt.gameObject;
		}

		if (m_LmgBeltMeshVisual != null)
			m_LmgBeltMeshVisual.SetActive(false);

		if (m_LmgTopCoverHinge != null)
		{
			m_LmgCoverInitialLocalRotation = m_LmgTopCoverHinge.localRotation;
			m_LmgCoverInitialLocalEulerAngles = m_LmgCoverInitialLocalRotation.eulerAngles;
		}
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		SyncGameplayAnchorsFromModuleSockets();
		if (!Application.isPlaying)
		{
			RefreshDefaultPartVisibility(false, false);
			RefreshOpticMountVisibility(false, false);
		}
	}
#endif

	private void LateUpdate()
	{
		if (!m_DrawBarrelDebugRay || !Application.isPlaying)
			return;

		Transform barrel = Application.isPlaying ? FireOriginTransform : BarrelTransform;
		if (barrel == null)
			return;

		Debug.DrawRay(barrel.position, barrel.forward * m_BarrelDebugRayLength, m_BarrelDebugRayColor);
	}

	private void OnDrawGizmos()
	{
		if (!m_DrawBarrelDebugRay)
			return;

		Transform barrel = Application.isPlaying ? FireOriginTransform : BarrelTransform;
		if (barrel == null)
			return;

		Gizmos.color = m_BarrelDebugRayColor;
		Vector3 start = barrel.position;
		Vector3 end = start + barrel.forward * m_BarrelDebugRayLength;
		Gizmos.DrawLine(start, end);
	}

	private void OnDestroy()
	{
		ClearAllMagazineVisuals();
		ClearAttachmentVisualsInternal();
	}
	#endregion

	#region Private Methods
#if UNITY_EDITOR
	private void SyncGameplayAnchorsFromModuleSockets()
	{
		if (m_Barrel == null && m_MuzzleModuleVisualSocket != null)
			m_Barrel = m_MuzzleModuleVisualSocket;

		if (m_SightPivot == null && m_OpticModuleVisualSocket != null)
			m_SightPivot = m_OpticModuleVisualSocket;
	}
#endif

	/// <summary>
	/// Строго параллельный индекс слота оружия и массива установленных модулей.
	/// </summary>
	private static WeaponAttachmentDefinition ResolveEquippedForWeaponSlot(
		WeaponAttachmentDefinition[] _equipped,
		int _slotIndex,
		WeaponAttachmentSlotType _slotType,
		int _railSocketIndex,
		out int _equippedSourceIndex)
	{
		_equippedSourceIndex = -1;
		if (_equipped == null || _equipped.Length == 0 || _slotIndex < 0 || _slotIndex >= _equipped.Length)
			return null;

		WeaponAttachmentDefinition attachment = _equipped[_slotIndex];
		if (attachment == null || !attachment.SupportsWeaponSlot(_slotType, _railSocketIndex))
			return null;

		_equippedSourceIndex = _slotIndex;
		return attachment;
	}

	private static GameObject ResolveAttachmentVisualPrefab(
		WeaponAttachmentDefinition _definition,
		ItemDefinition[] _equippedItems,
		int _equippedSourceIndex)
	{
		if (_definition != null && _definition.EquippedVisualPrefab != null)
			return _definition.EquippedVisualPrefab;

		if (_equippedItems == null || _equippedSourceIndex < 0 || _equippedSourceIndex >= _equippedItems.Length)
			return null;

		return _equippedItems[_equippedSourceIndex]?.EquippedVisualPrefab;
	}

	private Transform ResolveAttachmentVisualSocket(WeaponAttachmentSlotType _slotType, ref int _railVisualIndex)
	{
		switch (_slotType)
		{
			case WeaponAttachmentSlotType.Muzzle:
				return m_MuzzleModuleVisualSocket;
			case WeaponAttachmentSlotType.Optic:
				return m_OpticModuleVisualSocket;
			case WeaponAttachmentSlotType.SideRail:
				return m_SideRailModuleVisualSocket != null ? m_SideRailModuleVisualSocket : m_OpticModuleVisualSocket;
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
		m_UnderBarrelForegripVisualInstance = null;
		m_MuzzleAttachmentVisualInstance = null;
	}

	private Transform ResolveFireOriginTransform()
	{
		Transform muzzleExit = ResolveMuzzleExitTransform();
		return muzzleExit != null ? muzzleExit : BarrelTransform;
	}

	private Transform ResolveMuzzleExitTransform()
	{
		if (m_MuzzleAttachmentVisualInstance == null)
			return null;

		return FindChildRecursive(m_MuzzleAttachmentVisualInstance.transform, MuzzleExitTransformName);
	}

	private static Transform FindChildRecursive(Transform _root, string _name)
	{
		foreach (Transform t in _root.GetComponentsInChildren<Transform>(true))
		{
			if (t != _root && t.name == _name)
				return t;
		}

		return null;
	}

	private void RefreshDefaultPartVisibility(bool _hasOpticModule, bool _hasStockModule)
	{
		SetVisualGroupActive(m_DefaultOpticVisuals, !_hasOpticModule);
		SetVisualGroupActive(m_DefaultStockVisuals, !_hasStockModule);
	}

	private void RefreshOpticMountVisibility(bool _hasPicatinnyOpticModule, bool _hasSideRailOpticModule)
	{
		SetVisualGroupActive(m_OpticRailMountVisuals, !_hasSideRailOpticModule);
		SetVisualGroupActive(m_SideRailMountVisuals, !_hasPicatinnyOpticModule);
	}

	private static void SetVisualGroupActive(GameObject[] _visuals, bool _isActive)
	{
		if (_visuals == null)
			return;

		for (int i = 0; i < _visuals.Length; i++)
		{
			if (_visuals[i] != null && _visuals[i].activeSelf != _isActive)
				_visuals[i].SetActive(_isActive);
		}
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

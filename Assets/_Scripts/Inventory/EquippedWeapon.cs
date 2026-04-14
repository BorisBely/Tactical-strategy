using UnityEngine;

/// <summary>
/// Вешается на корень префаба оружия в руке. Точка входа для данных оружия: ствол, в будущем — патроны, состояние и т.д.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(60)]
public sealed class EquippedWeapon : MonoBehaviour
{
	#region Serialized Fields
	[Header("Ствол")]
	[Tooltip("Пустышка на конце дула: позиция и forward — линия выстрела. Если пусто — для геймплея берётся transform этого компонента (часто рукоять, не дуло).")]
	[SerializeField] private Transform m_Barrel;

	[Header("Гильза")]
	[Tooltip("Точка выброса гильзы: position и forward — направление выброса. Если пусто — позиция от Barrel, направление −Barrel.right.")]
	[SerializeField] private Transform m_ShellEject;

	[Header("Прицел (зрение в «готов»)")]
	[Tooltip("Пустышка прицела: <c>UnitVision</c> берёт отсюда конус FOV и LOS при оружии на готове (ось = forward).")]
	[SerializeField] private Transform m_SightPivot;

	[Header("Магазин")]
	[Tooltip("Точка, куда крепится отдельный visual вставленного магазина. Если пусто, визуал магазина в оружии не создаётся.")]
	[SerializeField] private Transform m_MagazineSocket;

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

	/// <summary>Узел для процедурной отдачи визуала; null — использовать корень инстанса.</summary>
	public Transform VisualRecoilKickPivot => m_VisualRecoilKickPivot;
	#endregion

	#region Private Fields
	private GameObject m_InsertedMagazineVisualInstance;
	private ItemDefinition m_CurrentMagazineVisualDefinition;
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
	}
	#endregion

	#region Private Methods
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

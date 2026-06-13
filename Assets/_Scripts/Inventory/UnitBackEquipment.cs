using System;
using UnityEngine;

/// <summary>
/// Визуал рюкзака на якоре Spine_02.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitBackEquipment : MonoBehaviour
{
	#region Events
	public event Action BackEquipmentChanged;
	#endregion

	#region Serialized Fields
	[Tooltip("Кость Spine_02 — родитель для EquippedVisualPrefab рюкзака.")]
	[SerializeField] private Transform m_BackAnchor;
	#endregion

	#region Private Fields
	private GameObject m_BackpackInstance;
	private ItemDefinition m_EquippedDefinition;
	#endregion

	#region Public Properties
	public ItemDefinition EquippedDefinition => m_EquippedDefinition;
	public Transform BackAnchor => m_BackAnchor;
	#endregion

	#region Public Methods
	public void ClearBack()
	{
		m_EquippedDefinition = null;
		if (m_BackpackInstance != null)
		{
			Destroy(m_BackpackInstance);
			m_BackpackInstance = null;
		}

		BackEquipmentChanged?.Invoke();
	}

	public bool TryEquip(ItemDefinition _item)
	{
		if (_item == null || !_item.IsEquipment || _item.EquipmentKind != EquipmentKind.Backpack)
			return false;

		if (m_BackAnchor == null)
		{
			Debug.LogWarning($"{nameof(UnitBackEquipment)}: не задан якорь Spine_02.", this);
			return false;
		}

		ClearBackInternal(false);
		m_EquippedDefinition = _item;

		GameObject prefab = _item.EquippedVisualPrefab;
		if (prefab == null)
		{
			BackEquipmentChanged?.Invoke();
			return true;
		}

		m_BackpackInstance = Instantiate(prefab, m_BackAnchor);
		m_BackpackInstance.transform.localPosition = prefab.transform.localPosition;
		m_BackpackInstance.transform.localRotation = prefab.transform.localRotation;
		m_BackpackInstance.transform.localScale = prefab.transform.localScale;
		DisablePhysicsOnEquippedVisual(m_BackpackInstance);

		BackEquipmentChanged?.Invoke();
		return true;
	}
	#endregion

	#region Private Methods
	private void ClearBackInternal(bool _notify)
	{
		m_EquippedDefinition = null;
		if (m_BackpackInstance != null)
		{
			Destroy(m_BackpackInstance);
			m_BackpackInstance = null;
		}

		if (_notify)
			BackEquipmentChanged?.Invoke();
	}

	private static void DisablePhysicsOnEquippedVisual(GameObject _root)
	{
		if (_root == null)
			return;

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

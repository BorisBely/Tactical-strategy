using System;
using UnityEngine;

/// <summary>
/// Визуал шлема на якоре Head. Декор задаётся предпочтениями юнита.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitHeadEquipment : MonoBehaviour
{
	#region Events
	public event Action HeadEquipmentChanged;
	#endregion

	#region Serialized Fields
	[Tooltip("Кость или пустой объект на голове — родитель для EquippedVisualPrefab шлема.")]
	[SerializeField] private Transform m_HeadAnchor;
	#endregion

	#region Private Fields
	private GameObject m_HelmetInstance;
	private ItemDefinition m_EquippedDefinition;
	#endregion

	#region Public Properties
	public ItemDefinition EquippedDefinition => m_EquippedDefinition;
	public Transform HeadAnchor => m_HeadAnchor;
	#endregion

	#region Public Methods
	public void ClearHead()
	{
		m_EquippedDefinition = null;
		if (m_HelmetInstance != null)
		{
			Destroy(m_HelmetInstance);
			m_HelmetInstance = null;
		}

		HeadEquipmentChanged?.Invoke();
	}

	public bool TryEquip(
		ItemDefinition _item,
		UnitIndividualTraits _traits,
		UnitCharacterAppearance _appearance)
	{
		if (_item == null || !_item.IsEquipment || _item.EquipmentKind != EquipmentKind.Helmet)
			return false;

		if (m_HeadAnchor == null)
		{
			Debug.LogWarning($"{nameof(UnitHeadEquipment)}: не задан якорь Head.", this);
			return false;
		}

		ClearHeadInternal(false);
		m_EquippedDefinition = _item;

		GameObject prefab = _item.EquippedVisualPrefab;
		if (prefab == null)
		{
			HeadEquipmentChanged?.Invoke();
			return true;
		}

		m_HelmetInstance = Instantiate(prefab, m_HeadAnchor);
		m_HelmetInstance.transform.localPosition = Vector3.zero;
		m_HelmetInstance.transform.localRotation = Quaternion.identity;
		m_HelmetInstance.transform.localScale = Vector3.one;
		DisablePhysicsOnEquippedVisual(m_HelmetInstance);

		ApplyUnitPreferencesToInstance(_item, _traits, _appearance);
		HeadEquipmentChanged?.Invoke();
		return true;
	}

	public void RefreshEquippedVisual(UnitIndividualTraits _traits, UnitCharacterAppearance _appearance)
	{
		if (m_HelmetInstance == null || m_EquippedDefinition == null)
			return;

		ApplyUnitPreferencesToInstance(m_EquippedDefinition, _traits, _appearance);
	}
	#endregion

	#region Private Methods
	private void ClearHeadInternal(bool _notify)
	{
		m_EquippedDefinition = null;
		if (m_HelmetInstance != null)
		{
			Destroy(m_HelmetInstance);
			m_HelmetInstance = null;
		}

		if (_notify)
			HeadEquipmentChanged?.Invoke();
	}

	private void ApplyUnitPreferencesToInstance(
		ItemDefinition _item,
		UnitIndividualTraits _traits,
		UnitCharacterAppearance _appearance)
	{
		if (m_HelmetInstance == null)
			return;

		HelmetEquippedVisual visual = m_HelmetInstance.GetComponent<HelmetEquippedVisual>();
		if (visual == null)
			return;

		EquipmentVisualProfileDefinition profile = _item.VisualProfile;
		string profileId = profile != null ? profile.ProfileId : string.Empty;
		CharacterGender gender = _appearance != null ? _appearance.Gender : CharacterGender.Male;

		if (_traits != null && _traits.TryGetPreference(profileId, out UnitEquipmentVisualPreferenceEntry preference))
			visual.ApplyPreferences(preference, gender);
		else if (profile != null)
			visual.ApplyPreferences(profile.CreateDefaultPreference(), gender);
		else
			visual.ApplyDefault();
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

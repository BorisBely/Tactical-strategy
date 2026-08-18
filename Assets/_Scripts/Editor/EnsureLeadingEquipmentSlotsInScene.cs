#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Размещает 3 ручных leading-слота экипировки в UnitInventory и PresetEquipmentPanel
/// (видны в Edit Mode без Play).
/// </summary>
public static class EnsureLeadingEquipmentSlotsInScene
{
	private const string c_SampleScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_RuntimeCellPrefabPath = "Assets/Prefabs/UI/InventoryCell.prefab";
	private const string c_MissionPrepCellPrefabPath = "Assets/Prefabs/UI/InventoryCellMissionPrep.prefab";

	private static readonly string[] s_UnitEmptyKeys =
	{
		InventorySlotUiUtility.EmptyEquipSlotGenericKey,
		InventorySlotUiUtility.EmptyEquipSlotGenericKey,
		InventorySlotUiUtility.EmptyEquipSlotGenericKey
	};

	private static readonly string[] s_UnitEmptyRuLabels =
	{
		"Пусто",
		"Пусто",
		"Пусто"
	};

	[MenuItem("Tools/Inventory/Ensure Leading Equipment Slots In SampleScene")]
	public static void EnsureInSampleScene()
	{
		Scene scene = EditorSceneManager.OpenScene(c_SampleScenePath, OpenSceneMode.Single);
		bool changed = false;

		changed |= EnsurePanelSlots("UnitInventory", c_RuntimeCellPrefabPath);
		changed |= EnsurePanelSlots("PresetEquipmentPanel", c_MissionPrepCellPrefabPath);

		if (changed)
		{
			EditorSceneManager.MarkSceneDirty(scene);
			EditorSceneManager.SaveScene(scene);
			Debug.Log($"{nameof(EnsureLeadingEquipmentSlotsInScene)}: leading equipment slots ensured and scene saved.");
		}
		else
			Debug.Log($"{nameof(EnsureLeadingEquipmentSlotsInScene)}: slots already present, no changes.");
	}

	private static bool EnsurePanelSlots(string _panelObjectName, string _prefabPath)
	{
		GameObject panelObject = FindSceneObjectByName(_panelObjectName);
		if (panelObject == null)
		{
			Debug.LogWarning($"{nameof(EnsureLeadingEquipmentSlotsInScene)}: '{_panelObjectName}' not found.");
			return false;
		}

		InventoryPanelView panel = panelObject.GetComponent<InventoryPanelView>();
		if (panel == null)
			panel = panelObject.GetComponentInChildren<InventoryPanelView>(true);

		if (panel == null || panel.SlotsContainerTransform == null)
		{
			Debug.LogWarning($"{nameof(EnsureLeadingEquipmentSlotsInScene)}: InventoryPanelView/Content missing on '{_panelObjectName}'.");
			return false;
		}

		InventorySlotView prefab = AssetDatabase.LoadAssetAtPath<InventorySlotView>(_prefabPath);
		if (prefab == null)
		{
			Debug.LogError($"{nameof(EnsureLeadingEquipmentSlotsInScene)}: prefab not found at '{_prefabPath}'.");
			return false;
		}

		Transform container = panel.SlotsContainerTransform;
		bool changed = false;

		for (int i = 0; i < 3; i++)
		{
			InventorySlotView slot = FindSceneLeadingSlot(container, i);
			if (slot == null)
			{
				slot = (InventorySlotView)PrefabUtility.InstantiatePrefab(prefab, container);
				slot.gameObject.name = $"EquipSlot_{i}";
				changed = true;
			}

			slot.transform.SetSiblingIndex(i);
			Undo.RecordObject(slot, "Ensure leading equipment slot");
			slot.SetEmptyLocalizationKey(s_UnitEmptyKeys[i]);

			SerializedObject so = new SerializedObject(slot);
			SerializedProperty keyProp = so.FindProperty("m_EmptyLocalizationKey");
			if (keyProp != null)
			{
				keyProp.stringValue = s_UnitEmptyKeys[i];
				so.ApplyModifiedPropertiesWithoutUndo();
			}

			TMP_Text nameText = slot.GetComponentInChildren<TMP_Text>(true);
			if (nameText != null)
			{
				Undo.RecordObject(nameText, "Set empty equip slot label");
				nameText.text = s_UnitEmptyRuLabels[i];
				EditorUtility.SetDirty(nameText);
			}

			EditorUtility.SetDirty(slot);
			PrefabUtility.RecordPrefabInstancePropertyModifications(slot);
		}

		EditorUtility.SetDirty(panel);
		return changed || true;
	}

	private static InventorySlotView FindSceneLeadingSlot(Transform _container, int _index)
	{
		int found = 0;
		for (int i = 0; i < _container.childCount; i++)
		{
			Transform child = _container.GetChild(i);
			InventorySlotView slot = child.GetComponent<InventorySlotView>();
			if (slot == null || slot.IsRuntimeSpawned)
				continue;

			if (found == _index)
				return slot;

			found++;
		}

		return null;
	}

	private static GameObject FindSceneObjectByName(string _name)
	{
		GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
		for (int i = 0; i < all.Length; i++)
		{
			GameObject go = all[i];
			if (go == null || go.name != _name)
				continue;
			if (!go.scene.IsValid() || !go.scene.isLoaded)
				continue;
			if (EditorUtility.IsPersistent(go))
				continue;
			return go;
		}

		return null;
	}
}
#endif

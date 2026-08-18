#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Печёт прозрачные Sprite-иконки из EquippedVisualPrefab / DropWorldPrefab в ItemDefinition.m_Icon.
/// Ориентация: identity + collider/renderer bounds (как runtime studio).
/// </summary>
public static class ItemIconBaker
{
	private const string c_InventoryRoot = "Assets/GameData/Inventory";
	private const string c_IconsFolder = "Assets/GameData/Inventory/Icons";
	private const int c_PreviewLayer = 31;

	[MenuItem("Tools/Inventory/Bake Item Icons")]
	public static void BakeAllItemIcons()
	{
		ItemDefinition[] items = LoadAllInventoryItems();
		BakeItems(items);
	}

	[MenuItem("Tools/Inventory/Bake Selected Item Icons")]
	public static void BakeSelectedItemIcons()
	{
		Object[] selection = Selection.objects;
		var items = new List<ItemDefinition>();
		for (int i = 0; i < selection.Length; i++)
		{
			if (selection[i] is ItemDefinition item)
				items.Add(item);
		}

		if (items.Count == 0)
		{
			Debug.LogWarning("Select one or more ItemDefinition assets.");
			return;
		}

		BakeItems(items.ToArray());
	}

	private static void BakeItems(ItemDefinition[] _items)
	{
		if (_items == null || _items.Length == 0)
			return;

		EnsureIconsFolder();

		var stage = new GameObject("ItemIconBaker_Stage");
		stage.hideFlags = HideFlags.HideAndDontSave;
		stage.transform.position = new Vector3(8000f, 8000f, 8000f);

		var camGo = new GameObject("ItemIconBaker_Camera");
		camGo.hideFlags = HideFlags.HideAndDontSave;
		camGo.transform.SetParent(stage.transform, false);
		Camera camera = camGo.AddComponent<Camera>();
		camera.enabled = false;
		camera.clearFlags = CameraClearFlags.SolidColor;
		camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
		camera.orthographic = true;
		camera.nearClipPlane = 0.01f;
		camera.farClipPlane = 50f;
		camera.cullingMask = 1 << c_PreviewLayer;
		camera.allowHDR = false;
		camera.allowMSAA = false;

		var lightGo = new GameObject("ItemIconBaker_Light");
		lightGo.hideFlags = HideFlags.HideAndDontSave;
		lightGo.transform.SetParent(stage.transform, false);
		lightGo.transform.localRotation = Quaternion.Euler(35f, -40f, 0f);
		Light light = lightGo.AddComponent<Light>();
		light.type = LightType.Directional;
		light.intensity = 1.1f;
		light.shadows = LightShadows.None;
		light.cullingMask = 1 << c_PreviewLayer;

		int size = InventoryItemIconCaptureUtility.IconSize;
		var rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
		{
			antiAliasing = 1,
			filterMode = FilterMode.Bilinear
		};
		camera.targetTexture = rt;

		int baked = 0;
		int skipped = 0;
		try
		{
			for (int i = 0; i < _items.Length; i++)
			{
				ItemDefinition item = _items[i];
				if (item == null)
				{
					skipped++;
					continue;
				}

				EditorUtility.DisplayProgressBar(
					"Bake Item Icons",
					item.name,
					(float)i / _items.Length);

				if (BakeOne(item, stage.transform, camera, rt))
					baked++;
				else
					skipped++;
			}
		}
		finally
		{
			EditorUtility.ClearProgressBar();
			camera.targetTexture = null;
			rt.Release();
			Object.DestroyImmediate(rt);
			Object.DestroyImmediate(stage);
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log($"ItemIconBaker: baked={baked}, skipped={skipped}, total={_items.Length}");
	}

	private static bool BakeOne(ItemDefinition _item, Transform _stage, Camera _camera, RenderTexture _rt)
	{
		GameObject prefab = _item.EquippedVisualPrefab != null ? _item.EquippedVisualPrefab : _item.DropWorldPrefab;
		if (prefab == null)
		{
			Debug.LogWarning($"ItemIconBaker: no visual prefab for '{_item.name}'.");
			return false;
		}

		GameObject instance = null;
		try
		{
			instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _stage);
			if (instance == null)
				instance = Object.Instantiate(prefab, _stage);

			instance.hideFlags = HideFlags.HideAndDontSave;
			instance.transform.localPosition = Vector3.zero;
			instance.transform.localScale = Vector3.one;

			bool isWeapon = _item.WeaponDefinition != null;
			instance.transform.localRotation =
				InventoryItemIconCaptureUtility.ResolvePresentationRotation(instance, isWeapon);

			InventoryItemIconCaptureUtility.SetLayerRecursively(instance, c_PreviewLayer);
			InventoryItemIconCaptureUtility.DisablePhysicsAndAudio(instance);

			Vector3 viewDir = InventoryItemIconCaptureUtility.ResolveViewDirection(instance, isWeapon);
			InventoryItemIconCaptureUtility.FitOrthographicCamera(_camera, instance, viewDir);
			_camera.Render();

			int size = InventoryItemIconCaptureUtility.IconSize;
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = _rt;
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
			texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
			texture.Apply(false, false);
			RenderTexture.active = previous;

			string assetPath = $"{c_IconsFolder}/{SanitizeFileName(_item.name)}.png";
			byte[] png = texture.EncodeToPNG();
			Object.DestroyImmediate(texture);
			File.WriteAllBytes(GetAbsolutePath(assetPath), png);
			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
			ConfigureSpriteImporter(assetPath);

			Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
			if (sprite == null)
			{
				Debug.LogWarning($"ItemIconBaker: failed to load sprite at '{assetPath}'.");
				return false;
			}

			SerializedObject so = new SerializedObject(_item);
			SerializedProperty iconProp = so.FindProperty("m_Icon");
			if (iconProp == null)
			{
				Debug.LogWarning($"ItemIconBaker: m_Icon missing on '{_item.name}'.");
				return false;
			}

			iconProp.objectReferenceValue = sprite;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(_item);
			return true;
		}
		finally
		{
			if (instance != null)
				Object.DestroyImmediate(instance);
		}
	}

	private static void ConfigureSpriteImporter(string _assetPath)
	{
		TextureImporter importer = AssetImporter.GetAtPath(_assetPath) as TextureImporter;
		if (importer == null)
			return;

		importer.textureType = TextureImporterType.Sprite;
		importer.spriteImportMode = SpriteImportMode.Single;
		importer.spritePixelsPerUnit = 100f;
		importer.mipmapEnabled = false;
		importer.filterMode = FilterMode.Bilinear;
		importer.alphaIsTransparency = true;
		importer.npotScale = TextureImporterNPOTScale.None;
		importer.SaveAndReimport();
	}

	private static void EnsureIconsFolder()
	{
		if (AssetDatabase.IsValidFolder(c_IconsFolder))
			return;

		if (!AssetDatabase.IsValidFolder("Assets/GameData"))
			AssetDatabase.CreateFolder("Assets", "GameData");
		if (!AssetDatabase.IsValidFolder(c_InventoryRoot))
			AssetDatabase.CreateFolder("Assets/GameData", "Inventory");
		AssetDatabase.CreateFolder(c_InventoryRoot, "Icons");
	}

	private static string GetAbsolutePath(string _assetPath)
	{
		string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
		return Path.Combine(projectRoot, _assetPath.Replace('/', Path.DirectorySeparatorChar));
	}

	private static string SanitizeFileName(string _name)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		char[] chars = _name.ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			for (int j = 0; j < invalid.Length; j++)
			{
				if (chars[i] == invalid[j])
				{
					chars[i] = '_';
					break;
				}
			}
		}

		return new string(chars);
	}

	private static ItemDefinition[] LoadAllInventoryItems()
	{
		string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { c_InventoryRoot });
		var list = new List<ItemDefinition>(guids.Length);
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			if (!path.Contains("/Item_"))
				continue;

			ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
			if (item != null)
				list.Add(item);
		}

		return list.ToArray();
	}
}
#endif

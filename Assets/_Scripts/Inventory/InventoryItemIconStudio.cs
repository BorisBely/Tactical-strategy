using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Один offscreen-рендер для иконок оружия со сборкой модов + LRU-кэш по хэшу сборки.
/// Обычные предметы используют bake <see cref="ItemDefinition.Icon"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class InventoryItemIconStudio : MonoBehaviour
{
	#region Constants
	private const int c_MaxCacheEntries = 48;
	private const int c_IconLayer = 31;
	#endregion

	#region Private Fields
	private static InventoryItemIconStudio s_Instance;
	private static bool s_ApplicationQuitting;

	private readonly Dictionary<int, CacheEntry> m_Cache = new Dictionary<int, CacheEntry>(64);
	private readonly LinkedList<int> m_LruOrder = new LinkedList<int>();

	private Camera m_Camera;
	private Light m_Light;
	private RenderTexture m_RenderTexture;
	private Transform m_StageRoot;
	#endregion

	#region Nested Types
	private sealed class CacheEntry
	{
		public Sprite Sprite;
		public Texture2D Texture;
		public LinkedListNode<int> LruNode;
	}
	#endregion

	#region Public Properties
	public static InventoryItemIconStudio Instance
	{
		get
		{
			if (s_ApplicationQuitting)
				return s_Instance;
			if (s_Instance != null)
				return s_Instance;
			EnsureInstance();
			return s_Instance;
		}
	}
	#endregion

	#region Bootstrap
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		s_Instance = null;
		s_ApplicationQuitting = false;
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		EnsureInstance();
	}

	private static void EnsureInstance()
	{
		if (s_Instance != null || s_ApplicationQuitting)
			return;

		var go = new GameObject(nameof(InventoryItemIconStudio));
		DontDestroyOnLoad(go);
		s_Instance = go.AddComponent<InventoryItemIconStudio>();
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (s_Instance != null && s_Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		s_Instance = this;
		EnsureStudioBuilt();
	}

	private void OnApplicationQuit()
	{
		s_ApplicationQuitting = true;
	}

	private void OnDestroy()
	{
		ClearCacheInternal();
		if (m_RenderTexture != null)
		{
			m_RenderTexture.Release();
			Destroy(m_RenderTexture);
			m_RenderTexture = null;
		}

		if (s_Instance == this)
			s_Instance = null;
	}
	#endregion

	#region Public Methods
	public static bool ShouldUseRuntimeIcon(InventorySlotRuntimeData _data)
	{
		if (_data.IsEmpty || _data.Definition == null || _data.InstanceState == null)
			return false;

		return _data.Definition.WeaponDefinition != null && _data.InstanceState.WeaponState != null;
	}

	public static int ComputeBuildHash(InventorySlotRuntimeData _data)
	{
		return InventoryItemIconCaptureUtility.ComputeWeaponBuildHash(_data);
	}

	public Sprite GetOrRender(InventorySlotRuntimeData _data)
	{
		if (!ShouldUseRuntimeIcon(_data))
			return _data.Definition != null ? _data.Definition.Icon : null;

		EnsureStudioBuilt();
		int hash = ComputeBuildHash(_data);
		if (m_Cache.TryGetValue(hash, out CacheEntry entry))
		{
			TouchLru(entry, hash);
			return entry.Sprite;
		}

		Sprite rendered = RenderWeaponIcon(_data);
		if (rendered == null)
			return _data.Definition.Icon;

		StoreCache(hash, rendered);
		return rendered;
	}

	public void InvalidateAll()
	{
		ClearCacheInternal();
	}
	#endregion

	#region Private Methods
	private void EnsureStudioBuilt()
	{
		if (m_Camera != null)
			return;

		var stageGo = new GameObject("IconStudioStage");
		stageGo.transform.SetParent(transform, false);
		stageGo.transform.position = new Vector3(5000f, 5000f, 5000f);
		m_StageRoot = stageGo.transform;

		var camGo = new GameObject("IconStudioCamera");
		camGo.transform.SetParent(m_StageRoot, false);
		m_Camera = camGo.AddComponent<Camera>();
		m_Camera.enabled = false;
		m_Camera.clearFlags = CameraClearFlags.SolidColor;
		m_Camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
		m_Camera.orthographic = true;
		m_Camera.nearClipPlane = 0.01f;
		m_Camera.farClipPlane = 50f;
		m_Camera.cullingMask = 1 << c_IconLayer;
		m_Camera.allowHDR = false;
		m_Camera.allowMSAA = false;
		m_Camera.depth = -100;

		m_RenderTexture = new RenderTexture(
			InventoryItemIconCaptureUtility.IconSize,
			InventoryItemIconCaptureUtility.IconSize,
			16,
			RenderTextureFormat.ARGB32)
		{
			name = "InventoryItemIconRT",
			antiAliasing = 1,
			filterMode = FilterMode.Bilinear,
			wrapMode = TextureWrapMode.Clamp
		};
		m_Camera.targetTexture = m_RenderTexture;

		var lightGo = new GameObject("IconStudioLight");
		lightGo.transform.SetParent(m_StageRoot, false);
		lightGo.transform.localRotation = Quaternion.Euler(35f, -40f, 0f);
		m_Light = lightGo.AddComponent<Light>();
		m_Light.type = LightType.Directional;
		m_Light.intensity = 1.1f;
		m_Light.shadows = LightShadows.None;
		m_Light.cullingMask = 1 << c_IconLayer;
	}

	private Sprite RenderWeaponIcon(InventorySlotRuntimeData _data)
	{
		ItemDefinition definition = _data.Definition;
		GameObject visualPrefab = definition.EquippedVisualPrefab != null
			? definition.EquippedVisualPrefab
			: definition.DropWorldPrefab;
		if (visualPrefab == null)
			return null;

		GameObject instance = null;
		try
		{
			instance = Instantiate(visualPrefab, m_StageRoot);
			instance.name = "IconCapture_" + definition.name;
			instance.transform.localPosition = Vector3.zero;
			instance.transform.localScale = Vector3.one;

			// Сначала identity + collider/renderer bounds → правильный исходный угол.
			bool isWeapon = definition.WeaponDefinition != null;
			instance.transform.localRotation =
				InventoryItemIconCaptureUtility.ResolvePresentationRotation(instance, isWeapon);

			WeaponRuntimeState weaponState = _data.InstanceState != null ? _data.InstanceState.WeaponState : null;
			InventoryItemIconCaptureUtility.ApplyWeaponVisualState(instance, definition, weaponState);

			// Модули/магазины спавнятся на default layer — слой после attach.
			InventoryItemIconCaptureUtility.SetLayerRecursively(instance, c_IconLayer);
			InventoryItemIconCaptureUtility.DisablePhysicsAndAudio(instance);

			Vector3 viewDir = InventoryItemIconCaptureUtility.ResolveViewDirection(instance, isWeapon);
			InventoryItemIconCaptureUtility.FitOrthographicCamera(m_Camera, instance, viewDir);
			m_Camera.Render();

			int size = InventoryItemIconCaptureUtility.IconSize;
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = m_RenderTexture;
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
			{
				name = "IconTex_" + definition.name,
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp
			};
			texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
			texture.Apply(false, false);
			RenderTexture.active = previous;

			return Sprite.Create(
				texture,
				new Rect(0f, 0f, size, size),
				new Vector2(0.5f, 0.5f),
				100f);
		}
		finally
		{
			InventoryItemIconCaptureUtility.DestroyCaptureInstanceImmediate(instance);
		}
	}

	private void StoreCache(int _hash, Sprite _sprite)
	{
		while (m_Cache.Count >= c_MaxCacheEntries && m_LruOrder.Count > 0)
			EvictOldest();

		var entry = new CacheEntry
		{
			Sprite = _sprite,
			Texture = _sprite.texture,
			LruNode = m_LruOrder.AddLast(_hash)
		};
		m_Cache[_hash] = entry;
	}

	private void TouchLru(CacheEntry _entry, int _hash)
	{
		if (_entry.LruNode != null)
			m_LruOrder.Remove(_entry.LruNode);
		_entry.LruNode = m_LruOrder.AddLast(_hash);
	}

	private void EvictOldest()
	{
		LinkedListNode<int> oldest = m_LruOrder.First;
		if (oldest == null)
			return;

		int hash = oldest.Value;
		m_LruOrder.RemoveFirst();
		if (!m_Cache.TryGetValue(hash, out CacheEntry entry))
			return;

		m_Cache.Remove(hash);
		DestroyCacheEntry(entry);
	}

	private void ClearCacheInternal()
	{
		foreach (KeyValuePair<int, CacheEntry> pair in m_Cache)
			DestroyCacheEntry(pair.Value);
		m_Cache.Clear();
		m_LruOrder.Clear();
	}

	private static void DestroyCacheEntry(CacheEntry _entry)
	{
		if (_entry == null)
			return;
		if (_entry.Sprite != null)
			Destroy(_entry.Sprite);
		if (_entry.Texture != null)
			Destroy(_entry.Texture);
	}
	#endregion
}

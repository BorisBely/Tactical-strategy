using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Рендерит инстанс <see cref="ItemDefinition.EquippedVisualPrefab"/> с визуалом модулей как у префаба
/// (<see cref="EquippedWeapon.RefreshAttachmentVisualsFromState"/> + пустой runtime state → берётся пресет префаба).
/// Выдаёт <see cref="Sprite"/> под опции TMP Dropdown. Нужны: отдельный слой, камера не видит игру, игровые камеры не видят этот слой.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponEquippedVisualPreviewPortraitFactory : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField]
	[Tooltip("Один пользовательский слой: временный объект оружия + направленный свет только на этом слое.")]
	private LayerMask m_PreviewSingleLayerMask = 1 << 15;

	[SerializeField]
	[Tooltip("Мировый якорь, куда временно ставится копия оружия (подальше от геймплей-сцены).")]
	private Vector3 m_IsolationPivot = new Vector3(800f, 800f, 800f);

	[SerializeField, Min(16)] private int m_RenderTextureWidth = 160;
	[SerializeField, Min(16)] private int m_RenderTextureHeight = 96;

	[SerializeField, Range(1f, 2.75f)] private float m_BoundsFitMultiplier = 1.12f;

	[SerializeField] private Color m_ClearColor = new Color(0f, 0f, 0f, 0f);

	[SerializeField] private Vector3 m_WeaponEulerOffset = new Vector3(-8f, 125f, 5f);

	[SerializeField, Min(0.01f)] private float m_CameraFarClipPlane = 48f;

	[SerializeField, Tooltip("Положение камеры: относительный «отъезд» от центра ограничивающего короба (в мирах локально повёрнутые).")]
	private Vector3 m_OrbitEuler = new Vector3(18f, -130f, -2f);

	[SerializeField, Min(0.5f)] private float m_BackDistanceFromExtents = 3.4f;

	[SerializeField] private float m_BackDistanceBias = 0.15f;

	[SerializeField, Tooltip("Во время захвата кадра временно выключает отбрасывание теней (на тестовой сборке может не нужно).")]
	private bool m_ShadowCastingOffDuringCapture = true;
	#endregion

	#region Private Fields
	private Camera m_PortraitCamera;
	private Light m_FillDirectionalLight;
	private RenderTexture m_WorkRt;

	private readonly Dictionary<int, Sprite> m_PortraitSpritesByWeaponItemInstanceId = new Dictionary<int, Sprite>(32);
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsurePortraitRigReady();
	}

	private void OnDestroy()
	{
		ClearCaches();

		if (m_WorkRt != null)
		{
			m_WorkRt.Release();
			Destroy(m_WorkRt);
			m_WorkRt = null;
		}

		if (m_PortraitCamera != null)
		{
			Destroy(m_PortraitCamera.gameObject);
			m_PortraitCamera = null;
			m_FillDirectionalLight = null;
		}
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (!IsExactlySingleLayer(mask: m_PreviewSingleLayerMask))
			Debug.LogWarning(
				$"{nameof(WeaponEquippedVisualPreviewPortraitFactory)} ({name}): в {nameof(m_PreviewSingleLayerMask)} нужен ровно один слой (один установленный бит).",
				this);
	}
#endif
	#endregion

	#region Public Methods
	public Sprite GetOrCreatePortraitSprite(ItemDefinition _weaponItem)
	{
		if (_weaponItem == null)
			return null;

		int id = _weaponItem.GetInstanceID();
		if (m_PortraitSpritesByWeaponItemInstanceId.TryGetValue(id, out Sprite cached))
			return cached;

		if (_weaponItem.EquippedVisualPrefab == null || !_weaponItem.IsEquipment ||
		    _weaponItem.EquipmentKind != EquipmentKind.Weapon ||
		    _weaponItem.WeaponDefinition == null)
		{
			Debug.LogWarning(
				$"{nameof(WeaponEquippedVisualPreviewPortraitFactory)}: «{_weaponItem.name}» должен быть снаряжением-оружием с WeaponDefinition и Equipped Visual Prefab.",
				_weaponItem);
			return null;
		}

		if (!TryGetSingleLayerIndex(m_PreviewSingleLayerMask, out int previewLayerIndex))
			return null;

		EnsurePortraitRigReady();
		GameObject disposableRoot = new GameObject($"WeaponPortraitStaging_{_weaponItem.name}");
		disposableRoot.transform.position = m_IsolationPivot;

		GameObject stagedWeapon = Instantiate(_weaponItem.EquippedVisualPrefab, disposableRoot.transform);
		stagedWeapon.transform.localPosition = Vector3.zero;
		stagedWeapon.transform.localRotation = Quaternion.Euler(m_WeaponEulerOffset);

		ItemInstanceState ephemeralState = ItemInstanceState.CreateForDefinition(_weaponItem);
		SetLayerRecursively(disposableRoot, previewLayerIndex);

		if (m_ShadowCastingOffDuringCapture)
			SetShadowCastingRecursively(disposableRoot.transform, false);

		EquippedWeapon equipped = stagedWeapon.GetComponent<EquippedWeapon>();
		if (equipped != null)
			equipped.RefreshAttachmentVisualsFromState(_weaponItem.WeaponDefinition, ephemeralState?.WeaponState);
		else
			Debug.LogWarning(
				$"{nameof(WeaponEquippedVisualPreviewPortraitFactory)}: префаб «{_weaponItem.EquippedVisualPrefab.name}» без {nameof(EquippedWeapon)} — модули не синхронизируются по данным платформы.",
				stagedWeapon);

		try
		{
			Bounds worldBounds = ComputeWorldRenderBounds(disposableRoot.transform);
			ConfigureCameraFacingBounds(worldBounds);
			CapturePortraitToCaches(id);
			return m_PortraitSpritesByWeaponItemInstanceId[id];
		}
		finally
		{
			m_PortraitCamera.targetTexture = null;
			m_FillDirectionalLight.enabled = false;

			if (disposableRoot != null)
				Destroy(disposableRoot);
		}
	}

	public void ClearCaches()
	{
		foreach (KeyValuePair<int, Sprite> kv in m_PortraitSpritesByWeaponItemInstanceId)
			DisposeRuntimeSprite(kv.Value);

		m_PortraitSpritesByWeaponItemInstanceId.Clear();
	}

	public void Invalidate(ItemDefinition _weaponItem)
	{
		if (_weaponItem == null)
			return;

		int sid = _weaponItem.GetInstanceID();
		if (!m_PortraitSpritesByWeaponItemInstanceId.TryGetValue(sid, out Sprite s))
			return;

		m_PortraitSpritesByWeaponItemInstanceId.Remove(sid);
		DisposeRuntimeSprite(s);
	}
	#endregion

	#region Private Methods
	private static void DisposeRuntimeSprite(Sprite _sprite)
	{
		if (_sprite == null)
			return;

		Destroy(_sprite);
	}

	private static bool TryGetSingleLayerIndex(LayerMask _mask, out int _layer)
	{
		_layer = -1;
		int value = _mask.value;
		if (value <= 0 || (value & (value - 1)) != 0)
		{
			Debug.LogError($"{nameof(WeaponEquippedVisualPreviewPortraitFactory)}: в LayerMask нужен ровно один слой для изоляции.");
			return false;
		}

		for (int i = 0; i < 32; i++)
		{
			if (((value >> i) & 1) != 0)
			{
				_layer = i;
				return true;
			}
		}

		return false;
	}

	private void CapturePortraitToCaches(int instanceIdCacheKey)
	{
		EnsureWorkingRenderTexture();
		m_PortraitCamera.targetTexture = m_WorkRt;
		m_PortraitCamera.allowHDR = false;
		m_PortraitCamera.cullingMask = m_PreviewSingleLayerMask.value;
		m_PortraitCamera.clearFlags = CameraClearFlags.SolidColor;
		m_PortraitCamera.backgroundColor = m_ClearColor;
		m_PortraitCamera.orthographic = true;
		m_PortraitCamera.farClipPlane = m_CameraFarClipPlane;

		UniversalAdditionalCameraData urpCam = m_PortraitCamera.GetUniversalAdditionalCameraData();
		urpCam.renderPostProcessing = false;
		urpCam.antialiasing = AntialiasingMode.None;

		m_FillDirectionalLight.cullingMask = m_PreviewSingleLayerMask.value;
		m_FillDirectionalLight.enabled = true;

		bool restoreEnabled = m_PortraitCamera.enabled;
		m_PortraitCamera.enabled = true;
		m_PortraitCamera.Render();
		m_PortraitCamera.enabled = restoreEnabled;

		Texture2D tex = TextureFromActiveRenderTexture();
		Sprite spr = Sprite.Create(tex, new Rect(0f, 0f, m_RenderTextureWidth, m_RenderTextureHeight), new Vector2(0.5f, 0.5f),
			100f, 0, SpriteMeshType.FullRect);
		tex.hideFlags = HideFlags.None;
		spr.hideFlags = HideFlags.None;
		spr.texture.filterMode = FilterMode.Bilinear;
		spr.texture.wrapMode = TextureWrapMode.Clamp;
		spr.texture.anisoLevel = 4;

		m_PortraitSpritesByWeaponItemInstanceId[instanceIdCacheKey] = spr;
	}

	private Texture2D TextureFromActiveRenderTexture()
	{
		var previousRt = RenderTexture.active;
		RenderTexture.active = m_WorkRt;
		try
		{
			Texture2D tex = new Texture2D(m_RenderTextureWidth, m_RenderTextureHeight, TextureFormat.RGBA32, false, false);
			tex.ReadPixels(new Rect(0f, 0f, m_RenderTextureWidth, m_RenderTextureHeight), 0, 0, false);
			tex.Apply(false, false);
			return tex;
		}
		finally
		{
			RenderTexture.active = previousRt;
		}
	}

	private void ConfigureCameraFacingBounds(Bounds _worldBounds)
	{
		Quaternion orbit = Quaternion.Euler(m_OrbitEuler);
		Vector3 back = orbit * (-Vector3.forward);
		float extentRadius = Mathf.Max(_worldBounds.extents.magnitude, 0.02f);

		Vector3 desiredPosition = _worldBounds.center + back.normalized *
			Mathf.Max(m_BackDistanceFromExtents * extentRadius + m_BackDistanceBias, 0.2f);

		m_PortraitCamera.transform.SetPositionAndRotation(desiredPosition, Quaternion.LookRotation(_worldBounds.center - desiredPosition, Vector3.up));
		SetOrthographicCoverageForCurrentPose(_worldBounds);
	}

	private void SetOrthographicCoverageForCurrentPose(Bounds _worldBounds)
	{
		Transform ct = m_PortraitCamera.transform;
		Vector3 c = _worldBounds.center;
		Vector3 e = _worldBounds.extents;

		Vector3 dx = Vector3.right * e.x;
		Vector3 dy = Vector3.up * e.y;
		Vector3 dz = Vector3.forward * e.z;

		Vector3[] corners =
		{
			c + dx + dy + dz,
			c + dx + dy - dz,
			c + dx - dy + dz,
			c + dx - dy - dz,
			c - dx + dy + dz,
			c - dx + dy - dz,
			c - dx - dy + dz,
			c - dx - dy - dz,
		};

		float verticalHalf = 0f;
		float horizontalHalf = 0f;
		float aspectInv = Mathf.Max(1e-4f, m_PortraitCamera.aspect);

		for (int i = 0; i < corners.Length; i++)
		{
			Vector3 local = ct.InverseTransformPoint(corners[i]);
			verticalHalf = Mathf.Max(verticalHalf, Mathf.Abs(local.y));
			horizontalHalf = Mathf.Max(horizontalHalf, Mathf.Abs(local.x));
		}

		float orthoCoverage = Mathf.Max(verticalHalf, horizontalHalf / aspectInv) * m_BoundsFitMultiplier;
		m_PortraitCamera.orthographicSize = Mathf.Max(orthoCoverage, 0.01f);
		m_PortraitCamera.nearClipPlane = 0.01f;
	}

	private void EnsurePortraitRigReady()
	{
		if (m_PortraitCamera != null && m_FillDirectionalLight != null)
			return;

		GameObject rigRoot = gameObject;

		GameObject camObj = new GameObject("WeaponPortraitCamera");
		camObj.transform.SetParent(rigRoot.transform, false);
		Camera portraitCam = camObj.AddComponent<Camera>();
		portraitCam.enabled = false;

		GameObject litObj = new GameObject("WeaponPortraitFillDirectionalLight");
		litObj.transform.SetParent(camObj.transform, false);
		Light directional = litObj.AddComponent<Light>();
		directional.type = LightType.Directional;
		directional.intensity = 1.05f;
		directional.color = Color.white;
		directional.transform.localRotation = Quaternion.Euler(40f, 24f, 0f);

		DirectConfigureUniversalPortraitCamera(portraitCam);

		m_PortraitCamera = portraitCam;
		m_FillDirectionalLight = directional;
	}

	private static void DirectConfigureUniversalPortraitCamera(Camera _camera)
	{
		if (_camera == null)
			return;

		UniversalAdditionalCameraData extra = _camera.GetUniversalAdditionalCameraData();
		extra.renderType = CameraRenderType.Base;
		extra.renderPostProcessing = false;
		extra.antialiasing = AntialiasingMode.None;
	}

	private void EnsureWorkingRenderTexture()
	{
		if (m_WorkRt != null && m_WorkRt.width == m_RenderTextureWidth && m_WorkRt.height == m_RenderTextureHeight)
			return;

		if (m_WorkRt != null)
		{
			m_WorkRt.Release();
			Destroy(m_WorkRt);
			m_WorkRt = null;
		}

		m_WorkRt = new RenderTexture(m_RenderTextureWidth, m_RenderTextureHeight, 16, RenderTextureFormat.ARGB32)
			{
				antiAliasing = 1,
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
			};
		m_WorkRt.Create();
	}

	private static Bounds ComputeWorldRenderBounds(Transform root)
	{
		Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
		if (renderers.Length == 0)
			return new Bounds(root.position, Vector3.one * 0.05f);

		Bounds b = renderers[0].bounds;
		for (int i = 1; i < renderers.Length; i++)
			b.Encapsulate(renderers[i].bounds);

		return b;
	}

	private static void SetLayerRecursively(GameObject _root, int _layerIndex)
	{
		Transform[] trs = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < trs.Length; i++)
			trs[i].gameObject.layer = _layerIndex;
	}

	private static void SetShadowCastingRecursively(Transform root, bool _enabled)
	{
		if (_enabled)
			return;

		Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++)
			renderers[i].shadowCastingMode = ShadowCastingMode.Off;
	}

	private static bool IsExactlySingleLayer(in LayerMask mask)
	{
		int v = mask.value;
		return v != 0 && (v & (v - 1)) == 0;
	}

	#endregion
}

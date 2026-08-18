using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Красная точка ЛЦУ на поверхности по линии ствола. Дальность 50 м. Скрыта в NotReady / патруле.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(72)]
public sealed class WeaponLaserDotVisual : MonoBehaviour
{
	#region Constants
	private const float c_BarrelRayStartOffset = 0.1f;
	private const float c_SurfaceOffset = 0.03f;
	private const float c_FadeStartNormalized = 0.8f;
	private const float c_WorldSize = 0.06f;
	private const float c_HideIfCameraFartherThan = 90f;
	private const int c_HitBufferSize = 16;
	private const string c_CircleTextureResourcesPath = "Shooting/LaserDotCircle";
	private static readonly Color s_DotColor = new Color(2.4f, 0.35f, 0.12f, 1f);
	private static readonly RaycastHit[] s_Hits = new RaycastHit[c_HitBufferSize];
	private static Material s_SharedMaterial;
	private static Mesh s_CircleMesh;
	private static Texture2D s_FallbackCircleTexture;
	#endregion

	#region Private Fields
	private EquippedWeapon m_Weapon;
	private UnitEquipment m_Equipment;
	private UnitEquippedWeaponPose m_Pose;
	private UnitWeaponRuntime m_WeaponRuntime;
	private Transform m_UnitRoot;
	private GameObject m_Dot;
	private Transform m_DotTransform;
	private MeshRenderer m_DotRenderer;
	private MaterialPropertyBlock m_PropertyBlock;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Weapon = GetComponent<EquippedWeapon>();
	}

	private void OnDisable()
	{
		SetDotVisible(false);
	}

	private void OnDestroy()
	{
		if (m_Dot != null)
			Destroy(m_Dot);
	}

	private void LateUpdate()
	{
		if (!Application.isPlaying || m_Weapon == null)
		{
			SetDotVisible(false);
			return;
		}

		if (!TryResolveHoldContext())
		{
			SetDotVisible(false);
			return;
		}

		WeaponAttachmentDefinition[] attachments = m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.EquippedAttachments
			: m_Weapon.PresetEquippedAttachments;
		WeaponAttachmentDefinition laser = WeaponLaserModifiers.FindLaser(attachments);
		if (laser == null)
		{
			SetDotVisible(false);
			return;
		}

		if (m_Pose != null && m_Pose.CurrentPose.IsPeacefulCarryPose())
		{
			SetDotVisible(false);
			return;
		}

		float maxRange = WeaponLaserModifiers.GetLaserDotMaxRangeMeters(laser);
		if (maxRange <= 0.01f)
		{
			SetDotVisible(false);
			return;
		}

		Transform barrel = m_Weapon.FireOriginTransform != null ? m_Weapon.FireOriginTransform : m_Weapon.BarrelTransform;
		if (barrel == null)
		{
			SetDotVisible(false);
			return;
		}

		Vector3 origin = barrel.position + barrel.forward * c_BarrelRayStartOffset;
		if (!WeaponVfxUtility.IsWithinEffectDistance(origin, c_HideIfCameraFartherThan))
		{
			SetDotVisible(false);
			return;
		}

		Vector3 direction = barrel.forward;
		int hitCount = Physics.RaycastNonAlloc(
			origin,
			direction,
			s_Hits,
			maxRange,
			~0,
			QueryTriggerInteraction.Ignore);
		if (hitCount <= 0)
		{
			SetDotVisible(false);
			return;
		}

		int nearestIndex = -1;
		float nearestDistance = float.MaxValue;
		for (int i = 0; i < hitCount; i++)
		{
			RaycastHit hit = s_Hits[i];
			if (hit.collider == null || IsSelfCollider(hit.collider))
				continue;
			if (hit.distance >= nearestDistance)
				continue;
			nearestDistance = hit.distance;
			nearestIndex = i;
		}

		if (nearestIndex < 0)
		{
			SetDotVisible(false);
			return;
		}

		RaycastHit surfaceHit = s_Hits[nearestIndex];
		float fade = EvaluateRangeFade(nearestDistance, maxRange);
		if (fade <= 0.01f)
		{
			SetDotVisible(false);
			return;
		}

		EnsureDot();
		Camera camera = WeaponVfxUtility.ResolveActiveCamera();
		Vector3 point = surfaceHit.point + surfaceHit.normal * c_SurfaceOffset;
		m_DotTransform.position = point;
		m_DotTransform.localScale = Vector3.one * c_WorldSize;
		if (camera != null)
		{
			Vector3 toCamera = camera.transform.position - point;
			if (toCamera.sqrMagnitude > 0.0001f)
				m_DotTransform.rotation = Quaternion.LookRotation(-toCamera.normalized, camera.transform.up);
		}
		else
		{
			m_DotTransform.rotation = Quaternion.LookRotation(-surfaceHit.normal);
		}

		Color color = s_DotColor;
		color.a = fade;
		m_PropertyBlock.SetColor("_Color", color);
		m_PropertyBlock.SetColor("_BaseColor", color);
		m_DotRenderer.SetPropertyBlock(m_PropertyBlock);
		SetDotVisible(true);
	}
	#endregion

	#region Private Methods
	private bool TryResolveHoldContext()
	{
		if (m_Equipment != null && m_Equipment.EquippedWeapon == m_Weapon)
			return true;

		m_Equipment = GetComponentInParent<UnitEquipment>();
		if (m_Equipment == null || m_Equipment.EquippedWeapon != m_Weapon)
			return false;

		m_UnitRoot = m_Equipment.transform;
		m_Pose = m_Equipment.GetComponent<UnitEquippedWeaponPose>();
		m_WeaponRuntime = m_Equipment.GetComponent<UnitWeaponRuntime>();
		return true;
	}

	private bool IsSelfCollider(Collider _collider)
	{
		if (m_UnitRoot == null || _collider == null)
			return false;
		Transform hitTransform = _collider.transform;
		return hitTransform == m_UnitRoot || hitTransform.IsChildOf(m_UnitRoot);
	}

	private static float EvaluateRangeFade(float _distance, float _maxRange)
	{
		if (_distance >= _maxRange)
			return 0f;
		float fadeStart = _maxRange * c_FadeStartNormalized;
		if (_distance <= fadeStart)
			return 1f;
		return 1f - Mathf.InverseLerp(fadeStart, _maxRange, _distance);
	}

	private void EnsureDot()
	{
		if (m_Dot != null)
			return;

		m_Dot = new GameObject("WeaponLaserDot");
		m_DotTransform = m_Dot.transform;
		MeshFilter filter = m_Dot.AddComponent<MeshFilter>();
		filter.sharedMesh = GetCircleMesh();
		m_DotRenderer = m_Dot.AddComponent<MeshRenderer>();
		m_DotRenderer.shadowCastingMode = ShadowCastingMode.Off;
		m_DotRenderer.receiveShadows = false;
		m_DotRenderer.lightProbeUsage = LightProbeUsage.Off;
		m_DotRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
		m_DotRenderer.sharedMaterial = GetSharedMaterial();
		m_PropertyBlock = new MaterialPropertyBlock();
		m_Dot.SetActive(false);
	}

	private void SetDotVisible(bool _visible)
	{
		if (m_Dot == null)
			return;
		if (m_Dot.activeSelf == _visible)
			return;
		m_Dot.SetActive(_visible);
	}

	private static Material GetSharedMaterial()
	{
		if (s_SharedMaterial != null)
			return s_SharedMaterial;

		Shader shader = Shader.Find("Hidden/WeaponLaserDot");
		if (shader == null)
			shader = Shader.Find("Sprites/Default");
		s_SharedMaterial = shader != null ? new Material(shader) : new Material(Shader.Find("Hidden/InternalErrorShader"));
		s_SharedMaterial.name = "WeaponLaserDot";
		s_SharedMaterial.hideFlags = HideFlags.HideAndDontSave;

		Texture circle = GetCircleTexture();
		s_SharedMaterial.mainTexture = circle;
		s_SharedMaterial.SetTexture("_MainTex", circle);
		s_SharedMaterial.SetColor("_Color", s_DotColor);
		s_SharedMaterial.renderQueue = (int)RenderQueue.Transparent;
		return s_SharedMaterial;
	}

	private static Mesh GetCircleMesh()
	{
		if (s_CircleMesh != null)
			return s_CircleMesh;

		const int segments = 32;
		s_CircleMesh = new Mesh
		{
			name = "WeaponLaserDotCircle",
			hideFlags = HideFlags.HideAndDontSave
		};

		Vector3[] vertices = new Vector3[segments + 1];
		Vector2[] uvs = new Vector2[segments + 1];
		int[] triangles = new int[segments * 3];
		vertices[0] = Vector3.zero;
		uvs[0] = new Vector2(0.5f, 0.5f);
		for (int i = 0; i < segments; i++)
		{
			float angle = (i / (float)segments) * Mathf.PI * 2f;
			float x = Mathf.Cos(angle) * 0.5f;
			float y = Mathf.Sin(angle) * 0.5f;
			vertices[i + 1] = new Vector3(x, y, 0f);
			uvs[i + 1] = new Vector2(x + 0.5f, y + 0.5f);
			triangles[i * 3] = 0;
			triangles[i * 3 + 1] = i + 1;
			triangles[i * 3 + 2] = i + 1 < segments ? i + 2 : 1;
		}

		s_CircleMesh.vertices = vertices;
		s_CircleMesh.uv = uvs;
		s_CircleMesh.triangles = triangles;
		s_CircleMesh.RecalculateBounds();
		return s_CircleMesh;
	}

	private static Texture GetCircleTexture()
	{
		Texture2D fromResources = Resources.Load<Texture2D>(c_CircleTextureResourcesPath);
		if (fromResources != null)
			return fromResources;

		Texture2D builtin = Resources.GetBuiltinResource<Texture2D>("Default-Particle.psd");
		if (builtin != null)
			return builtin;

		return GetFallbackCircleTexture();
	}

	private static Texture2D GetFallbackCircleTexture()
	{
		if (s_FallbackCircleTexture != null)
			return s_FallbackCircleTexture;

		const int size = 64;
		s_FallbackCircleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
		{
			name = "WeaponLaserDotCircle",
			hideFlags = HideFlags.HideAndDontSave,
			wrapMode = TextureWrapMode.Clamp,
			filterMode = FilterMode.Bilinear
		};

		Color[] pixels = new Color[size * size];
		float center = (size - 1) * 0.5f;
		float radius = center;
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float dx = (x - center) / radius;
				float dy = (y - center) / radius;
				float dist = Mathf.Sqrt(dx * dx + dy * dy);
				float alpha = 1f - Mathf.SmoothStep(0.55f, 1f, dist);
				pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
			}
		}

		s_FallbackCircleTexture.SetPixels(pixels);
		s_FallbackCircleTexture.Apply(false, true);
		return s_FallbackCircleTexture;
	}
	#endregion
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Transparent pose preview: draws the live vehicle's enabled meshes at a destination yaw
/// via Graphics.DrawMesh using transparent copies of the vehicle's own materials.
/// Includes boarded unit SkinnedMeshRenderers (BakeMesh).
/// </summary>
public sealed class VehicleMovePoseGhostVisual
{
	#region Constants
	private const float c_GhostAlpha = 0.9f;
	#endregion

	#region Nested Types
	private struct MeshPart
	{
		public Mesh Mesh;
		public Transform Transform;
		public Renderer Renderer;
		public Material[] GhostMaterials;
	}

	private struct SkinnedPart
	{
		public SkinnedMeshRenderer Renderer;
		public Mesh BakedMesh;
		public Material[] GhostMaterials;
	}
	#endregion

	#region Private Fields
	private static readonly Dictionary<Material, Material> s_GhostBySource = new Dictionary<Material, Material>(32);
	private readonly List<MeshPart> m_MeshParts = new List<MeshPart>(64);
	private readonly List<SkinnedPart> m_SkinnedParts = new List<SkinnedPart>(16);
	private Transform m_VehicleRoot;
	private Vector3 m_WorldPoint;
	private float m_HeadingYawDegrees;
	private bool m_Active;
	private int m_Layer;
	#endregion

	#region Public Methods
	public void SetPose(Transform _vehicleRoot, Vector3 _worldPoint, float _headingYawDegrees)
	{
		if (_vehicleRoot == null)
		{
			Clear();
			return;
		}

		if (m_VehicleRoot != _vehicleRoot || m_MeshParts.Count == 0)
			RebuildParts(_vehicleRoot);

		m_VehicleRoot = _vehicleRoot;
		m_WorldPoint = _worldPoint;
		m_HeadingYawDegrees = _headingYawDegrees;
		m_Layer = _vehicleRoot.gameObject.layer;
		m_Active = m_MeshParts.Count > 0 || m_SkinnedParts.Count > 0;
	}

	public void Clear()
	{
		m_Active = false;
		m_VehicleRoot = null;
		m_MeshParts.Clear();
		DestroyBakedMeshes();
	}

	public void Draw()
	{
		if (!m_Active || m_VehicleRoot == null)
			return;

		Matrix4x4 vehicleWorld = m_VehicleRoot.localToWorldMatrix;
		Matrix4x4 vehicleInverse = vehicleWorld.inverse;
		Quaternion yaw = Quaternion.Euler(0f, m_HeadingYawDegrees, 0f);
		Matrix4x4 previewWorld = Matrix4x4.TRS(m_WorldPoint, yaw, m_VehicleRoot.lossyScale);

		for (int i = 0; i < m_MeshParts.Count; i++)
		{
			MeshPart part = m_MeshParts[i];
			if (part.Mesh == null || part.Transform == null || part.Renderer == null)
				continue;
			if (part.GhostMaterials == null || part.GhostMaterials.Length == 0)
				continue;
			if (!part.Renderer.enabled || !part.Renderer.gameObject.activeInHierarchy)
				continue;

			Matrix4x4 drawMatrix = previewWorld * (vehicleInverse * part.Transform.localToWorldMatrix);
			DrawPart(part.Mesh, drawMatrix, part.GhostMaterials);
		}

		for (int i = 0; i < m_SkinnedParts.Count; i++)
		{
			SkinnedPart part = m_SkinnedParts[i];
			if (part.Renderer == null || part.BakedMesh == null)
				continue;
			if (part.GhostMaterials == null || part.GhostMaterials.Length == 0)
				continue;
			if (!part.Renderer.enabled || !part.Renderer.gameObject.activeInHierarchy)
				continue;
			if (part.Renderer.sharedMesh == null)
				continue;

			part.Renderer.BakeMesh(part.BakedMesh);
			Matrix4x4 drawMatrix = previewWorld * (vehicleInverse * part.Renderer.localToWorldMatrix);
			DrawPart(part.BakedMesh, drawMatrix, part.GhostMaterials);
		}
	}
	#endregion

	#region Private Methods
	private void DrawPart(Mesh _mesh, Matrix4x4 _drawMatrix, Material[] _ghostMaterials)
	{
		int subMeshCount = _mesh.subMeshCount;
		for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
		{
			Material material = _ghostMaterials[Mathf.Min(subMesh, _ghostMaterials.Length - 1)];
			if (material == null)
				continue;

			Graphics.DrawMesh(
				_mesh,
				_drawMatrix,
				material,
				m_Layer,
				null,
				subMesh,
				null,
				ShadowCastingMode.Off,
				false,
				null,
				LightProbeUsage.Off);
		}
	}

	private void RebuildParts(Transform _vehicleRoot)
	{
		m_MeshParts.Clear();
		DestroyBakedMeshes();
		if (_vehicleRoot == null)
			return;

		MeshFilter[] filters = _vehicleRoot.GetComponentsInChildren<MeshFilter>(true);
		for (int i = 0; i < filters.Length; i++)
		{
			MeshFilter filter = filters[i];
			if (filter == null || filter.sharedMesh == null)
				continue;
			if (!filter.TryGetComponent(out MeshRenderer renderer))
				continue;

			Material[] ghosts = BuildGhostMaterials(renderer.sharedMaterials);
			if (ghosts == null)
				continue;

			m_MeshParts.Add(new MeshPart
			{
				Mesh = filter.sharedMesh,
				Transform = filter.transform,
				Renderer = renderer,
				GhostMaterials = ghosts
			});
		}

		SkinnedMeshRenderer[] skinned = _vehicleRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		for (int i = 0; i < skinned.Length; i++)
		{
			SkinnedMeshRenderer renderer = skinned[i];
			if (renderer == null || renderer.sharedMesh == null)
				continue;

			Material[] ghosts = BuildGhostMaterials(renderer.sharedMaterials);
			if (ghosts == null)
				continue;

			Mesh baked = new Mesh
			{
				name = renderer.name + " (MovePoseGhostBake)",
				hideFlags = HideFlags.HideAndDontSave
			};

			m_SkinnedParts.Add(new SkinnedPart
			{
				Renderer = renderer,
				BakedMesh = baked,
				GhostMaterials = ghosts
			});
		}
	}

	private void DestroyBakedMeshes()
	{
		for (int i = 0; i < m_SkinnedParts.Count; i++)
		{
			Mesh baked = m_SkinnedParts[i].BakedMesh;
			if (baked != null)
				Object.Destroy(baked);
		}

		m_SkinnedParts.Clear();
	}

	private static Material[] BuildGhostMaterials(Material[] _shared)
	{
		if (_shared == null || _shared.Length == 0)
			return null;

		Material[] ghosts = new Material[_shared.Length];
		bool any = false;
		for (int m = 0; m < _shared.Length; m++)
		{
			ghosts[m] = GetOrCreateGhostMaterial(_shared[m]);
			if (ghosts[m] != null)
				any = true;
		}

		return any ? ghosts : null;
	}

	private static Material GetOrCreateGhostMaterial(Material _source)
	{
		if (_source == null)
			return null;

		if (s_GhostBySource.TryGetValue(_source, out Material cached) && cached != null)
			return cached;

		Material ghost = new Material(_source)
		{
			name = _source.name + " (MovePoseGhost)",
			hideFlags = HideFlags.HideAndDontSave
		};
		ApplyGhostTransparency(ghost);
		s_GhostBySource[_source] = ghost;
		return ghost;
	}

	private static void ApplyGhostTransparency(Material _material)
	{
		if (_material.HasProperty("_BaseColor"))
		{
			Color color = _material.GetColor("_BaseColor");
			color.a = Mathf.Clamp01(color.a * c_GhostAlpha);
			_material.SetColor("_BaseColor", color);
		}

		if (_material.HasProperty("_Color"))
		{
			Color color = _material.GetColor("_Color");
			color.a = Mathf.Clamp01(color.a * c_GhostAlpha);
			_material.SetColor("_Color", color);
		}

		if (_material.HasProperty("_Surface"))
			_material.SetFloat("_Surface", 1f);
		if (_material.HasProperty("_Blend"))
			_material.SetFloat("_Blend", 0f);
		if (_material.HasProperty("_AlphaClip"))
			_material.SetFloat("_AlphaClip", 0f);
		if (_material.HasProperty("_SrcBlend"))
			_material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
		if (_material.HasProperty("_DstBlend"))
			_material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
		if (_material.HasProperty("_SrcBlendAlpha"))
			_material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
		if (_material.HasProperty("_DstBlendAlpha"))
			_material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
		if (_material.HasProperty("_ZWrite"))
			_material.SetFloat("_ZWrite", 0f);

		_material.DisableKeyword("_ALPHATEST_ON");
		_material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
		_material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
		_material.SetOverrideTag("RenderType", "Transparent");
		_material.SetShaderPassEnabled("DepthOnly", false);
		_material.SetShaderPassEnabled("SHADOWCASTER", false);
		_material.renderQueue = (int)RenderQueue.Transparent;
	}
	#endregion
}

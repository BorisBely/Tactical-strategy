using System;
using UnityEngine;

/// <summary>
/// Мишень полигона: включается из UI, при попадании пишет лог через <see cref="ShootingRangeHitLogger"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class ShootingRangeTarget : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private DamageableTarget m_Damageable;
	[SerializeField] private Collider m_Collider;
	[SerializeField] private Renderer m_Renderer;
	[SerializeField] private Color m_IntactColor = new Color(0.35f, 0.75f, 1f, 1f);
	[SerializeField] private Color m_DisabledColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
	#endregion

	#region Private Fields
	private ShootingRangeTargetRegistry m_Registry;
	private MaterialPropertyBlock m_PropertyBlock;
	private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
	private static readonly int s_ColorId = Shader.PropertyToID("_Color");

	private bool m_IsUserEnabled;
	#endregion

	#region Public Properties
	public string DisplayName => gameObject.name;
	public bool IsUserEnabled => m_IsUserEnabled;
	public bool IsAvailableForTargeting => isActiveAndEnabled && m_IsUserEnabled;
	public Collider TargetCollider => m_Collider;
	#endregion

	#region Public Events
	public event Action<ShootingRangeTarget> StateChanged;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Damageable == null)
			m_Damageable = GetComponent<DamageableTarget>();
		if (m_Collider == null)
			m_Collider = GetComponent<Collider>();
		if (m_Renderer == null)
			m_Renderer = GetComponent<Renderer>();

		EnsureDamageableConfigured();
		m_PropertyBlock = new MaterialPropertyBlock();
	}

	private void OnEnable()
	{
		ResolveRegistry();
		m_Registry?.Register(this);
		ApplyColliderState();
		ApplyVisualState();
	}

	private void OnDisable()
	{
		m_Registry?.Unregister(this);
	}
	#endregion

	#region Public Methods
	public Vector3 GetAimPointWorld()
	{
		if (m_Collider != null)
			return m_Collider.bounds.center;
		return transform.position;
	}

	public void SetUserEnabled(bool _enabled)
	{
		m_IsUserEnabled = _enabled;
		ApplyColliderState();
		ApplyVisualState();
		NotifyStateChanged();
	}

	public void ResetTarget()
	{
		m_IsUserEnabled = false;
		m_Damageable?.ResetHealth();
		ApplyColliderState();
		ApplyVisualState();
		NotifyStateChanged();
	}

	public void ResetTargetHealth()
	{
		m_Damageable?.ResetHealth();
		NotifyStateChanged();
	}

	public bool TryEvaluateFaceHitAccuracy(
		Vector3 _hitPointWorld,
		Vector3 _hitNormalWorld,
		out ShootingRangeFaceHitAccuracy _accuracy)
	{
		_accuracy = default;
		if (m_Collider == null || _hitNormalWorld.sqrMagnitude < 1e-8f)
			return false;

		if (m_Collider is SphereCollider sphereCollider)
			return TryEvaluateSphereHitAccuracy(_hitPointWorld, _hitNormalWorld, sphereCollider, out _accuracy);

		return TryEvaluateBoxFaceHitAccuracy(_hitPointWorld, _hitNormalWorld, out _accuracy);
	}
	#endregion

	#region Private Methods
	private bool TryEvaluateSphereHitAccuracy(
		Vector3 _hitPointWorld,
		Vector3 _hitNormalWorld,
		SphereCollider _sphereCollider,
		out ShootingRangeFaceHitAccuracy _accuracy)
	{
		_accuracy = default;
		Transform colliderTransform = _sphereCollider.transform;
		Vector3 centerWorld = colliderTransform.TransformPoint(_sphereCollider.center);
		Vector3 incoming = -_hitNormalWorld.normalized;

		Vector3 referenceUp = Mathf.Abs(Vector3.Dot(incoming, Vector3.up)) > 0.9f
			? Vector3.forward
			: Vector3.up;
		Vector3 tangentU = Vector3.Cross(referenceUp, incoming).normalized;
		Vector3 tangentV = Vector3.Cross(incoming, tangentU).normalized;

		Vector3 toHit = _hitPointWorld - centerWorld;
		float offsetHorizontal = Vector3.Dot(toHit, tangentU);
		float offsetVertical = Vector3.Dot(toHit, tangentV);
		float offsetFromCenter = Mathf.Sqrt(offsetHorizontal * offsetHorizontal + offsetVertical * offsetVertical);

		Vector3 scale = colliderTransform.lossyScale;
		float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
		float radiusMeters = _sphereCollider.radius * maxScale;

		_accuracy = new ShootingRangeFaceHitAccuracy(
			offsetFromCenter,
			offsetHorizontal,
			offsetVertical,
			radiusMeters);
		return true;
	}

	private bool TryEvaluateBoxFaceHitAccuracy(
		Vector3 _hitPointWorld,
		Vector3 _hitNormalWorld,
		out ShootingRangeFaceHitAccuracy _accuracy)
	{
		_accuracy = default;
		Transform colliderTransform = m_Collider.transform;
		Vector3 localHit = colliderTransform.InverseTransformPoint(_hitPointWorld);
		Vector3 localNormal = colliderTransform.InverseTransformDirection(_hitNormalWorld).normalized;
		Vector3 absNormal = new Vector3(Mathf.Abs(localNormal.x), Mathf.Abs(localNormal.y), Mathf.Abs(localNormal.z));

		Vector3 boxCenterLocal = Vector3.zero;
		Vector3 halfExtentsLocal = Vector3.one * 0.5f;
		if (m_Collider is BoxCollider boxCollider)
		{
			boxCenterLocal = boxCollider.center;
			halfExtentsLocal = boxCollider.size * 0.5f;
		}
		else
		{
			Bounds bounds = m_Collider.bounds;
			boxCenterLocal = colliderTransform.InverseTransformPoint(bounds.center);
			Vector3 localSize = colliderTransform.InverseTransformVector(bounds.size);
			halfExtentsLocal = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z)) * 0.5f;
		}

		int faceAxis;
		float faceSign;
		if (absNormal.x >= absNormal.y && absNormal.x >= absNormal.z)
		{
			faceAxis = 0;
			faceSign = Mathf.Sign(localNormal.x);
		}
		else if (absNormal.y >= absNormal.x && absNormal.y >= absNormal.z)
		{
			faceAxis = 1;
			faceSign = Mathf.Sign(localNormal.y);
		}
		else
		{
			faceAxis = 2;
			faceSign = Mathf.Sign(localNormal.z);
		}

		Vector3 faceCenterLocal = boxCenterLocal;
		if (faceAxis == 0)
			faceCenterLocal.x = boxCenterLocal.x + faceSign * halfExtentsLocal.x;
		else if (faceAxis == 1)
			faceCenterLocal.y = boxCenterLocal.y + faceSign * halfExtentsLocal.y;
		else
			faceCenterLocal.z = boxCenterLocal.z + faceSign * halfExtentsLocal.z;

		Vector3 faceCenterWorld = colliderTransform.TransformPoint(faceCenterLocal);
		Vector3 faceNormalWorld = colliderTransform.TransformDirection(
			faceAxis == 0 ? new Vector3(faceSign, 0f, 0f) :
			faceAxis == 1 ? new Vector3(0f, faceSign, 0f) :
			new Vector3(0f, 0f, faceSign)).normalized;

		Vector3 referenceUp = Mathf.Abs(Vector3.Dot(faceNormalWorld, Vector3.up)) > 0.9f
			? Vector3.forward
			: Vector3.up;
		Vector3 tangentU = Vector3.Cross(referenceUp, faceNormalWorld).normalized;
		Vector3 tangentV = Vector3.Cross(faceNormalWorld, tangentU).normalized;

		Vector3 offsetWorld = _hitPointWorld - faceCenterWorld;
		float offsetHorizontal = Vector3.Dot(offsetWorld, tangentU);
		float offsetVertical = Vector3.Dot(offsetWorld, tangentV);
		float offsetFromCenter = Mathf.Sqrt(offsetHorizontal * offsetHorizontal + offsetVertical * offsetVertical);

		float faceHalfExtentU = faceAxis == 0 ? halfExtentsLocal.y : halfExtentsLocal.x;
		float faceHalfExtentV = faceAxis == 2 ? halfExtentsLocal.y : halfExtentsLocal.z;
		float faceHalfExtentMeters = Mathf.Max(
			colliderTransform.TransformVector(
				faceAxis == 0 ? new Vector3(0f, faceHalfExtentU, 0f) :
				faceAxis == 1 ? new Vector3(faceHalfExtentU, 0f, 0f) :
				new Vector3(faceHalfExtentU, 0f, 0f)).magnitude,
			colliderTransform.TransformVector(
				faceAxis == 0 ? new Vector3(0f, 0f, faceHalfExtentV) :
				faceAxis == 1 ? new Vector3(0f, 0f, faceHalfExtentV) :
				new Vector3(0f, faceHalfExtentV, 0f)).magnitude);

		_accuracy = new ShootingRangeFaceHitAccuracy(
			offsetFromCenter,
			offsetHorizontal,
			offsetVertical,
			faceHalfExtentMeters);
		return true;
	}

	private void ResolveRegistry()
	{
		if (m_Registry != null)
			return;

#if UNITY_2023_1_OR_NEWER
		m_Registry = FindAnyObjectByType<ShootingRangeTargetRegistry>(FindObjectsInactive.Exclude);
#else
		m_Registry = FindObjectOfType<ShootingRangeTargetRegistry>();
#endif
	}

	private void EnsureDamageableConfigured()
	{
		if (m_Damageable == null)
			m_Damageable = gameObject.AddComponent<DamageableTarget>();

		m_Damageable.SetMaxHealth(100000f, true);
	}

	private void ApplyColliderState()
	{
		if (m_Collider == null)
			return;

		m_Collider.enabled = m_IsUserEnabled;
	}

	private void ApplyVisualState()
	{
		if (m_Renderer == null)
			return;

		Color color = m_IsUserEnabled ? m_IntactColor : m_DisabledColor;
		m_Renderer.GetPropertyBlock(m_PropertyBlock);
		m_PropertyBlock.SetColor(s_BaseColorId, color);
		m_PropertyBlock.SetColor(s_ColorId, color);
		m_Renderer.SetPropertyBlock(m_PropertyBlock);
	}

	private void NotifyStateChanged()
	{
		StateChanged?.Invoke(this);
	}
	#endregion
}

using System;
using UnityEngine;

/// <summary>
/// Мишень полигона: считает попадания, после N выстрелов «падает» и перестаёт быть целью для <see cref="UnitVision"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class ShootingRangeTarget : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField, Min(1)] private int m_HitsToDefeat = 10;
	[SerializeField] private DamageableTarget m_Damageable;
	[SerializeField] private Collider m_Collider;
	[SerializeField] private Renderer m_Renderer;
	[SerializeField] private Color m_IntactColor = new Color(0.35f, 0.75f, 1f, 1f);
	[SerializeField] private Color m_DamagedColor = new Color(1f, 0.45f, 0.1f, 1f);
	[SerializeField] private Color m_DefeatedColor = new Color(0.25f, 0.25f, 0.25f, 0.35f);
	[SerializeField] private Color m_DisabledColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
	#endregion

	#region Private Fields
	private ShootingRangeTargetRegistry m_Registry;
	private MaterialPropertyBlock m_PropertyBlock;
	private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
	private static readonly int s_ColorId = Shader.PropertyToID("_Color");

	private int m_HitCount;
	private bool m_IsDefeated;
	private bool m_IsUserEnabled = true;
	#endregion

	#region Public Properties
	public string DisplayName => gameObject.name;
	public int HitsToDefeat => m_HitsToDefeat;
	public int HitCount => m_HitCount;
	public int RemainingHits => Mathf.Max(0, m_HitsToDefeat - m_HitCount);
	public bool IsUserEnabled => m_IsUserEnabled;
	public bool IsDefeated => m_IsDefeated;
	public bool IsAvailableForTargeting => isActiveAndEnabled && m_IsUserEnabled && !m_IsDefeated;
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
		if (m_Damageable != null)
			m_Damageable.Damaged += HandleDamaged;
		ApplyVisualState();
	}

	private void OnDisable()
	{
		if (m_Damageable != null)
			m_Damageable.Damaged -= HandleDamaged;
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
		m_HitCount = 0;
		m_IsDefeated = false;
		m_Damageable?.ResetHealth();
		ApplyColliderState();
		ApplyVisualState();
		NotifyStateChanged();
	}
	#endregion

	#region Private Methods
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

	private void HandleDamaged(DamageHitInfo _info)
	{
		if (!IsAvailableForTargeting)
			return;

		m_HitCount++;
		ApplyVisualState();

		if (m_HitCount >= m_HitsToDefeat)
			MarkDefeated();

		NotifyStateChanged();
	}

	private void MarkDefeated()
	{
		if (m_IsDefeated)
			return;

		m_IsDefeated = true;
		ApplyColliderState();
		ApplyVisualState();
		m_Registry?.NotifyTargetEliminated(this);
		NotifyStateChanged();
	}

	private void ApplyColliderState()
	{
		if (m_Collider == null)
			return;

		m_Collider.enabled = m_IsUserEnabled && !m_IsDefeated;
	}

	private void ApplyVisualState()
	{
		if (m_Renderer == null)
			return;

		Color color;
		if (!m_IsUserEnabled)
			color = m_DisabledColor;
		else if (m_IsDefeated)
			color = m_DefeatedColor;
		else if (m_HitCount <= 0)
			color = m_IntactColor;
		else
		{
			float t = Mathf.Clamp01((float)m_HitCount / m_HitsToDefeat);
			color = Color.Lerp(m_IntactColor, m_DamagedColor, t);
		}

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

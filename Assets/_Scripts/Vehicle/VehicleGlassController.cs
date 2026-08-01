using UnityEngine;

/// <summary>
/// Управляет опусканием/поднятием стёкол машины.
/// Ничего не знает про пассажиров — только получает <c>NeedOpenWindows</c>.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleGlassController : MonoBehaviour
{
	#region Constants
	private static readonly string[] s_GlassNames =
	{
		"SM_Veh_Light_Armored_Car_Glass_01.011",
		"SM_Veh_Light_Armored_Car_Glass_01.012",
		"SM_Veh_Light_Armored_Car_Glass_01.021",
		"SM_Veh_Light_Armored_Car_Glass_01.022"
	};
	#endregion

	#region Serialized Fields
	[SerializeField] private float m_LoweredDeltaY = -0.36f;
	[SerializeField] private float m_MoveSpeed = 1.2f;
	#endregion

	#region Private Fields
	private Transform[] m_Glasses;
	private float[] m_OriginalLocalY;
	private bool m_Cached;
	private bool m_DebugMissingLogged;
	#endregion

	#region Public Properties
	public bool NeedOpenWindows { get; set; }
	public bool IsFullyOpen { get; private set; }
	public bool IsFullyClosed { get; private set; }
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		CacheGlassTransforms();
	}

	private void Update()
	{
		if (m_Glasses == null || m_Glasses.Length == 0)
		{
			if (!m_DebugMissingLogged)
			{
				Debug.LogWarning($"[VehicleGlassController] No glass transforms found on '{name}'.");
				m_DebugMissingLogged = true;
			}

			return;
		}

		float delta = Time.deltaTime * m_MoveSpeed;
		float targetOffset = NeedOpenWindows ? m_LoweredDeltaY : 0f;

		bool allAtTarget = true;

		for (int i = 0; i < m_Glasses.Length; i++)
		{
			if (m_Glasses[i] == null)
				continue;

			Vector3 pos = m_Glasses[i].localPosition;
			float targetY = m_OriginalLocalY[i] + targetOffset;
			float newY = Mathf.MoveTowards(pos.y, targetY, delta);
			pos.y = newY;
			m_Glasses[i].localPosition = pos;

			if (!Mathf.Approximately(newY, targetY))
				allAtTarget = false;
		}

		float loweredY = m_OriginalLocalY.Length > 0 ? m_OriginalLocalY[0] + m_LoweredDeltaY : 0f;
		float closedY = m_OriginalLocalY.Length > 0 ? m_OriginalLocalY[0] : 0f;

		IsFullyOpen = allAtTarget && NeedOpenWindows;
		IsFullyClosed = allAtTarget && !NeedOpenWindows;
	}
	#endregion

	#region Public Methods
	public void Configure(Transform _root)
	{
		CacheGlassTransforms();
	}

	public bool HasGlasses()
	{
		return m_Glasses != null && m_Glasses.Length > 0;
	}
	#endregion

	#region Private Methods
	private void CacheGlassTransforms()
	{
		if (m_Cached)
			return;

		var list = new System.Collections.Generic.List<Transform>(s_GlassNames.Length);
		var origY = new System.Collections.Generic.List<float>(s_GlassNames.Length);

		for (int i = 0; i < s_GlassNames.Length; i++)
		{
			Transform found = FindDeep(transform, s_GlassNames[i]);
			if (found != null)
			{
				list.Add(found);
				origY.Add(found.localPosition.y);
			}
		}

		m_Glasses = list.ToArray();
		m_OriginalLocalY = origY.ToArray();
		m_Cached = true;
	}

	private static Transform FindDeep(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;
		if (_root.name == _name)
			return _root;

		for (int i = 0; i < _root.childCount; i++)
		{
			Transform found = FindDeep(_root.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}
	#endregion
}

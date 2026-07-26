using UnityEngine;

/// <summary>
/// Samples ground under the vehicle for surface speed multiplier and roughness penalty.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleSurfaceProbe : MonoBehaviour
{
	#region Constants
	private const float c_DefaultMultiplier = 0.7f;
	private const float c_RayHeight = 1.5f;
	private const float c_RayLength = 4f;
	#endregion

	#region Serialized Fields
	[SerializeField] private LayerMask m_GroundMask = ~0;
	[SerializeField] private float m_SampleRadius = 1.6f;
	#endregion

	#region Private Fields
	private readonly RaycastHit[] m_Hits = new RaycastHit[8];
	private float m_SurfaceMultiplier = c_DefaultMultiplier;
	private float m_RoughnessMultiplier = 1f;
	private string m_LastSurfaceName = "Default";
	#endregion

	#region Public Properties
	public float SurfaceMultiplier => m_SurfaceMultiplier;
	public float RoughnessMultiplier => m_RoughnessMultiplier;
	public float CombinedMultiplier => Mathf.Clamp(m_SurfaceMultiplier * m_RoughnessMultiplier, 0.35f, 1f);
	public string LastSurfaceName => m_LastSurfaceName;
	#endregion

	#region Unity Lifecycle
	private void FixedUpdate()
	{
		Sample();
	}
	#endregion

	#region Public Methods
	public void Sample()
	{
		Vector3 origin = transform.position + Vector3.up * c_RayHeight;
		int hitCount = 0;
		float heightSum = 0f;
		float heightSqSum = 0f;
		float surfaceMul = c_DefaultMultiplier;
		string surfaceName = "Default";
		bool gotCenter = false;

		TrySample(origin, ref gotCenter, ref surfaceMul, ref surfaceName, ref hitCount, ref heightSum, ref heightSqSum);

		Vector3 right = transform.right;
		Vector3 forward = transform.forward;
		right.y = 0f;
		forward.y = 0f;
		if (right.sqrMagnitude > 0.001f) right.Normalize();
		if (forward.sqrMagnitude > 0.001f) forward.Normalize();

		TrySample(origin + right * m_SampleRadius, ref gotCenter, ref surfaceMul, ref surfaceName, ref hitCount, ref heightSum, ref heightSqSum);
		TrySample(origin - right * m_SampleRadius, ref gotCenter, ref surfaceMul, ref surfaceName, ref hitCount, ref heightSum, ref heightSqSum);
		TrySample(origin + forward * m_SampleRadius, ref gotCenter, ref surfaceMul, ref surfaceName, ref hitCount, ref heightSum, ref heightSqSum);
		TrySample(origin - forward * m_SampleRadius, ref gotCenter, ref surfaceMul, ref surfaceName, ref hitCount, ref heightSum, ref heightSqSum);

		m_SurfaceMultiplier = surfaceMul;
		m_LastSurfaceName = surfaceName;

		if (hitCount < 2)
		{
			m_RoughnessMultiplier = 1f;
			return;
		}

		float mean = heightSum / hitCount;
		float variance = Mathf.Max(0f, (heightSqSum / hitCount) - mean * mean);
		float stdDev = Mathf.Sqrt(variance);
		// 0 m → 1.0, ~0.35 m+ → 0.55
		m_RoughnessMultiplier = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(stdDev / 0.35f));
	}
	#endregion

	#region Private Methods
	private void TrySample(
		Vector3 _origin,
		ref bool _gotCenter,
		ref float _surfaceMul,
		ref string _surfaceName,
		ref int _hitCount,
		ref float _heightSum,
		ref float _heightSqSum)
	{
		if (!Physics.Raycast(_origin, Vector3.down, out RaycastHit hit, c_RayLength, m_GroundMask,
			    QueryTriggerInteraction.Ignore))
			return;

		_hitCount++;
		_heightSum += hit.point.y;
		_heightSqSum += hit.point.y * hit.point.y;

		if (_gotCenter)
			return;

		_gotCenter = true;
		ResolveSurface(hit.collider, out _surfaceMul, out _surfaceName);
	}

	private static void ResolveSurface(Collider _collider, out float _multiplier, out string _name)
	{
		_multiplier = c_DefaultMultiplier;
		_name = "Default";
		if (_collider == null)
			return;

		PhysicsMaterial mat = _collider.sharedMaterial;
		string raw = mat != null ? mat.name : _collider.name;
		if (string.IsNullOrEmpty(raw))
			return;

		string key = raw.ToLowerInvariant();
		if (key.Contains("concrete") || key.Contains("road") || key.Contains("asphalt"))
		{
			_multiplier = 1f;
			_name = "Concrete";
			return;
		}

		if (key.Contains("metal"))
		{
			_multiplier = 0.85f;
			_name = "Metal";
			return;
		}

		if (key.Contains("wood"))
		{
			_multiplier = 0.85f;
			_name = "Wood";
			return;
		}

		if (key.Contains("dirt") || key.Contains("grass") || key.Contains("earth"))
		{
			_multiplier = 0.75f;
			_name = "Dirt";
			return;
		}

		if (key.Contains("gravel"))
		{
			_multiplier = 0.65f;
			_name = "Gravel";
			return;
		}

		if (key.Contains("sand"))
		{
			_multiplier = 0.55f;
			_name = "Sand";
			return;
		}

		if (key.Contains("glass"))
		{
			_multiplier = 0.6f;
			_name = "Glass";
			return;
		}

		// Default Plane / unnamed → treat as packed dirt-road mix
		_multiplier = 0.85f;
		_name = "Default";
	}
	#endregion
}

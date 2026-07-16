using UnityEngine;

public sealed class GrenadeDetachedPart : MonoBehaviour
{
	public float Lifetime = 3f;
	public float SinkSpeed = 0.5f;

	private float m_Elapsed;
	private bool m_IsSinking;

	private void Update()
	{
		m_Elapsed += Time.deltaTime;
		if (m_Elapsed >= Lifetime)
		{
			Destroy(gameObject);
			return;
		}

		if (transform.position.y < -0.1f)
			m_IsSinking = true;

		if (m_IsSinking)
		{
			Rigidbody rb = GetComponent<Rigidbody>();
			if (rb != null && !rb.isKinematic)
			{
				rb.isKinematic = true;
				Collider col = GetComponent<Collider>();
				if (col != null)
					col.enabled = false;
			}

			transform.position += Vector3.down * SinkSpeed * Time.deltaTime;

			if (transform.position.y < -5f)
				Destroy(gameObject);
		}
	}
}

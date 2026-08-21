using UnityEngine;

/// <summary>
/// Empty spawn pin for the 150x50 combat test arena. No gameplay, no spawning.
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatTestSpawnMarker : MonoBehaviour
{
	public enum MarkerSide
	{
		Player = 0,
		Enemy = 1,
		Neutral = 2
	}

	[SerializeField] private MarkerSide m_Side;

	public MarkerSide Side
	{
		get { return m_Side; }
		set { m_Side = value; }
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		Color color = m_Side == MarkerSide.Player
			? new Color(0.2f, 0.75f, 1f, 0.9f)
			: m_Side == MarkerSide.Enemy
				? new Color(1f, 0.25f, 0.2f, 0.9f)
				: new Color(1f, 0.85f, 0.2f, 0.9f);
		Gizmos.color = color;
		Vector3 feet = transform.position;
		Vector3 chest = feet + Vector3.up * 1.1f;
		Gizmos.DrawWireSphere(chest, 0.45f);
		Gizmos.DrawLine(feet, chest);
		Gizmos.DrawLine(chest, chest + transform.forward * 1.6f);
	}
#endif
}

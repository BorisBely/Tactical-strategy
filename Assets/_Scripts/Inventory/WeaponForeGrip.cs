using UnityEngine;

/// <summary>
/// Left-hand grip point on a foregrip attachment visual. Overrides weapon body LeftHandGrip while attached.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponForeGrip : MonoBehaviour
{
	public const string LeftHandGripName = "LeftHandGrip";

	[SerializeField] private Transform m_LeftHandGrip;

	public Transform LeftHandGrip => m_LeftHandGrip;

	public void SetLeftHandGrip(Transform _leftHandGrip)
	{
		m_LeftHandGrip = _leftHandGrip;
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (m_LeftHandGrip == null)
		{
			Transform found = transform.Find(LeftHandGripName);
			if (found == null)
			{
				foreach (Transform t in GetComponentsInChildren<Transform>(true))
				{
					if (t != transform && t.name == LeftHandGripName)
					{
						found = t;
						break;
					}
				}
			}

			m_LeftHandGrip = found;
		}
	}
#endif
}

using UnityEngine;

/// <summary>
/// World visual affiliation cue on a target. Observers interpret it locally.
/// Does not write Identity, Relationship, or fire. Does not know who is looking.
/// EvidenceStrength / EvidenceType are reserved; Identity math ignores them.
/// </summary>
[DisallowMultipleComponent]
public sealed class VisualIdentityEvidence : MonoBehaviour
{
	#region Serialized
	[SerializeField] private VisualAffiliation m_PrimaryAffiliation = VisualAffiliation.Unknown;
	[SerializeField, Range(0f, 1f)] private float m_EvidenceStrength = 1f;
	[SerializeField] private VisualIdentityEvidenceType m_EvidenceType = VisualIdentityEvidenceType.Unknown;
	#endregion

	#region Public Properties
	public VisualAffiliation PrimaryAffiliation => m_PrimaryAffiliation;
	public float EvidenceStrength => m_EvidenceStrength;
	public VisualIdentityEvidenceType EvidenceType => m_EvidenceType;
	#endregion

	#region Public Methods
	public static VisualIdentityEvidence GetOrCreate(GameObject _root)
	{
		if (_root == null)
			return null;
		if (_root.TryGetComponent(out VisualIdentityEvidence existing) && existing != null)
			return existing;
		return _root.AddComponent<VisualIdentityEvidence>();
	}

	public void SetPrimaryAffiliation(VisualAffiliation _affiliation)
	{
		m_PrimaryAffiliation = _affiliation;
	}

	public void SetEvidenceStrength(float _strength)
	{
		m_EvidenceStrength = Mathf.Clamp01(_strength);
	}

	public void SetEvidenceType(VisualIdentityEvidenceType _type)
	{
		m_EvidenceType = _type;
	}
	#endregion
}

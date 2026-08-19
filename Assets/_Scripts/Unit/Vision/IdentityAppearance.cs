using UnityEngine;

/// <summary>
/// Optional world-look cue on a target. What observers can try to identify — not UnitTeam.
/// Not placed on Unit.prefab (opt-in, same rule as DetectionProcessor).
/// Per-observer overrides on DetectionProcessor take precedence.
/// </summary>
[DisallowMultipleComponent]
public sealed class IdentityAppearance : MonoBehaviour
{
	#region Serialized
	[SerializeField] private ObservableAffiliation m_Affiliation = ObservableAffiliation.Unknown;
	#endregion

	#region Public Properties
	public ObservableAffiliation Affiliation => m_Affiliation;
	#endregion

	#region Public Methods
	public void SetAffiliation(ObservableAffiliation _affiliation)
	{
		m_Affiliation = _affiliation;
	}
	#endregion
}

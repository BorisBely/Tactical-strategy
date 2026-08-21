using System;
using UnityEngine;

/// <summary>
/// Legacy observer-relative look on a target. DetectionProcessor no longer reads this.
/// Use <see cref="VisualIdentityEvidence"/> (Player/Enemy/Civilian) instead.
/// </summary>
[Obsolete("Use VisualIdentityEvidence. DetectionProcessor maps world look by observer side.")]
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

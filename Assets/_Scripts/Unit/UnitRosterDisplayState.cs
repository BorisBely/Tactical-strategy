using UnityEngine;

/// <summary>
/// Позывной/имя юнита для UI списков (инвентарь, предмиссия).
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitRosterDisplayState : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private string m_Callsign;
	#endregion

	#region Public Properties
	public string Callsign => m_Callsign;
	public string DisplayName => string.IsNullOrWhiteSpace(m_Callsign) ? gameObject.name : m_Callsign;
	#endregion

	#region Public Methods
	public void SetCallsign(string _callsign)
	{
		m_Callsign = _callsign ?? string.Empty;
	}

	public static UnitRosterDisplayState GetOrCreate(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return null;

		if (!_unitRoot.TryGetComponent(out UnitRosterDisplayState state))
			state = _unitRoot.AddComponent<UnitRosterDisplayState>();

		return state;
	}
	#endregion
}

using UnityEngine;

/// <summary>
/// Позывной/имя юнита для UI списков (инвентарь, предмиссия).
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitRosterDisplayState : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private string m_Callsign;
	[SerializeField] private string m_FirstName;
	[SerializeField] private string m_LastName;
	#endregion

	#region Public Properties
	public string Callsign => m_Callsign;
	public string FirstName => m_FirstName;
	public string LastName => m_LastName;

	public string FullName
	{
		get
		{
			bool hasFirst = !string.IsNullOrWhiteSpace(m_FirstName);
			bool hasLast = !string.IsNullOrWhiteSpace(m_LastName);
			if (hasFirst && hasLast)
				return $"{m_FirstName} {m_LastName}";
			if (hasFirst)
				return m_FirstName;
			if (hasLast)
				return m_LastName;
			return Callsign;
		}
	}

	public string DisplayName => string.IsNullOrWhiteSpace(m_Callsign) ? gameObject.name : m_Callsign;
	#endregion

	#region Public Methods
	public void SetCallsign(string _callsign)
	{
		m_Callsign = _callsign ?? string.Empty;
	}

	public void SetName(string _firstName, string _lastName)
	{
		m_FirstName = _firstName ?? string.Empty;
		m_LastName = _lastName ?? string.Empty;
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

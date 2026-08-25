using UnityEngine;

/// <summary>
/// #9 observer-local ally report knowledge for one AI tick. Not vision. Not sound.
/// Does not imply Observed, AimPoint, or Fire.
/// </summary>
public readonly struct AIReportContact
{
	#region Public Fields
	public readonly Transform Reporter;
	public readonly Transform Subject;
	public readonly Vector3 Position;
	public readonly PerceivedIdentity Identity;
	public readonly float Confidence;
	public readonly float Time;
	public readonly float Age;
	#endregion

	#region Constructors
	public AIReportContact(
		Transform _reporter,
		Transform _subject,
		Vector3 _position,
		PerceivedIdentity _identity,
		float _confidence,
		float _time,
		float _age)
	{
		Reporter = _reporter;
		Subject = _subject;
		Position = _position;
		Identity = _identity;
		Confidence = _confidence;
		Time = _time;
		Age = Mathf.Max(0f, _age);
	}
	#endregion
}

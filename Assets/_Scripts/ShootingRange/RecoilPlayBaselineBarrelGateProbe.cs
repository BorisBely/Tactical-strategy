using UnityEngine;

/// <summary>
/// N8 Play probe: logs barrel gate vs aim+Offset after a 0.4s pause. Does not retune recoil numbers.
/// </summary>
[DisallowMultipleComponent]
public sealed class RecoilPlayBaselineBarrelGateProbe : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponRecoilController m_RecoilController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[TextArea(4, 12)]
	[SerializeField] private string m_LastLog;
	#endregion

	#region Public Properties
	public string LastLog => m_LastLog;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>() ??
			                   FindAnyObjectByType<UnitWeaponFireController>();
		if (m_RecoilController == null)
			m_RecoilController = GetComponent<UnitWeaponRecoilController>() ??
			                     FindAnyObjectByType<UnitWeaponRecoilController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>() ??
			                  FindAnyObjectByType<UnitWeaponRuntime>();
	}
	#endregion

	#region Public Methods
	[ContextMenu("Log N8 Gate Sample")]
	public void LogGateSample()
	{
		string weapon = m_WeaponRuntime != null && m_WeaponRuntime.CurrentWeaponDefinition != null
			? m_WeaponRuntime.CurrentWeaponDefinition.name
			: "?";
		Vector2 offset = m_RecoilController != null ? m_RecoilController.RecoilOffset : Vector2.zero;
		int shotIndex = m_RecoilController != null ? m_RecoilController.RecoilShotIndex : 0;
		bool aligned = m_FireController != null && m_FireController.DebugIsBarrelAlignedEnoughToFire();
		float error = m_FireController != null ? m_FireController.DebugLastBarrelAimErrorDegrees : -1f;
		string attempt = m_FireController != null ? m_FireController.LastShotAttemptResult.ToString() : "?";
		m_LastLog =
			weapon +
			" RecoilOffset=" + offset.magnitude.ToString("F3") +
			"° RecoilShotIndex=" + shotIndex +
			" barrelAligned=" + aligned +
			" error=" + error.ToString("F2") +
			"° lastAttempt=" + attempt +
			" ResetRecoilOnStopFiring=" +
			(m_FireController != null && m_FireController.ResetRecoilOnStopFiring);
		Debug.Log("[RecoilPlayBaseline N8] " + m_LastLog);
	}
	#endregion
}

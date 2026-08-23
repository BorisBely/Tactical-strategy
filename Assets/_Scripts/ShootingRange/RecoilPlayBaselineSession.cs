using UnityEngine;

/// <summary>
/// Optional human Play host. Preferred path is Tools/Tests/Run Recoil Play Baseline (Auto) —
/// it does not hang recorder/session/probe on the unit.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-40)]
public sealed class RecoilPlayBaselineSession : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private RecoilPlayBaselineRecorder m_Recorder;
	[SerializeField] private RecoilPlayBaselineBarrelGateProbe m_BarrelProbe;
	[SerializeField] private bool m_EnableHitLogging = true;
	[TextArea(6, 16)]
	[SerializeField] private string m_ConditionLog;
	#endregion

	#region Public Properties
	public RecoilPlayBaselineRecorder Recorder => m_Recorder;
	public RecoilPlayBaselineBarrelGateProbe BarrelProbe => m_BarrelProbe;
	public string ConditionLog => m_ConditionLog;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Recorder == null)
			m_Recorder = GetComponent<RecoilPlayBaselineRecorder>() ??
			             gameObject.AddComponent<RecoilPlayBaselineRecorder>();
		if (m_BarrelProbe == null)
			m_BarrelProbe = GetComponent<RecoilPlayBaselineBarrelGateProbe>() ??
			                gameObject.AddComponent<RecoilPlayBaselineBarrelGateProbe>();
	}

	private void OnEnable()
	{
		if (m_EnableHitLogging)
			ShootingRangeHitLogger.LoggingEnabled = true;
		EnsureRings();
		m_ConditionLog = BuildConditionLog();
		Debug.Log("[RecoilPlayBaseline]\n" + m_ConditionLog);
	}
	#endregion

	#region Public Methods
	[ContextMenu("Refresh Conditions")]
	public void RefreshConditions()
	{
		m_ConditionLog = BuildConditionLog();
		Debug.Log("[RecoilPlayBaseline]\n" + m_ConditionLog);
	}
	#endregion

	#region Private Methods
	private void EnsureRings()
	{
		ShootingRangeTarget[] targets = FindObjectsByType<ShootingRangeTarget>(FindObjectsInactive.Exclude);
		for (int i = 0; i < targets.Length; i++)
		{
			if (targets[i] == null)
				continue;
			if (targets[i].GetComponent<RecoilPlayBaselineTargetRings>() == null)
				targets[i].gameObject.AddComponent<RecoilPlayBaselineTargetRings>();
		}
	}

	private string BuildConditionLog()
	{
		UnitWeaponRuntime runtime = FindAnyObjectByType<UnitWeaponRuntime>();
		UnitCombatStats stats = FindAnyObjectByType<UnitCombatStats>();
		UnitWeaponReadyHandsLayer hands = FindAnyObjectByType<UnitWeaponReadyHandsLayer>();
		UnitAnimatorStance stance = FindAnyObjectByType<UnitAnimatorStance>();
		string weapon = runtime != null && runtime.CurrentWeaponDefinition != null
			? runtime.CurrentWeaponDefinition.name
			: "none";
		float recoilControl = stats != null ? stats.RecoilControl : -1f;
		string pose = hands != null ? hands.EffectivePoseState.ToString() : "?";
		string stanceName = stance != null ? stance.CurrentStance.ToString() : "?";
		bool prone = stance != null && stance.CurrentStance == LocomotionStance.Prone;
		return
			"Play protocol\n" +
			"- Weapon expect " + RecoilPlayBaselineProtocol.ReferenceWeaponAssetName + " FullAuto, no attachments. Now: " +
			weapon + "\n" +
			"- RecoilControl expect " + RecoilPlayBaselineProtocol.NeutralRecoilControl.ToString("F0") +
			". Now: " + recoilControl.ToString("F0") + "\n" +
			"- Pose: " + pose + "  Stance: " + stanceName + "  Prone must be OFF (" + (prone ? "FAIL prone" : "OK") + ")\n" +
			"- One target, aim at face center. Rings 10/25/50/100 cm on gizmos.\n" +
			"- 3 repeats / case. Median. A1 8 shots. A5: 3 shots, StopFiring 0.4s, 4th shot.\n" +
			"- N8: RecoilPlayBaselineBarrelGateProbe on M4 then M249/PKM.";
	}
	#endregion
}

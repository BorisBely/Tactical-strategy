/// <summary>
/// #14B.2 pose request. Readiness owns the ask; CombatReadiness executes.
/// Never Fire, never G6.
/// </summary>
public struct ReadinessPoseRequest
{
	#region Public Fields
	public ReadinessState State;
	public WeaponPoseState Pose;
	public WeaponPoseState FromPose;
	public WeaponPoseMode Mode;
	public float Duration;
	public bool IsPeaceful;
	public bool FromLifeGate;
	#endregion

	#region Public Properties
	public bool RequestsFire => false;
	public bool ChangesG6 => false;
	#endregion
}

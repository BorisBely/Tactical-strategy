using UnityEngine;

/// <summary>
/// Editor/SIM Play skill helpers mirroring <see cref="UnitCombatStats"/> RecoilControl curve.
/// </summary>
public static class RecoilPlaySkillUtility
{
	#region Constants
	private const float c_MaxSkill = 100f;
	private const float c_WorstKickMultiplier = 1.2f;
	private const float c_BestKickMultiplier = 0.8f;
	private const float c_WorstRecoveryMultiplier = 0.8f;
	private const float c_BestRecoveryMultiplier = 1.2f;
	#endregion

	#region Public Methods
	public static float GetRecoilControlKickMultiplier(float _recoilControlSkill)
	{
		return EvaluateSkillMultiplier(_recoilControlSkill, c_WorstKickMultiplier, c_BestKickMultiplier);
	}

	public static float GetRecoilControlRecoveryMultiplier(float _recoilControlSkill)
	{
		return EvaluateSkillMultiplier(_recoilControlSkill, c_WorstRecoveryMultiplier, c_BestRecoveryMultiplier);
	}

	public static void ApplyRecoilControlToContext(ref WeaponRecoilContext _context, float _recoilControlSkill)
	{
		_context.SkillKickMultiplier = GetRecoilControlKickMultiplier(_recoilControlSkill);
		_context.SkillRecoveryMultiplier = GetRecoilControlRecoveryMultiplier(_recoilControlSkill);
	}
	#endregion

	#region Private Methods
	private static float EvaluateSkillMultiplier(float _skill, float _worst, float _best)
	{
		float normalized = Mathf.InverseLerp(0f, c_MaxSkill, _skill);
		return Mathf.Max(0.01f, Mathf.Lerp(_worst, _best, normalized));
	}
	#endregion
}

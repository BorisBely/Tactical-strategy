using System.Collections.Generic;

public struct WeaponBalanceRow
{
	public WeaponBalanceCase Case;
	public RecoilSampleResult Recoil;
	public AccuracySampleResult Accuracy;
	public FireControlSampleResult FireControl;
	public WeaponBalanceScore Score;
	public WeaponBalanceVerdict Verdict;
	public List<string> Notes;

	public static WeaponBalanceRow Create(
		in WeaponBalanceCase _case,
		in RecoilSampleResult _recoil,
		in AccuracySampleResult _accuracy,
		in FireControlSampleResult _fireControl,
		in WeaponBalanceScore _score,
		WeaponBalanceVerdict _verdict,
		List<string> _notes)
	{
		return new WeaponBalanceRow
		{
			Case = _case,
			Recoil = _recoil,
			Accuracy = _accuracy,
			FireControl = _fireControl,
			Score = _score,
			Verdict = _verdict,
			Notes = _notes ?? new List<string>()
		};
	}
}

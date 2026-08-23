using System.Collections.Generic;

public struct WeaponBalanceRelativeMetric
{
	public string WeaponName;
	public string MetricName;
	public float Value;
	public float M4Value;
	public float Ratio;
}

public struct WeaponBalanceWeaponSummary
{
	public string WeaponName;
	public WeaponClassType WeaponClass;
	public WeaponBalanceRow BaselineRow;
	public WeaponBalanceScoreDetail ScoreDetail;
	public WeaponBalanceVerdict Verdict;
	public WeaponBalanceWarnKind WarnKind;
	public List<WeaponBalanceRelativeMetric> RelativeToM4;
}

public struct WeaponBalanceLoadoutDelta
{
	public string WeaponName;
	public string LoadoutLabel;
	public WeaponPoseState Pose;
	public WeaponBalanceStance Stance;
	public WeaponBalanceMovement Movement;
	public float DistanceMeters;
	public WeaponFireMode FireMode;
	public float BaseOffsetMag5;
	public float LoadoutOffsetMag5;
	public float DeltaDegrees;
	public WeaponBalanceWarnKind WarnKind;

	public string FormatCaseContext()
	{
		string movement = Movement == WeaponBalanceMovement.Idle
			? "idle"
			: Movement.ToString();
		return "[" + Pose + "/" + Stance + "/" + movement + "/" +
		       DistanceMeters.ToString("F0") + "m/" + FireMode + "]";
	}
}

public struct WeaponBalanceClassGroup
{
	public WeaponClassType ClassType;
	public List<string> WeaponNames;
	public float MinOffsetMag5;
	public float MaxOffsetMag5;
	public float MedianOffsetMag5;
}

public struct WeaponBalanceOutlierRecord
{
	public string WeaponName;
	public string CaseLabel;
	public string MetricName;
	public float Actual;
	public string Expected;
	public WeaponBalanceWarnKind WarnKind;
	public WeaponBalanceVerdict Severity;
	public bool PlayNeeded;
	public string Reason;
}

public struct WeaponBalanceAutoDisciplineRow
{
	public string WeaponName;
	public float DistanceMeters;
	public WeaponFireMode SelectedMode;
	public float GroupDiameterMeters;
	public bool AutoAcceptable;
	public int PlannerSeriesLength;
	public float PlannerDisplacementMeters;
}

public struct WeaponBalancePlayCorrelation
{
	public string WeaponName;
	public string LoadoutLabel;
	public float AnalyticalOffsetMag5;
	public float ReplayOffsetMag5;
	public float Delta;
	public bool Pass;
}

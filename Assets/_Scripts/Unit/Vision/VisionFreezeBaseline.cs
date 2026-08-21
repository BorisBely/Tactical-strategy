using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Frozen perception baseline for AI handoff (2026-08-19).
/// Vision = perception. Vision ≠ orders / search / tactics.
/// Do not retune these literals while developing AI. AI reads PerceivedContact.
/// </summary>
public static class VisionFreezeBaseline
{
	#region Frozen Detection
	public const float FovHalfDegrees = 60f;
	public const float FovEdgeFactor = 0.15f;
	public const float AcquireThreshold = 0.25f;
	public const float AcquireTimeSeconds = 0.35f;
	#endregion

	#region Frozen Memory
	public const float RecentlyLostSeconds = 5f;
	public const float MemoryHorizonSeconds = 30f;
	public const float MemoryShape = 1.5f;
	public const float MemoryStale = 0.25f;
	#endregion

	#region Frozen Identity
	public const float IdentifyTimeSeconds = 4f;
	public const float IdentityCommit = 0.50f;
	#endregion

	#region Frozen Threat
	public const float ThreatHighMeters = 25f;
	public const float ThreatMediumMeters = 80f;
	#endregion

	#region Companion freeze (Block A, not AI knobs)
	public const float LoseThreshold = 0.20f;
	public const float LossTimeSeconds = 2.5f;
	public const float DistanceCurvePlateauT = 0.10f;
	public const float DistanceCurveEdgeFactor = 0.08f;
	public const float DistanceDefaultRangeMeters = 150f;
	#endregion

	public struct ReportResult
	{
		public string Body;
		public int PassCount;
		public int FailCount;
	}

	public static ReportResult BuildReport()
	{
		var sb = new StringBuilder(2048);
		int pass = 0;
		int fail = 0;

		void Check(string _name, bool _ok, string _detail)
		{
			if (_ok)
			{
				pass++;
				sb.Append("PASS ").Append(_name).Append(" | ").AppendLine(_detail);
			}
			else
			{
				fail++;
				sb.Append("FAIL ").Append(_name).Append(" | ").AppendLine(_detail);
			}
		}

		bool Near(float _a, float _b)
		{
			return Mathf.Abs(_a - _b) < 0.0001f;
		}

		sb.AppendLine("VISION FREEZE / AI HANDOFF");
		sb.AppendLine("==========================");
		sb.AppendLine("stamp=" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		sb.AppendLine("Vision = perception");
		sb.AppendLine("Vision ≠ orders");
		sb.AppendLine("Vision ≠ search");
		sb.AppendLine("Vision ≠ tactics");
		sb.AppendLine("AI reads PerceivedContact via IPerceivedContactRegistry");
		sb.AppendLine("---");
		sb.AppendLine("FROZEN DETECTION");
		sb.AppendLine($"  FOV half     = {FovHalfDegrees:0}°");
		sb.AppendLine($"  FOV edge     = {FovEdgeFactor:0.00}");
		sb.AppendLine($"  Acquire      = {AcquireThreshold:0.00}");
		sb.AppendLine($"  AcquireTime  = {AcquireTimeSeconds:0.00} s");
		sb.AppendLine("FROZEN MEMORY");
		sb.AppendLine($"  RecentlyLost = {RecentlyLostSeconds:0} s");
		sb.AppendLine($"  Horizon      = {MemoryHorizonSeconds:0} s");
		sb.AppendLine($"  Shape        = {MemoryShape:0.0}");
		sb.AppendLine($"  Stale        = {MemoryStale:0.00}");
		sb.AppendLine("FROZEN IDENTITY");
		sb.AppendLine($"  IdentifyTime = {IdentifyTimeSeconds:0} s");
		sb.AppendLine($"  Commit       = {IdentityCommit:0.00}");
		sb.AppendLine("FROZEN THREAT");
		sb.AppendLine($"  High         <= {ThreatHighMeters:0} m");
		sb.AppendLine($"  Medium       <= {ThreatMediumMeters:0} m");
		sb.AppendLine($"  Low          >  {ThreatMediumMeters:0} m");
		sb.AppendLine("---");

		Check("Freeze_FovHalf",
			Near(DetectionQualityMath.DefaultFovHalfDegrees, FovHalfDegrees),
			$"math={DetectionQualityMath.DefaultFovHalfDegrees:0}");
		Check("Freeze_FovEdge",
			Near(DetectionQualityMath.DefaultFovEdgeFactor, FovEdgeFactor),
			$"math={DetectionQualityMath.DefaultFovEdgeFactor:0.00}");
		Check("Freeze_Acquire",
			Near(DetectionQualityMath.DefaultAcquireThreshold, AcquireThreshold),
			$"math={DetectionQualityMath.DefaultAcquireThreshold:0.00}");
		Check("Freeze_AcquireTime",
			Near(DetectionQualityMath.DefaultAcquireTime, AcquireTimeSeconds),
			$"math={DetectionQualityMath.DefaultAcquireTime:0.00}");

		Check("Freeze_RecentlyLost",
			Near(MemoryDecayMath.DefaultRecentlyLostSeconds, RecentlyLostSeconds),
			$"math={MemoryDecayMath.DefaultRecentlyLostSeconds:0}");
		Check("Freeze_Horizon",
			Near(MemoryDecayMath.DefaultHorizonSeconds, MemoryHorizonSeconds),
			$"math={MemoryDecayMath.DefaultHorizonSeconds:0}");
		Check("Freeze_Shape",
			Near(MemoryDecayMath.DefaultShapeExponent, MemoryShape),
			$"math={MemoryDecayMath.DefaultShapeExponent:0.0}");
		Check("Freeze_Stale",
			Near(MemoryDecayMath.DefaultStaleThreshold, MemoryStale),
			$"math={MemoryDecayMath.DefaultStaleThreshold:0.00}");

		Check("Freeze_IdentifyTime",
			Near(IdentityKnowledgeMath.DefaultIdentifyTimeSeconds, IdentifyTimeSeconds),
			$"math={IdentityKnowledgeMath.DefaultIdentifyTimeSeconds:0}");
		Check("Freeze_Commit",
			Near(IdentityKnowledgeMath.DefaultCommitThreshold, IdentityCommit),
			$"math={IdentityKnowledgeMath.DefaultCommitThreshold:0.00}");
		Check("Freeze_ThreatHigh",
			Near(IdentityKnowledgeMath.DefaultThreatHighMeters, ThreatHighMeters),
			$"math={IdentityKnowledgeMath.DefaultThreatHighMeters:0}");
		Check("Freeze_ThreatMedium",
			Near(IdentityKnowledgeMath.DefaultThreatMediumMeters, ThreatMediumMeters),
			$"math={IdentityKnowledgeMath.DefaultThreatMediumMeters:0}");

		Check("Companion_LoseThreshold",
			Near(DetectionQualityMath.DefaultLoseThreshold, LoseThreshold),
			$"math={DetectionQualityMath.DefaultLoseThreshold:0.00}");
		Check("Companion_LossTime",
			Near(DetectionQualityMath.DefaultLossTime, LossTimeSeconds),
			$"math={DetectionQualityMath.DefaultLossTime:0.00}");
		Check("Companion_DistancePlateau",
			Near(DetectionQualityMath.EvaluateDistanceCurve(DistanceCurvePlateauT), 1f),
			$"math={DetectionQualityMath.EvaluateDistanceCurve(DistanceCurvePlateauT):0.00}");
		Check("Companion_DistanceEdge",
			Near(DetectionQualityMath.EvaluateDistanceCurve(1f), DistanceCurveEdgeFactor),
			$"math={DetectionQualityMath.EvaluateDistanceCurve(1f):0.00}");
		Check("Companion_DistanceDefaultRange",
			Near(DetectionQualityMath.DefaultFarMeters, DistanceDefaultRangeMeters),
			$"math={DetectionQualityMath.DefaultFarMeters:0}");

		Check("Contract_IdentityIsAffiliationClass",
			System.Enum.GetNames(typeof(PerceivedIdentity)).Length == 4,
			"Unknown/Friendly/Neutral/Hostile only");
		Check("Contract_UnknownIsNotFriendly",
			PerceivedIdentity.Unknown != PerceivedIdentity.Friendly,
			"Unknown ≠ Friendly");
		Check("Contract_HostileFarIsLow",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, 100f) == ThreatLevel.Low,
			"100 m Hostile → Low");
		Check("Contract_FriendlyThreatNone",
			IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Friendly, 10f) == ThreatLevel.None,
			"Friendly → None");

		sb.AppendLine("---");
		sb.Append("RESULT=").Append(fail == 0 ? "PASS" : "FAIL");
		sb.Append(" pass=").Append(pass);
		sb.Append(" fail=").Append(fail);

		return new ReportResult
		{
			Body = sb.ToString(),
			PassCount = pass,
			FailCount = fail
		};
	}
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ключевые точки дистанционных кривых для оптических модулей.
/// Меньше 1 = лучше (точнее / быстрее прицеливание).
/// </summary>
public static class OpticDistanceCurveLibrary
{
	#region Nested Types
	public readonly struct DistanceKeyframe
	{
		public readonly float DistanceMeters;
		public readonly float Value;

		public DistanceKeyframe(float _distanceMeters, float _value)
		{
			DistanceMeters = _distanceMeters;
			Value = _value;
		}
	}

	public readonly struct OpticDistanceCurves
	{
		public readonly DistanceKeyframe[] DispersionKeyframes;
		public readonly DistanceKeyframe[] AimTimeKeyframes;
		public readonly string SweetSpotLabel;

		public OpticDistanceCurves(
			string _sweetSpotLabel,
			DistanceKeyframe[] _dispersionKeyframes,
			DistanceKeyframe[] _aimTimeKeyframes)
		{
			SweetSpotLabel = _sweetSpotLabel;
			DispersionKeyframes = _dispersionKeyframes;
			AimTimeKeyframes = _aimTimeKeyframes;
		}
	}
	#endregion

	#region Public Methods
	public static OpticDistanceCurves GetCurves(OpticDistanceProfileKind _kind)
	{
		return _kind switch
		{
			OpticDistanceProfileKind.Collimator => s_Reddot1,
			OpticDistanceProfileKind.Holographic => s_Reddot2,
			OpticDistanceProfileKind.Hybrid => s_EotechG33,
			OpticDistanceProfileKind.VariableMagnification => s_VortexRazor,
			OpticDistanceProfileKind.Scope3x => s_Scope1_3x,
			OpticDistanceProfileKind.Scope4x => s_Acog,
			OpticDistanceProfileKind.Scope4Long => s_Scope4,
			OpticDistanceProfileKind.Scope5Long => s_Scope5,
			OpticDistanceProfileKind.Scope9Long => s_Scope9,
			OpticDistanceProfileKind.AkCollimator => s_AkReddot4,
			OpticDistanceProfileKind.AkPso => s_AkScope11,
			_ => s_FlatNeutral
		};
	}

	public static OpticDistanceCurves GetCurvesForAttachment(WeaponAttachmentDefinition _attachment)
	{
		if (_attachment == null)
			return s_FlatNeutral;

		string name = _attachment.name ?? string.Empty;
		if (s_NamedCurves.TryGetValue(name, out OpticDistanceCurves named))
			return named;

		return GetCurves(ResolveKind(_attachment));
	}

	public static AnimationCurve BuildCurve(IReadOnlyList<DistanceKeyframe> _keyframes)
	{
		if (_keyframes == null || _keyframes.Count == 0)
			return AnimationCurve.Linear(0f, 1f, 100f, 1f);

		var keys = new Keyframe[_keyframes.Count];
		for (int i = 0; i < _keyframes.Count; i++)
		{
			keys[i] = new Keyframe(_keyframes[i].DistanceMeters, _keyframes[i].Value)
			{
				weightedMode = WeightedMode.None,
				inTangent = 0f,
				outTangent = 0f
			};
		}

		return new AnimationCurve(keys);
	}

	public static void ApplyToProfile(WeaponDistanceAimProfile _profile, WeaponAttachmentDefinition _attachment)
	{
		if (_profile == null || _attachment == null)
			return;

		OpticDistanceCurves curves = GetCurvesForAttachment(_attachment);
		_profile.SetCurves(
			BuildCurve(curves.DispersionKeyframes),
			BuildCurve(curves.AimTimeKeyframes));
	}

	public static OpticDistanceProfileKind ResolveKind(WeaponAttachmentDefinition _attachment)
	{
		if (_attachment == null || _attachment.AttachmentType != WeaponAttachmentType.Optic)
			return OpticDistanceProfileKind.Collimator;

		string name = _attachment.name ?? string.Empty;
		if (name.Contains("AK_Reddot4"))
			return OpticDistanceProfileKind.AkCollimator;
		if (name.Contains("AK_Scope11"))
			return OpticDistanceProfileKind.AkPso;
		if (name.Contains("Reddot2"))
			return OpticDistanceProfileKind.Holographic;
		if (name.Contains("EOTech") || name.Contains("G33"))
			return OpticDistanceProfileKind.Hybrid;
		if (name.Contains("Vortex") || name.Contains("ELCAN") || name.Contains("SpecterDR"))
			return OpticDistanceProfileKind.VariableMagnification;
		if (name.Contains("Scope1_3x"))
			return OpticDistanceProfileKind.Scope3x;
		if (name.Contains("Scope4"))
			return OpticDistanceProfileKind.Scope4Long;
		if (name.Contains("Scope5"))
			return OpticDistanceProfileKind.Scope5Long;
		if (name.Contains("Scope9"))
			return OpticDistanceProfileKind.Scope9Long;
		if (name.Contains("ACOG") || name.Contains("SUSAT") || name.Contains("Mosin_Scope"))
			return OpticDistanceProfileKind.Scope4x;
		if (name.Contains("Reddot") || name.Contains("RDC") || name.Contains("Aimpoint"))
			return OpticDistanceProfileKind.Collimator;

		return OpticDistanceProfileKind.Collimator;
	}
	#endregion

	#region Named Lookup
	private static readonly Dictionary<string, OpticDistanceCurves> s_NamedCurves = BuildNamedCurves();

	private static Dictionary<string, OpticDistanceCurves> BuildNamedCurves()
	{
		var map = new Dictionary<string, OpticDistanceCurves>
		{
			["Attachment_M4_Reddot1"] = s_Reddot1,
			["Attachment_M4_Reddot3"] = s_Reddot3,
			["Attachment_M4_RDC"] = s_Rdc,
			["Attachment_M4_Aimpoint"] = s_Aimpoint,
			["Attachment_M4_Reddot2"] = s_Reddot2,
			["Attachment_M4_EOTech_G33"] = s_EotechG33,
			["Attachment_M4_Vortex_Razor"] = s_VortexRazor,
			["Attachment_M4_ELCAN_SpecterDR"] = s_ElcanSpecterDr,
			["Attachment_M4_Scope1_3x"] = s_Scope1_3x,
			["Attachment_M4_ACOG"] = s_Acog,
			["Attachment_M4_SUSAT"] = s_Susat,
			["Attachment_M4_ACOG_RMR"] = s_AcogRmr,
			["Attachment_Mosin_Scope8"] = s_MosinScope8,
			["Attachment_M4_Scope4"] = s_Scope4,
			["Attachment_M4_Scope5"] = s_Scope5,
			["Attachment_M4_Scope9"] = s_Scope9,
			["Attachment_AK_Reddot4_Rail"] = s_AkReddot4,
			["Attachment_AK_Scope11"] = s_AkScope11
		};
		return map;
	}
	#endregion

	#region Curve Data
	private static readonly OpticDistanceCurves s_FlatNeutral = Make("нейтральный",
		new[] { K(0f, 1f), K(100f, 1f) },
		new[] { K(0f, 1f), K(100f, 1f) });

	private static readonly OpticDistanceCurves s_Reddot1 = Make("0–15 м",
		new[] { K(0f, 0.72f), K(15f, 0.76f), K(25f, 0.88f), K(40f, 1.08f), K(60f, 1.28f), K(100f, 1.48f) },
		new[] { K(0f, 0.86f), K(15f, 0.92f), K(25f, 1.02f), K(40f, 1.15f), K(60f, 1.32f), K(100f, 1.50f) });

	private static readonly OpticDistanceCurves s_Reddot3 = Make("0–15 м (компактный)",
		new[] { K(0f, 0.71f), K(15f, 0.75f), K(25f, 0.90f), K(40f, 1.10f), K(60f, 1.30f), K(100f, 1.50f) },
		new[] { K(0f, 0.87f), K(15f, 0.93f), K(25f, 1.03f), K(40f, 1.16f), K(60f, 1.34f), K(100f, 1.52f) });

	private static readonly OpticDistanceCurves s_Rdc = Make("0–15 м (открытый)",
		new[] { K(0f, 0.70f), K(15f, 0.74f), K(25f, 0.92f), K(40f, 1.12f), K(60f, 1.32f), K(100f, 1.52f) },
		new[] { K(0f, 0.88f), K(15f, 0.94f), K(25f, 1.05f), K(40f, 1.18f), K(60f, 1.35f), K(100f, 1.54f) });

	private static readonly OpticDistanceCurves s_Aimpoint = Make("0–15 м (трубчатый)",
		new[] { K(0f, 0.73f), K(15f, 0.77f), K(25f, 0.86f), K(40f, 1.06f), K(60f, 1.26f), K(100f, 1.45f) },
		new[] { K(0f, 0.84f), K(15f, 0.90f), K(25f, 1.00f), K(40f, 1.13f), K(60f, 1.30f), K(100f, 1.48f) });

	private static readonly OpticDistanceCurves s_Reddot2 = Make("0–20 м",
		new[] { K(0f, 0.74f), K(20f, 0.78f), K(35f, 0.98f), K(50f, 1.12f), K(70f, 1.28f), K(100f, 1.42f) },
		new[] { K(0f, 0.88f), K(20f, 0.94f), K(35f, 1.04f), K(50f, 1.14f), K(70f, 1.26f), K(100f, 1.38f) });

	private static readonly OpticDistanceCurves s_EotechG33 = Make("0–20 и 40–70 м",
		new[] { K(0f, 0.80f), K(15f, 0.82f), K(20f, 0.86f), K(35f, 1.12f), K(45f, 0.78f), K(60f, 0.74f), K(75f, 0.88f), K(100f, 1.05f) },
		new[] { K(0f, 0.98f), K(20f, 1.06f), K(35f, 1.20f), K(45f, 1.14f), K(60f, 1.10f), K(75f, 1.18f), K(100f, 1.28f) });

	private static readonly OpticDistanceCurves s_VortexRazor = Make("10–60 м (1–6x)",
		new[] { K(0f, 1.10f), K(10f, 0.94f), K(25f, 0.82f), K(40f, 0.74f), K(60f, 0.76f), K(80f, 0.90f), K(100f, 1.06f) },
		new[] { K(0f, 1.30f), K(10f, 1.16f), K(25f, 1.04f), K(40f, 1.00f), K(60f, 1.02f), K(80f, 1.12f), K(100f, 1.22f) });

	private static readonly OpticDistanceCurves s_ElcanSpecterDr = Make("10–60 м (1–4x)",
		new[] { K(0f, 1.06f), K(10f, 0.92f), K(25f, 0.80f), K(45f, 0.72f), K(60f, 0.74f), K(80f, 0.94f), K(100f, 1.10f) },
		new[] { K(0f, 1.24f), K(10f, 1.10f), K(25f, 0.98f), K(45f, 0.92f), K(60f, 0.96f), K(80f, 1.06f), K(100f, 1.16f) });

	private static readonly OpticDistanceCurves s_Scope1_3x = Make("20–40 м",
		new[] { K(0f, 1.20f), K(15f, 1.10f), K(20f, 0.86f), K(40f, 0.74f), K(55f, 0.94f), K(75f, 1.12f), K(100f, 1.28f) },
		new[] { K(0f, 1.32f), K(15f, 1.20f), K(20f, 1.06f), K(40f, 0.94f), K(55f, 1.04f), K(75f, 1.16f), K(100f, 1.28f) });

	private static readonly OpticDistanceCurves s_Acog = Make("40–50 м",
		new[] { K(0f, 1.24f), K(30f, 1.06f), K(40f, 0.78f), K(50f, 0.72f), K(65f, 0.80f), K(85f, 0.92f), K(100f, 1.02f) },
		new[] { K(0f, 1.36f), K(30f, 1.16f), K(40f, 1.04f), K(50f, 0.96f), K(65f, 1.00f), K(85f, 1.06f), K(100f, 1.12f) });

	private static readonly OpticDistanceCurves s_Susat = Make("40–50 м (SUSAT)",
		new[] { K(0f, 1.26f), K(30f, 1.08f), K(40f, 0.80f), K(50f, 0.74f), K(65f, 0.78f), K(85f, 0.90f), K(100f, 1.00f) },
		new[] { K(0f, 1.38f), K(30f, 1.18f), K(40f, 1.06f), K(50f, 0.98f), K(65f, 1.02f), K(85f, 1.04f), K(100f, 1.10f) });

	private static readonly OpticDistanceCurves s_AcogRmr = Make("40–50 м + RMR вблизи",
		new[] { K(0f, 0.92f), K(20f, 0.88f), K(40f, 0.80f), K(50f, 0.76f), K(65f, 0.82f), K(85f, 0.94f), K(100f, 1.04f) },
		new[] { K(0f, 1.10f), K(20f, 1.02f), K(40f, 1.08f), K(50f, 1.00f), K(65f, 1.04f), K(85f, 1.08f), K(100f, 1.14f) });

	private static readonly OpticDistanceCurves s_MosinScope8 = Make("40–50 м (высокое увеличение)",
		new[] { K(0f, 1.30f), K(30f, 1.12f), K(40f, 0.82f), K(50f, 0.76f), K(65f, 0.74f), K(85f, 0.78f), K(100f, 0.86f) },
		new[] { K(0f, 1.42f), K(30f, 1.22f), K(40f, 1.10f), K(50f, 1.02f), K(65f, 0.96f), K(85f, 0.98f), K(100f, 1.04f) });

	private static readonly OpticDistanceCurves s_Scope4 = Make("60–70 м",
		new[] { K(0f, 1.42f), K(40f, 1.18f), K(60f, 0.76f), K(70f, 0.72f), K(85f, 0.80f), K(100f, 0.86f) },
		new[] { K(0f, 1.52f), K(40f, 1.30f), K(60f, 1.06f), K(70f, 1.00f), K(85f, 1.04f), K(100f, 1.08f) });

	private static readonly OpticDistanceCurves s_Scope5 = Make("70–80 м",
		new[] { K(0f, 1.50f), K(50f, 1.12f), K(70f, 0.74f), K(80f, 0.70f), K(95f, 0.68f), K(100f, 0.66f) },
		new[] { K(0f, 1.58f), K(50f, 1.24f), K(70f, 1.00f), K(80f, 0.94f), K(100f, 0.90f) });

	private static readonly OpticDistanceCurves s_Scope9 = Make("80–100 м",
		new[] { K(0f, 1.60f), K(60f, 1.08f), K(80f, 0.66f), K(100f, 0.58f) },
		new[] { K(0f, 1.65f), K(60f, 1.20f), K(80f, 0.90f), K(100f, 0.82f) });

	private static readonly OpticDistanceCurves s_AkReddot4 = Make("0–15 м (AK)",
		new[] { K(0f, 0.76f), K(15f, 0.80f), K(25f, 0.94f), K(40f, 1.12f), K(60f, 1.32f), K(100f, 1.52f) },
		new[] { K(0f, 0.92f), K(15f, 0.98f), K(25f, 1.08f), K(40f, 1.20f), K(60f, 1.37f), K(100f, 1.54f) });

	private static readonly OpticDistanceCurves s_AkScope11 = Make("50–60 м (PSO)",
		new[] { K(0f, 1.14f), K(35f, 1.00f), K(50f, 0.78f), K(60f, 0.76f), K(75f, 0.80f), K(100f, 0.86f) },
		new[] { K(0f, 1.32f), K(35f, 1.14f), K(50f, 1.00f), K(60f, 0.96f), K(75f, 1.00f), K(100f, 1.08f) });
	#endregion

	#region Helpers
	private static DistanceKeyframe K(float _distanceMeters, float _value) =>
		new DistanceKeyframe(_distanceMeters, _value);

	private static OpticDistanceCurves Make(
		string _label,
		DistanceKeyframe[] _dispersion,
		DistanceKeyframe[] _aim) =>
		new OpticDistanceCurves(_label, _dispersion, _aim);
	#endregion
}

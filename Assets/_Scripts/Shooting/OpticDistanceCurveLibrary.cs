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
			OpticDistanceProfileKind.Collimator => s_CollimatorMod1,
			OpticDistanceProfileKind.Holographic => s_HolographicMod1,
			OpticDistanceProfileKind.Hybrid => s_HybridSight,
			OpticDistanceProfileKind.VariableMagnification => s_Optic1To6x,
			OpticDistanceProfileKind.Scope3x => s_Optic3x,
			OpticDistanceProfileKind.Scope4x => s_Optic4x,
			OpticDistanceProfileKind.Scope4Long => s_SniperScopeMod0,
			OpticDistanceProfileKind.Scope5Long => s_SniperScopeMod1,
			OpticDistanceProfileKind.Scope9Long => s_SniperScopeMod2,
			OpticDistanceProfileKind.AkCollimator => s_StandardAkCollimator,
			OpticDistanceProfileKind.AkPso => s_AkSideRailOptic,
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

	public static float EvaluateDispersionMultiplier(
		WeaponAttachmentDefinition _attachment,
		float _distanceMeters)
	{
		OpticDistanceCurves curves = GetCurvesForAttachment(_attachment);
		return EvaluateKeyframes(curves.DispersionKeyframes, _distanceMeters);
	}

	public static float EvaluateAimTimeMultiplier(
		WeaponAttachmentDefinition _attachment,
		float _distanceMeters)
	{
		OpticDistanceCurves curves = GetCurvesForAttachment(_attachment);
		return EvaluateKeyframes(curves.AimTimeKeyframes, _distanceMeters);
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

	#region Curve Data
	private static readonly OpticDistanceCurves s_FlatNeutral = Make("нейтральный",
		new[] { K(0f, 1f), K(100f, 1f) },
		new[] { K(0f, 1f), K(100f, 1f) });

	private static readonly OpticDistanceCurves s_CollimatorMod1 = Make("0–15 м, пересечение 25 м",
		new[] { K(0f, 0.91f), K(10f, 0.90f), K(15f, 0.92f), K(25f, 1.00f), K(40f, 1.06f), K(100f, 1.10f) },
		new[] { K(0f, 0.96f), K(15f, 0.98f), K(25f, 1.00f), K(40f, 1.06f), K(100f, 1.08f) });

	private static readonly OpticDistanceCurves s_CollimatorMod2 = Make("0–15 м, пересечение 30 м",
		new[] { K(0f, 0.92f), K(10f, 0.91f), K(15f, 0.91f), K(30f, 1.00f), K(45f, 1.06f), K(100f, 1.10f) },
		new[] { K(0f, 0.96f), K(15f, 0.98f), K(30f, 1.00f), K(45f, 1.06f), K(100f, 1.08f) });

	private static readonly OpticDistanceCurves s_CollimatorMod3 = Make("10–20 м, пересечение 30 м",
		new[] { K(0f, 0.92f), K(10f, 0.90f), K(20f, 0.90f), K(30f, 1.00f), K(100f, 1.06f) },
		new[] { K(0f, 0.96f), K(10f, 0.98f), K(20f, 1.00f), K(35f, 1.04f), K(100f, 1.06f) });

	private static readonly OpticDistanceCurves s_Optic2x = Make("20–45 м, пересечение 45 м",
		new[] { K(0f, 0.98f), K(20f, 0.92f), K(35f, 0.90f), K(45f, 1.00f), K(100f, 1.04f) },
		new[] { K(0f, 0.98f), K(20f, 1.00f), K(35f, 0.98f), K(45f, 1.00f), K(100f, 1.04f) });

	private static readonly OpticDistanceCurves s_HolographicMod1 = Make("0–20 м, пересечение 35 м",
		new[] { K(0f, 0.92f), K(20f, 0.93f), K(35f, 1.00f), K(100f, 1.06f) },
		new[] { K(0f, 0.96f), K(20f, 0.98f), K(35f, 1.02f), K(100f, 1.06f) });

	private static readonly OpticDistanceCurves s_HybridSight = Make("0–20 и 35–55 м, пересечение 55 м",
		new[] { K(0f, 0.92f), K(20f, 0.94f), K(35f, 1.04f), K(45f, 0.92f), K(55f, 0.90f), K(75f, 0.96f), K(100f, 1.04f) },
		new[] { K(0f, 1.02f), K(20f, 1.08f), K(35f, 1.12f), K(45f, 1.08f), K(55f, 1.02f), K(75f, 1.06f), K(100f, 1.10f) });

	private static readonly OpticDistanceCurves s_Optic1To6x = Make("0–60 м, пересечение 65 м",
		new[] { K(0f, 1.06f), K(10f, 1.03f), K(25f, 0.96f), K(40f, 0.94f), K(60f, 0.92f), K(80f, 0.98f), K(100f, 1.06f) },
		new[] { K(0f, 1.22f), K(10f, 1.13f), K(25f, 1.07f), K(40f, 1.03f), K(60f, 1.03f), K(80f, 1.09f), K(100f, 1.13f) });

	private static readonly OpticDistanceCurves s_Optic1To4xMod1 = Make("0–50 м, пересечение 60 м",
		new[] { K(0f, 1.04f), K(20f, 0.98f), K(45f, 0.86f), K(60f, 0.88f), K(80f, 0.96f), K(100f, 1.06f) },
		new[] { K(0f, 1.16f), K(20f, 1.10f), K(45f, 0.98f), K(60f, 1.00f), K(80f, 1.06f), K(100f, 1.10f) });

	private static readonly OpticDistanceCurves s_Optic3x = Make("35–55 м, пересечение 55 м",
		new[] { K(0f, 1.12f), K(20f, 0.96f), K(40f, 0.82f), K(55f, 0.88f), K(100f, 1.04f) },
		new[] { K(0f, 1.20f), K(20f, 1.04f), K(40f, 0.98f), K(55f, 1.02f), K(100f, 1.08f) });

	private static readonly OpticDistanceCurves s_Optic4x = Make("40–50 м, пересечение 60 м",
		new[] { K(0f, 1.12f), K(40f, 0.88f), K(50f, 0.84f), K(60f, 0.90f), K(100f, 1.02f) },
		new[] { K(0f, 1.24f), K(40f, 1.04f), K(50f, 0.98f), K(65f, 1.00f), K(100f, 1.06f) });

	private static readonly OpticDistanceCurves s_Optic4xMod1 = Make("0–15 и 40–50 м, пересечение 60 м",
		new[] { K(0f, 1.10f), K(15f, 1.04f), K(30f, 1.04f), K(40f, 0.90f), K(50f, 0.86f), K(60f, 0.88f), K(100f, 1.00f) },
		new[] { K(0f, 1.22f), K(13f, 1.14f), K(40f, 1.06f), K(50f, 1.00f), K(55f, 1.02f), K(100f, 1.06f) });

	private static readonly OpticDistanceCurves s_Optic3_5x = Make("30–50 м, пересечение 60 м",
		new[] { K(0f, 0.98f), K(20f, 0.94f), K(40f, 0.88f), K(50f, 0.84f), K(65f, 0.88f), K(100f, 1.02f) },
		new[] { K(0f, 1.08f), K(20f, 1.02f), K(40f, 1.04f), K(50f, 0.98f), K(65f, 1.02f), K(100f, 1.06f) });

	private static readonly OpticDistanceCurves s_SniperScopeMod0 = Make("60–70 м",
		new[] { K(0f, 1.28f), K(40f, 1.08f), K(60f, 0.88f), K(70f, 0.86f), K(85f, 0.92f), K(100f, 0.94f) },
		new[] { K(0f, 1.40f), K(40f, 1.24f), K(60f, 1.08f), K(70f, 1.02f), K(100f, 1.06f) });

	private static readonly OpticDistanceCurves s_SniperScopeMod1 = Make("70–80 м",
		new[] { K(0f, 1.34f), K(50f, 1.06f), K(70f, 0.86f), K(80f, 0.84f), K(95f, 0.90f), K(100f, 0.82f) },
		new[] { K(0f, 1.44f), K(50f, 1.18f), K(70f, 1.04f), K(80f, 0.98f), K(100f, 0.94f) });

	private static readonly OpticDistanceCurves s_SniperScopeMod2 = Make("80–100 м",
		new[] { K(0f, 1.40f), K(60f, 1.06f), K(80f, 0.88f), K(100f, 0.86f) },
		new[] { K(0f, 1.39f), K(60f, 1.12f), K(80f, 1.02f), K(100f, 0.97f) });

	private static readonly OpticDistanceCurves s_StandardAkCollimator = Make("быстрая вскидка, хуже на дальности",
		new[] { K(0f, 1.00f), K(25f, 1.00f), K(60f, 1.06f), K(100f, 1.10f) },
		new[] { K(0f, 0.90f), K(25f, 0.94f), K(60f, 1.04f), K(100f, 1.08f) });

	private static readonly OpticDistanceCurves s_AkSideRailOptic = Make("40–50 м, пересечение 60 м",
		new[] { K(0f, 1.06f), K(35f, 0.98f), K(50f, 0.86f), K(60f, 0.84f), K(100f, 0.94f) },
		new[] { K(0f, 1.22f), K(35f, 1.10f), K(50f, 1.00f), K(60f, 0.98f), K(100f, 1.04f) });

	// Named lookup must be initialized after all curve static fields above.
	private static readonly Dictionary<string, OpticDistanceCurves> s_NamedCurves = BuildNamedCurves();

	private static Dictionary<string, OpticDistanceCurves> BuildNamedCurves()
	{
		var map = new Dictionary<string, OpticDistanceCurves>
		{
			["Attachment_M4_Reddot1"] = s_CollimatorMod1,
			["Attachment_M4_Reddot3"] = s_CollimatorMod2,
			["Attachment_M4_RDC"] = s_CollimatorMod3,
			["Attachment_M4_Aimpoint"] = s_Optic2x,
			["Attachment_M4_Reddot2"] = s_HolographicMod1,
			["Attachment_M4_EOTech_G33"] = s_HybridSight,
			["Attachment_M4_Vortex_Razor"] = s_Optic1To6x,
			["Attachment_M4_ELCAN_SpecterDR"] = s_Optic1To4xMod1,
			["Attachment_M4_Scope1_3x"] = s_Optic3x,
			["Attachment_M4_ACOG"] = s_Optic4x,
			["Attachment_M4_SUSAT"] = s_Optic4xMod1,
			["Attachment_M4_ACOG_RMR"] = s_Optic3_5x,
			["Attachment_Mosin_Scope8"] = s_Optic3_5x,
			["Attachment_M4_Scope4"] = s_SniperScopeMod0,
			["Attachment_M4_Scope5"] = s_SniperScopeMod1,
			["Attachment_M4_Scope9"] = s_SniperScopeMod2,
			["Attachment_AK_Reddot4_Rail"] = s_StandardAkCollimator,
			["Attachment_AK_Scope11"] = s_AkSideRailOptic
		};
		return map;
	}
	#endregion

	#region Helpers
	private const float c_MinDistanceMeters = 0f;
	private const float c_MaxDistanceMeters = 100f;
	private const float c_MinMultiplier = 0.01f;

	private static float EvaluateKeyframes(IReadOnlyList<DistanceKeyframe> _keyframes, float _distanceMeters)
	{
		if (_keyframes == null || _keyframes.Count == 0)
			return 1f;

		float clampedDistance = Mathf.Clamp(_distanceMeters, c_MinDistanceMeters, c_MaxDistanceMeters);
		int count = _keyframes.Count;
		if (count == 1)
			return Mathf.Max(c_MinMultiplier, _keyframes[0].Value);

		if (clampedDistance <= _keyframes[0].DistanceMeters)
			return Mathf.Max(c_MinMultiplier, _keyframes[0].Value);
		if (clampedDistance >= _keyframes[count - 1].DistanceMeters)
			return Mathf.Max(c_MinMultiplier, _keyframes[count - 1].Value);

		for (int i = 0; i < count - 1; i++)
		{
			float t0 = _keyframes[i].DistanceMeters;
			float t1 = _keyframes[i + 1].DistanceMeters;
			if (t0 > clampedDistance || clampedDistance > t1)
				continue;

			if (Mathf.Approximately(t1, t0))
				return Mathf.Max(c_MinMultiplier, _keyframes[i].Value);

			float t = (clampedDistance - t0) / (t1 - t0);
			return Mathf.Max(
				c_MinMultiplier,
				Mathf.Lerp(_keyframes[i].Value, _keyframes[i + 1].Value, t));
		}

		return Mathf.Max(c_MinMultiplier, _keyframes[count - 1].Value);
	}

	private static DistanceKeyframe K(float _distanceMeters, float _value) =>
		new DistanceKeyframe(_distanceMeters, _value);

	private static OpticDistanceCurves Make(
		string _label,
		DistanceKeyframe[] _dispersion,
		DistanceKeyframe[] _aim) =>
		new OpticDistanceCurves(_label, _dispersion, _aim);
	#endregion
}

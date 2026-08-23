using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Дистанционные кривые точности для модулей на планке (ЛЦУ).
/// Меньше 1 = точнее. Кривые только по разбросу; скорость прицеливания — через <see cref="WeaponAttachmentDefinition.AimTimeModifier"/>.
/// </summary>
public static class RailAttachmentDistanceCurveLibrary
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

	public readonly struct RailDistanceCurves
	{
		public readonly DistanceKeyframe[] DispersionKeyframes;
		public readonly DistanceKeyframe[] AimTimeKeyframes;
		public readonly string SweetSpotLabel;
		public readonly bool IsImproved;

		public RailDistanceCurves(
			string _sweetSpotLabel,
			DistanceKeyframe[] _dispersionKeyframes,
			DistanceKeyframe[] _aimTimeKeyframes,
			bool _isImproved)
		{
			SweetSpotLabel = _sweetSpotLabel;
			DispersionKeyframes = _dispersionKeyframes;
			AimTimeKeyframes = _aimTimeKeyframes;
			IsImproved = _isImproved;
		}
	}
	#endregion

	#region Public Methods
	public static RailDistanceCurves GetCurvesForAttachment(WeaponAttachmentDefinition _attachment)
	{
		if (_attachment == null || _attachment.AttachmentType != WeaponAttachmentType.LaserDesignator)
			return s_FlatNeutral;

		string name = _attachment.name ?? string.Empty;
		if (s_NamedCurves.TryGetValue(name, out RailDistanceCurves named))
			return named;

		return s_FlatNeutral;
	}

	public static float EvaluateDispersionMultiplier(
		WeaponAttachmentDefinition _attachment,
		float _distanceMeters)
	{
		RailDistanceCurves curves = GetCurvesForAttachment(_attachment);
		return EvaluateKeyframes(curves.DispersionKeyframes, _distanceMeters);
	}

	public static float EvaluateAimTimeMultiplier(
		WeaponAttachmentDefinition _attachment,
		float _distanceMeters)
	{
		RailDistanceCurves curves = GetCurvesForAttachment(_attachment);
		return EvaluateKeyframes(curves.AimTimeKeyframes, _distanceMeters);
	}

	public static bool IsImprovedLaser(WeaponAttachmentDefinition _attachment)
	{
		if (_attachment == null)
			return false;
		if (_attachment.IsImprovedLaser)
			return true;
		return GetCurvesForAttachment(_attachment).IsImproved;
	}

	/// <summary>Peak PointAim spread. Named assets only; unknown lasers return 1.</summary>
	public static float EvaluatePointAimSpreadModifier(WeaponAttachmentDefinition _attachment)
	{
		if (!TryGetNamedCurves(_attachment, out RailDistanceCurves curves))
			return 1f;
		return curves.IsImproved ? 0.88f : 0.95f;
	}

	/// <summary>Peak PointAim aim-time. Named assets only.</summary>
	public static float EvaluatePointAimAimTimeModifier(WeaponAttachmentDefinition _attachment)
	{
		if (!TryGetNamedCurves(_attachment, out RailDistanceCurves curves))
			return 1f;
		return curves.IsImproved ? 0.90f : 0.95f;
	}

	/// <summary>Small Aiming acquisition (aim-time) bonus. Named assets only.</summary>
	public static float EvaluateAimingAcquisitionModifier(WeaponAttachmentDefinition _attachment)
	{
		if (!TryGetNamedCurves(_attachment, out RailDistanceCurves curves))
			return 1f;
		return curves.IsImproved ? 0.92f : 0.97f;
	}

	/// <summary>0..1 share of PointAim bonus remaining at distance (PLAN §42).</summary>
	public static float EvaluatePointAimEffect01(WeaponAttachmentDefinition _attachment, float _distanceMeters)
	{
		if (!TryGetNamedCurves(_attachment, out RailDistanceCurves curves))
			return EvaluateDefaultEffect01(_attachment != null && _attachment.IsImprovedLaser, _distanceMeters);

		return EvaluateDefaultEffect01(curves.IsImproved, _distanceMeters);
	}

	private static bool TryGetNamedCurves(WeaponAttachmentDefinition _attachment, out RailDistanceCurves _curves)
	{
		_curves = s_FlatNeutral;
		if (_attachment == null || _attachment.AttachmentType != WeaponAttachmentType.LaserDesignator)
			return false;
		string name = _attachment.name ?? string.Empty;
		if (!s_NamedCurves.TryGetValue(name, out _curves))
			return false;
		return true;
	}

	private static float EvaluateDefaultEffect01(bool _improved, float _distanceMeters)
	{
		DistanceKeyframe[] keys = _improved ? s_ImprovedEffect : s_BasicEffect;
		return EvaluateKeyframes(keys, _distanceMeters);
	}
	#endregion

	#region Curve Data
	private static readonly RailDistanceCurves s_FlatNeutral = Make("нейтральный",
		new[] { K(0f, 1f), K(150f, 1f) },
		new[] { K(0f, 1f), K(150f, 1f) },
		false);

	/// <summary>Improved / compact ЛЦУ: бонус PointAim дальше, лёгкий acquisition для Aiming.</summary>
	private static readonly RailDistanceCurves s_CompactLaser = Make("0–150 м",
		new[] { K(0f, 0.88f), K(75f, 0.90f), K(150f, 0.94f) },
		new[] { K(0f, 0.94f), K(100f, 0.96f), K(150f, 1.00f) },
		true);

	/// <summary>Basic / тактический ЛЦУ: бонус PointAim 0–50 м.</summary>
	private static readonly RailDistanceCurves s_TacticalLaser = Make("0–50 м",
		new[] { K(0f, 0.90f), K(50f, 0.92f), K(100f, 1.00f), K(150f, 1.00f) },
		new[] { K(0f, 0.98f), K(75f, 1.00f), K(150f, 1.00f) },
		false);

	private static readonly DistanceKeyframe[] s_BasicEffect =
	{
		K(0f, 1f), K(10f, 1f), K(20f, 0.90f), K(30f, 0.70f), K(50f, 0.40f), K(100f, 0.10f), K(150f, 0f)
	};

	private static readonly DistanceKeyframe[] s_ImprovedEffect =
	{
		K(0f, 1f), K(10f, 1f), K(20f, 0.95f), K(30f, 0.85f), K(50f, 0.60f), K(100f, 0.25f), K(150f, 0.05f)
	};

	private static readonly Dictionary<string, RailDistanceCurves> s_NamedCurves = BuildNamedCurves();

	private static Dictionary<string, RailDistanceCurves> BuildNamedCurves()
	{
		return new Dictionary<string, RailDistanceCurves>
		{
			["Attachment_M4_Laser2"] = s_CompactLaser,
			["Attachment_M4_Laser1"] = s_TacticalLaser
		};
	}
	#endregion

	#region Helpers
	private const float c_MinDistanceMeters = 0f;
	private const float c_MaxDistanceMeters = 150f;
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

	private static RailDistanceCurves Make(
		string _label,
		DistanceKeyframe[] _dispersion,
		DistanceKeyframe[] _aimTime,
		bool _improved) =>
		new RailDistanceCurves(_label, _dispersion, _aimTime, _improved);
	#endregion
}

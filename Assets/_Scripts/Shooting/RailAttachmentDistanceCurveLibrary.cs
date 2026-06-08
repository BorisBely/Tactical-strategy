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
		public readonly string SweetSpotLabel;

		public RailDistanceCurves(string _sweetSpotLabel, DistanceKeyframe[] _dispersionKeyframes)
		{
			SweetSpotLabel = _sweetSpotLabel;
			DispersionKeyframes = _dispersionKeyframes;
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
	#endregion

	#region Curve Data
	private static readonly RailDistanceCurves s_FlatNeutral = Make("нейтральный",
		new[] { K(0f, 1f), K(100f, 1f) });

	/// <summary>Компактный ЛЦУ: лёгкий бонус точности до 15 м.</summary>
	private static readonly RailDistanceCurves s_CompactLaser = Make("0–15 м",
		new[] { K(0f, 0.92f), K(15f, 0.94f), K(25f, 1.00f), K(100f, 1.00f) });

	/// <summary>Тактический ЛЦУ: лёгкий бонус точности до 25 м.</summary>
	private static readonly RailDistanceCurves s_TacticalLaser = Make("0–25 м",
		new[] { K(0f, 0.90f), K(15f, 0.88f), K(25f, 0.92f), K(35f, 1.00f), K(100f, 1.00f) });

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

	private static RailDistanceCurves Make(string _label, DistanceKeyframe[] _dispersion) =>
		new RailDistanceCurves(_label, _dispersion);
	#endregion
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// B12 / N7: canonical degree-based recoil balance sheet from live assets + PredictOffset MATH.
/// Replaces legacy P-sheet for calibration. Does not modify assets.
/// </summary>
public static class RecoilBalanceSheetN7Runner
{
	#region Constants
	private static readonly string[] s_ReferenceWeaponOrder =
	{
		"Weapon_M4_ModA_1",
		"Weapon_AK47",
		"Weapon_AK74",
		"Weapon_M249",
		"Weapon_PKM",
		"Weapon_MK12",
		"Weapon_SVD",
		"Weapon_BenelliM4",
		"Weapon_M2Browning_127",
		"Weapon_MK19"
	};
	#endregion

	#region Nested Types
	public struct Row
	{
		public string WeaponName;
		public WeaponFireMode BaselineMode;
		public float Vertical;
		public float Horizontal;
		public float Recovery;
		public float SemiMultiplier;
		public float AutoMultiplier;
		public float Offset3;
		public float Offset5;
		public float Offset8;
		public float Offset10;
		public float Pause020;
		public float Pause040;
		public float Pause080;
		public float NetDriftPerShot;
	}
	#endregion

	#region Public Methods
	public static string Build(IReadOnlyList<WeaponDefinition> _weapons)
	{
		var rows = new List<Row>(_weapons != null ? _weapons.Count : 0);
		if (_weapons != null)
		{
			for (int i = 0; i < _weapons.Count; i++)
			{
				if (_weapons[i] != null)
					rows.Add(BuildRow(_weapons[i]));
			}
		}

		rows.Sort(CompareRows);

		var sb = new StringBuilder(16384);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilBalanceSheet N7 (degrees)");
		sb.AppendLine("Canonical balance table. Legacy RecoilPerShot / P units — do not calibrate from old sheet.");
		sb.AppendLine("Baseline: ResolveBaselineFireMode, Aiming stand, no attachments, InstanceHash=0.");
		sb.AppendLine("Offset N = |Offset| after N shots (inter-shot recovery ×0.7). NetDrift = (|Off|10 − |Off|5) / 5.");
		sb.AppendLine();
		sb.AppendLine(
			"Weapon\tMode\tV°\tH°\tRec°/s\tSemi×\tAuto×\tOff3\tOff5\tOff8\tOff10\tp0.2\tp0.4\tp0.8\tNetDrift");
		for (int i = 0; i < rows.Count; i++)
		{
			Row row = rows[i];
			AppendRowLine(sb, in row, culture);
		}
		sb.AppendLine();
		sb.AppendLine("Reference block (B0–B8 frozen anchors):");
		AppendReferenceNote(sb, rows, "Weapon_M4_ModA_1", "B0 M4 anchor Off5=0.313°", culture);
		AppendReferenceNote(sb, rows, "Weapon_M249", "B8 LMG M249 Off5=0.254°", culture);
		AppendReferenceNote(sb, rows, "Weapon_PKM", "B8 LMG PKM Off5=0.296°", culture);
		sb.AppendLine("Rifle class @5: M4 < AK-74 < AK-47 (B11). Semi×/Auto× frozen B9/B10.");
		return sb.ToString();
	}

	public static Row BuildRow(WeaponDefinition _weapon)
	{
		WeaponFireMode fireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(_weapon);
		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_weapon,
			fireMode,
			WeaponPoseState.Aiming,
			RecoilPlayBaselineProtocol.StandingKickMultiplier,
			RecoilPlayBaselineProtocol.StandingRecoveryMultiplier);

		float off3 = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in context, 3);
		float off5 = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in context, 5);
		float off8 = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in context, 8);
		float off10 = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in context, 10);

		return new Row
		{
			WeaponName = _weapon.name,
			BaselineMode = fireMode,
			Vertical = _weapon.VerticalRecoil,
			Horizontal = _weapon.HorizontalRecoil,
			Recovery = _weapon.RecoilRecoveryPerSecond,
			SemiMultiplier = _weapon.SemiAutoRecoilMultiplier,
			AutoMultiplier = _weapon.AutoRecoilMultiplier,
			Offset3 = off3,
			Offset5 = off5,
			Offset8 = off8,
			Offset10 = off10,
			Pause020 = WeaponRecoilMath.PredictOffsetMagnitudeAfterBurstAndPause(in context, 5, 0.2f),
			Pause040 = WeaponRecoilMath.PredictOffsetMagnitudeAfterBurstAndPause(in context, 5, 0.4f),
			Pause080 = WeaponRecoilMath.PredictOffsetMagnitudeAfterBurstAndPause(in context, 5, 0.8f),
			NetDriftPerShot = (off10 - off5) / 5f
		};
	}
	#endregion

	#region Private Methods
	private static int CompareRows(Row _a, Row _b)
	{
		int orderA = IndexOfReference(_a.WeaponName);
		int orderB = IndexOfReference(_b.WeaponName);
		if (orderA >= 0 || orderB >= 0)
		{
			if (orderA < 0)
				return 1;
			if (orderB < 0)
				return -1;
			return orderA.CompareTo(orderB);
		}

		return string.CompareOrdinal(_a.WeaponName, _b.WeaponName);
	}

	private static int IndexOfReference(string _name)
	{
		for (int i = 0; i < s_ReferenceWeaponOrder.Length; i++)
		{
			if (s_ReferenceWeaponOrder[i] == _name)
				return i;
		}

		return -1;
	}

	private static void AppendRowLine(StringBuilder _sb, in Row _row, CultureInfo _culture)
	{
		_sb.Append(_row.WeaponName);
		_sb.Append('\t');
		_sb.Append(_row.BaselineMode);
		_sb.Append('\t');
		_sb.Append(_row.Vertical.ToString("F3", _culture));
		_sb.Append('\t');
		_sb.Append(_row.Horizontal.ToString("F3", _culture));
		_sb.Append('\t');
		_sb.Append(_row.Recovery.ToString("F2", _culture));
		_sb.Append('\t');
		_sb.Append(_row.SemiMultiplier.ToString("F2", _culture));
		_sb.Append('\t');
		_sb.Append(_row.AutoMultiplier.ToString("F2", _culture));
		_sb.Append('\t');
		_sb.Append(_row.Offset3.ToString("F3", _culture));
		_sb.Append('\t');
		_sb.Append(_row.Offset5.ToString("F3", _culture));
		_sb.Append('\t');
		_sb.Append(_row.Offset8.ToString("F3", _culture));
		_sb.Append('\t');
		_sb.Append(_row.Offset10.ToString("F3", _culture));
		_sb.Append('\t');
		_sb.Append(_row.Pause020.ToString("F3", _culture));
		_sb.Append('\t');
		_sb.Append(_row.Pause040.ToString("F3", _culture));
		_sb.Append('\t');
		_sb.Append(_row.Pause080.ToString("F3", _culture));
		_sb.Append('\t');
		_sb.AppendLine(_row.NetDriftPerShot.ToString("F4", _culture));
	}

	private static void AppendReferenceNote(
		StringBuilder _sb,
		List<Row> _rows,
		string _weaponName,
		string _label,
		CultureInfo _culture)
	{
		for (int i = 0; i < _rows.Count; i++)
		{
			if (_rows[i].WeaponName != _weaponName)
				continue;
			_sb.AppendLine(
				"  " + _label + " → Off5=" + _rows[i].Offset5.ToString("F3", _culture) + "°");
			return;
		}
	}
	#endregion
}

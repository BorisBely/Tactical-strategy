using System;
using UnityEngine;

/// <summary>One reproducible balance matrix node (Phase G).</summary>
public readonly struct WeaponBalanceCase : IEquatable<WeaponBalanceCase>
{
	public readonly WeaponDefinition Weapon;
	public readonly WeaponClassType WeaponClass;
	public readonly WeaponFireMode FireMode;
	public readonly AmmoDefinition Ammo;
	public readonly WeaponAttachmentDefinition[] Attachments;
	public readonly string LoadoutLabel;
	public readonly WeaponPoseState Pose;
	public readonly WeaponBalanceStance Stance;
	public readonly WeaponBalanceMovement Movement;
	public readonly float DistanceMeters;
	public readonly float RecoilControlSkill;
	public readonly bool IsTurret;
	public readonly int CaseId;

	public WeaponBalanceCase(
		WeaponDefinition _weapon,
		WeaponFireMode _fireMode,
		AmmoDefinition _ammo,
		WeaponAttachmentDefinition[] _attachments,
		string _loadoutLabel,
		WeaponPoseState _pose,
		WeaponBalanceStance _stance,
		WeaponBalanceMovement _movement,
		float _distanceMeters,
		float _recoilControlSkill,
		bool _isTurret)
	{
		Weapon = _weapon;
		WeaponClass = _weapon != null ? _weapon.WeaponClass : WeaponClassType.Unknown;
		FireMode = _fireMode;
		Ammo = _ammo;
		Attachments = _attachments;
		LoadoutLabel = _loadoutLabel ?? "Base";
		Pose = _pose;
		Stance = _stance;
		Movement = _movement;
		DistanceMeters = _distanceMeters;
		RecoilControlSkill = _recoilControlSkill;
		IsTurret = _isTurret;
		CaseId = ComputeCaseId(
			_weapon,
			_fireMode,
			_ammo,
			_attachments,
			_pose,
			_stance,
			_movement,
			_distanceMeters,
			_recoilControlSkill,
			_isTurret);
	}

	public bool Equals(WeaponBalanceCase _other)
	{
		return CaseId == _other.CaseId;
	}

	public override bool Equals(object _obj)
	{
		return _obj is WeaponBalanceCase other && Equals(other);
	}

	public override int GetHashCode()
	{
		return CaseId;
	}

	public static int ComputeCaseId(in WeaponBalanceCase _case)
	{
		return ComputeCaseId(
			_case.Weapon,
			_case.FireMode,
			_case.Ammo,
			_case.Attachments,
			_case.Pose,
			_case.Stance,
			_case.Movement,
			_case.DistanceMeters,
			_case.RecoilControlSkill,
			_case.IsTurret);
	}

	private static int ComputeCaseId(
		WeaponDefinition _weapon,
		WeaponFireMode _fireMode,
		AmmoDefinition _ammo,
		WeaponAttachmentDefinition[] _attachments,
		WeaponPoseState _pose,
		WeaponBalanceStance _stance,
		WeaponBalanceMovement _movement,
		float _distanceMeters,
		float _recoilControlSkill,
		bool _isTurret)
	{
		unchecked
		{
			int hash = 17;
			hash = hash * 31 + (_weapon != null ? _weapon.name.GetHashCode() : 0);
			hash = hash * 31 + (int)_fireMode;
			hash = hash * 31 + (_ammo != null ? _ammo.name.GetHashCode() : 0);
			hash = hash * 31 + HashAttachments(_attachments);
			hash = hash * 31 + (int)_pose;
			hash = hash * 31 + (int)_stance;
			hash = hash * 31 + (int)_movement;
			hash = hash * 31 + Mathf.RoundToInt(_distanceMeters * 100f);
			hash = hash * 31 + Mathf.RoundToInt(_recoilControlSkill);
			hash = hash * 31 + (_isTurret ? 1 : 0);
			return hash;
		}
	}

	private static int HashAttachments(WeaponAttachmentDefinition[] _attachments)
	{
		if (_attachments == null || _attachments.Length == 0)
			return 0;
		unchecked
		{
			int hash = 19;
			for (int i = 0; i < _attachments.Length; i++)
			{
				WeaponAttachmentDefinition attachment = _attachments[i];
				hash = hash * 31 + (attachment != null ? attachment.name.GetHashCode() : 0);
			}

			return hash;
		}
	}
}

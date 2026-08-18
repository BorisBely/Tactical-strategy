using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Source of truth for weapon local poses (Standing/Crouch/Vehicle × LowReady/HipFire/HipFireWalk/HipFireCrouchWalk/PointAim/Aiming/NotReady/NotReadyPatrol/HighReady).
/// PreAim is derived at runtime (LowReady→Aiming), not stored.
/// Runtime builds a cache; editors mutate <see cref="m_Poses"/> only.
/// </summary>
[CreateAssetMenu(
	fileName = "WeaponPoseDefinition",
	menuName = "Polygone/Weapons/Weapon Pose Definition",
	order = 10)]
public sealed class WeaponPoseDefinition : ScriptableObject
{
	[SerializeField] private List<WeaponPoseEntry> m_Poses = new List<WeaponPoseEntry>();

	private Dictionary<WeaponPoseKey, WeaponPoseEntry> m_Cache;
	private bool m_CacheDirty = true;

	public IReadOnlyList<WeaponPoseEntry> Poses => m_Poses;

	private void OnEnable()
	{
		m_CacheDirty = true;
		EnsureSeededPoseSlots();
	}

	private void OnValidate()
	{
		m_CacheDirty = true;
		EnsureSeededPoseSlots();
	}

	public void InvalidateCache() => m_CacheDirty = true;

	public bool TryGetPose(WeaponStance _stance, WeaponPoseState _pose, out WeaponPoseEntry _entry)
	{
		EnsureCache();
		return m_Cache.TryGetValue(new WeaponPoseKey(_stance, _pose), out _entry);
	}

	public bool TryGetPose(WeaponPoseKey _key, out WeaponPoseEntry _entry)
	{
		EnsureCache();
		return m_Cache.TryGetValue(_key, out _entry);
	}

	/// <summary>Blended from→to for a stance. Falls back Standing when stance entry missing.</summary>
	public void GetBlended(
		WeaponStance _stance,
		WeaponPoseState _from,
		WeaponPoseState _to,
		float _blend01,
		out Vector3 _position,
		out Quaternion _rotation)
	{
		ResolveLocalPose(_stance, _from, out Vector3 fromPos, out Quaternion fromRot);
		ResolveLocalPose(_stance, _to, out Vector3 toPos, out Quaternion toRot);

		float t = Mathf.Clamp01(_blend01);
		_position = Vector3.Lerp(fromPos, toPos, t);
		_rotation = Quaternion.Slerp(fromRot, toRot, t);
	}

	/// <summary>PreAim is always derived (LowReady→Aiming). HighReady and others use authored slots.</summary>
	public void ResolveLocalPose(
		WeaponStance _stance,
		WeaponPoseState _pose,
		out Vector3 _position,
		out Quaternion _rotation)
	{
		if (_pose == WeaponPoseState.PreAim)
		{
			GetPoseOrFallback(_stance, WeaponPoseState.LowReady, out WeaponPoseEntry low);
			GetPoseOrFallback(_stance, WeaponPoseState.Aiming, out WeaponPoseEntry aim);
			PreAimPoseUtility.BlendLocal(
				low.Position,
				low.Rotation,
				aim.Position,
				aim.Rotation,
				PreAimPoseUtility.WeaponBlend,
				out _position,
				out _rotation);
			return;
		}

		GetPoseOrFallback(_stance, _pose, out WeaponPoseEntry entry);
		_position = entry.Position;
		_rotation = entry.Rotation;
	}

	/// <summary>Legacy binary blend LowReady→PointAim (old NotReady→Ready).</summary>
	public void GetBlended(
		WeaponStance _stance,
		float _readyBlend01,
		out Vector3 _position,
		out Quaternion _rotation)
	{
		GetBlended(_stance, WeaponPoseState.LowReady, WeaponPoseState.PointAim, _readyBlend01, out _position, out _rotation);
	}

	public void GetPoseOrFallback(
		WeaponStance _stance,
		WeaponPoseState _pose,
		out WeaponPoseEntry _entry)
	{
		if (TryGetPose(_stance, _pose, out _entry) && _entry != null)
			return;

		// Same-stance relatives before copying Standing — otherwise missing Vehicle HighReady
		// used standing HighReady and looked broken in the seat.
		if (TrySameStanceRelative(_stance, _pose, out _entry))
			return;

		if (_stance != WeaponStance.Standing && TryGetPose(WeaponStance.Standing, _pose, out _entry) && _entry != null)
			return;

		if (_stance != WeaponStance.Standing && TrySameStanceRelative(WeaponStance.Standing, _pose, out _entry))
			return;

		if (TryGetPose(WeaponStance.Standing, WeaponPoseState.LowReady, out _entry) && _entry != null)
			return;

		_entry = new WeaponPoseEntry
		{
			Stance = _stance,
			PoseState = _pose,
			Position = Vector3.zero,
			EulerAngles = Vector3.zero,
		};
	}

	private bool TrySameStanceRelative(WeaponStance _stance, WeaponPoseState _pose, out WeaponPoseEntry _entry)
	{
		_entry = null;
		switch (_pose)
		{
			case WeaponPoseState.HipFire:
				return TryGetPose(_stance, WeaponPoseState.LowReady, out _entry) && _entry != null;
			case WeaponPoseState.HipFireWalk:
				return (TryGetPose(_stance, WeaponPoseState.HipFire, out _entry) && _entry != null)
				       || (TryGetPose(_stance, WeaponPoseState.LowReady, out _entry) && _entry != null);
			case WeaponPoseState.HipFireCrouchWalk:
				return (TryGetPose(_stance, WeaponPoseState.HipFire, out _entry) && _entry != null)
				       || (TryGetPose(_stance, WeaponPoseState.HipFireWalk, out _entry) && _entry != null)
				       || (TryGetPose(_stance, WeaponPoseState.LowReady, out _entry) && _entry != null);
			case WeaponPoseState.Aiming:
				return TryGetPose(_stance, WeaponPoseState.PointAim, out _entry) && _entry != null;
			case WeaponPoseState.HighReady:
				return (TryGetPose(_stance, WeaponPoseState.Aiming, out _entry) && _entry != null)
				       || (TryGetPose(_stance, WeaponPoseState.PointAim, out _entry) && _entry != null);
			case WeaponPoseState.NotReadyPatrol:
				return (TryGetPose(_stance, WeaponPoseState.NotReady, out _entry) && _entry != null)
				       || (TryGetPose(_stance, WeaponPoseState.LowReady, out _entry) && _entry != null);
			case WeaponPoseState.NotReady:
				return TryGetPose(_stance, WeaponPoseState.LowReady, out _entry) && _entry != null;
			default:
				return false;
		}
	}

	public void SetOrAddPose(WeaponStance _stance, WeaponPoseState _pose, Vector3 _position, Vector3 _euler)
	{
		for (int i = 0; i < m_Poses.Count; i++)
		{
			WeaponPoseEntry e = m_Poses[i];
			if (e == null)
				continue;
			if (e.Stance != _stance || e.PoseState != _pose)
				continue;
			e.Position = _position;
			e.EulerAngles = _euler;
			m_CacheDirty = true;
			return;
		}

		m_Poses.Add(new WeaponPoseEntry
		{
			Stance = _stance,
			PoseState = _pose,
			Position = _position,
			EulerAngles = _euler,
		});
		m_CacheDirty = true;
	}

	/// <summary>Fill slots from flat ItemDefinition-style vectors (migration).</summary>
	public void ImportFromFlatFields(
		Vector3 _standNotReadyPos,
		Vector3 _standNotReadyEu,
		Vector3 _standReadyPos,
		Vector3 _standReadyEu,
		Vector3 _crouchNotReadyPos,
		Vector3 _crouchNotReadyEu,
		Vector3 _crouchReadyPos,
		Vector3 _crouchReadyEu,
		Vector3 _vehicleNotReadyPos,
		Vector3 _vehicleNotReadyEu,
		Vector3 _vehicleReadyPos,
		Vector3 _vehicleReadyEu)
	{
		m_Poses.Clear();
		SetOrAddPose(WeaponStance.Standing, WeaponPoseState.LowReady, _standNotReadyPos, _standNotReadyEu);
		SetOrAddPose(WeaponStance.Standing, WeaponPoseState.PointAim, _standReadyPos, _standReadyEu);

		Vector3 cNrP = IsZero(_crouchNotReadyPos) && IsZero(_crouchNotReadyEu) ? _standNotReadyPos : _crouchNotReadyPos;
		Vector3 cNrE = IsZero(_crouchNotReadyPos) && IsZero(_crouchNotReadyEu) ? _standNotReadyEu : _crouchNotReadyEu;
		Vector3 cRdP = IsZero(_crouchReadyPos) && IsZero(_crouchReadyEu) ? _standReadyPos : _crouchReadyPos;
		Vector3 cRdE = IsZero(_crouchReadyPos) && IsZero(_crouchReadyEu) ? _standReadyEu : _crouchReadyEu;
		SetOrAddPose(WeaponStance.Crouching, WeaponPoseState.LowReady, cNrP, cNrE);
		SetOrAddPose(WeaponStance.Crouching, WeaponPoseState.PointAim, cRdP, cRdE);

		Vector3 vNrP = IsZero(_vehicleNotReadyPos) && IsZero(_vehicleNotReadyEu) ? _standNotReadyPos : _vehicleNotReadyPos;
		Vector3 vNrE = IsZero(_vehicleNotReadyPos) && IsZero(_vehicleNotReadyEu) ? _standNotReadyEu : _vehicleNotReadyEu;
		Vector3 vRdP = IsZero(_vehicleReadyPos) && IsZero(_vehicleReadyEu) ? _standReadyPos : _vehicleReadyPos;
		Vector3 vRdE = IsZero(_vehicleReadyPos) && IsZero(_vehicleReadyEu) ? _standReadyEu : _vehicleReadyEu;
		SetOrAddPose(WeaponStance.Vehicle, WeaponPoseState.LowReady, vNrP, vNrE);
		SetOrAddPose(WeaponStance.Vehicle, WeaponPoseState.PointAim, vRdP, vRdE);

		EnsureSeededPoseSlots();
	}

	/// <summary>Seed HipFire / HipFireWalk / Aiming / NotReady / HighReady / NotReadyPatrol when those slots are missing.</summary>
	public void EnsureSeededPoseSlots()
	{
		WeaponStance[] stances = { WeaponStance.Standing, WeaponStance.Crouching, WeaponStance.Vehicle };
		for (int s = 0; s < stances.Length; s++)
		{
			WeaponStance stance = stances[s];
			bool hasLow = TryGetPoseExact(stance, WeaponPoseState.LowReady, out WeaponPoseEntry low);
			bool hasPoint = TryGetPoseExact(stance, WeaponPoseState.PointAim, out WeaponPoseEntry point);
			if (!hasLow && !hasPoint)
				continue;

			if (!TryGetPoseExact(stance, WeaponPoseState.HipFire, out _))
			{
				if (hasLow && hasPoint)
				{
					SetOrAddPose(
						stance,
						WeaponPoseState.HipFire,
						Vector3.Lerp(low.Position, point.Position, 0.35f),
						LerpEuler(low.EulerAngles, point.EulerAngles, 0.35f));
				}
				else if (hasLow)
				{
					SetOrAddPose(stance, WeaponPoseState.HipFire, low.Position, low.EulerAngles);
				}
			}

			if (!TryGetPoseExact(stance, WeaponPoseState.Aiming, out _) && hasPoint)
				SetOrAddPose(stance, WeaponPoseState.Aiming, point.Position, point.EulerAngles);

			if (!TryGetPoseExact(stance, WeaponPoseState.NotReady, out _) && hasLow)
				SetOrAddPose(stance, WeaponPoseState.NotReady, low.Position, low.EulerAngles);

			if (!TryGetPoseExact(stance, WeaponPoseState.HighReady, out _))
			{
				if (TryGetPoseExact(stance, WeaponPoseState.Aiming, out WeaponPoseEntry aimHr))
					SetOrAddPose(stance, WeaponPoseState.HighReady, aimHr.Position, aimHr.EulerAngles);
				else if (hasPoint)
					SetOrAddPose(stance, WeaponPoseState.HighReady, point.Position, point.EulerAngles);
			}

			if (!TryGetPoseExact(stance, WeaponPoseState.NotReadyPatrol, out _))
			{
				if (TryGetPoseExact(stance, WeaponPoseState.NotReady, out WeaponPoseEntry hold))
					SetOrAddPose(stance, WeaponPoseState.NotReadyPatrol, hold.Position, hold.EulerAngles);
				else if (hasLow)
					SetOrAddPose(stance, WeaponPoseState.NotReadyPatrol, low.Position, low.EulerAngles);
			}

			if (!TryGetPoseExact(stance, WeaponPoseState.HipFireWalk, out _)
			    && TryGetPoseExact(stance, WeaponPoseState.HipFire, out WeaponPoseEntry hipWalkSeed))
			{
				SetOrAddPose(
					stance,
					WeaponPoseState.HipFireWalk,
					hipWalkSeed.Position,
					hipWalkSeed.EulerAngles);
			}

			if (!TryGetPoseExact(stance, WeaponPoseState.HipFireCrouchWalk, out _)
			    && TryGetPoseExact(stance, WeaponPoseState.HipFire, out WeaponPoseEntry hipCrouchWalkSeed))
			{
				SetOrAddPose(
					stance,
					WeaponPoseState.HipFireCrouchWalk,
					hipCrouchWalkSeed.Position,
					hipCrouchWalkSeed.EulerAngles);
			}
		}
	}

	private bool TryGetPoseExact(WeaponStance _stance, WeaponPoseState _pose, out WeaponPoseEntry _entry)
	{
		EnsureCache();
		return m_Cache.TryGetValue(new WeaponPoseKey(_stance, _pose), out _entry) && _entry != null;
	}

	private void EnsureCache()
	{
		if (!m_CacheDirty && m_Cache != null)
			return;

		m_Cache = new Dictionary<WeaponPoseKey, WeaponPoseEntry>(16);
		for (int i = 0; i < m_Poses.Count; i++)
		{
			WeaponPoseEntry e = m_Poses[i];
			if (e == null)
				continue;
			m_Cache[e.Key] = e;
		}

		m_CacheDirty = false;
	}

	private static bool IsZero(Vector3 _v) => _v == Vector3.zero;

	private static Vector3 LerpEuler(Vector3 _a, Vector3 _b, float _t)
	{
		Quaternion q = Quaternion.Slerp(Quaternion.Euler(_a), Quaternion.Euler(_b), _t);
		return q.eulerAngles;
	}
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Веса зон и точки прицеливания для <see cref="UnitVision"/> по <see cref="UnitBodyHitZone"/>.
/// </summary>
public static class UnitBodyHitZoneVisionUtility
{
	public readonly struct VisionAimCandidate
	{
		public readonly Vector3 Point;
		public readonly float Weight;

		public VisionAimCandidate(Vector3 _point, float _weight)
		{
			Point = _point;
			Weight = _weight;
		}
	}

	public static float GetZonePriorityWeight(BodyPartType _bodyPart)
	{
		// Порядок прицела среди видимых зон: грудь → голова → живот → шея → руки → ноги.
		switch (_bodyPart)
		{
			case BodyPartType.Chest:
				return 120f;
			case BodyPartType.Head:
				return 110f;
			case BodyPartType.Abdomen:
				return 100f;
			case BodyPartType.Neck:
				return 90f;
			case BodyPartType.LeftArm:
			case BodyPartType.RightArm:
				return 65f;
			case BodyPartType.LeftLeg:
			case BodyPartType.RightLeg:
				return 45f;
			default:
				return 50f;
		}
	}

	public static bool TryGetCombinedBounds(IReadOnlyList<UnitBodyHitZone> _zones, out Bounds _bounds)
	{
		_bounds = default;
		bool hasBounds = false;

		for (int i = 0; i < _zones.Count; i++)
		{
			UnitBodyHitZone zone = _zones[i];
			if (zone == null || !zone.TryGetComponent(out Collider col) || !col.enabled)
				continue;

			if (!hasBounds)
			{
				_bounds = col.bounds;
				hasBounds = true;
			}
			else
			{
				_bounds.Encapsulate(col.bounds);
			}
		}

		return hasBounds;
	}

	public static Collider TryGetPreferredCollider(IReadOnlyList<UnitBodyHitZone> _zones, BodyPartType _preferredPart)
	{
		for (int i = 0; i < _zones.Count; i++)
		{
			UnitBodyHitZone zone = _zones[i];
			if (zone == null || zone.BodyPart != _preferredPart || !zone.TryGetComponent(out Collider col) || !col.enabled)
				continue;

			return col;
		}

		return null;
	}

	public static Collider TryGetFirstCollider(IReadOnlyList<UnitBodyHitZone> _zones)
	{
		for (int i = 0; i < _zones.Count; i++)
		{
			UnitBodyHitZone zone = _zones[i];
			if (zone != null && zone.TryGetComponent(out Collider col) && col.enabled)
				return col;
		}

		return null;
	}

	public static void BuildAimCandidates(BodyPartType _bodyPart, Collider _collider, List<VisionAimCandidate> _out)
	{
		_out.Clear();
		if (_collider == null)
			return;

		Bounds b = _collider.bounds;
		Vector3 c = b.center;
		Vector3 e = b.extents;
		float zoneWeight = GetZonePriorityWeight(_bodyPart);

		switch (_bodyPart)
		{
			case BodyPartType.Head:
				_out.Add(new VisionAimCandidate(c, zoneWeight));
				_out.Add(new VisionAimCandidate(new Vector3(c.x, c.y + e.y * 0.55f, c.z), zoneWeight * 0.92f));
				_out.Add(new VisionAimCandidate(new Vector3(c.x, c.y + e.y * 0.15f, c.z + e.z * 0.65f), zoneWeight * 0.78f));
				break;

			case BodyPartType.Neck:
				_out.Add(new VisionAimCandidate(c, zoneWeight));
				_out.Add(new VisionAimCandidate(new Vector3(c.x, c.y + e.y * 0.35f, c.z), zoneWeight * 0.85f));
				break;

			case BodyPartType.Chest:
			case BodyPartType.Abdomen:
				_out.Add(new VisionAimCandidate(c, zoneWeight));
				_out.Add(new VisionAimCandidate(new Vector3(c.x, c.y + e.y * 0.35f, c.z), zoneWeight * 0.88f));
				_out.Add(new VisionAimCandidate(new Vector3(c.x, c.y - e.y * 0.25f, c.z), zoneWeight * 0.72f));
				_out.Add(new VisionAimCandidate(new Vector3(c.x + e.x * 0.75f, c.y, c.z), zoneWeight * 0.55f));
				_out.Add(new VisionAimCandidate(new Vector3(c.x - e.x * 0.75f, c.y, c.z), zoneWeight * 0.55f));
				break;

			case BodyPartType.LeftArm:
			case BodyPartType.RightArm:
				_out.Add(new VisionAimCandidate(c, zoneWeight));
				_out.Add(new VisionAimCandidate(new Vector3(c.x, c.y - e.y * 0.55f, c.z), zoneWeight * 0.82f));
				_out.Add(new VisionAimCandidate(new Vector3(c.x, c.y + e.y * 0.2f, c.z), zoneWeight * 0.7f));
				break;

			case BodyPartType.LeftLeg:
			case BodyPartType.RightLeg:
				_out.Add(new VisionAimCandidate(c, zoneWeight));
				_out.Add(new VisionAimCandidate(new Vector3(c.x, c.y - e.y * 0.55f, c.z), zoneWeight * 0.8f));
				break;

			default:
				_out.Add(new VisionAimCandidate(c, zoneWeight));
				_out.Add(new VisionAimCandidate(new Vector3(c.x, c.y + e.y * 0.35f, c.z), zoneWeight * 0.75f));
				break;
		}
	}
}

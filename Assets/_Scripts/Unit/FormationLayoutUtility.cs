using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Генерация локальных слотов формации и назначение юнитов по текущим позициям.
/// Локальные координаты: X — вправо относительно фронта, Z — вперёд к цели.
/// </summary>
public static class FormationLayoutUtility
{
	#region Constants
	private const float c_CenterBandSpacingFraction = 0.34f;
	private const float c_MinCenterBandRadiusMeters = 0.45f;
	private const float c_FlankYawOffsetDegrees = 38f;
	private const float c_MaxFlankYawOffsetDegrees = 52f;
	private const float c_DoubleFileFlankYawDegrees = 40f;
	private const float c_TacticalColumnFlankYawDegrees = 42f;
	private const float c_WideReconScoutYawDegrees = 30f;
	private const float c_WideReconRearFlankMinYawDegrees = 45f;
	private const float c_WideReconScoutForwardDepthFraction = 0.25f;
	private const float c_WideReconFrontToCenterDepthFraction = 0.60f;
	private const float c_WideReconFrontToBackDepthFraction = 1.10f;
	private const float c_WideReconBackRowDepthStepFraction = 0.95f;
	private const float c_WideReconFrontRearDepthGapMultiplier = 2f;
	private const float c_DiamondSmallCountThreshold = 5;

	/// <summary>Временно: индивидуальные сектора слотов (live sector + per-slot прибытие/превью).</summary>
	public static bool IndividualSlotSectorsEnabled = false;

	/// <summary>Порядок X / X+1..7: частые → специализированные. Не совпадает с numeric enum.</summary>
	private static readonly FormationType[] s_FormationHotkeyOrder =
	{
		FormationType.TacticalColumn,
		FormationType.DoubleFile,
		FormationType.SingleFile,
		FormationType.Wedge,
		FormationType.Line,
		FormationType.Diamond,
		FormationType.WideReconWedge,
	};
	#endregion

	#region Public Types
	public readonly struct FormationSlotLayout
	{
		public FormationSlotLayout(Vector3 _localOffset, float _facingAngleDegrees, int _slotIndex)
		{
			LocalOffset = _localOffset;
			FacingAngleDegrees = _facingAngleDegrees;
			SlotIndex = _slotIndex;
		}

		public Vector3 LocalOffset { get; }
		public float FacingAngleDegrees { get; }
		public int SlotIndex { get; }
	}

	public readonly struct FormationBuildResult
	{
		public FormationBuildResult(List<Vector3> _offsets, List<float> _facingAngles)
		{
			Offsets = _offsets;
			FacingAngles = _facingAngles;
		}

		public List<Vector3> Offsets { get; }
		public List<float> FacingAngles { get; }
	}

	/// <summary>Фиксированная привязка юнита к локальному слоту формации (не меняется при повороте фронта).</summary>
	public readonly struct FormationUnitSlotBinding
	{
		public FormationUnitSlotBinding(Vector2 _localOffset, float _facingOffsetFromForward)
		{
			LocalOffset = _localOffset;
			FacingOffsetFromForward = _facingOffsetFromForward;
		}

		public Vector2 LocalOffset { get; }
		/// <summary>Смещение взгляда относительно фронта формации (жёлтой стрелки), градусы.</summary>
		public float FacingOffsetFromForward { get; }
	}
	#endregion

	#region Public Methods
	public static bool IsGroupFormation(FormationType _formation, int _unitCount)
	{
		return _unitCount >= 2 && _formation != FormationType.None;
	}

	public static FormationType NormalizeGroupFormation(FormationType _formation)
	{
		return _formation == FormationType.None ? s_FormationHotkeyOrder[0] : _formation;
	}

	public static FormationType CycleFormation(FormationType _current)
	{
		FormationType normalized = NormalizeGroupFormation(_current);
		for (int i = 0; i < s_FormationHotkeyOrder.Length; i++)
		{
			if (s_FormationHotkeyOrder[i] != normalized)
				continue;

			return s_FormationHotkeyOrder[(i + 1) % s_FormationHotkeyOrder.Length];
		}

		return s_FormationHotkeyOrder[0];
	}

	public static FormationType FormationFromHotkeyIndex(int _index1Based)
	{
		int clamped = Mathf.Clamp(_index1Based, 1, s_FormationHotkeyOrder.Length);
		return s_FormationHotkeyOrder[clamped - 1];
	}

	public static string GetDisplayName(FormationType _formation)
	{
		return _formation switch
		{
			FormationType.SingleFile => "По одному",
			FormationType.DoubleFile => "По двое",
			FormationType.TacticalColumn => "Тактическая колонна",
			FormationType.Wedge => "Клин",
			FormationType.WideReconWedge => "Широкий клин разведки",
			FormationType.Line => "Линия",
			FormationType.Diamond => "Алмаз",
			_ => "—",
		};
	}

	public static FormationBuildResult BuildFormation(
		FormationType _formation,
		IReadOnlyList<RtsUnitMember> _units,
		Vector3 _centerPoint,
		Vector3 _formationForward,
		float _spacing,
		bool _applyJitter = true,
		float _jitterAmount = 0.1f)
	{
		int count = _units != null ? _units.Count : 0;
		if (count == 0)
			return new FormationBuildResult(new List<Vector3>(), new List<float>());

		Vector3 forward = _formationForward;
		forward.y = 0f;
		if (forward.sqrMagnitude < 0.0001f)
			forward = Vector3.forward;
		forward.Normalize();

		Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
		float baseAngle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
		float spacing = Mathf.Max(0.1f, _spacing);

		List<FormationSlotLayout> slots = GenerateSlots(_formation, count, spacing, baseAngle);
		AssignUnitsToSlots(
			_units,
			slots,
			_centerPoint,
			forward,
			right,
			_formation,
			out List<Vector3> offsets,
			out List<float> facingAngles,
			out int[] _);

		if (_applyJitter && _jitterAmount > 0.0001f)
		{
			float jitter = Mathf.Min(_jitterAmount, spacing * 0.3f);
			for (int i = 0; i < offsets.Count; i++)
			{
				offsets[i] += right * Random.Range(-jitter, jitter);
				offsets[i] += forward * Random.Range(-jitter, jitter);
			}
		}

		return new FormationBuildResult(offsets, facingAngles);
	}

	/// <summary>Один раз назначает юнитов слотам; результат используется для поворота всего строя.</summary>
	public static FormationUnitSlotBinding[] CreateStableBindings(
		FormationType _formation,
		IReadOnlyList<RtsUnitMember> _units,
		Vector3 _centerPoint,
		float _spacing,
		Vector3? _formationForwardOverride = null)
	{
		int count = _units != null ? _units.Count : 0;
		if (count == 0)
			return System.Array.Empty<FormationUnitSlotBinding>();

		Vector3 forward = ResolveFormationForward(_units, _centerPoint, _formationForwardOverride);
		Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
		float baseAngle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
		float spacing = Mathf.Max(0.1f, _spacing);

		List<FormationSlotLayout> slots = GenerateSlots(_formation, count, spacing, baseAngle);
		AssignUnitsToSlots(
			_units,
			slots,
			_centerPoint,
			forward,
			right,
			_formation,
			out List<Vector3> _,
			out List<float> facingAngles,
			out int[] unitToSlotIndex);

		var bindings = new FormationUnitSlotBinding[count];
		for (int unitIndex = 0; unitIndex < count; unitIndex++)
		{
			int slotIndex = unitToSlotIndex[unitIndex];
			if (slotIndex < 0 || slotIndex >= slots.Count)
				continue;

			FormationSlotLayout slot = slots[slotIndex];
			Vector2 local = new Vector2(slot.LocalOffset.x, slot.LocalOffset.z);
			bindings[unitIndex] = new FormationUnitSlotBinding(local, slot.FacingAngleDegrees);
		}

		return bindings;
	}

	/// <summary>Конечный угол взгляда слота относительно фронта формации (0° = жёлтая стрелка).</summary>
	public static float ResolveSlotFacingOffsetDegrees(
		FormationType _formation,
		float _localX,
		float _localZ,
		float _spacing,
		int _slotCount = 0)
	{
		if (!IndividualSlotSectorsEnabled)
			return 0f;

		float spacing = Mathf.Max(0.1f, _spacing);
		float centerBand = GetCenterBandRadiusMeters(spacing);

		switch (_formation)
		{
			case FormationType.Line:
			case FormationType.SingleFile:
				return 0f;

			case FormationType.DoubleFile:
				return ResolveSignedFlankYaw(_localX, centerBand, c_DoubleFileFlankYawDegrees, c_DoubleFileFlankYawDegrees);

			case FormationType.TacticalColumn:
				return ResolveSignedFlankYaw(_localX, centerBand, c_TacticalColumnFlankYawDegrees, c_TacticalColumnFlankYawDegrees);

			case FormationType.Wedge:
				return ResolveWedgeSlotFacingOffset(_localX, _localZ, centerBand);

			case FormationType.WideReconWedge:
				return ResolveWideReconWedgeSlotFacingOffset(_localX, _localZ, centerBand);

			case FormationType.Diamond:
				return ResolveDiamondSlotFacingOffset(_localX, _localZ, _slotCount);

			default:
				return 0f;
		}
	}

	public static float ResolveSlotWorldFacingAngle(float _formationForwardYawDegrees, float _slotFacingOffsetDegrees)
	{
		return Mathf.Repeat(_formationForwardYawDegrees + _slotFacingOffsetDegrees + 180f, 360f) - 180f;
	}

	public static float ResolveFormationForwardYawDegrees(Vector3 _formationForward)
	{
		Vector3 forward = _formationForward;
		forward.y = 0f;
		if (forward.sqrMagnitude < 0.0001f)
			return 0f;

		return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
	}

	/// <summary>Поворачивает кэшированное строение: позиции слотов и сохранённые углы взгляда.</summary>
	public static float GetCenterBandRadiusMeters(float _spacing)
	{
		return Mathf.Max(c_MinCenterBandRadiusMeters, Mathf.Max(0.1f, _spacing) * c_CenterBandSpacingFraction);
	}

	/// <summary>
	/// Сектор взгляда по боковому положению в строю: центр — по ходу, края — вперёд с лёгким уводом наружу.
	/// Бок выбирается по положению относительно центра группы (без перекрёстного огня при развороте).
	/// </summary>
	public static float ResolveRuntimeSectorWorldAngle(
		Vector3 _unitWorldPos,
		Vector3 _formationCenterWorld,
		Vector3 _movementForwardXZ,
		float _centerBandRadiusMeters)
	{
		Vector3 forward = _movementForwardXZ;
		forward.y = 0f;
		if (forward.sqrMagnitude < 1e-6f)
			forward = Vector3.forward;
		else
			forward.Normalize();

		Vector3 toUnit = _unitWorldPos - _formationCenterWorld;
		toUnit.y = 0f;
		float alongMarch = Vector3.Dot(toUnit, forward);
		Vector3 lateral = toUnit - forward * alongMarch;
		float lateralSqr = lateral.sqrMagnitude;
		float bandSqr = _centerBandRadiusMeters * _centerBandRadiusMeters;

		float baseYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
		if (lateralSqr < bandSqr)
			return baseYaw;

		Vector3 right = Vector3.Cross(Vector3.up, forward);
		float side = Mathf.Sign(Vector3.Dot(lateral, right));
		if (Mathf.Approximately(side, 0f))
			return baseYaw;

		float lateralMag = Mathf.Sqrt(lateralSqr);
		float flankT = Mathf.Clamp01(
			(lateralMag - _centerBandRadiusMeters) / Mathf.Max(0.01f, _centerBandRadiusMeters));
		float yawOffset = Mathf.Lerp(c_FlankYawOffsetDegrees, c_MaxFlankYawOffsetDegrees, flankT);
		return baseYaw + side * yawOffset;
	}

	public static FormationBuildResult ApplyBindings(
		FormationUnitSlotBinding[] _bindings,
		Vector3 _formationForward)
	{
		if (_bindings == null || _bindings.Length == 0)
			return new FormationBuildResult(new List<Vector3>(), new List<float>());

		Vector3 forward = _formationForward;
		forward.y = 0f;
		if (forward.sqrMagnitude < 0.0001f)
			forward = Vector3.forward;
		forward.Normalize();

		Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

		var offsets = new List<Vector3>(_bindings.Length);
		var facings = new List<float>(_bindings.Length);
		float formationYaw = ResolveFormationForwardYawDegrees(forward);
		for (int i = 0; i < _bindings.Length; i++)
		{
			Vector2 local = _bindings[i].LocalOffset;
			offsets.Add(right * local.x + forward * local.y);
			facings.Add(ResolveSlotWorldFacingAngle(formationYaw, _bindings[i].FacingOffsetFromForward));
		}

		return new FormationBuildResult(offsets, facings);
	}

	public static Vector3 ResolveFormationForward(IReadOnlyList<RtsUnitMember> _units, Vector3 _centerPoint, Vector3? _overrideForward = null)
	{
		if (_overrideForward.HasValue)
		{
			Vector3 fwd = _overrideForward.Value;
			fwd.y = 0f;
			if (fwd.sqrMagnitude > 0.0001f)
				return fwd.normalized;
		}

		Vector3 avg = Vector3.zero;
		int count = 0;
		for (int i = 0; i < _units.Count; i++)
		{
			if (_units[i] == null)
				continue;
			avg += _units[i].transform.position;
			count++;
		}

		if (count == 0)
			return Vector3.forward;

		avg /= count;
		Vector3 toTarget = _centerPoint - avg;
		toTarget.y = 0f;
		return toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : Vector3.forward;
	}
	#endregion

	#region Slot Generation
	private static void ResolveSpacingAxes(
		FormationType _formation,
		float _spacing,
		out float _widthSpacing,
		out float _depthSpacing)
	{
		float widthMul;
		float depthMul;
		switch (_formation)
		{
			case FormationType.TacticalColumn:
				widthMul = 1.15f;
				depthMul = 0.85f;
				break;
			case FormationType.DoubleFile:
				widthMul = 1.25f;
				depthMul = 0.9f;
				break;
			case FormationType.SingleFile:
				widthMul = 0f;
				depthMul = 1.15f;
				break;
			case FormationType.Wedge:
				widthMul = 1.2f;
				depthMul = 0.9f;
				break;
			case FormationType.Line:
				widthMul = 1.25f;
				depthMul = 0f;
				break;
			case FormationType.Diamond:
				widthMul = 1.05f;
				depthMul = 1.05f;
				break;
			case FormationType.WideReconWedge:
				widthMul = 1.45f;
				depthMul = 0.8f;
				break;
			default:
				widthMul = 1f;
				depthMul = 1f;
				break;
		}

		_widthSpacing = _spacing * widthMul;
		_depthSpacing = _spacing * depthMul;
	}

	private static List<FormationSlotLayout> GenerateSlots(
		FormationType _formation,
		int _count,
		float _spacing,
		float _baseAngle)
	{
		ResolveSpacingAxes(_formation, _spacing, out float widthSpacing, out float depthSpacing);

		var localPoints = new List<Vector2>(_count);
		switch (_formation)
		{
			case FormationType.SingleFile:
				BuildSingleFileSlots(localPoints, _count, depthSpacing);
				break;
			case FormationType.DoubleFile:
				BuildDoubleFileSlots(localPoints, _count, widthSpacing, depthSpacing);
				break;
			case FormationType.TacticalColumn:
				BuildTacticalColumnSlots(localPoints, _count, widthSpacing, depthSpacing);
				break;
			case FormationType.Wedge:
				BuildWedgeSlots(localPoints, _count, widthSpacing, depthSpacing, _wide: false);
				break;
			case FormationType.WideReconWedge:
				BuildWideReconWedgeSlots(localPoints, _count, widthSpacing, depthSpacing);
				break;
			case FormationType.Line:
				BuildLineSlots(localPoints, _count, widthSpacing);
				break;
			case FormationType.Diamond:
				BuildDiamondSlots(localPoints, _count, widthSpacing, depthSpacing);
				break;
			default:
				BuildSingleFileSlots(localPoints, _count, depthSpacing);
				break;
		}

		var slots = new List<FormationSlotLayout>(_count);
		for (int i = 0; i < localPoints.Count; i++)
		{
			Vector2 p = localPoints[i];
			float facing = ResolveSlotFacingOffsetDegrees(_formation, p.x, p.y, _spacing, _count);
			slots.Add(new FormationSlotLayout(new Vector3(p.x, 0f, p.y), facing, i));
		}

		return slots;
	}

	private static void BuildSingleFileSlots(List<Vector2> _out, int _count, float _depthSpacing)
	{
		for (int i = 0; i < _count; i++)
			_out.Add(new Vector2(0f, -i * _depthSpacing));
	}

	private static void BuildDoubleFileSlots(List<Vector2> _out, int _count, float _widthSpacing, float _depthSpacing)
	{
		float half = _widthSpacing * 0.5f;
		for (int i = 0; i < _count; i++)
		{
			int row = i / 2;
			bool right = (i & 1) == 1;
			_out.Add(new Vector2(right ? half : -half, -row * _depthSpacing));
		}
	}

	private static void BuildTacticalColumnSlots(List<Vector2> _out, int _count, float _widthSpacing, float _depthSpacing)
	{
		float side = _widthSpacing * 0.85f;
		for (int i = 0; i < _count; i++)
		{
			bool left = (i & 1) == 0;
			_out.Add(new Vector2(left ? -side : side, -i * _depthSpacing));
		}
	}

	private static void BuildWedgeSlots(
		List<Vector2> _out,
		int _count,
		float _widthSpacing,
		float _depthSpacing,
		bool _wide)
	{
		if (_count <= 0)
			return;

		_out.Add(Vector2.zero);

		int placed = 1;
		int row = 1;
		while (placed < _count)
		{
			float widthFactor = _wide ? 2.2f + row * 0.35f : 1f + (row - 1) * 0.15f;
			float rowWidth = _widthSpacing * widthFactor;
			int rowCapacity = 2;

			for (int side = 0; side < rowCapacity && placed < _count; side++)
			{
				float x = side == 0 ? -rowWidth : rowWidth;
				_out.Add(new Vector2(x, -row * _depthSpacing));
				placed++;
			}

			row++;
		}
	}

	private static void BuildWideReconWedgeSlots(
		List<Vector2> _out,
		int _count,
		float _widthSpacing,
		float _depthSpacing)
	{
		float wide = _widthSpacing * (2.4f / 1.45f);
		float scoutZ = _depthSpacing * c_WideReconScoutForwardDepthFraction;
		float centerGap = _depthSpacing * c_WideReconFrontToCenterDepthFraction * c_WideReconFrontRearDepthGapMultiplier;
		float firstBackGap = _depthSpacing * c_WideReconFrontToBackDepthFraction * c_WideReconFrontRearDepthGapMultiplier;
		float backRowStep = _depthSpacing * c_WideReconBackRowDepthStepFraction * c_WideReconFrontRearDepthGapMultiplier;
		int placed = 0;
		int row = 0;
		while (placed < _count)
		{
			if (row == 0)
			{
				if (placed < _count)
				{
					_out.Add(new Vector2(-wide, scoutZ));
					placed++;
				}
				if (placed < _count)
				{
					_out.Add(new Vector2(wide, scoutZ));
					placed++;
				}
			}
			else if (row == 1)
			{
				if (placed < _count)
				{
					_out.Add(new Vector2(0f, scoutZ - centerGap));
					placed++;
				}
			}
			else
			{
				float backZ = scoutZ - firstBackGap - (row - 2) * backRowStep;
				if (placed < _count)
				{
					_out.Add(new Vector2(-wide, backZ));
					placed++;
				}
				if (placed < _count)
				{
					_out.Add(new Vector2(wide, backZ));
					placed++;
				}
			}

			row++;
		}
	}

	private static void BuildLineSlots(List<Vector2> _out, int _count, float _widthSpacing)
	{
		float totalWidth = (_count - 1) * _widthSpacing;
		float startX = -totalWidth * 0.5f;
		for (int i = 0; i < _count; i++)
			_out.Add(new Vector2(startX + i * _widthSpacing, 0f));
	}

	private static void BuildDiamondSlots(List<Vector2> _out, int _count, float _widthSpacing, float _depthSpacing)
	{
		if (_count == 1)
		{
			_out.Add(Vector2.zero);
			return;
		}

		if (_count <= 4)
		{
			if (_count >= 1)
				_out.Add(new Vector2(0f, _depthSpacing * 0.65f));
			if (_count >= 2)
				_out.Add(new Vector2(-_widthSpacing * 0.75f, 0f));
			if (_count >= 3)
				_out.Add(new Vector2(_widthSpacing * 0.75f, 0f));
			if (_count >= 4)
				_out.Add(new Vector2(0f, -_depthSpacing * 0.65f));
			return;
		}

		float radiusX = _widthSpacing * (0.75f + 0.08f * Mathf.Min(_count, 12));
		float radiusZ = _depthSpacing * (0.75f + 0.08f * Mathf.Min(_count, 12));
		for (int i = 0; i < _count; i++)
		{
			float t = (float)i / _count;
			float angle = t * Mathf.PI * 2f - Mathf.PI * 0.5f;
			_out.Add(new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusZ));
		}
	}
	#endregion

	#region Slot Facing
	private static float ResolveSignedFlankYaw(
		float _localX,
		float _centerBand,
		float _minYawDegrees,
		float _maxYawDegrees)
	{
		if (Mathf.Abs(_localX) < _centerBand)
			return 0f;

		float side = Mathf.Sign(_localX);
		if (Mathf.Approximately(side, 0f))
			return 0f;

		float lateralMag = Mathf.Abs(_localX);
		float flankT = Mathf.Clamp01((lateralMag - _centerBand) / Mathf.Max(0.01f, _centerBand));
		float yawOffset = Mathf.Lerp(_minYawDegrees, _maxYawDegrees, flankT);
		return side * yawOffset;
	}

	private static float ResolveWedgeSlotFacingOffset(float _localX, float _localZ, float _centerBand)
	{
		if (Mathf.Abs(_localX) < _centerBand && _localZ >= -0.01f)
			return 0f;

		return ResolveSignedFlankYaw(_localX, _centerBand, c_FlankYawOffsetDegrees, c_MaxFlankYawOffsetDegrees);
	}

	private static float ResolveWideReconWedgeSlotFacingOffset(float _localX, float _localZ, float _centerBand)
	{
		if (_localZ > 0.01f)
		{
			if (_localX < -_centerBand)
				return -c_WideReconScoutYawDegrees;
			if (_localX > _centerBand)
				return c_WideReconScoutYawDegrees;
			return 0f;
		}

		if (Mathf.Abs(_localX) < _centerBand)
			return 0f;

		return ResolveSignedFlankYaw(
			_localX,
			_centerBand,
			c_WideReconRearFlankMinYawDegrees,
			c_MaxFlankYawOffsetDegrees);
	}

	private static float ResolveDiamondSlotFacingOffset(float _localX, float _localZ, int _slotCount)
	{
		if (_slotCount <= 0)
			return 0f;

		if (_slotCount < c_DiamondSmallCountThreshold)
		{
			const float c_AxisEpsilon = 0.05f;
			if (_localZ > c_AxisEpsilon && Mathf.Abs(_localX) <= c_AxisEpsilon)
				return 0f;
			if (_localX < -c_AxisEpsilon && Mathf.Abs(_localZ) <= c_AxisEpsilon)
				return -90f;
			if (_localX > c_AxisEpsilon && Mathf.Abs(_localZ) <= c_AxisEpsilon)
				return 90f;
			if (_localZ < -c_AxisEpsilon && Mathf.Abs(_localX) <= c_AxisEpsilon)
				return 180f;
		}

		if (Mathf.Abs(_localX) < 0.001f && Mathf.Abs(_localZ) < 0.001f)
			return 0f;

		return Mathf.Atan2(_localX, _localZ) * Mathf.Rad2Deg;
	}
	#endregion

	#region Assignment
	private static void AssignUnitsToSlots(
		IReadOnlyList<RtsUnitMember> _units,
		IReadOnlyList<FormationSlotLayout> _slots,
		Vector3 _centerPoint,
		Vector3 _forward,
		Vector3 _right,
		FormationType _formation,
		out List<Vector3> _offsets,
		out List<float> _facingAngles,
		out int[] _unitToSlotIndex)
	{
		int count = _units.Count;
		_offsets = new List<Vector3>(count);
		_facingAngles = new List<float>(count);
		_unitToSlotIndex = new int[count];
		for (int i = 0; i < count; i++)
		{
			_offsets.Add(Vector3.zero);
			_facingAngles.Add(0f);
			_unitToSlotIndex[i] = -1;
		}

		if (_slots.Count == 0)
			return;

		int assignCount = Mathf.Min(count, _slots.Count);
		var slotWorldPositions = new Vector3[_slots.Count];
		for (int i = 0; i < _slots.Count; i++)
		{
			FormationSlotLayout slot = _slots[i];
			slotWorldPositions[i] = _centerPoint + _right * slot.LocalOffset.x + _forward * slot.LocalOffset.z;
		}

		FormationType sortMode = ResolveAssignmentSortMode(_formation, _slots);
		List<int> leaderCandidates = CollectLeaderCandidates(_units);
		int[] bestUnitToSlot = null;
		float bestTravel = float.MaxValue;

		for (int c = 0; c < leaderCandidates.Count; c++)
		{
			int leaderUnitIndex = leaderCandidates[c];
			if (TryBuildAssignment(
				    _units,
				    _slots,
				    slotWorldPositions,
				    assignCount,
				    leaderUnitIndex,
				    _formation,
				    sortMode,
				    _forward,
				    _right,
				    leaderCandidates.Count > 1,
				    out int[] candidateUnitToSlot,
				    out float candidateTravel,
				    out List<PendingAssignmentStep> _)
			    && candidateTravel < bestTravel)
			{
				bestTravel = candidateTravel;
				bestUnitToSlot = candidateUnitToSlot;
			}
		}

		if (bestUnitToSlot == null)
			return;

		for (int unitIndex = 0; unitIndex < count; unitIndex++)
		{
			int slotIndex = bestUnitToSlot[unitIndex];
			if (slotIndex < 0)
				continue;

			AssignUnitToSlot(
				_units,
				_slots,
				unitIndex,
				slotIndex,
				_forward,
				_right,
				_offsets,
				_facingAngles,
				_unitToSlotIndex);
		}
	}

	private readonly struct PendingAssignmentStep
	{
		public PendingAssignmentStep(int _unitIndex, int _slotIndex, float _distanceMeters, string _reason)
		{
			UnitIndex = _unitIndex;
			SlotIndex = _slotIndex;
			DistanceMeters = _distanceMeters;
			Reason = _reason;
		}

		public int UnitIndex { get; }
		public int SlotIndex { get; }
		public float DistanceMeters { get; }
		public string Reason { get; }
	}

	private static bool TryBuildAssignment(
		IReadOnlyList<RtsUnitMember> _units,
		IReadOnlyList<FormationSlotLayout> _slots,
		Vector3[] _slotWorldPositions,
		int _assignCount,
		int _leaderUnitIndex,
		FormationType _formation,
		FormationType _sortMode,
		Vector3 _forward,
		Vector3 _right,
		bool _jointLeaderSearch,
		out int[] _unitToSlot,
		out float _totalTravel,
		out List<PendingAssignmentStep> _steps)
	{
		int count = _units.Count;
		_unitToSlot = new int[count];
		_steps = new List<PendingAssignmentStep>(_assignCount);
		for (int i = 0; i < count; i++)
			_unitToSlot[i] = -1;

		if (_leaderUnitIndex < 0 || _leaderUnitIndex >= count || _assignCount <= 0)
		{
			_totalTravel = float.MaxValue;
			return false;
		}

		RtsUnitMember leaderUnit = _units[_leaderUnitIndex];
		if (leaderUnit == null)
		{
			_totalTravel = float.MaxValue;
			return false;
		}

		Vector3 leaderPos = leaderUnit.transform.position;
		float leaderDist = Vector3.Distance(leaderPos, _slotWorldPositions[0]);
		_unitToSlot[_leaderUnitIndex] = 0;
		_totalTravel = leaderDist;

		int leaderNearestSlot = FindNearestSlotIndex(leaderPos, _slotWorldPositions, _assignCount);
		float leaderNearestDist = Vector3.Distance(leaderPos, _slotWorldPositions[leaderNearestSlot]);
		string leaderReason = _jointLeaderSearch
			? "rank leader candidate -> slot 0 (joint opt)"
			: leaderNearestSlot == 0
				? "rank leader (closest to slot 0) -> slot 0"
				: $"rank leader -> slot 0 forced (nearest was slot {leaderNearestSlot} @ {leaderNearestDist:F2}m)";
		_steps.Add(new PendingAssignmentStep(_leaderUnitIndex, 0, leaderDist, leaderReason));

		var remainingUnits = new List<int>();
		for (int i = 0; i < count; i++)
		{
			if (i == _leaderUnitIndex)
				continue;
			remainingUnits.Add(i);
		}

		var remainingSlots = new List<int>();
		for (int slotIndex = 1; slotIndex < _assignCount; slotIndex++)
			remainingSlots.Add(slotIndex);

		if (remainingSlots.Count == 0)
			return true;

		if (TryBuildOrderedRemainingAssignment(
			    _units,
			    _slots,
			    _slotWorldPositions,
			    remainingUnits,
			    remainingSlots,
			    _sortMode,
			    _forward,
			    _right,
			    out var orderedPairs,
			    out float orderedTravel)
		    && TryBuildMinCostRemainingAssignment(
			    _units,
			    _slotWorldPositions,
			    remainingUnits,
			    remainingSlots,
			    out var minCostPairs,
			    out float minCostTravel))
		{
			bool useOrdered = orderedTravel <= minCostTravel;
			var chosenPairs = useOrdered ? orderedPairs : minCostPairs;
			float chosenTravel = useOrdered ? orderedTravel : minCostTravel;
			string reason = useOrdered ? "ordered (no crossing)" : "min-cost matching";

			for (int i = 0; i < chosenPairs.Count; i++)
			{
				(int unitIndex, int slotIndex, float distance) = chosenPairs[i];
				_unitToSlot[unitIndex] = slotIndex;
				_totalTravel += distance;
				_steps.Add(new PendingAssignmentStep(unitIndex, slotIndex, distance, reason));
			}

			return true;
		}

		_totalTravel = float.MaxValue;
		return false;
	}

	private static bool TryBuildOrderedRemainingAssignment(
		IReadOnlyList<RtsUnitMember> _units,
		IReadOnlyList<FormationSlotLayout> _slots,
		Vector3[] _slotWorldPositions,
		List<int> _remainingUnits,
		List<int> _remainingSlots,
		FormationType _sortMode,
		Vector3 _forward,
		Vector3 _right,
		out List<(int unitIndex, int slotIndex, float distance)> _pairs,
		out float _totalTravel)
	{
		_pairs = new List<(int, int, float)>();
		_totalTravel = 0f;
		if (_remainingUnits.Count == 0 || _remainingSlots.Count == 0)
			return true;

		_remainingUnits.Sort((a, b) => CompareUnitsForAssignment(_units, a, b, _forward, _right, _sortMode));
		_remainingSlots.Sort((a, b) => CompareSlotsForAssignment(_slots[a], _slots[b], _sortMode));

		int assignCount = Mathf.Min(_remainingUnits.Count, _remainingSlots.Count);
		for (int i = 0; i < assignCount; i++)
		{
			int unitIndex = _remainingUnits[i];
			int slotIndex = _remainingSlots[i];
			RtsUnitMember unit = _units[unitIndex];
			if (unit == null)
				continue;

			float distance = Vector3.Distance(unit.transform.position, _slotWorldPositions[slotIndex]);
			_pairs.Add((unitIndex, slotIndex, distance));
			_totalTravel += distance;
		}

		return true;
	}

	private static bool TryBuildMinCostRemainingAssignment(
		IReadOnlyList<RtsUnitMember> _units,
		Vector3[] _slotWorldPositions,
		List<int> _remainingUnits,
		List<int> _remainingSlots,
		out List<(int unitIndex, int slotIndex, float distance)> _pairs,
		out float _totalTravel)
	{
		_pairs = new List<(int, int, float)>();
		_totalTravel = 0f;

		int unitCount = _remainingUnits.Count;
		int slotCount = _remainingSlots.Count;
		if (slotCount == 0 || unitCount == 0)
			return true;

		int size = Mathf.Max(unitCount, slotCount);
		const float c_DummyCost = 1_000_000f;
		var cost = new float[size, size];
		for (int row = 0; row < size; row++)
		{
			for (int col = 0; col < size; col++)
			{
				if (row < unitCount && col < slotCount)
				{
					RtsUnitMember unit = _units[_remainingUnits[row]];
					Vector3 unitPos = unit != null ? unit.transform.position : Vector3.zero;
					cost[row, col] = Vector3.Distance(unitPos, _slotWorldPositions[_remainingSlots[col]]);
				}
				else
				{
					cost[row, col] = c_DummyCost;
				}
			}
		}

		int[] slotForUnitRow = SolveMinCostAssignment(cost, size);
		for (int row = 0; row < unitCount; row++)
		{
			int col = slotForUnitRow[row];
			if (col < 0 || col >= slotCount)
				continue;

			int unitIndex = _remainingUnits[row];
			int slotIndex = _remainingSlots[col];
			float distance = cost[row, col];
			_pairs.Add((unitIndex, slotIndex, distance));
			_totalTravel += distance;
		}

		return true;
	}

	private static List<int> CollectLeaderCandidates(IReadOnlyList<RtsUnitMember> _units)
	{
		int bestRank = -1;
		for (int i = 0; i < _units.Count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit == null)
				continue;
			bestRank = Mathf.Max(bestRank, ResolveUnitRankIndex(unit));
		}

		var candidates = new List<int>();
		for (int i = 0; i < _units.Count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit == null)
				continue;
			if (ResolveUnitRankIndex(unit) == bestRank)
				candidates.Add(i);
		}

		if (candidates.Count == 0)
			candidates.Add(0);
		return candidates;
	}

	private static int FindUnitIndexForSlot(int[] _unitToSlot, int _slotIndex)
	{
		for (int i = 0; i < _unitToSlot.Length; i++)
		{
			if (_unitToSlot[i] == _slotIndex)
				return i;
		}

		return 0;
	}

	private static int FindNearestSlotIndex(Vector3 _unitPosition, Vector3[] _slotWorldPositions, int _assignCount)
	{
		int nearestSlot = 0;
		float nearestDistSqr = float.MaxValue;
		for (int slotIndex = 0; slotIndex < _assignCount; slotIndex++)
		{
			float distSqr = (_unitPosition - _slotWorldPositions[slotIndex]).sqrMagnitude;
			if (distSqr < nearestDistSqr)
			{
				nearestDistSqr = distSqr;
				nearestSlot = slotIndex;
			}
		}

		return nearestSlot;
	}

	private static FormationType ResolveAssignmentSortMode(
		FormationType _formation,
		IReadOnlyList<FormationSlotLayout> _slots)
	{
		switch (_formation)
		{
			case FormationType.Line:
				return FormationType.Line;
			case FormationType.Diamond:
				return FormationType.Diamond;
			default:
				return InferSortMode(_slots);
		}
	}

	private static int CompareSlotsForAssignment(
		FormationSlotLayout _a,
		FormationSlotLayout _b,
		FormationType _sortMode)
	{
		switch (_sortMode)
		{
			case FormationType.Line:
				if (!Mathf.Approximately(_a.LocalOffset.x, _b.LocalOffset.x))
					return _a.LocalOffset.x.CompareTo(_b.LocalOffset.x);
				return _a.SlotIndex.CompareTo(_b.SlotIndex);

			case FormationType.Diamond:
				if (!Mathf.Approximately(_b.LocalOffset.z, _a.LocalOffset.z))
					return _b.LocalOffset.z.CompareTo(_a.LocalOffset.z);
				return _a.LocalOffset.x.CompareTo(_b.LocalOffset.x);

			default:
				if (!Mathf.Approximately(_b.LocalOffset.z, _a.LocalOffset.z))
					return _b.LocalOffset.z.CompareTo(_a.LocalOffset.z);
				if (!Mathf.Approximately(_a.LocalOffset.x, _b.LocalOffset.x))
					return _a.LocalOffset.x.CompareTo(_b.LocalOffset.x);
				return _a.SlotIndex.CompareTo(_b.SlotIndex);
		}
	}

	private static int CompareUnitsForAssignment(
		IReadOnlyList<RtsUnitMember> _units,
		int _a,
		int _b,
		Vector3 _forward,
		Vector3 _right,
		FormationType _sortMode)
	{
		Vector3 posA = _units[_a].transform.position;
		Vector3 posB = _units[_b].transform.position;

		float depthA = Vector3.Dot(posA, _forward);
		float depthB = Vector3.Dot(posB, _forward);
		float sideA = Vector3.Dot(posA, _right);
		float sideB = Vector3.Dot(posB, _right);

		switch (_sortMode)
		{
			case FormationType.Line:
				if (!Mathf.Approximately(sideA, sideB))
					return sideA.CompareTo(sideB);
				return depthB.CompareTo(depthA);

			case FormationType.Diamond:
				if (!Mathf.Approximately(depthB, depthA))
					return depthB.CompareTo(depthA);
				return sideA.CompareTo(sideB);

			default:
				if (!Mathf.Approximately(depthB, depthA))
					return depthB.CompareTo(depthA);
				return sideA.CompareTo(sideB);
		}
	}

	/// <summary>Hungarian algorithm: returns slot column index for each unit row.</summary>
	private static int[] SolveMinCostAssignment(float[,] _cost, int _size)
	{
		int n = _size;
		var u = new float[n + 1];
		var v = new float[n + 1];
		var p = new int[n + 1];
		var way = new int[n + 1];

		for (int i = 1; i <= n; i++)
		{
			p[0] = i;
			int j0 = 0;
			var minv = new float[n + 1];
			var used = new bool[n + 1];
			for (int j = 0; j <= n; j++)
				minv[j] = float.MaxValue;

			do
			{
				used[j0] = true;
				int i0 = p[j0];
				float delta = float.MaxValue;
				int j1 = 0;
				for (int j = 1; j <= n; j++)
				{
					if (used[j])
						continue;

					float cur = _cost[i0 - 1, j - 1] - u[i0] - v[j];
					if (cur < minv[j])
					{
						minv[j] = cur;
						way[j] = j0;
					}

					if (minv[j] < delta)
					{
						delta = minv[j];
						j1 = j;
					}
				}

				for (int j = 0; j <= n; j++)
				{
					if (used[j])
					{
						u[p[j]] += delta;
						v[j] -= delta;
					}
					else
					{
						minv[j] -= delta;
					}
				}

				j0 = j1;
			}
			while (p[j0] != 0);

			do
			{
				int j1 = way[j0];
				p[j0] = p[j1];
				j0 = j1;
			}
			while (j0 != 0);
		}

		var result = new int[n];
		for (int j = 1; j <= n; j++)
		{
			if (p[j] != 0)
				result[p[j] - 1] = j - 1;
		}

		return result;
	}

	private static void AssignUnitToSlot(
		IReadOnlyList<RtsUnitMember> _units,
		IReadOnlyList<FormationSlotLayout> _slots,
		int _unitIndex,
		int _slotIndex,
		Vector3 _forward,
		Vector3 _right,
		List<Vector3> _offsets,
		List<float> _facingAngles,
		int[] _unitToSlotIndex)
	{
		if (_unitIndex < 0 || _unitIndex >= _units.Count || _slotIndex < 0 || _slotIndex >= _slots.Count)
			return;

		FormationSlotLayout slot = _slots[_slotIndex];
		_offsets[_unitIndex] = _right * slot.LocalOffset.x + _forward * slot.LocalOffset.z;
		_facingAngles[_unitIndex] = slot.FacingAngleDegrees;
		_unitToSlotIndex[_unitIndex] = _slotIndex;
	}

	private static int ResolveUnitRankIndex(RtsUnitMember _unit)
	{
		if (_unit == null)
			return -1;

		UnitCombatStats stats = _unit.GetComponent<UnitCombatStats>();
		if (stats == null)
			return -1;

		return UnitCombatRankCycle.GetRankAssetNameIndex(stats.RankPreset);
	}

	private static FormationType InferSortMode(IReadOnlyList<FormationSlotLayout> _slots)
	{
		if (_slots.Count <= 1)
			return FormationType.SingleFile;

		bool hasDepth = false;
		bool hasWidth = false;
		for (int i = 0; i < _slots.Count; i++)
		{
			if (Mathf.Abs(_slots[i].LocalOffset.z) > 0.01f)
				hasDepth = true;
			if (Mathf.Abs(_slots[i].LocalOffset.x) > 0.01f)
				hasWidth = true;
		}

		if (hasWidth && !hasDepth)
			return FormationType.Line;
		if (hasDepth)
			return FormationType.SingleFile;
		return FormationType.Diamond;
	}
	#endregion
}

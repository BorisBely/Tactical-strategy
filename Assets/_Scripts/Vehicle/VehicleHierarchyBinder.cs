using System.Collections.Generic;
using CombatVehicleSystem;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Авто-сборка иерархии: hinges дверей, seats, exits, колёса, Plug — по именам мешей.
/// </summary>
public static class VehicleHierarchyBinder
{
	#region Constants
	private const string c_DoorFl = "SM_Veh_Light_Armored_Car_fl";
	private const string c_DoorFr = "SM_Veh_Light_Armored_Car_fr";
	private const string c_DoorBl = "SM_Veh_Light_Armored_Car_bl";
	private const string c_DoorBr = "SM_Veh_Light_Armored_Car_br";
	private const string c_WheelFl = "SM_Veh_Light_Armored_Car_01_Wheel_fl";
	private const string c_WheelFr = "SM_Veh_Light_Armored_Car_01_Wheel_fr";
	private const string c_WheelRl = "SM_Veh_Light_Armored_Car_01_Wheel_rl";
	private const string c_WheelRr = "SM_Veh_Light_Armored_Car_01_Wheel_rr";
	private const string c_WheelBl = "SM_Veh_Light_Armored_Car_01_Wheel_bl";
	private const string c_WheelBr = "SM_Veh_Light_Armored_Car_01_Wheel_br";
	private const string c_Plug = "SM_Veh_Light_Armored_Car_01_Plug";
	#endregion

	#region Public Methods
	public static void EnsureBound(VehicleController _vehicle)
	{
		if (_vehicle == null)
			return;

		_vehicle.EnsureComponents();
		Transform root = _vehicle.transform;

		Transform doorFl = FindDeep(root, c_DoorFl);
		Transform doorFr = FindDeep(root, c_DoorFr);
		Transform doorBl = FindDeep(root, c_DoorBl);
		Transform doorBr = FindDeep(root, c_DoorBr);

		Transform hingeFl = EnsureDoorHinge(root, doorFl, "Hinge_FL", new Vector3(-0.95f, 1.0f, 1.05f));
		Transform hingeFr = EnsureDoorHinge(root, doorFr, "Hinge_FR", new Vector3(0.95f, 1.0f, 1.05f));
		Transform hingeBl = EnsureDoorHinge(root, doorBl, "Hinge_BL", new Vector3(-0.95f, 1.0f, -0.55f));
		Transform hingeBr = EnsureDoorHinge(root, doorBr, "Hinge_BR", new Vector3(0.95f, 1.0f, -0.55f));

		// Approach beside the door panel (not the hinge pivot), far enough for NavMesh.
		Transform approachFl = EnsureDoorApproachMarker(root, doorFl, hingeFl, "Approach_FL", -1f, 1.05f, 0.55f);
		Transform approachFr = EnsureDoorApproachMarker(root, doorFr, hingeFr, "Approach_FR", 1f, 1.05f, 0.55f);
		Transform approachBl = EnsureDoorApproachMarker(root, doorBl, hingeBl, "Approach_BL", -1f, -0.55f, 0.55f);
		Transform approachBr = EnsureDoorApproachMarker(root, doorBr, hingeBr, "Approach_BR", 1f, -0.55f, 0.55f);

		Transform exitFl = EnsureDoorApproachMarker(root, doorFl, hingeFl, "Exit_FL", -1f, 1.05f, 0.75f);
		Transform exitFr = EnsureDoorApproachMarker(root, doorFr, hingeFr, "Exit_FR", 1f, 1.05f, 0.75f);
		Transform exitBl = EnsureDoorApproachMarker(root, doorBl, hingeBl, "Exit_BL", -1f, -0.55f, 0.75f);
		Transform exitBr = EnsureDoorApproachMarker(root, doorBr, hingeBr, "Exit_BR", 1f, -0.55f, 0.75f);

		Transform seatDriver = EnsureChild(root, "Seat_Driver", new Vector3(-0.38f, 0.95f, 0.55f));
		Transform seatCmd = EnsureChild(root, "Seat_Commander", new Vector3(0.38f, 0.95f, 0.55f));
		Transform seatGunner = EnsureChild(root, "Seat_Gunner", new Vector3(0f, 1.55f, -0.15f));
		Transform seatRearL = EnsureChild(root, "Seat_Rear_L", new Vector3(-0.4f, 0.95f, -0.55f));
		Transform seatRearC = EnsureChild(root, "Seat_Rear_C", new Vector3(0f, 0.95f, -0.55f));
		Transform seatRearR = EnsureChild(root, "Seat_Rear_R", new Vector3(0.4f, 0.95f, -0.55f));
		Transform litter1 = EnsureChild(root, "Litter_1", new Vector3(-0.25f, 1.05f, -0.9f));
		Transform litter2 = EnsureChild(root, "Litter_2", new Vector3(0.25f, 1.05f, -0.9f));

		if (!_vehicle.Seats.TryGetSeat(VehicleSeatId.Driver, out _))
		{
			_vehicle.Seats.SetSeats(new[]
			{
				// PreferredDoor: к какой двери подходят при «Сесть» (Side Any).
				MakeSeat(VehicleSeatId.Driver, seatDriver, VehicleDoorId.FrontLeft, false),
				MakeSeat(VehicleSeatId.Commander, seatCmd, VehicleDoorId.FrontRight, false),
				MakeSeat(VehicleSeatId.Gunner, seatGunner, VehicleDoorId.FrontRight, false),
				MakeSeat(VehicleSeatId.RearLeft, seatRearL, VehicleDoorId.RearLeft, false),
				MakeSeat(VehicleSeatId.RearCenter, seatRearC, VehicleDoorId.RearLeft, false),
				MakeSeat(VehicleSeatId.RearRight, seatRearR, VehicleDoorId.RearRight, false),
				MakeSeat(VehicleSeatId.Litter1, litter1, VehicleDoorId.RearLeft, true),
				MakeSeat(VehicleSeatId.Litter2, litter2, VehicleDoorId.RearRight, true)
			});
		}

		_vehicle.Doors.SetDoors(new[]
		{
			MakeDoor(VehicleDoorId.FrontLeft, hingeFl, approachFl, exitFl, 75f, false),
			MakeDoor(VehicleDoorId.FrontRight, hingeFr, approachFr, exitFr, 75f, true),
			MakeDoor(VehicleDoorId.RearLeft, hingeBl, approachBl, exitBl, 75f, false),
			MakeDoor(VehicleDoorId.RearRight, hingeBr, approachBr, exitBr, 75f, true)
		});
		Transform wheelFl = FindDeep(root, c_WheelFl);
		Transform wheelFr = FindDeep(root, c_WheelFr);
		Transform wheelRl = FindDeep(root, c_WheelRl) ?? FindDeep(root, c_WheelBl);
		Transform wheelRr = FindDeep(root, c_WheelRr) ?? FindDeep(root, c_WheelBr);

		BindPhysicsWheels(_vehicle, wheelFl, wheelFr, wheelRl, wheelRr);

		if (_vehicle.TryGetComponent(out VehicleWheelVisuals wheelVisuals))
			wheelVisuals.enabled = false;

		Transform plug = FindDeep(root, c_Plug);
		_vehicle.GunnerHatch.Configure(_vehicle.Seats, plug != null ? plug.gameObject : null);

		if (!root.TryGetComponent(out BoxCollider box) && root.GetComponent<Collider>() == null)
		{
			box = root.gameObject.AddComponent<BoxCollider>();
			box.center = new Vector3(0f, 1.35f, 0.1f);
			box.size = new Vector3(2.6f, 1.9f, 4.8f);
		}

		_vehicle.EnsureSelectionCollider();

		if (_vehicle.TryGetComponent(out VehicleBodyTilt bodyTilt) && bodyTilt.enabled)
		{
			if (_vehicle.TryGetComponent(out WheeledMotor wheeled))
				bodyTilt.BindMotor(wheeled);
			bodyTilt.RebuildHierarchy();
		}

		if (root.TryGetComponent(out NavMeshAgent agent) && !agent.isOnNavMesh)
		{
			if (NavMesh.SamplePosition(root.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
			{
				// NavMesh Y can sit above the visual ground — align XZ only, then snap wheels to ground.
				Vector3 pos = root.position;
				pos.x = hit.position.x;
				pos.z = hit.position.z;
				root.position = pos;
				agent.Warp(pos);
			}
		}

		_vehicle.SnapChassisToGround(_force: true);
	}
	#endregion

	#region Private Helpers
	private static void BindPhysicsWheels(
		VehicleController _vehicle,
		Transform _wheelFl,
		Transform _wheelFr,
		Transform _wheelRl,
		Transform _wheelRr)
	{
		if (_vehicle == null)
			return;

		// New architecture: VehicleData present → wheels handled by VehicleSuspension.
		if (_vehicle.UseNewArchitecture)
		{
			Debug.Log($"[Binder] Skipped wheel bind — new architecture for {_vehicle.name}");
			return;
		}

		if (!_vehicle.TryGetComponent(out WheeledMotor wheeledMotor))
			return;

		float steerAngle = 32f;
		if (_vehicle.Brain != null && _vehicle.Brain.Tuning != null)
			steerAngle = _vehicle.Brain.Tuning.DefaultSteerAngle;

		VehicleTuning tuning = _vehicle.Brain != null ? _vehicle.Brain.Tuning : null;

		// Log WC state BEFORE bind.
		var preLog = new System.Text.StringBuilder(256);
		preLog.Append("WC_GEO pre-bind: ");
		var existingWCs = _vehicle.GetComponentsInChildren<WheelCollider>(true);
		foreach (var wc in existingWCs)
		{
			wc.GetWorldPose(out Vector3 wp, out _);
			JointSpring s = wc.suspensionSpring;
			preLog.Append(
				$"[{wc.name} en={wc.enabled} r={wc.radius:F2} sd={wc.suspensionDistance:F2}" +
				$" sp={s.spring:F0} dp={s.damper:F0} tp={s.targetPosition:F2}" +
				$" fap={wc.forceAppPointDistance:F2}" +
				$" trY={wc.transform.position.y:F2} wcPoseY={wp.y:F2}] ");
		}
		Debug.Log(preLog.ToString(), _vehicle);

		var axles = new List<WheelAxle>(4);
		TryAddPhysicsAxle(axles, _vehicle.transform, _wheelFl, "WheelCollider_FL", true, true, steerAngle, tuning);
		TryAddPhysicsAxle(axles, _vehicle.transform, _wheelFr, "WheelCollider_FR", true, true, steerAngle, tuning);
		TryAddPhysicsAxle(axles, _vehicle.transform, _wheelRl, "WheelCollider_RL", true, false, steerAngle, tuning);
		TryAddPhysicsAxle(axles, _vehicle.transform, _wheelRr, "WheelCollider_RR", true, false, steerAngle, tuning);

		if (axles.Count > 0)
			wheeledMotor.SetAxles(axles.ToArray());

		if (_vehicle.Brain != null && _vehicle.Brain.Tuning != null)
			wheeledMotor.ApplyTuning(_vehicle.Brain.Tuning);

		// WheelColliders are created after the unit blocker, so the blocker must be told
		// to ignore non-wheel drive colliders (WC skipped — Unity 6 ground desync).
		_vehicle.UnitBlocker?.RefreshIgnoredDriveColliders();
		// Reset sprung masses BEFORE snap so WC internal state is consistent when body moves.
		wheeledMotor.ResetSprungMassesSafe();
		_vehicle.SnapChassisToGround(_force: true);
		Physics.SyncTransforms();

		// Diagnostic: log actual WC geometry right after binding.
		var geoLog = new System.Text.StringBuilder(256);
		geoLog.Append("WC_GEO post-bind: ");
		for (int i = 0; i < wheeledMotor.Axles.Length; i++)
		{
			WheelCollider wc = wheeledMotor.Axles[i]?.Collider;
			if (wc == null) continue;
			Vector3 localPos = wc.transform.localPosition;
			Vector3 hubWorld = wc.transform.TransformPoint(wc.center);
			wc.GetWorldPose(out Vector3 wcPos, out _);
			geoLog.Append(
				$"[{wc.name} local=({localPos.x:F2},{localPos.y:F2},{localPos.z:F2})" +
				$" center={wc.center} hubW={hubWorld.y:F2} wcPoseY={wcPos.y:F2}" +
				$" suspDist={wc.suspensionDistance:F2} radius={wc.radius:F2}] ");
		}
		Debug.Log(geoLog.ToString(), _vehicle);
	}

	private static void TryAddPhysicsAxle(
		List<WheelAxle> _axles,
		Transform _root,
		Transform _visual,
		string _colliderName,
		bool _motor,
		bool _steer,
		float _steerAngle,
		VehicleTuning _tuning)
	{
		if (_visual == null || _root == null)
			return;

		WheelCollider col = EnsureWheelCollider(_root, _visual, _colliderName, _tuning);
		if (col == null)
			return;

		_axles.Add(new WheelAxle
		{
			Collider = col,
			Visual = _visual,
			ApplyMotor = _motor,
			ApplySteer = _steer,
			SteerAngle = _steerAngle
		});
	}

	private static WheelCollider EnsureWheelCollider(Transform _root, Transform _visual, string _name, VehicleTuning _tuning = null)
	{
		Transform existing = _root.Find(_name);
		GameObject colGo;
		if (existing != null)
		{
			colGo = existing.gameObject;
		}
		else
		{
			colGo = new GameObject(_name);
			colGo.transform.SetParent(_root, false);
		}

		Vector3 localPos = _root.InverseTransformPoint(_visual.position);
		colGo.transform.localPosition = localPos;
		colGo.transform.localRotation = Quaternion.identity;

		if (!colGo.TryGetComponent(out WheelCollider col))
			col = colGo.AddComponent<WheelCollider>();

		col.radius = _tuning != null ? _tuning.WheelRadius : 0.45f;
		col.forceAppPointDistance = _tuning != null ? _tuning.ForceAppPointDistance : 0f;
		col.center = Vector3.zero;
		col.mass = _tuning != null ? _tuning.WheelMass : 100f;
		col.wheelDampingRate = 0.25f;
		col.suspensionDistance = _tuning != null ? _tuning.SuspensionDistance : 0.30f;

		JointSpring spring = col.suspensionSpring;
		spring.spring = _tuning != null ? _tuning.SpringForce : 50000f;
		spring.damper = _tuning != null ? _tuning.DamperForce : 4000f;
		spring.targetPosition = _tuning != null ? _tuning.TargetPosition : 0.55f;
		col.suspensionSpring = spring;

		WheelFrictionCurve forward = col.forwardFriction;
		forward.extremumSlip = 2f;
		forward.extremumValue = 1f;
		forward.asymptoteSlip = 0.8f;
		forward.asymptoteValue = 0.5f;
		forward.stiffness = _tuning != null ? _tuning.ForwardStiffness : 3f;
		col.forwardFriction = forward;

		WheelFrictionCurve sideways = col.sidewaysFriction;
		sideways.extremumSlip = 0.5f;
		sideways.extremumValue = 1f;
		sideways.asymptoteSlip = 0.5f;
		sideways.asymptoteValue = 0.75f;
		sideways.stiffness = _tuning != null ? _tuning.SidewaysStiffness : 2f;
		col.sidewaysFriction = sideways;

		// Smooth contact resolution: more substeps = less constraint impulse on first contact.
		col.ConfigureVehicleSubsteps(10f, 30, 20);

		return col;
	}

	private static VehicleDoorController.DoorBinding MakeDoor(
		VehicleDoorId _id, Transform _hinge, Transform _approach, Transform _exit, float _angle, bool _invert)
	{
		return new VehicleDoorController.DoorBinding
		{
			DoorId = _id,
			Hinge = _hinge,
			ApproachPoint = _approach,
			ExitPoint = _exit,
			OpenAngle = _angle,
			InvertOpen = _invert
		};
	}

	private static VehicleSeatLayout.SeatBinding MakeSeat(
		VehicleSeatId _id, Transform _anchor, VehicleDoorId _door, bool _litter)
	{
		return new VehicleSeatLayout.SeatBinding
		{
			SeatId = _id,
			Anchor = _anchor,
			PreferredDoor = _door,
			IsLitter = _litter
		};
	}

	private static Transform EnsureDoorHinge(
		Transform _root,
		Transform _doorMesh,
		string _hingeName,
		Vector3 _localPos)
	{
		// FindDeep: hinges may already live under BodyVisualRoot from a previous rebuild.
		Transform hinge = FindDeep(_root, _hingeName);
		if (hinge == null)
		{
			var go = new GameObject(_hingeName);
			hinge = go.transform;
			hinge.SetParent(_root, false);
			hinge.localPosition = _localPos;
			hinge.localRotation = Quaternion.identity;
		}

		if (_doorMesh != null && _doorMesh.parent != hinge)
		{
			Vector3 worldPos = _doorMesh.position;
			Quaternion worldRot = _doorMesh.rotation;
			_doorMesh.SetParent(hinge, true);
			_doorMesh.SetPositionAndRotation(worldPos, worldRot);
		}

		return hinge;
	}

	private static Transform EnsureChild(Transform _root, string _name, Vector3 _localPos)
	{
		Transform child = _root.Find(_name);
		if (child != null)
			return child;

		var go = new GameObject(_name);
		child = go.transform;
		child.SetParent(_root, false);
		child.localPosition = _localPos;
		child.localRotation = Quaternion.identity;
		return child;
	}

	/// <summary>
	/// Создаёт/обновляет точку подхода сбоку от панели двери.
	/// Если в префабе уже есть одноимённый dummy (например Approach_FL), он
	/// используется как есть — позволяет задать правильную точку вручную.
	/// </summary>
	private static Transform EnsureDoorApproachMarker(
		Transform _root,
		Transform _doorMesh,
		Transform _hinge,
		string _name,
		float _sideSign,
		float _fallbackZ,
		float _outwardMeters = 0.55f)
	{
		Transform marker = EnsureMarker(_root, _name, new Vector3(_sideSign * 2f, 0f, _fallbackZ), out bool created);
		marker.SetParent(_root, true);

		// Dummy из префаба: не трогаем позицию/поворот, доверяем настройке в редакторе.
		if (!created)
			return marker;

		// Автоматический расчёт только для свежесозданной точки.
		// Используем позицию шарнира, чтобы точка оказалась у двери, а не по
		// середине большой/смещённой меши.
		Vector3 doorLocal = new Vector3(_sideSign * 0.95f, 1f, _fallbackZ);
		if (_hinge != null)
			doorLocal = _root.InverseTransformPoint(_hinge.position);
		else if (_doorMesh != null)
			doorLocal = _root.InverseTransformPoint(_doorMesh.position);

		float side = Mathf.Sign(_sideSign);
		if (Mathf.Abs(side) < 0.01f)
			side = doorLocal.x >= 0f ? 1f : -1f;

		// Stand just outside the door panel (~0.55–0.75 m from door skin), not 2 m away.
		float lateral = Mathf.Abs(doorLocal.x) + _outwardMeters;
		lateral = Mathf.Clamp(lateral, 1.15f, 1.65f);
		Vector3 local = new Vector3(side * lateral, 0f, doorLocal.z);
		marker.localPosition = local;
		marker.localRotation = Quaternion.LookRotation(new Vector3(-side, 0f, 0f), Vector3.up);
		return marker;
	}

	/// <summary>
	/// Создаёт служебную точку (approach/exit). Если точка уже есть в префабе,
	/// возвращает её без изменения позиции — это позволяет расставить dummy-точки
	/// вручную в редакторе и не перезаписывать их автоматическим расчётом.
	/// </summary>
	private static Transform EnsureMarker(Transform _root, string _name, Vector3 _localPos, out bool _created)
	{
		Transform child = _root.Find(_name);
		if (child != null)
		{
			_created = false;
			return child;
		}

		// May live under BodyVisualRoot from older setups — pull back to root.
		child = FindDeep(_root, _name);
		if (child != null)
		{
			child.SetParent(_root, true);
			_created = false;
			return child;
		}

		var go = new GameObject(_name);
		child = go.transform;
		child.SetParent(_root, false);
		child.localPosition = _localPos;
		child.localRotation = Quaternion.identity;
		_created = true;
		return child;
	}

	private static Transform FindDeep(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;
		if (_root.name == _name)
			return _root;

		for (int i = 0; i < _root.childCount; i++)
		{
			Transform found = FindDeep(_root.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}
	#endregion
}

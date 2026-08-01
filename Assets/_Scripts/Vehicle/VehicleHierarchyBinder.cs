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

		_vehicle.GlassController?.Configure(root);

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

		_vehicle.EnsurePhysicsDrive();
		if (!_vehicle.TryGetComponent(out WheeledMotor wheeledMotor))
			return;

		float steerAngle = 32f;
		if (_vehicle.Brain != null && _vehicle.Brain.Tuning != null)
			steerAngle = _vehicle.Brain.Tuning.DefaultSteerAngle;

		var axles = new List<WheelAxle>(4);
		TryAddPhysicsAxle(axles, _vehicle.transform, _wheelFl, "WheelCollider_FL", true, true, steerAngle);
		TryAddPhysicsAxle(axles, _vehicle.transform, _wheelFr, "WheelCollider_FR", true, true, steerAngle);
		TryAddPhysicsAxle(axles, _vehicle.transform, _wheelRl, "WheelCollider_RL", true, false, steerAngle);
		TryAddPhysicsAxle(axles, _vehicle.transform, _wheelRr, "WheelCollider_RR", true, false, steerAngle);

		if (axles.Count > 0)
			wheeledMotor.SetAxles(axles.ToArray());

		if (_vehicle.Brain != null && _vehicle.Brain.Tuning != null)
			wheeledMotor.ApplyTuning(_vehicle.Brain.Tuning);

		// WheelColliders are created after the unit blocker, so the blocker must be told
		// to ignore non-wheel drive colliders (WC skipped — Unity 6 ground desync).
		_vehicle.UnitBlocker?.RefreshIgnoredDriveColliders();
		_vehicle.BounceWheelCollidersAfterBind("bind-wheels");
	}

	private static void TryAddPhysicsAxle(
		List<WheelAxle> _axles,
		Transform _root,
		Transform _visual,
		string _colliderName,
		bool _motor,
		bool _steer,
		float _steerAngle)
	{
		if (_visual == null || _root == null)
			return;

		WheelCollider col = EnsureWheelCollider(_root, _visual, _colliderName);
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

	private static WheelCollider EnsureWheelCollider(Transform _root, Transform _visual, string _name)
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

		// Match Low_Poly_Vehicles_Controller (project 000) BRDM2 wheel setup.
		const float radius = 0.45f;
		col.radius = radius;
		col.forceAppPointDistance = -1f;
		col.center = Vector3.zero;
		col.mass = 100f;
		col.wheelDampingRate = 0.25f;
		// Unity 6000.4: keep the contact-proven travel. Mid targetPosition (0.55) + longer
		// travel previously left WC grounded=0 and freefall without support box.
		col.suspensionDistance = 0.18f;

		JointSpring spring = col.suspensionSpring;
		spring.spring = 35000f;
		spring.damper = 4500f;
		// 1 = rest at full compression → body sits lower; proven grounded=4/4 in this project.
		spring.targetPosition = 1f;
		col.suspensionSpring = spring;

		WheelFrictionCurve forward = col.forwardFriction;
		forward.extremumSlip = 2f;
		forward.extremumValue = 1f;
		forward.asymptoteSlip = 0.8f;
		forward.asymptoteValue = 0.5f;
		forward.stiffness = 3f;
		col.forwardFriction = forward;

		WheelFrictionCurve sideways = col.sidewaysFriction;
		sideways.extremumSlip = 0.5f;
		sideways.extremumValue = 1f;
		sideways.asymptoteSlip = 0.5f;
		sideways.asymptoteValue = 0.75f;
		sideways.stiffness = 2f;
		col.sidewaysFriction = sideways;

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

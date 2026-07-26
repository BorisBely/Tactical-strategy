using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Polygone/Vehicle Physics/Vehicle Physics Profile", fileName = "VehicleProfile_")]
public sealed class VehiclePhysicsProfile : ScriptableObject
{
	#region Nested Types
	[Serializable]
	public sealed class EngineSettings
	{
		[Tooltip("Кривая крутящего момента (Н·м) по оборотам (RPM)")]
		public AnimationCurve TorqueCurve = AnimationCurve.Linear(800f, 200f, 4000f, 500f);
		[Tooltip("Холостой ход (RPM)"), Min(100f)]
		public float IdleRPM = 800f;
		[Tooltip("Максимальные обороты / отсечка"), Min(500f)]
		public float MaxRPM = 4500f;
		[Tooltip("Торможение двигателем при нулевом газе (Н·м)"), Min(0f)]
		public float EngineBrakeTorque = 80f;
		[Tooltip("Момент инерции двигателя (кг·м²)"), Min(0.01f)]
		public float EngineInertia = 0.35f;
		[Tooltip("Скорость отклика на газ"), Min(0.1f)]
		public float ThrottleResponse = 4f;
	}

	[Serializable]
	public sealed class TransmissionSettings
	{
		[Tooltip("Передаточные числа (первое = задний ход, остальные = 1..N)")]
		public float[] GearRatios = { -3.5f, 3.8f, 2.1f, 1.4f, 1.0f, 0.75f };
		[Tooltip("Главная пара"), Min(1f)]
		public float FinalDrive = 3.73f;
		[Tooltip("Обороты повышения передачи"), Min(100f)]
		public float ShiftUpRPM = 3800f;
		[Tooltip("Обороты понижения передачи"), Min(100f)]
		public float ShiftDownRPM = 1800f;
		[Tooltip("Время переключения (сек)"), Min(0f)]
		public float ShiftTime = 0.2f;
		[Tooltip("Максимальный момент через сцепление (Н·м)"), Min(0f)]
		public float ClutchMaxTorque = 2500f;
	}

	[Serializable]
	public sealed class DifferentialSettings
	{
		public enum Type { Open, Locked, LimitedSlip }
		[Tooltip("Тип дифференциала")]
		public Type DiffType = Type.Open;
		[Tooltip("Порог разницы RPM для LimitedSlip"), Min(0f)]
		public float LockThreshold = 50f;
		[Tooltip("Сила блокировки (0 = open, 1 = fully locked)"), Range(0f, 1f)]
		public float LockStrength = 0.6f;
		[Tooltip("Преднатяг (Н·м)"), Min(0f)]
		public float Preload = 50f;
	}

	[Serializable]
	public sealed class DrivetrainSettings
	{
		public enum Type { FWD, RWD, AWD, SixWheel, EightWheel, Tracked }
		[Tooltip("Тип привода")]
		public Type DriveType = Type.AWD;
		[Tooltip("Распределение момента по осям (сумма = 1)")]
		public float[] TorqueSplit = { 0.4f, 0.6f };
		[Tooltip("Блокировка межосевого дифференциала"), Range(0f, 1f)]
		public float CenterDiffLock;
	}

	[Serializable]
	public sealed class SuspensionSettings
	{
		[Tooltip("Желаемая просадка под собственным весом (м)"), Min(0.001f)]
		public float DesiredSag = 0.06f;
		[Tooltip("Дорожный просвет (м)"), Min(0.01f)]
		public float RideHeight = 0.4f;
		[Tooltip("Полный ход подвески (м)"), Min(0.01f)]
		public float Travel = 0.25f;
		[Tooltip("Собственная частота (Гц, 0.8-2.5)"), Range(0.5f, 5f)]
		public float NaturalFrequency = 1.4f;
		[Tooltip("Коэффициент демпфирования (0.4-1.0)"), Range(0.1f, 2f)]
		public float DampingRatio = 0.7f;
		[Tooltip("Отношение отбоя к сжатию"), Min(1f)]
		public float DamperReboundRatio = 2f;
		[Tooltip("Положение покоя (0 = вывешено, 1 = полностью сжато)"), Range(0f, 1f)]
		public float TargetPosition = 0.5f;
		[Tooltip("Жёсткость стабилизатора поперечной устойчивости (Н·м/рад)"), Min(0f)]
		public float AntiRollStiffness = 5000f;
	}

	[Serializable]
	public sealed class WheelSettings
	{
		[Tooltip("Радиус колеса (м)"), Min(0.1f)]
		public float Radius = 0.45f;
		[Tooltip("Масса колеса (кг)"), Min(1f)]
		public float Mass = 100f;
		[Tooltip("Ширина колеса (м)"), Min(0.05f)]
		public float Width = 0.3f;
		[Tooltip("Момент инерции колеса (кг·м²)"), Min(0.01f)]
		public float Inertia = 2.5f;
	}

	[Serializable]
	public sealed class TireSettings
	{
		public enum TireTypeEnum { Road, OffRoad, AllTerrain, Mud, Sand }
		[Tooltip("Тип шины")]
		public TireTypeEnum TireType = TireTypeEnum.AllTerrain;
		[Tooltip("Коэффициент продольного сцепления"), Range(0.1f, 2f)]
		public float ForwardGrip = 0.95f;
		[Tooltip("Коэффициент бокового сцепления"), Range(0.1f, 2f)]
		public float LateralGrip = 0.85f;
		[Tooltip("Сопротивление качению (коэффициент)"), Range(0f, 0.5f)]
		public float RollingResistance = 0.03f;
		[Tooltip("Штраф сцепления на мокрой поверхности"), Range(0.1f, 1f)]
		public float WetPenalty = 0.7f;
	}

	[Serializable]
	public sealed class StabilitySettings
	{
		[Tooltip("Предельная угловая скорость до Safety (°/с)"), Min(0f)]
		public float MaxAngularSpeed = 120f;
		[Tooltip("Максимальное время в воздухе до Recovery (с)"), Min(0f)]
		public float MaxAirborneTime = 0.5f;
		[Tooltip("Максимальная сила удара до подавления (Н)"), Min(0f)]
		public float MaxBounceForce = 50000f;
		[Tooltip("Момент антипереворота (Н·м)"), Min(0f)]
		public float AntiFlipTorque = 20000f;
		[Tooltip("Защита от NaN/Inf скорости")]
		public bool NumericalGuardEnabled = true;
	}

	[Serializable]
	public sealed class AerodynamicsSettings
	{
		[Tooltip("Коэффициент лобового сопротивления Cₓ"), Range(0.1f, 2f)]
		public float DragCoefficient = 0.6f;
		[Tooltip("Лобовая площадь (м²)"), Min(0.5f)]
		public float FrontalArea = 3.5f;
		[Tooltip("Плотность воздуха (кг/м³)")]
		public float AirDensity = 1.225f;
	}

	[Serializable]
	public sealed class SteeringSettings
	{
		[Tooltip("Максимальный угол поворота колёс (°)"), Range(1f, 60f)]
		public float MaxSteerAngle = 30f;
		[Tooltip("Скорость поворота руля (°/с)"), Min(1f)]
		public float SteerRate = 160f;
		[Tooltip("Коэффициент Аккермана (0 = параллельно, 1 = полный)"), Range(0f, 1f)]
		public float Ackermann = 0.5f;
	}
	#endregion

	#region Serialized Fields
	[Header("Масса и центр масс")]
	[SerializeField, Min(100f), Tooltip("Полная масса (кг)")]
	private float m_Mass = 2200f;
	[SerializeField, Tooltip("Центр масс пустой машины (локальные координаты)")]
	private Vector3 m_BaseCenterOfMass = new(0f, -0.4f, 0.1f);

	[Header("Скоростные ограничения")]
	[SerializeField, Min(1f), Tooltip("Максимальная скорость вперёд (км/ч)")]
	private float m_MaxSpeedKmh = 90f;
	[SerializeField, Min(1f), Tooltip("Максимальная скорость заднего хода (км/ч)")]
	private float m_MaxReverseSpeedKmh = 20f;

	[Header("Тормоза")]
	[SerializeField, Min(0f), Tooltip("Максимальный тормозной момент (Н·м)")]
	private float m_MaxBrakeTorque = 5000f;
	[SerializeField, Min(0f), Tooltip("Мягкий тормоз (Н·м)")]
	private float m_SoftBrakeTorque = 1600f;
	[SerializeField, Min(0f), Tooltip("Торможение при отпущенном газе (Н·м)")]
	private float m_CoastDecelTorque = 450f;
	[SerializeField, Range(0f, 1f), Tooltip("Баланс перед/зад (0 = только зад, 1 = только перед)")]
	private float m_BrakeBalance = 0.6f;

	[Header("Модули")]
	[SerializeField] private EngineSettings m_Engine = new();
	[SerializeField] private TransmissionSettings m_Transmission = new();
	[SerializeField] private DifferentialSettings m_Differential = new();
	[SerializeField] private DrivetrainSettings m_Drivetrain = new();
	[SerializeField] private SuspensionSettings m_Suspension = new();
	[SerializeField] private WheelSettings m_Wheel = new();
	[SerializeField] private TireSettings m_Tire = new();
	[SerializeField] private StabilitySettings m_Stability = new();
	[SerializeField] private AerodynamicsSettings m_Aerodynamics = new();
	[SerializeField] private SteeringSettings m_Steering = new();
	#endregion

	#region Public Properties
	public float Mass => m_Mass;
	public Vector3 BaseCenterOfMass => m_BaseCenterOfMass;
	public float MaxSpeedKmh => m_MaxSpeedKmh;
	public float MaxReverseSpeedKmh => m_MaxReverseSpeedKmh;
	public float MaxBrakeTorque => m_MaxBrakeTorque;
	public float SoftBrakeTorque => m_SoftBrakeTorque;
	public float CoastDecelTorque => m_CoastDecelTorque;
	public float BrakeBalance => m_BrakeBalance;
	public EngineSettings Engine => m_Engine;
	public TransmissionSettings Transmission => m_Transmission;
	public DifferentialSettings Differential => m_Differential;
	public DrivetrainSettings Drivetrain => m_Drivetrain;
	public SuspensionSettings Suspension => m_Suspension;
	public WheelSettings Wheel => m_Wheel;
	public TireSettings Tire => m_Tire;
	public StabilitySettings Stability => m_Stability;
	public AerodynamicsSettings Aerodynamics => m_Aerodynamics;
	public SteeringSettings Steering => m_Steering;
	#endregion
}

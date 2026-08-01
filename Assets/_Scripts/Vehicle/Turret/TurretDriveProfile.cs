using System;
using UnityEngine;

/// <summary>
/// Тип привода турели. Mechanical — ручной (нет щитов), Electric — электромоторы (установлен щит).
/// </summary>
public enum TurretDriveType
{
	Mechanical,
	Electric
}

/// <summary>
/// Параметры разгона/торможения одной оси поворота (тура или база).
/// </summary>
[Serializable]
public class YawAxisProfile
{
	public float MaxSpeed;
	public float Acceleration;
	public float Deceleration;
}

/// <summary>
/// Полный профиль механики привода турели: турель + база + люфт + точность.
/// </summary>
[Serializable]
public class TurretDriveProfile
{
	public YawAxisProfile TurretAxis = new YawAxisProfile();
	public YawAxisProfile BaseAxis = new YawAxisProfile();

	[Tooltip("Люфт при смене направления, градусы")]
	public float Backlash;

	[Tooltip("Допустимая ошибка наведения, градусы. Внутри этого сектора механизм считает цель наведённой")]
	public float AimTolerance;
}

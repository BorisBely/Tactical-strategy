using UnityEngine;

/// <summary>
/// Вешается на корень префаба оружия в руке. Точка входа для данных оружия: ствол, в будущем — патроны, состояние и т.д.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(60)]
public sealed class EquippedWeapon : MonoBehaviour
{
	#region Serialized Fields
	[Header("Ствол")]
	[Tooltip("Пустышка на конце дула: позиция и forward — линия выстрела. Если пусто — для геймплея берётся transform этого компонента (часто рукоять, не дуло).")]
	[SerializeField] private Transform m_Barrel;

	[Header("Прицел (зрение в «готов»)")]
	[Tooltip("Пустышка прицела: <c>UnitVision</c> берёт отсюда конус FOV и LOS при оружии на готове (ось = forward).")]
	[SerializeField] private Transform m_SightPivot;

	[Header("Отладка")]
	[Tooltip("Луч из пустышки Barrel (только если она назначена). В Game view включи Gizmos на вкладке Game.")]
	[SerializeField] private bool m_DrawBarrelDebugRay;
	[SerializeField, Min(0.01f)] private float m_BarrelDebugRayLength = 4f;
	[SerializeField] private Color m_BarrelDebugRayColor = new Color(0f, 0.92f, 1f, 1f);
	#endregion

	#region Public Properties
	/// <summary>Точка выстрела: позиция и <c>forward</c> — направление ствола.</summary>
	public Transform BarrelTransform => m_Barrel != null ? m_Barrel : transform;

	/// <summary>Прицел для конуса зрения; null если не задан.</summary>
	public Transform SightPivotTransform => m_SightPivot;
	#endregion

	#region Unity Lifecycle
	private void LateUpdate()
	{
		if (!m_DrawBarrelDebugRay || m_Barrel == null || !Application.isPlaying)
			return;

		Transform b = m_Barrel;
		Debug.DrawRay(b.position, b.forward * m_BarrelDebugRayLength, m_BarrelDebugRayColor);
	}

	private void OnDrawGizmos()
	{
		if (!m_DrawBarrelDebugRay || m_Barrel == null)
			return;

		Gizmos.color = m_BarrelDebugRayColor;
		Vector3 start = m_Barrel.position;
		Vector3 end = start + m_Barrel.forward * m_BarrelDebugRayLength;
		Gizmos.DrawLine(start, end);
	}
	#endregion
}

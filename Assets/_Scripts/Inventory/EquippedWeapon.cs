using UnityEngine;

/// <summary>
/// Вешается на корень префаба оружия в руке. Точка входа для данных оружия: ствол, в будущем — патроны, состояние и т.д.
/// </summary>
[DisallowMultipleComponent]
public sealed class EquippedWeapon : MonoBehaviour
{
	#region Serialized Fields
	[Header("Ствол")]
	[Tooltip("Объект дула: forward = ось ствола / направление выстрела. Если пусто — используется transform этого объекта.")]
	[SerializeField] private Transform m_Barrel;

	[Header("Отладка")]
	[SerializeField] private bool m_DrawDebugAimRay;
	[SerializeField, Min(0.1f)] private float m_DebugAimRayLength = 4f;
	[SerializeField] private Color m_DebugAimRayColor = new Color(0f, 1f, 1f, 0.9f);
	#endregion

	#region Public Properties
	/// <summary>Точка выстрела: позиция и <c>forward</c> — направление ствола.</summary>
	public Transform BarrelTransform => m_Barrel != null ? m_Barrel : transform;
	#endregion

	#region Unity Lifecycle
	private void LateUpdate()
	{
		if (!m_DrawDebugAimRay)
			return;

		Transform b = BarrelTransform;
		Debug.DrawRay(b.position, b.forward * m_DebugAimRayLength, m_DebugAimRayColor);
	}
	#endregion
}

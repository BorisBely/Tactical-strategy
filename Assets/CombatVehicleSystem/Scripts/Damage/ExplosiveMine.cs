using UnityEngine;

namespace CombatVehicleSystem
{
	public class ExplosiveMine : MonoBehaviour
	{
		#region Serialized Fields
		[SerializeField] private float m_ExplosionForce = 12000f;
		#endregion

		#region Public Properties
		public float ExplosionForce => m_ExplosionForce;
		#endregion
	}
}

using UnityEngine;

/// <summary>
/// Physics look probe for #13.3 Play. Linecast is not a shot.
/// </summary>
public sealed class PhysicsCoverLosProbe : ICoverLineOfSightProbe
{
	#region Public Methods
	public bool HasClearLook(Vector3 _from, Vector3 _to)
	{
		return !Physics.Linecast(_from, _to, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
	}
	#endregion
}

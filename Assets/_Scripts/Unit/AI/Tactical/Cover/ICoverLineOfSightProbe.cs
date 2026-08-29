using UnityEngine;

/// <summary>
/// Look probe for #13.3 visibility / fire-lane. Not a shot. Not G6.
/// </summary>
public interface ICoverLineOfSightProbe
{
	bool HasClearLook(Vector3 _from, Vector3 _to);
}

using UnityEngine;

/// <summary>
/// Can a soldier's body occupy this point? Not crouch vs standing classification.
/// </summary>
public interface ICoverClearanceProbe
{
	bool HasBodyClearance(Vector3 _position, Vector3 _normal);
}

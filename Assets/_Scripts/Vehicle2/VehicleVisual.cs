using UnityEngine;

/// <summary>
/// Spins visual wheel meshes based on WheelState.Rpm.
/// No physics — pure visual.
/// </summary>
public class VehicleVisual
{
	private readonly Transform[] m_WheelVisuals;
	private readonly string[] m_WheelMeshNames =
	{
		"SM_Veh_Light_Armored_Car_01_Wheel_fl",
		"SM_Veh_Light_Armored_Car_01_Wheel_fr",
		"SM_Veh_Light_Armored_Car_01_Wheel_rl",
		"SM_Veh_Light_Armored_Car_01_Wheel_rr",
	};

	public VehicleVisual(Transform root)
	{
		m_WheelVisuals = new Transform[4];
		for (int i = 0; i < 4; i++)
			m_WheelVisuals[i] = FindDeep(root, m_WheelMeshNames[i]);
	}

	public void Update(WheelState[] states)
	{
		for (int i = 0; i < 4; i++)
		{
			if (m_WheelVisuals[i] == null) continue;
			m_WheelVisuals[i].Rotate(Vector3.right, states[i].Rpm * 6f * Time.deltaTime, Space.Self);
		}
	}

	private static Transform FindDeep(Transform root, string name)
	{
		if (root == null || string.IsNullOrEmpty(name)) return null;
		if (root.name == name) return root;
		for (int i = 0; i < root.childCount; i++)
		{
			Transform found = FindDeep(root.GetChild(i), name);
			if (found != null) return found;
		}
		return null;
	}
}

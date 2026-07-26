namespace VehicleNavigation
{
	public sealed class PathBuildOptions
	{
		public bool AllowPartialPath { get; set; } = true;
		public bool AllowDirectFallback { get; set; } = true;
		public float SampleRadiusFrom { get; set; } = 3f;
		public float SampleRadiusTo { get; set; } = 4f;

		public static PathBuildOptions Default => new PathBuildOptions();

		public static PathBuildOptions SafeOnly => new PathBuildOptions
		{
			AllowDirectFallback = false,
			AllowPartialPath = false
		};

		public static PathBuildOptions ForReverse => new PathBuildOptions
		{
			SampleRadiusFrom = 5f,
			SampleRadiusTo = 5f
		};
	}
}

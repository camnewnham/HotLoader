using Grasshopper.Kernel;

namespace HotComponent
{
    /// <summary>
    /// Placeholder that will be replaced with a real ad-hoc component at runtime.
    /// </summary>
    public class PlaceholderComponent : HotComponent
    {
        public PlaceholderComponent() : base("Ad Hoc Placeholder", "Ad Hoc", "Placeholder for ad-hoc components") { }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "This is a placeholder. Create a project or load a dll from the right click menu.");
        }
    }
}

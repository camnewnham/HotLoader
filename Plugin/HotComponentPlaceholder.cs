using Grasshopper.Kernel;

namespace HotComponents
{
    /// <summary>
    /// Placeholder that will be replaced with a real ad-hoc component at runtime.
    /// </summary>
    public class HotComponentPlaceholder : HotComponent
    {
        public HotComponentPlaceholder() : base("Hot Placeholder", "Hot", "Placeholder for hot component") { }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "This is a placeholder. Create a project or load a dll from the right click menu.");
        }
    }
}

using Grasshopper.Kernel;

namespace HotLoader
{
    /// <summary>
    /// Placeholder that will be replaced with a real hot component at runtime.
    /// </summary>
    public class HotComponentPlaceholder : HotComponentBase
    {
        public HotComponentPlaceholder() : base("Custom C# Component", "C#", "A custom component written in C#") { }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "This is a placeholder. Double click to edit the source code with your native C# editor.");
        }
    }
}

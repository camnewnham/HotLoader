using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;

namespace HotLoader
{
    /// <summary>
    /// Simple attributes that enables double-clicking to edit the source of a <see cref="HotComponentBase"/>
    /// </summary>
    internal class HotComponentAttributes : GH_ComponentAttributes
    {
        public HotComponentAttributes(IGH_Component component) : base(component)
        {
            if (!(component is HotComponentBase))
            {
                throw new InvalidOperationException($"Can not create {nameof(HotComponentAttributes)} for {component?.GetType().FullName}");
            }
        }

        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (ContentBox.Contains(e.CanvasLocation))
            {
                HotComponentBase component = Owner as HotComponentBase;
                if (component != null)
                {
                    component.EditSourceProject();
                    return GH_ObjectResponse.Handled;
                }
            }
            return base.RespondToMouseDoubleClick(sender, e);
        }
    }
}

using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace HotComponent
{
    public class PluginInfo : GH_AssemblyInfo
    {
        public override string Name => "Ad Hoc";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "Ad hoc components for hot-loading";

        public override Guid Id => new Guid("af2b08ae-f8c1-4131-8475-9df2c44e17e3");

        //Return a string identifying you or your company.
        public override string AuthorName => "Cameron Newnham";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "https://github.com/camnewnham";
    }
}
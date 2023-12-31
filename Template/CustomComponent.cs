﻿using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using HotLoader;

public class MyHotComponent : HotComponentBase
{
    public MyHotComponent() : base("Custom Component", "Custom", "My custom addition component")
    {
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("First number", "A", "The first number to add", GH_ParamAccess.item);
        pManager.AddNumberParameter("Second number", "B", "The second number to add", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Result", "C", "The output of the addition", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        double a = 0;
        double b = 0;
        if (!DA.GetData(0, ref a))
        {
            return;
        }
        if (!DA.GetData(1, ref b))
        {
            return;
        }
        DA.SetData(0, new GH_Number(a + b));
    }
}
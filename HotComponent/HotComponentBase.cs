using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace HotComponent
{
    public abstract class HotComponentBase : GH_Component
    {
        private static string AssemblyTemplateFolder => Path.Combine(Path.GetDirectoryName(Assembly.GetAssembly(typeof(HotComponentBase)).Location), "template");

        protected override System.Drawing.Bitmap Icon => null;
        public sealed override Guid ComponentGuid => new Guid("82fdaf19-4493-44f7-b394-630218e6808c");

        public HotComponentBase(string name, string nickname, string description)
          : base(name, nickname, description, "Ad Hoc", "Ad Hoc")
        {
        }

        /// <summary>
        /// The binary data containing the inherited component dll. Second priority load.
        /// </summary>
        private byte[] m_binary = null;

        /// <summary>
        /// Serialized data belonging to the child (inherited) component.
        /// </summary>
        private byte[] m_chunk = null;

        /// <summary>
        /// The number of input components at serialization time
        /// </summary>
        private int m_inputParamCount = 0;
        /// <summary>
        /// The number of output components at serialization time
        /// </summary>
        private int m_outputParamCount = 0;

        /// <summary>
        /// If this component is due for a replacement, this is the component that should replace it.
        /// </summary>
        private bool m_pendingComponentLoad;

        public sealed override bool ReadFull(GH_IReader reader)
        {
            return base.ReadFull(reader);
        }

        public sealed override bool WriteFull(GH_IWriter writer)
        {
            return base.WriteFull(writer);
        }

        public sealed override bool Read(GH_IReader reader)
        {
            reader.TryGetInt32("inputCount", ref m_inputParamCount);
            reader.TryGetInt32("outputCount", ref m_outputParamCount);

            if (reader.ItemExists("binary"))
            {
                m_binary = reader.GetByteArray("binary");
            }

            if (this is PlaceholderComponent)
            {
                m_pendingComponentLoad = m_binary != null;

                for (int i = 0; i < m_inputParamCount; i++)
                {
                    Params.RegisterInputParam(new Param_GenericObject());
                }
                for (int i = 0; i < m_outputParamCount; i++)
                {
                    Params.RegisterOutputParam(new Param_GenericObject());
                }
            }

            if (reader.ItemExists("adhoc_chunk"))
            {
                m_chunk = reader.GetByteArray("adhoc_chunk");
            }

            ReadNestedChunk(this);

            return base.Read(reader);
        }

        public sealed override bool Write(GH_IWriter writer)
        {
            writer.SetInt32("inputCount", Params.Input.Count);
            writer.SetInt32("outputCount", Params.Output.Count);
            if (m_binary != null)
            {
                writer.SetByteArray("binary", m_binary);
            }
            WriteNestedChunk(writer);

            return base.Write(writer);
        }

        /// <summary>
        /// Reads the chunk belonging to the inherited component
        /// </summary>
        private void ReadNestedChunk(HotComponentBase component)
        {
            if (m_chunk != null)
            {
                using (MemoryStream ms = new MemoryStream(m_chunk))
                {
                    using (BinaryReader br = new BinaryReader(ms))
                    {
                        GH_LooseChunk chunk = new GH_LooseChunk("adhoc_chunk");
                        chunk.Read(br);
                        component.OnRead(chunk);
                    }
                }
            }
        }

        /// <summary>
        /// Writes the chunk belonging to the inherited component
        /// </summary>
        private void WriteNestedChunk(GH_IWriter writer)
        {
            GH_LooseChunk chunk = new GH_LooseChunk("adhoc_chunk");
            OnWrite(chunk);
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    chunk.Write(bw);
                }
                writer.SetByteArray("adhoc_chunk", ms.ToArray());
            }
        }

        /// <summary>
        /// <inheritdoc />
        /// Note to inheritors: ensure you call the base method.
        /// </summary>
        public override void AddedToDocument(GH_Document document)
        {
            if (m_pendingComponentLoad)
            {
                m_pendingComponentLoad = false;
                if (document.Context == GH_DocumentContext.None)
                {
                    document.ContextChanged += OnDocumentContextChanged;
                }
                else
                {
                    ReplaceComponent(LoadComponentFromByteArray(m_binary));
                }
            }
            base.AddedToDocument(document);
        }

        private void OnDocumentContextChanged(object sender, GH_DocContextEventArgs e)
        {
            e.Document.ContextChanged -= OnDocumentContextChanged;
            ReplaceComponent(LoadComponentFromByteArray(m_binary));
        }

        /// <summary>
        /// Override this to read custom data.
        /// </summary>
        /// <param name="reader">The GH reader</param>

        protected virtual void OnRead(GH_IReader reader) { }
        /// <summary>
        /// Override this to write custom data.  
        /// </summary>
        /// <param name="writer">The GH writer</param>
        protected virtual void OnWrite(GH_IWriter writer) { }

        /// <summary>
        /// Instantiates temporary parameters to retain wire hookups.  
        /// Override this to provide real parameters.
        /// </summary>
        /// <param name="pManager">The input parameter manager</param>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            for (int i = 0; i < m_inputParamCount; i++)
            {
                pManager.AddGenericParameter("Placeholder", "P", "Temporary parameter until this component is replaced by an inheritor.", GH_ParamAccess.tree);
            }
        }

        /// <summary>
        /// Instantiates temporary parameters to retain wire hookups.  
        /// Override this to provide real parameters.
        /// </summary>
        /// <param name="pManager">The output parameter manager</param>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            for (int i = 0; i < m_outputParamCount; i++)
            {
                pManager.AddGenericParameter("Placeholder", "P", "Temporary parameter until this component is replaced by an inheritor.", GH_ParamAccess.tree);
            }
        }

        /// <summary>
        /// Adds additional menu items to the right click menu.  
        /// Note: ensure you call the base implementation.
        /// </summary>
        /// <param name="menu">The toolstrip menu</param>
        public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            Menu_AppendItem(menu, "Select replacement", (obj, arg) => PickReplacement()).ToolTipText = $"Select a dll containing a hot component";
            if (this is PlaceholderComponent)
            {
                Menu_AppendItem(menu, "Generate project", (obj, arg) => GenerateNewProject()).ToolTipText = "Generates a .csproj from a template.";
            }
            base.AppendAdditionalMenuItems(menu);
        }

        /// <summary>
        /// Shows a file picker and attempts dll replacement
        /// </summary>
        private void PickReplacement()
        {
            OpenFileDialog dialog = new OpenFileDialog()
            {
                FileName = "Select a dll or gha",
                Filter = "Grasshopper Component Libraries (*.dll)|*.dll",
                Title = "Select dll"
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                using (Stream fs = dialog.OpenFile())
                {
                    if (LoadComponentFromStream(fs) is HotComponentBase component)
                    {
                        ReplaceComponent(component);
                    }
                }
            }
        }

        private void GenerateNewProject()
        {
            Debug.Assert(this is PlaceholderComponent, "New projects should only be generated for placeholder components. Existing components should instantiate the existing source code.");
            string folder = Path.Combine(Path.GetTempPath(), "HotComponents", Guid.NewGuid().ToString());
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            string csproj = null;
            foreach (FileInfo file in new DirectoryInfo(AssemblyTemplateFolder).GetFiles())
            {
                string newFilePath = Path.Combine(folder, file.Name);
                File.Copy(file.FullName, Path.Combine(folder, newFilePath));
                if (file.Extension == ".csproj")
                {
                    if (csproj != null)
                    {
                        throw new FileLoadException("Found multiple csproj files in template.");
                    }
                    csproj = newFilePath;
                }
            }
            if (csproj == null)
            {
                throw new FileNotFoundException("CSProj was not found when generating project.");
            }

            UpdateAssemblyReferences(csproj);


            Process.Start(csproj);
        }

        /// <summary>
        /// Updates a CSProj to reference this dll
        /// </summary>
        /// <param name="csprojPath">The path to the .csproj</param>
        private void UpdateAssemblyReferences(string csprojPath)
        {
            string assemblyPath = Assembly.GetAssembly(typeof(HotComponentBase)).Location;

            string txt = File.ReadAllText(csprojPath);
            string replaced = new Regex(@"(?<=<HintPath>).+HotComponent.gha(?=<\/HintPath>)").Replace(txt, assemblyPath);
            File.WriteAllText(csprojPath, replaced);
        }

        /// <summary>
        /// Loads a component from a file path. 
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private HotComponentBase LoadComponentFromFilePath(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            {
                return LoadComponentFromStream(fs);
            }
        }

        /// <summary>
        /// Loads a component from a byte stream.
        /// </summary>
        /// <param name="stream">The stream of thge dll</param>
        /// <exception cref="EntryPointNotFoundException">Thrown when either zero or multiple IGH_Component are found in the assembly.</exception>
        private HotComponentBase LoadComponentFromStream(Stream stream)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return LoadComponentFromByteArray(ms.ToArray());
            }
        }

        /// <summary>
        /// Loads a component from a byte array representing an assembly
        /// </summary>
        /// <param name="data">Binary representing the assembly dll</param>
        /// <returns>The component</returns>
        private HotComponentBase LoadComponentFromByteArray(byte[] data)
        {
            Assembly assm = Assembly.Load(data);

            Type[] componentTypes = assm.GetExportedTypes().Where(
                type => typeof(HotComponentBase).IsAssignableFrom(type)
                ).ToArray();

            if (componentTypes.Length != 1)
            {
                throw new EntryPointNotFoundException($"Found {componentTypes.Length} {nameof(IGH_Component)} in assembly but expected 1.");
            }

            HotComponentBase inst = Activator.CreateInstance(componentTypes[0]) as HotComponentBase;
            inst.m_binary = data;

            return inst;
        }

        /// <summary>
        /// Replaces the current component in the document with a new component.
        /// </summary>
        /// <param name="newComponent">The new component</param>
        private void ReplaceComponent(HotComponentBase newComponent)
        {
            if (newComponent == null)
            {
                throw new ArgumentNullException("newComponent");
            }

            GH_Document doc = OnPingDocument();

            int[] inputParamsToMigrate = new int[Math.Min(newComponent.Params.Input.Count, Params.Input.Count)];
            int[] outputParamsToMigrate = new int[Math.Min(newComponent.Params.Output.Count, Params.Output.Count)];

            for (int i = 0; i < inputParamsToMigrate.Length; i++)
            {
                inputParamsToMigrate[i] = i;
            }
            for (int i = 0; i < outputParamsToMigrate.Length; i++)
            {
                outputParamsToMigrate[i] = i;
            }

            GH_UpgradeUtil.MigrateInputParameters(this, newComponent, inputParamsToMigrate, inputParamsToMigrate);
            GH_UpgradeUtil.MigrateOutputParameters(this, newComponent, outputParamsToMigrate, outputParamsToMigrate);

            newComponent.CreateAttributes();
            newComponent.Attributes.Pivot = Attributes.Pivot;
            newComponent.Attributes.ExpireLayout();
            doc.DestroyAttributeCache();
            doc.DestroyObjectTable();
            int index = doc.Objects.IndexOf(this);
            doc.RemoveObject(this, update: false);

            ReadNestedChunk(newComponent);
            doc.AddObject(newComponent, update: false, index);

            doc.ScheduleSolution(5, (_) =>
            {
                newComponent.ExpireSolution(false);
            });
            Grasshopper.Instances.ActiveCanvas.Invalidate();
        }
    }
}
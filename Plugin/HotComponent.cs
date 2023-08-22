using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HotComponents
{
    /// <summary>
    /// Base class for implementing hot-load components.  
    /// Contains serialization and instantiation logic. 
    /// </summary>
    public abstract class HotComponent : GH_Component
    {
        /// <summary>
        /// Folder path for the template which contains an example implementation of a Hot Component
        /// </summary>
        private static string AssemblyTemplateFolder => Path.Combine(Path.GetDirectoryName(Assembly.GetAssembly(typeof(HotComponent)).Location), "template");
        protected override System.Drawing.Bitmap Icon => null;
        public sealed override Guid ComponentGuid => new Guid("82fdaf19-4493-44f7-b394-630218e6808c");

        /// <summary>
        /// File system watcher waiting for compilation changes
        /// </summary>
        private FileSystemWatcher m_fileSystemWatcher = null;

        /// <summary>
        /// Temporary state denoting that we are currently replacing this component due to file changes
        /// </summary>
        private bool m_reloadingFileChanges = false;

        public HotComponent(string name, string nickname, string description)
          : base(name, nickname, description, "Hot", "Hot")
        {
        }

        /// <summary>
        /// Path to the source for this component.  
        /// May not be assigned. 
        /// </summary>
        private string m_sourcePath = null;

        /// <summary>
        /// Zip of the source code for this component
        /// </summary>
        private byte[] m_source = null;

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
            reader.TryGetString("projectPath", ref m_sourcePath);

            if (reader.ItemExists("binary"))
            {
                m_binary = reader.GetByteArray("binary");
            }

            if (reader.ItemExists("source"))
            {
                m_source = reader.GetByteArray("source");
            }

            if (this is HotComponentPlaceholder)
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
            if (m_sourcePath != null)
            {
                writer.SetString("projectPath", m_sourcePath);
            }
            if (m_binary != null)
            {
                writer.SetByteArray("binary", m_binary);
            }
            if (m_source != null)
            {
                writer.SetByteArray("source", m_source);
            }
            WriteNestedChunk(writer);

            return base.Write(writer);
        }

        /// <summary>
        /// Reads the chunk belonging to the inherited component
        /// </summary>
        private void ReadNestedChunk(HotComponent component)
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
                    LoadComponentFromCache();
                }
            }
            StartFolderWatcher();
            base.AddedToDocument(document);
        }

        /// <summary>
        /// <inheritdoc />
        /// Note to inheritors: ensure you call the base method.
        /// </summary>
        public override void RemovedFromDocument(GH_Document document)
        {
            StopFolderWatcher();
            base.RemovedFromDocument(document);
        }

        /// <summary>
        /// Called once after a document finishes loading.  
        /// Used to ensure wire hookups are complete before we swap out a component.
        /// </summary>
        private void OnDocumentContextChanged(object sender, GH_DocContextEventArgs e)
        {
            e.Document.ContextChanged -= OnDocumentContextChanged;
            LoadComponentFromCache();
        }

        /// <summary>
        /// Loads a component from the serialized binary stored in this component.
        /// </summary>
        private void LoadComponentFromCache()
        {
            ReplaceComponent(LoadComponentFromByteArray(m_binary), m_sourcePath);
            StartFolderWatcher();
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
            Menu_AppendItem(menu, "Edit project", (obj, arg) => EditComponent()).ToolTipText = "Edits the code for this component.";
            base.AppendAdditionalMenuItems(menu);
        }

        /// <summary>
        /// Finds the .csproj in a folder
        /// </summary>
        /// <param name="path">The folder path</param>
        /// <returns>The path to the csproj in the folder</returns>
        private static string GetCsProj(string path)
        {
            foreach (FileInfo file in new DirectoryInfo(path).GetFiles("*.csproj"))
            {
                return file.FullName;
            }
            throw new FileNotFoundException($"Unable to find .csproj in {path}");
        }

        /// <summary>
        /// Creates a new project path string
        /// </summary>
        /// <returns>A temporary project path</returns>
        private static string CreateProjectPath()
        {
            return Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "HotComponents", Guid.NewGuid().ToString())).FullName;
        }

        /// <summary>
        /// Generates a new csproj from a template
        /// </summary>
        /// <returns>The path to the generated csproject</returns>
        private string GenerateNewProject()
        {
            Debug.Assert(this is HotComponentPlaceholder, "New projects should only be generated for placeholder components. Existing components should instantiate the existing source code.");
            Debug.Assert(m_source == null, "Source was not null when generating a new project");
            Debug.Assert(m_sourcePath == null, "Project path was not null when generating a new project");
            string folder = CreateProjectPath();
            foreach (FileInfo file in new DirectoryInfo(AssemblyTemplateFolder).GetFiles())
            {
                File.Copy(file.FullName, Path.Combine(folder, file.Name));
            }

            string csproj = GetCsProj(folder);
            UpdateAssemblyReferences(csproj);
            m_sourcePath = folder;
            return csproj;
        }

        /// <summary>
        /// Restores the cached project into a temporary folder.
        /// </summary>
        private string RestoreProject()
        {
            if (m_source == null)
            {
                throw new InvalidOperationException("Can not restore a project that does not have cached source code.");
            }


            string tmpZipFile = Path.Combine(Path.GetTempPath(), "HotComponents", "tmp.zip");
            if (File.Exists(tmpZipFile)) File.Delete(tmpZipFile);
            File.WriteAllBytes(tmpZipFile, m_source);
            string newPath = CreateProjectPath();

            ZipFile.ExtractToDirectory(tmpZipFile, newPath);

            string csproj = GetCsProj(newPath);
            UpdateAssemblyReferences(csproj);
            return csproj;
        }

        /// <summary>
        /// If the project already exists, open the existing file.  
        /// If it does not exist, instantiate the template and launch it.
        /// </summary>
        private void EditComponent()
        {
            if (m_sourcePath == null)
            {
                if (!(this is HotComponentPlaceholder))
                {
                    throw new InvalidOperationException("Can not edit a component that does not have a project.");
                }
                string csproj = GenerateNewProject();
                Process.Start(csproj);
            }
            else if (Directory.Exists(m_sourcePath))
            {
                Process.Start(GetCsProj(m_sourcePath));
            }
            else
            {
                Process.Start(RestoreProject());
            }
            UpdateAssemblyReferences(GetCsProj(m_sourcePath));
            StartFolderWatcher();
        }

        /// <summary>
        /// Caches the source code for the current project and stores in <see cref="m_source"/>
        /// </summary>
        private void CacheSource(string folder)
        {
            m_sourcePath = folder;

            if (!Directory.Exists(m_sourcePath))
            {
                throw new InvalidOperationException("Can not cache source code; project path does not exist.");
            }

            DirectoryInfo workingFolder = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "HotComponents", "Cache"));
            if (workingFolder.Exists)
            {
                workingFolder.Delete(true);
            }
            workingFolder.Create();

            // Copy source data
            CopyDirectoryRecursive(m_sourcePath, workingFolder.FullName, path => Path.GetFileName(path) is string name &&
                name != "bin" &&
                name != "obj" &&
                !name.StartsWith(".")
            );

            string tmpZipFile = Path.Combine(Path.GetTempPath(), "HotComponents", "tmp.zip");
            if (File.Exists(tmpZipFile)) File.Delete(tmpZipFile);

            ZipFile.CreateFromDirectory(workingFolder.FullName, tmpZipFile, CompressionLevel.Optimal, false, null);

            using (FileStream fs = File.OpenRead(tmpZipFile))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    fs.CopyTo(ms);
                    m_source = ms.ToArray();
                }
            };
        }

        /// <summary>
        /// Copies a directory recursively with a filter to exclude certain folders
        /// </summary>
        /// <param name="sourceDir">The source to copy</param>
        /// <param name="destinationDir">The destination</param>
        /// <param name="folderNameFilter">A function which returns true if a folder should be included. If null, all folders are included</param>
        /// <exception cref="DirectoryNotFoundException"></exception>
        private static void CopyDirectoryRecursive(string sourceDir, string destinationDir, Func<string, bool> folderNameFilter = null)
        {
            if (folderNameFilter != null && !folderNameFilter(sourceDir)) return;

            DirectoryInfo dir = new DirectoryInfo(sourceDir);

            DirectoryInfo[] dirs = dir.GetDirectories();

            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath);
            }

            foreach (DirectoryInfo subDir in dirs)
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectoryRecursive(subDir.FullName, newDestinationDir, folderNameFilter);
            }
        }

        /// <summary>
        /// Starts watching the <see cref="m_sourcePath"/> directory for file changes.
        /// </summary>
        private void StartFolderWatcher()
        {
            StopFolderWatcher();
            if (!Directory.Exists(m_sourcePath))
            {
                return;
            }

            string outputPath = Path.Combine(m_sourcePath, "bin");
            if (!Directory.Exists(outputPath))
            {
                // Ensure the bin directory exists
                Directory.CreateDirectory(outputPath);
            }

            m_fileSystemWatcher = new FileSystemWatcher(outputPath)
            {
                NotifyFilter = NotifyFilters.FileName,
                EnableRaisingEvents = true,
                Filter = "*.dll",
                IncludeSubdirectories = false,
            };
            m_fileSystemWatcher.Created += OnBinaryCreated;
        }

        /// <summary>
        /// Called when a dll file changes in the build output directory
        /// </summary>
        private void OnBinaryCreated(object sender, FileSystemEventArgs e)
        {
            if (m_reloadingFileChanges) return;
            m_reloadingFileChanges = true;
            StopFolderWatcher();
            string path = e.FullPath;
            Grasshopper.Instances.DocumentEditor.BeginInvoke((Action)(() =>
            {
                if (OnPingDocument() != null)
                {
                    CacheSource(Directory.GetParent(Path.GetDirectoryName(path)).FullName);
                    using (FileStream fs = File.OpenRead(path))
                    {
                        ReplaceComponent(LoadComponentFromStream(fs), m_sourcePath);
                    }
                    StartFolderWatcher();
                }
                m_reloadingFileChanges = false;
            }));
        }

        /// <summary>
        /// Stops any current file system watcher.
        /// </summary>
        private void StopFolderWatcher()
        {
            if (m_fileSystemWatcher != null)
            {
                m_fileSystemWatcher.Created -= OnBinaryCreated;
                m_fileSystemWatcher.Dispose();
                m_fileSystemWatcher = null;
            }
        }

        /// <summary>
        /// Updates a CSProj to reference this dll
        /// </summary>
        /// <param name="csprojPath">The path to the .csproj</param>
        private void UpdateAssemblyReferences(string csprojPath)
        {
            string assemblyPath = Assembly.GetAssembly(typeof(HotComponent)).Location;

            string txt = File.ReadAllText(csprojPath);
            string replaced = new Regex(@"(?<=<HintPath>).+HotComponents.gha(?=<\/HintPath>)").Replace(txt, assemblyPath);
            File.WriteAllText(csprojPath, replaced);
        }

        /// <summary>
        /// Loads a component from a file path. 
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private HotComponent LoadComponentFromFilePath(string path)
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
        private HotComponent LoadComponentFromStream(Stream stream)
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
        private HotComponent LoadComponentFromByteArray(byte[] data)
        {
            Assembly assm = Assembly.Load(data);

            Type[] componentTypes = assm.GetExportedTypes().Where(
                type => typeof(HotComponent).IsAssignableFrom(type)
                ).ToArray();

            if (componentTypes.Length != 1)
            {
                throw new EntryPointNotFoundException($"Found {componentTypes.Length} {nameof(IGH_Component)} in assembly but expected 1.");
            }

            HotComponent inst = Activator.CreateInstance(componentTypes[0]) as HotComponent;
            inst.m_binary = data;

            return inst;
        }

        /// <summary>
        /// Replaces the current component in the document with a new component.
        /// </summary>
        /// <param name="newComponent">The new component</param>
        private void ReplaceComponent(HotComponent newComponent, string pathToSource)
        {
            newComponent.m_sourcePath = pathToSource;

            GH_Document doc = OnPingDocument();

            SwapWires(this, newComponent);

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

        /// <summary>
        /// Swaps input and output wires from one component to another.  
        /// </summary>
        /// <param name="source">The source component</param>
        /// <param name="target">The target component</param>
        private static void SwapWires(IGH_Component source, IGH_Component target)
        {
            for (int i = 0; i < Math.Min(source.Params.Input.Count, target.Params.Input.Count); i++)
            {
                foreach (IGH_Param paramSource in source.Params.Input[i].Sources)
                {
                    target.Params.Input[i].AddSource(paramSource);
                }
            }

            for (int i = 0; i < Math.Min(source.Params.Output.Count, target.Params.Output.Count); i++)
            {
                foreach (IGH_Param recipient in source.Params.Output[i].Recipients)
                {
                    recipient.AddSource(target.Params.Output[i]);
                }
            }
        }
    }
}
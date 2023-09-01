﻿using Grasshopper.Kernel;
using System;
using System.Collections.Generic;

namespace HotLoader
{
    /// <summary>
    /// Hotscript manages lifecycle events for script components.  
    /// It allows you to provide subscribe and unsubscribe methods, equivalent to
    /// AddedToDocument and RemovedFromDocument in GH_Component
    /// </summary>
    public static class HotScript
    {
        /// <summary>
        /// Container for a mapping of script instances to components and actions
        /// </summary>
        private class ScriptInstance : IDisposable
        {
            public IGH_ScriptInstance Instance;
            public IGH_Component Component;
            private Action unsubscribe;

            public ScriptInstance(IGH_ScriptInstance instance, IGH_Component component, Func<Action> subscribe)
            {
                Instance = instance;
                Component = component;
                Component.SolutionExpired += OnSolutionExpired;
                unsubscribe = subscribe();
            }


            private void OnSolutionExpired(IGH_DocumentObject sender, GH_SolutionExpiredEventArgs e)
            {
                if (sender.OnPingDocument() == null)
                {
                    Dispose(); // Removed from document.. or document closing
                }
            }

            /// <summary>
            /// Called when this script instance needs cleaning up
            /// </summary>
            public void Dispose()
            {
                Component.SolutionExpired -= OnSolutionExpired;
                if (instanceMap.Remove(Component))
                {
                    unsubscribe();
                }
            }
        }

        /// <summary>
        /// Map of components to the instances inside them.  
        /// When an instance changes (i.e. is recompiled), we need to destroy the old one. 
        /// If a component 
        /// </summary>
        private static Dictionary<IGH_Component, ScriptInstance> instanceMap = new Dictionary<IGH_Component, ScriptInstance>();


        /// <summary>
        /// Registers a script instance for lifecycle events. The lifecycle events (subscribe and unsubscribe) are called only once per script.  
        /// </summary>
        /// <param name="instance">The script instance</param>
        /// <param name="subscribe">An action that performs initialization such as subscribing to events.</param>
        /// <param name="unsubscribe">An action that performs cleanup actions such as unsubscribing from events.</param>

        public static void Watch(this IGH_ScriptInstance instance, Action subscribe, Action unsubscribe)
        {
            Watch(instance, GetComponent(instance), () =>
            {
                subscribe();
                return unsubscribe;
            });
        }

        /// <summary>
        /// Registers a script instance. The instance must include the function signatures AddedToDocument(GH_Document doc) and RemovedFromDocument(GH_Document doc)
        /// </summary>
        /// <param name="instance">The script instance</param>
        public static void Watch(this IGH_ScriptInstance instance)
        {
            IGH_Component component = GetComponent(instance);
            Watch(instance, component, CreateGenericSubscribe(instance, component.OnPingDocument()));
        }

        /// <summary>
        /// Uses reflection to appropriately call AddedToDocument and RemovedFromDocument on a script component
        /// </summary>
        /// <returns>A function that is the subscriber and unsubscriber calling the referenced methods via reflection.</returns>
        private static Func<Action> CreateGenericSubscribe(IGH_ScriptInstance instance, GH_Document doc)
        {
            const string addMethodName = "AddedToDocument";
            const string removeMethodName = "RemovedFromDocument";

            System.Reflection.MethodInfo addedToDocumentMethod = instance.GetType().GetMethod(addMethodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (addedToDocumentMethod == null)
            {
                throw new MissingMethodException($"Unable to find {addMethodName}. Did you forget to define it in your script component?");
            }
            System.Reflection.ParameterInfo[] addArgs = addedToDocumentMethod.GetParameters();
            if (addArgs.Length > 1 || (addArgs.Length == 1 && addArgs[0].ParameterType != typeof(GH_Document)))
            {
                throw new MissingMethodException($"{addMethodName} did not have the correct arguments defined. It should have have no arguments, or a GH_Document");
            }

            System.Reflection.MethodInfo removedFromDocumentMethod = instance.GetType().GetMethod(removeMethodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (removedFromDocumentMethod == null)
            {
                throw new MissingMethodException($"Unable to find {removeMethodName}. Did you forget to define it in your script component?");
            }
            System.Reflection.ParameterInfo[] removeArgs = removedFromDocumentMethod.GetParameters();
            if (removeArgs.Length > 1 || (removeArgs.Length == 1 && removeArgs[0].ParameterType != typeof(GH_Document)))
            {
                throw new MissingMethodException($"{removeMethodName} did not have the correct arguments defined. It should have have no arguments, or a GH_Document");
            }

            return () =>
            {
                addedToDocumentMethod.Invoke(instance, addArgs.Length == 0 ? new object[0] : new object[] { doc });
                return () =>
                {
                    removedFromDocumentMethod.Invoke(instance, removeArgs.Length == 0 ? new object[0] : new object[] { doc });
                };
            };
        }


        /// <summary>
        /// Registers a script instance for lifecycle events. The lifecycle events (subscribe and unsubscribe) are called only once per script.  
        /// </summary>
        /// <param name="instance">The script instance</param>
        /// <param name="subscribe">A subscription action that returns and unsubscription action.</param>
        public static void Watch(this IGH_ScriptInstance instance, Func<Action> subscribe)
        {
            Watch(instance, GetComponent(instance), subscribe);
        }

        /// <summary>
        /// Registers a script instance for lifecycle events. The lifecycle events (subscribe and unsubscribe) are called only once per script.  
        /// </summary>
        /// <param name="instance">The script instance</param>
        /// <param name="component">The component that owns the instance</param>
        /// <param name="subscribe">A subscription action that returns and unsubscription action.</param>
        public static void Watch(this IGH_ScriptInstance instance, IGH_Component component, Func<Action> subscribe)
        {
            if (instanceMap.TryGetValue(component, out ScriptInstance existing))
            {
                if (existing.Instance.GetType() == instance.GetType())
                {
                    return; // No change
                }
                else
                {
                    // Destroy the old script
                    existing.Dispose();
                }
            }

            instanceMap.Add(component, new ScriptInstance(instance, component, subscribe));
        }

        /// <summary>
        /// Uses reflection to get the Component field of a script instance. 
        /// Syntactic sugar to avoid needing to always pass the two together
        /// </summary>
        /// <param name="scriptInstance">A script instance</param>
        /// <returns>The Component field of the script instance.</returns>
        private static IGH_Component GetComponent(IGH_ScriptInstance scriptInstance)
        {
            return scriptInstance.GetType().GetField("Component", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(scriptInstance) as IGH_Component;
        }

        /// <summary>
        /// Schedules a new solution on the component that owns a script instance
        /// </summary>
        /// <param name="instance">The script to expire</param>
        /// <param name="action">The action to execute, or null</param>
        public static void ScheduleSolution(this IGH_ScriptInstance instance, Action action = null)
        {
            ScheduleSolution(GetComponent(instance), action);
        }

        /// <summary>
        /// Schedules a new solution and executes an action.
        /// </summary>
        /// <param name="component">The component to expire</param>
        /// <param name="action">The action to execute, or null</param>
        public static void ScheduleSolution(this IGH_Component component, Action action = null)
        {
            Grasshopper.Instances.DocumentEditor.BeginInvoke((Action)(() =>
            {
                if (component.OnPingDocument() is GH_Document componentDoc)
                {
                    if (component.Locked)
                    {
                        return;
                    }

                    componentDoc.ScheduleSolution(5, (doc) =>
                    {
                        action?.Invoke();
                        component.ExpireSolution(false);
                    });
                }
                else
                {
                    Rhino.RhinoApp.WriteLine($"Received a call to {nameof(ScheduleSolution)} from a script that is no longer registered. Did you forget to unsubscribe from an event?");
                }
            }));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using TransformChangesDebugger.API.Patches;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TransformChangesDebugger.API
{
    /// <summary>
    /// Based on delegate changes to specific transforms / components can be excluded at runtime
    /// </summary>
    public delegate bool ShouldSkipTransformChangeExecutionPredicate(IlWeavedValuesArray ilWeavedValuesArray, Component changingComponent);
    
    /// <summary>
    /// Manager class that allows to control all backend aspects of transform change tracking, change skipping and assembly patching
    /// </summary>
    public class TransformChangesDebuggerManager
    {
        /// <summary>
        /// Fired when global change tracking was enabled/disabled
        /// </summary>
        public static event EventHandler<bool> IsTrackingEnabledChanged;
        
        internal static Dictionary<Transform, List<InterceptedCallbackBase>> RegisteredSetInterceptorObjectsToInterceptorMethods = new Dictionary<Transform, List<InterceptedCallbackBase>>();
        private static List<ShouldSkipTransformChangeExecutionPredicate> ShouldSkipTransformChangeExecutionPredicates = new List<ShouldSkipTransformChangeExecutionPredicate>();
        private static HashSet<TransformModifier> ShouldSkipTransformChangeDoneViaModifiers = new HashSet<TransformModifier>();
        public static List<FileInfo> AllUserPatchableAssyPaths = new List<FileInfo>();

        private static bool _isTrackingEnabled;
        /// <summary>
        /// Controls change tracking for whole, if disabled no assemblies will be patched and no callbacks will be executed
        /// </summary>
        public static bool IsTrackingEnabled
        {
            get => _isTrackingEnabled;
            set
            {
                var isDifferent = _isTrackingEnabled != value;
                _isTrackingEnabled = value;
                
                if(isDifferent) 
                    IsTrackingEnabledChanged?.Invoke(null, _isTrackingEnabled);
            }
        }

        //Those realistically should never be patched
        //TODO: that could possibly be exposed as setting in editor config?
        private static readonly List<string> PreExcludeSystemAssemblyFromHarmonyPatchingIfFullPathContains = new List<string>()
        {
            "0Harmony",
            "ICSharpCode.NRefactory.dll",
            @"MonoBleedingEdge\lib",
            "nunit.framework",
            "Unity.RenderPipelines",
            "Unity.Rider.Editor",
            "Unity.Searcher.Editor",
            "Unity.ShaderGraph",
            "Unity.VisualStudio",
            "Unity.VSCode.Editor",
            "UnityEditor.Android.Extensions",
            "UnityEditor.PackageManagerUIModule",
            "UnityEditor.SceneTemplateModule",
            "UnityEditor.TestRunner",
            "UnityEditor.WindowsStandalone.Extensions",
        };

        private static readonly Harmony HarmonyInstance = new Harmony("TransformChangesDebuggerManager");
        private static bool IsInitialized;

        /// <summary>
        /// Initializes change tracking by patching selected assemblies, call is only needed if you're not using default `TransformChangesDebuggerInitializer`
        /// </summary>
        /// <param name="allAvailableAssyPaths">All assemblies (full file paths) that are available for change tracking, even if not enabled right now tracking can be enabled at runtime</param>
        /// <param name="assemblyPathsChosenToPatchByUser">Assemblies (full file paths) to enabled change tracking for instantly</param>
        public static void Initialize(List<FileInfo> allAvailableAssyPaths, List<FileInfo> assemblyPathsChosenToPatchByUser)
        {
            if (IsInitialized) throw new Exception("Already initialized");
            
            AllUserPatchableAssyPaths = allAvailableAssyPaths
                .Where(a => PreExcludeSystemAssemblyFromHarmonyPatchingIfFullPathContains.All(e => !a.FullName.Contains(e)))
                .ToList();
            
            if(!IsTrackingEnabled 
               #if UNITY_EDITOR
               || !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode 
               #endif
               || !GameObject.FindObjectOfType<TrackTransformChanges>()) return;
            
            var sw = new Stopwatch();
            sw.Start();

            //SendMessage is used as all libraries will have access to it hence code can be redirected there, it'll then be routed to this library for processing
            CoreInterceptionPatch.PatchSendMessage(HarmonyInstance); 
        
            TransformPatches.InterceptMethodsToEnableChangeTracking(HarmonyInstance, assemblyPathsChosenToPatchByUser);

            Debug.Log($"{nameof(TransformChangesDebugger)} took {sw.ElapsedMilliseconds}ms to initialize. You can use Assembles tab in tool window to specify which assemblies making changes should be watched. Less assembles will result in faster initialization time. You can adjust assembles ad-hoc during playmode.");
            IsInitialized = true;
        }
        
        /// <summary>
        /// Allows to enable change tracking for selected assemblies at runtime
        /// </summary>
        /// <param name="assemblyPaths">Full file paths to assemblies</param>
        /// <returns>General statistics around number of methods patched for specific assemblies as well as time taken</returns>
        public static RedirectSetterMethodsFromCallingCodeResult EnableChangeTracking(List<FileInfo> assemblyPaths)
        {
            if (!IsTrackingEnabled)
            {
                Debug.LogWarning("Tracking changes is disabled, no patching will take place till enabled.");
                return new RedirectSetterMethodsFromCallingCodeResult(new List<RedirectSetterMethodsFromCallingCodeForAssyResult>(), 0);
            }
            
            return TransformPatches.InterceptMethodsToEnableChangeTracking(HarmonyInstance, assemblyPaths);
        }

        /// <summary>
        /// Allows to respond to position changes to specific object by executing handler
        /// </summary>
        /// <returns>Created callback that can be later used to remove callback when no longer needed</returns>
        public static InterceptedVector3Callback RegisterCallbackForPositionChanges(TrackTransformChanges interceptFor, InterceptedVector3SetMethod handler)
        {
            return RegisterCallbackForVector3Interception(interceptFor, handler, ChangeType.Position);
        }
    
        /// <summary>
        /// Allows to respond to scale changes to specific object by executing handler
        /// </summary>
        /// <returns>Created callback that can be later used to remove callback when no longer needed</returns>
        public static InterceptedVector3Callback RegisterCallbackForScaleChanges(TrackTransformChanges interceptFor, InterceptedVector3SetMethod handler)
        {
            return RegisterCallbackForVector3Interception(interceptFor, handler, ChangeType.Scale);
        }

        /// <summary>
        /// Allows to respond to rotation changes to specific object by executing handler
        /// </summary>
        /// <returns>Created callback that can be later used to remove callback when no longer needed</returns>
        public static InterceptedQuaternionCallback RegisterCallbackForRotationChanges(TrackTransformChanges interceptFor, InterceptedQuaternionSetMethod handler)
        {
            return RegisterCallbackForQuaternionInterception(interceptFor, handler, ChangeType.Rotation);
        }
        
        /// <summary>
        /// Removes previously created callback for changes to object 
        /// </summary>
        /// <param name="callback">Previously created callback</param>
        public static void RemoveCallback(InterceptedCallbackBase callback)
        {
            var objectToHandlersKv = RegisteredSetInterceptorObjectsToInterceptorMethods.FirstOrDefault(kv => kv.Value.Any(c => callback == c));
            if (objectToHandlersKv.Value != null)
            {
                var handler = objectToHandlersKv.Value.First(cb => cb == callback);
                objectToHandlersKv.Value.Remove(handler);

                if (objectToHandlersKv.Value.Any())
                {
                    RegisteredSetInterceptorObjectsToInterceptorMethods.Remove(objectToHandlersKv.Key);
                }
            }
        }
        
        private static InterceptedVector3Callback RegisterCallbackForVector3Interception(TrackTransformChanges interceptFor, InterceptedVector3SetMethod handler, ChangeType changeType)
        {
            var existingHandlers = GetExistingHandlers(interceptFor);
            if(existingHandlers.Any(h => h.Type == changeType))
            {
                Debug.LogWarning($"Handler for object: '{interceptFor.gameObject.name}' and type: {changeType} was already added, make sure that was your intention.", interceptFor.gameObject);
            }

            var callback = new InterceptedVector3Callback(changeType, handler);
            existingHandlers.Add(callback);
            return callback;
        }

        private static InterceptedQuaternionCallback RegisterCallbackForQuaternionInterception(TrackTransformChanges interceptFor, InterceptedQuaternionSetMethod handler, ChangeType changeType)
        {
            var existingHandlers = GetExistingHandlers(interceptFor);
            if(existingHandlers.Any(h => h.Type == changeType))
            {
                Debug.LogWarning($"Handler for object: '{interceptFor.gameObject.name}' and type: {changeType} was already added, remove it first.", interceptFor.gameObject);
                return InterceptedQuaternionCallback.Empty;
            }

            var callback = new InterceptedQuaternionCallback(changeType, handler);
            existingHandlers.Add(callback);
            return callback;
        }
        
        /// <summary>
        /// Allows to skip specific changes at runtime for intercepted method calls. This is useful for eg. in debugging scenarios to narrow down which changes cause issues
        /// </summary>
        /// <param name="predicate">Predicate that decides if change should be executed</param>
        /// <returns>Same predicate that was passed in, this can be later used to unregister</returns>
        public static ShouldSkipTransformChangeExecutionPredicate SkipTransformChangesFor(ShouldSkipTransformChangeExecutionPredicate predicate)
        {
            ShouldSkipTransformChangeExecutionPredicates.Add(predicate);
            return predicate;
        }
        
        /// <summary>
        /// Allows to skip specific changes at runtime for intercepted method calls. This is useful for eg. in debugging scenarios to narrow down which changes cause issues
        /// </summary>
        /// <param name="modifier">Specific modifier making the change</param>
        public static void SkipTransformChangesFor(TransformModifier modifier)
        {
            ShouldSkipTransformChangeDoneViaModifiers.Add(modifier);
        }
        
        /// <summary>
        /// Removed previously added <see cref="SkipTransformChangesFor"/> predicate, effectively re-enabling changes
        /// </summary>
        public static void RemoveSkipTransformChangesFor(ShouldSkipTransformChangeExecutionPredicate predicate)
        {
            ShouldSkipTransformChangeExecutionPredicates.Remove(predicate);
        }

        /// <summary>
        /// Removed previously added <see cref="SkipTransformChangesFor"/> modifier, effectively re-enabling changes
        /// </summary>
        public static void RemoveSkipTransformChangesFor(TransformModifier modifier)
        {
            ShouldSkipTransformChangeDoneViaModifiers.Remove(modifier);
        }

        /// <summary>
        /// Resolves whether specific transform modifier changes are excluded from executing
        /// </summary>
        /// <param name="transformModifier"></param>
        /// <returns></returns>
        public static bool ShouldSkipTransformChangeDueToDisabledModifier(TransformModifier transformModifier)
        {
            return ShouldSkipTransformChangeDoneViaModifiers.Contains(transformModifier);
        }

        internal static bool ShouldSkipTransformChangesExecution(IlWeavedValuesArray ilWeavedValuesArray, Component changingComponent)
        {
            if (ShouldSkipTransformChangeDoneViaModifiers.Any(m => m.CallingObject == ilWeavedValuesArray.CallingObject 
                                                                   && m.CallingFromMethodName == ilWeavedValuesArray.CallingFromMethodName)
            ) return true;
            
            if(ShouldSkipTransformChangeExecutionPredicates.Any(predicate => predicate(ilWeavedValuesArray, changingComponent))) return true;

            return false;
        }

        private static List<InterceptedCallbackBase> GetExistingHandlers(TrackTransformChanges interceptFor, ChangeType? type = null)
        {
            if (!RegisteredSetInterceptorObjectsToInterceptorMethods.ContainsKey(interceptFor.transform))
                RegisteredSetInterceptorObjectsToInterceptorMethods[interceptFor.transform] = new List<InterceptedCallbackBase>();

            var existingHandlers = RegisteredSetInterceptorObjectsToInterceptorMethods[interceptFor.transform];
            return type.HasValue ? existingHandlers.Where(h => h.Type == type).ToList() : existingHandlers;
        }
    }
}
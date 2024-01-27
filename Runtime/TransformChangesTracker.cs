using System;
using System.Collections.Generic;
using System.Linq;
using TransformChangesDebugger.API.Extensions;
using TransformChangesDebugger.API.Patches;
using UnityEngine;

namespace TransformChangesDebugger.API
{
    /// <summary>
    /// This class captures and makes sense of data captured for objects with <see cref="TrackTransformChanges"/> on a frame-by-frame basis
    /// </summary>
    public static class TransformChangesTracker
    {
        private static readonly Dictionary<TrackTransformChanges, List<TransformChange>> EmptyFrameChanges = new Dictionary<TrackTransformChanges, List<TransformChange>>();
        private static readonly List<TransformChange> EmptyFrameChangesForTrackedObject = new List<TransformChange>();
        
        /// <summary>
        /// Fires when new transform change has been captured for tracked object
        /// </summary>
        public static event EventHandler<TransformChange> OnTransformChangeAdded;

        /// <summary>
        /// Maximum number of frames that data will be kept for. For performance reasons it's best to keep that within reasonable limits.
        /// </summary>
        public static int KeepChangesDataForMaximumNumberOfFrames { get; set; } = 1000; 
        
        /// <summary>
        /// Enable for tracking to initialize regardless of scene having any TrackTransformChanges objects initially. This is helpful if you're tracking dynamically instantiated objects.
        /// </summary>
        public static bool InitializeTrackingEvenWithoutAnyTrackedObjects { get; set; } = false; 
        
        //PERF: potentially might need to pool TransformChange objects?
        private static readonly Dictionary<int, Dictionary<TrackTransformChanges, List<TransformChange>>> FrameToChangedObjectToChangesMap = new Dictionary<int, Dictionary<TrackTransformChanges, List<TransformChange>>>();
        private static readonly Dictionary<TrackTransformChanges, List<InterceptedCallbackBase>> TrackTransformChangeToRegisteredCallbacksMap = new Dictionary<TrackTransformChanges, List<InterceptedCallbackBase>>();
        private static readonly Dictionary<TrackTransformChanges, Dictionary<ChangeType, TransformChange>> LastChangeOfTypeToObject = new Dictionary<TrackTransformChanges, Dictionary<ChangeType, TransformChange>>();
        
        public static int AllAvailableTrackedDataFrameCount => FrameToChangedObjectToChangesMap.Count;
        
        /// <summary>
        /// Starts tracking changes for specific object. This method needs to be called if subclassing <see cref="TrackTransformChanges"/> 
        /// </summary>
        public static void TrackChanges(TrackTransformChanges trackingFor)
        {
            if (TrackTransformChangeToRegisteredCallbacksMap.ContainsKey(trackingFor))
            {
                Debug.Log($"Changes are already tracked for object: {trackingFor.name}", trackingFor);
                return;
            }

            var callbacks = new List<InterceptedCallbackBase>();
            TrackTransformChangeToRegisteredCallbacksMap.Add(trackingFor, callbacks);
            
            callbacks.Add(TransformChangesDebuggerManager.RegisterCallbackForPositionChanges(trackingFor, 
                (ilWeavedValues, newValue) => trackingFor.HandlePositionChange(TrackChange(CreateTransformChange(trackingFor, ilWeavedValues, newValue), Time.frameCount)))
            );
            
            callbacks.Add(TransformChangesDebuggerManager.RegisterCallbackForRotationChanges(trackingFor, 
                (ilWeavedValues, newValue) => trackingFor.HandleRotationChange(TrackChange(CreateTransformChange(trackingFor, ilWeavedValues, newValue), Time.frameCount)))
            );
            
            callbacks.Add(TransformChangesDebuggerManager.RegisterCallbackForScaleChanges(trackingFor, 
                (ilWeavedValues, newValue) => trackingFor.HandleScaleChange(TrackChange(CreateTransformChange(trackingFor, ilWeavedValues, newValue), Time.frameCount)))
            );
        }

        /// <summary>
        /// Stops tracking changes for specific <see cref="TrackTransformChanges"/> object
        /// </summary>
        public static void StopTrackingChanges(TrackTransformChanges trackTransformChanges)
        {
            if (TrackTransformChangeToRegisteredCallbacksMap.TryGetValue(trackTransformChanges, out var callbacks))
            {
                callbacks.ForEach(TransformChangesDebuggerManager.RemoveCallback);
                TrackTransformChangeToRegisteredCallbacksMap.Remove(trackTransformChanges);
            }
            else
            {
                Debug.LogWarning($"Object: '{trackTransformChanges.name}' is not tracked, can't stop tracking.", trackTransformChanges);
            }
        }

        /// <summary>
        /// Gets list of all tracked objects that there's any captured data for
        /// </summary>
        public static List<TrackTransformChanges> GetTrackedObjects() => GetTrackedObjects(null);
        
        /// <summary>
        /// Gets list of all tracked objects that have tracking data for specific frame
        /// </summary>
        public static List<TrackTransformChanges> GetTrackedObjects(int forFrame) => GetTrackedObjects(new int?(forFrame));
        public static List<TrackTransformChanges> GetTrackedObjects(int? forFrame)
        {
            return FrameToChangedObjectToChangesMap
                .Where(kv => !forFrame.HasValue || kv.Key == forFrame.Value)
                .SelectMany(kv => kv.Value.Keys)
                .GroupBy(trackedObject => trackedObject)
                .Select(g => g.First())
                .ToList();
        }

        internal static int GetNumberOfFrameDataNewerThan(int frameIndex)
        {
            var orderedFrameIndexes = FrameToChangedObjectToChangesMap.Select(kv => kv.Key).OrderByDescending(k => k).ToList();
            var index = orderedFrameIndexes.IndexOf(frameIndex);

            if (index == -1) return 0;

            return index;
        }

        /// <summary>
        /// Gets frame index with most recent changes
        /// </summary>
        /// <param name="skipNNewestFrames">allow to skip first n frames with captured data</param>
        /// <returns></returns>
        public static int GetNewestFrameNumberWithTrackedChanges(int skipNNewestFrames)
        {
            return FrameToChangedObjectToChangesMap.Select(kv => kv.Key).OrderByDescending(k => k).Skip(Math.Abs(skipNNewestFrames)).FirstOrDefault();
        }
        
        /// <summary>
        /// Resolves changes for specific frame and specific object
        /// </summary>
        public static List<TransformChange> GetFrameChangesForTrackedObject(int forFrame, TrackTransformChanges trackingFor)
        {
            var frameChangeObjectToChangesMap = GetFrameChanges(forFrame);

            if (!frameChangeObjectToChangesMap.ContainsKey(trackingFor))
                return EmptyFrameChangesForTrackedObject;

            return frameChangeObjectToChangesMap[trackingFor];
        }

        /// <summary>
        /// Resolves changes to all tracked object in specified frame 
        /// </summary>
        public static Dictionary<TrackTransformChanges, List<TransformChange>> GetFrameChanges(int forFrame)
        {
            if (!FrameToChangedObjectToChangesMap.ContainsKey(forFrame))
                return EmptyFrameChanges;

            return FrameToChangedObjectToChangesMap[forFrame];
        }

        /// <summary>
        /// Gets all <see cref="TransformModifier"/> entries for tracked changes
        /// </summary>
        public static List<TransformModifier> GetModifiers() => GetModifiers(null);
        
        /// <summary>
        /// Gets all <see cref="TransformModifier"/> entries for tracked changes in specific frame
        /// </summary>
        public static List<TransformModifier> GetModifiers(int frame) => GetModifiers(new int?(frame));
        
        /// <summary>
        /// Gets all <see cref="TransformModifier"/> entries for specific object
        /// </summary>
        public static List<TransformModifier> GetModifiers(TrackTransformChanges forObject) => GetModifiers(null, forObject);
        
        /// <summary>
        /// Gets all <see cref="TransformModifier"/> entries for tracked changes in frame
        /// </summary>
        public static List<TransformModifier> GetModifiers(int frame, TrackTransformChanges forObject) => GetModifiers(new int?(frame), forObject);
        
        private static List<TransformModifier> GetModifiers(int? frame = null, TrackTransformChanges forObject = null)
        {
            var changedObjectToChangesMapInScope = frame.HasValue 
                ? FrameToChangedObjectToChangesMap.TryGetValue(frame.Value, out var c) ? c : new Dictionary<TrackTransformChanges, List<TransformChange>>()
                : FrameToChangedObjectToChangesMap.SelectMany(kv => kv.Value);

            var transformChangesInScope = changedObjectToChangesMapInScope
                .SelectMany(kv => kv.Value)
                .Where(c1 => !forObject || c1.ModifiedObject == forObject);
                
            return CreateModifiers(transformChangesInScope);
        }

        public static List<TransformModifier> CreateModifiers(IEnumerable<TransformChange> transformChangesInScope)
        {
            return transformChangesInScope.GroupBy(c => new {c.CallingObject, c.CallingFromMethodName})
                .Select(g => new TransformModifier(g.Key.CallingObject, g.Key.CallingFromMethodName, g.ToList()))
                .ToList();
        }
        
        public static bool HasAnyChanges()
        {
            return FrameToChangedObjectToChangesMap.Any(c => c.Value.Any(v => v.Key));
        }

        public static void RemoveAllTrackedData()
        {
            FrameToChangedObjectToChangesMap.Clear();
        }

        private static TransformChange CreateTransformChange(TrackTransformChanges trackingFor, IlWeavedValuesArray ilWeavedValues, object newValue)
        {
            if (!LastChangeOfTypeToObject.ContainsKey(trackingFor))
            {
                LastChangeOfTypeToObject[trackingFor] = new Dictionary<ChangeType, TransformChange>()
                {
                    [ChangeType.Position] = null,
                    [ChangeType.Rotation] = null,
                    [ChangeType.Scale] = null
                };
            }
            
            var previousSameTypeChange = LastChangeOfTypeToObject[trackingFor][ilWeavedValues.ChangeType];
            var transformChange = new TransformChange(ilWeavedValues.CallingObject, ilWeavedValues.CallingFromMethodName, newValue, ilWeavedValues.ChangeType, 
                Time.frameCount, trackingFor, ilWeavedValues.WasOriginalChangeSkipped, ilWeavedValues.FullMethodNameForInterceptedCall, 
                ilWeavedValues.CalledMethodArguments, ilWeavedValues.ValueBeforeChange, previousSameTypeChange);
            
            LastChangeOfTypeToObject[transformChange.ModifiedObject][transformChange.ChangeType] = transformChange;

            return transformChange;
        }

        private static TransformChange TrackChange(TransformChange transformChange, int currentFrame)
        {
            EnsureMapContainersExistForFrameChange(transformChange, currentFrame);

            var currentFrameChangesForTrackedObject = GetFrameChangesForTrackedObject(currentFrame, transformChange.ModifiedObject);
            currentFrameChangesForTrackedObject.Add(transformChange);
            OnTransformChangeAdded?.Invoke(null, transformChange);

            TrimExcessTrackedChangesFrames(KeepChangesDataForMaximumNumberOfFrames);


            return transformChange;
        }

        private static void EnsureMapContainersExistForFrameChange(TransformChange transformChange, int currentFrame)
        {
            if (!FrameToChangedObjectToChangesMap.ContainsKey(currentFrame))
                FrameToChangedObjectToChangesMap.Add(currentFrame, new Dictionary<TrackTransformChanges, List<TransformChange>>());

            if (!FrameToChangedObjectToChangesMap[currentFrame].ContainsKey(transformChange.ModifiedObject))
                FrameToChangedObjectToChangesMap[currentFrame].Add(transformChange.ModifiedObject, new List<TransformChange>());
        }

        private static void TrimExcessTrackedChangesFrames(int keepChangesDataForMaximumNumberOfFrames)
        {
            var excessFrameKeysToRemove = FrameToChangedObjectToChangesMap
                .Select(kv => kv.Key)
                .OrderByDescending(f => f)
                .Skip(keepChangesDataForMaximumNumberOfFrames)
                .ToList();

            foreach (var key in excessFrameKeysToRemove)
            {
                FrameToChangedObjectToChangesMap.Remove(key);
            }
        }
    }


    public interface ISerializableTransformModifier
    {
        string CallingObjectFullPath { get; }
        string CallingFromMethodName { get; }
    }
    
    /// <summary>
    /// Class that groups changes by specific modifiers, that is same calling object and originating method name
    /// </summary>
    public class ComparisonOnlyTransformModifier: ISerializableTransformModifier
    {
        /// <summary>
        /// Object that initiated change
        /// </summary>
        public Component CallingObject { get; }
        
        /// <summary>
        /// Name of object that initiated change
        /// </summary>
        public string CallingObjectName => CallingObject ? CallingObject.name : "None";
        
        /// <summary>
        /// Full hierarchy path of object that initiated change
        /// </summary>
        public string CallingObjectFullPath { get; private set; }
    
        /// <summary>
        /// Original method name that initiated change
        /// </summary>
        public string CallingFromMethodName { get; }

        public ComparisonOnlyTransformModifier(Component callingObject, string callingFromMethodName)
        {
            CallingObject = callingObject;
            CallingFromMethodName = callingFromMethodName;
            CallingObjectFullPath = CallingObject ? CallingObject.GetFullPath() : "None";
        }
        
        public override int GetHashCode()
        {
            return CallingObjectFullPath.GetHashCode() + CallingFromMethodName.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            
            var otherModifier = (ComparisonOnlyTransformModifier) obj;
            return otherModifier.CallingObject == this.CallingObject && otherModifier.CallingFromMethodName == this.CallingFromMethodName;
        }
    }
    
    public class TransformModifier: ComparisonOnlyTransformModifier {
        
        /// <summary>
        /// List of all changes done by modifier
        /// </summary>
        public List<TransformChange> Changes { get; }

        public TransformModifier(Component callingObject, string callingFromMethodName, List<TransformChange> changes)
            : base(callingObject, callingFromMethodName)
        {
            Changes = changes;
        }
    }
}
using System;
using UnityEngine;
using UnityEngine.Events;

namespace TransformChangesDebugger.API
{
    /// <summary>
    /// Add this class to game objects that changes you want to be tracked
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Transform Debugger/Track Transform Changes")]
    [DefaultExecutionOrder(-1000)] //run before other scripts to start tracking as early as possible
    public class TrackTransformChanges : MonoBehaviour
    {
        [Serializable] public class UnityEvent : UnityEvent<TransformChange> { }

        /// <summary>
        /// Fired on every tracked position related change, could be used to pin point exact change that's causing issue in programmatic way
        /// </summary>
        public UnityEvent PositionChanged = new UnityEvent();
        
        /// <summary>
        /// Fired on every tracked rotation related change, could be used to pin point exact change that's causing issue in programmatic way
        /// </summary>
        public UnityEvent RotationChanged = new UnityEvent();
        
        /// <summary>
        /// Fired on every tracked scale related change, could be used to pin point exact change that's causing issue in programmatic way
        /// </summary>
        public UnityEvent ScaleChanged = new UnityEvent();
        

        private void Awake()
        {
            TransformChangesTracker.TrackChanges(this);
        }

        private void OnDestroy()
        {
            TransformChangesTracker.StopTrackingChanges(this);
        }
        
        /// <summary>
        /// Fired on every tracked position related change, override in derived class for custom handling
        /// </summary>
        public virtual void HandlePositionChange(TransformChange transformChange)
        {
            PositionChanged?.Invoke(transformChange);
        }
        
        /// <summary>
        /// Fired on every tracked rotation related change, override in derived class for custom handling
        /// </summary>
        public virtual void HandleRotationChange(TransformChange transformChange)
        {
            RotationChanged?.Invoke(transformChange);
        }
        
        /// <summary>
        /// Fired on every tracked scale related change, override in derived class for custom handling
        /// </summary>
        public virtual void HandleScaleChange(TransformChange transformChange)
        {
            ScaleChanged?.Invoke(transformChange);
        }
    }
}
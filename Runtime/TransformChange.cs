using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TransformChangesDebugger.API.Extensions;
using TransformChangesDebugger.API.Patches;
using UnityEngine;

namespace TransformChangesDebugger.API
{
    /// <summary>
    /// Details about specific change to transform
    /// </summary>
    public class TransformChange
    {
        private static Dictionary<Type, Dictionary<string, string>> TypeToFullMethodNameToDisplayNameMap = new Dictionary<Type, Dictionary<string, string>>();
        
        /// <summary>
        /// Object that initiated the change
        /// </summary>
        public Component CallingObject { get; }
        
        /// <summary>
        /// Method name where the change originated
        /// </summary>
        public string CallingFromMethodName { get; }
        
        /// <summary>
        /// Full method name that originally caused transform change, eg. 'UnityEngine.Transform.set_position(UnityEngine.Vector3)', 'UnityEngine.Transform.Translate(UnityEngine.Vector3)'.
        /// Full list can be found in class 'TranspiledMethodDefinitions' <see cref="TranspiledMethodDefinitions"/>
        /// </summary>
        public string TrackedDueToInterceptedMethodCallFullName { get; set; }
        
        /// <summary>
        /// Method arguments passed in to original changing method, eg for 'UnityEngine.Transform.Rotate(UnityEngine.Vector3,UnityEngine.Space)' that could be { (0, 1, 2), Space.Self }
        /// </summary>
        public object[] TrackedDueToInterceptedMethodArguments { get; set; }
        
        /// <summary>
        /// New value after change's been made
        /// </summary>
        //PERF: those values will be boxed in many scenarios, do we need to have different class implementations to prevent boxing?
        public object NewValue { get; }
        
        /// <summary>
        /// Value right before change was made
        /// </summary>
        public object ValueBeforeChange { get; }
        
        /// <summary>
        /// ChangeType - either position/rotation/scale
        /// </summary>
        public ChangeType ChangeType { get; }
        
        /// <summary>
        /// Frame index when change occured
        /// </summary>
        public int ChangedInFrame { get; }
        
        /// <summary>
        /// Object which transform has been modified
        /// </summary>
        public TrackTransformChanges ModifiedObject { get; set; }
        
        /// <summary>
        /// Indicates whether change was not made as it was skipped at runtime due to skip setup - <see cref="TransformChangesDebuggerManager.SkipTransformChangesFor"/>
        /// </summary>
        public bool WasChangeSkipped { get; }
        
        /// <summary>
        /// Previous tracked change of same type, this can be traversed to create a chain of changes of same type in reversed order
        /// </summary>
        public TransformChange PreviousSameTypeChange { get; }


        public TransformChange(Component callingObject, string callingFromMethodName, object newValue, ChangeType changeType, int changedInFrame, 
            TrackTransformChanges modifiedObject, bool wasChangeSkipped, string trackedDueToInterceptedMethodCallFullName, object[] trackedDueToInterceptedMethodArguments, object valueBeforeChange, TransformChange previousSameTypeChange)
        {
            CallingObject = callingObject;
            CallingFromMethodName = callingFromMethodName;
            NewValue = newValue;
            ChangeType = changeType;
            ChangedInFrame = changedInFrame;
            ModifiedObject = modifiedObject;
            WasChangeSkipped = wasChangeSkipped;
            TrackedDueToInterceptedMethodCallFullName = trackedDueToInterceptedMethodCallFullName;
            TrackedDueToInterceptedMethodArguments = trackedDueToInterceptedMethodArguments;
            ValueBeforeChange = valueBeforeChange;
            PreviousSameTypeChange = previousSameTypeChange;
        }
        
        /// <summary>
        /// Resolves method name from full IL to standard .NET
        /// </summary>
        /// <param name="callingFromMethodName">Full IL method name</param>
        /// <param name="type">.NET Type where method is from</param>
        /// <returns></returns>
        public  static string SimplifyMethodName(string callingFromMethodName, Type type)
        {
            if(!TypeToFullMethodNameToDisplayNameMap.ContainsKey(type))
                TypeToFullMethodNameToDisplayNameMap[type] = new Dictionary<string, string>();

            if (!TypeToFullMethodNameToDisplayNameMap[type].ContainsKey(callingFromMethodName))
            {
                var foundMethod = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static |
                                                               BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.ResolveFullName() == callingFromMethodName);

                TypeToFullMethodNameToDisplayNameMap[type][callingFromMethodName] = foundMethod != null
                    ? foundMethod.Name
                    : callingFromMethodName;
            }

            return TypeToFullMethodNameToDisplayNameMap[type][callingFromMethodName];
        }

        /// <summary>
        /// Indicates whether last change new value is different than current change <see cref="ValueBeforeChange"/> which likely means there's a change that happened and was not captured
        /// </summary>
        /// <returns></returns>
        public bool IsMismatchWithPreviousChange() 
        {
            if (PreviousSameTypeChange == null) return false;

            return !PreviousSameTypeChange.NewValue.Equals(ValueBeforeChange);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TransformChangesDebugger.API.Patches
{
    internal class CoreInterceptionPatch
    {
        public static readonly string InterceptorMethodNameForSendMessageBeforeOriginalExecution = "$HandleGlobalInterceptorCallback_Before$";
        public static readonly string InterceptorMethodNameForSendMessageAfterOriginalExecution = "$HandleGlobalInterceptorCallback_After$";
    
        public static readonly MethodInfo SendMessageMethod = typeof(Component)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m =>
            {
                var parameters = m.GetParameters();
                return m.Name == nameof(Component.SendMessage) && parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(object);
            });
    
        public static void PatchSendMessage(Harmony harmony)
        {
            //hook global send message
            var sendMessagePrefix = typeof(CoreInterceptionPatch).GetMethod(nameof(SendMessagePrefix), BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(SendMessageMethod, prefix: new HarmonyMethod(sendMessagePrefix));
        }

        //TODO: concurrency - right now tool assumes sync execution. To get before and after call extensions we have to capture values before 2 calls to SendPrefix
        //then those calls will be matched up, in single thread that's simply done via last-value, for concurrent scenarios we could have incrementing ID that'd be send to
        //calling code via values-array (similar to skip-changes flag) and then the code would pass it back on second code. That id then could be looked up and correct
        //weavedValuesArray can be retrieved.
        private static IlWeavedValuesArray LastIlWeavedValuesArray;
        
        private static bool SendMessagePrefix(Component __instance, string methodName, object value)
        {
            if (methodName == InterceptorMethodNameForSendMessageBeforeOriginalExecution)
            {
                var ilWeavedValuesArray = new IlWeavedValuesArray((object[]) value);
                
                var valueBeforeChange = GetTransformValue(__instance, ilWeavedValuesArray.ChangeType);
                ilWeavedValuesArray.SetValueBeforeChangeCallMade(valueBeforeChange);
                
                var shouldSkipTransformChangeExecution = TransformChangesDebuggerManager.ShouldSkipTransformChangesExecution(ilWeavedValuesArray, __instance);
                ilWeavedValuesArray.SetShouldExecuteOriginalCall(!shouldSkipTransformChangeExecution);
                
                LastIlWeavedValuesArray = ilWeavedValuesArray;
                
                //TODO: allow to use GetComponent<TrackTransformChanges> and pass before change / after change / possibly even skip? That could be quite expensive, perhaps an optional add on?
            
                return false;
            }
            else if (methodName == InterceptorMethodNameForSendMessageAfterOriginalExecution)
            {
                var changeReason = TranspiledMethodDefinitions.FullTrackedMethodNameToChangeTypeMap[LastIlWeavedValuesArray.FullMethodNameForInterceptedCall];
                var newValue = GetTransformValue(__instance, changeReason);
                LastIlWeavedValuesArray.SetNewValueAfterOriginalCallMade(newValue);
                
                TryExecuteCallbackIfTracked(LastIlWeavedValuesArray, __instance.transform);
                return false;
            }

            //default implementation of 2 param send message
            __instance.SendMessage(methodName, value, SendMessageOptions.RequireReceiver);
            return false; //that won't run actual send message method
        }

        private static object GetTransformValue(Component __instance, ChangeType changeType)
        {
            object newValue = null;
            switch (changeType)
            {
                case ChangeType.Position:
                    newValue = __instance.transform.position;
                    break;
                case ChangeType.Rotation:
                    newValue = __instance.transform.rotation;
                    break;
                case ChangeType.Scale:
                    newValue = __instance.transform.localScale;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return newValue;
        }

        //if new types are added, make sure to add appropriate callback types
        private static readonly Dictionary<ChangeType, Type> MethodInterceptionParamTypeToHandledTypeMap = new Dictionary<ChangeType, Type>()
        {
            [ChangeType.Position] = typeof(Vector3),
            [ChangeType.Rotation] = typeof(Quaternion),
            [ChangeType.Scale] = typeof(Vector3),
        };
        
        private static void TryExecuteCallbackIfTracked(IlWeavedValuesArray ilWeavedValuesArray, Transform transform)
        {
            //TODO PERF: measure speed for bigger dictionaries?
            if (TransformChangesDebuggerManager.RegisteredSetInterceptorObjectsToInterceptorMethods.TryGetValue(transform, out var interceptedCallbacks))
            {
                foreach (var interceptedCallback in interceptedCallbacks.Where(c => c.Type == ilWeavedValuesArray.ChangeType))
                {
                    if (ilWeavedValuesArray.NewValue is Vector3 vector3Value && interceptedCallback is InterceptedVector3Callback vector3Callback)
                    {
                        vector3Callback.Handler(ilWeavedValuesArray, vector3Value);
                    }
                    else if (ilWeavedValuesArray.NewValue is Quaternion quaternionValue && interceptedCallback is InterceptedQuaternionCallback quaternionCallback)
                    {
                        quaternionCallback.Handler(ilWeavedValuesArray, quaternionValue);
                    }
                    else if (MethodInterceptionParamTypeToHandledTypeMap.Select(kv => kv.Value)
                        .All(t => t != ilWeavedValuesArray.NewValue.GetType()))
                    {
                        throw new Exception($"No callback handler implemented for type: {ilWeavedValuesArray.NewValue}, need to implement");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Raw IL weaved values for intercepted call
    /// </summary>
    public struct IlWeavedValuesArray
    {
        private readonly object[] _values;
        
        /// <summary>
        /// Object that's calling intercepted method
        /// </summary>
        public Component CallingObject => _values[0] as Component; 
        
        /// <summary>
        /// Original method name making a call to intercepted-method
        /// </summary>
        public string CallingFromMethodName => _values[1] as string;
        
        /// <summary>
        /// Method call arguments for original method name making a call to intercepted-method
        /// </summary>
        public object[] CalledMethodArguments => _values[2] as object[];
        
        /// <summary>
        /// Value after original call
        /// </summary>
        public object NewValue { get; private set; }
        
        /// <summary>
        /// Value before original call
        /// </summary>
        public object ValueBeforeChange { get; private set; }
        
        /// <summary>
        /// Type of change, position/rotation/scale
        /// </summary>
        public ChangeType ChangeType { get; private set; }
        
        /// <summary>
        /// Full method name that's being intercepted
        /// </summary>
        public string FullMethodNameForInterceptedCall => _values[3] as string;
        internal bool WasOriginalChangeSkipped => !(bool)_values[4];

        internal void SetShouldExecuteOriginalCall(bool shouldExecuteOriginalCall)
        {
            _values[4] = shouldExecuteOriginalCall;
        }
        
        internal void SetValueBeforeChangeCallMade(object value)
        {
            ValueBeforeChange = value;
        }
        
        internal void SetNewValueAfterOriginalCallMade(object value)
        {
            NewValue = value;
        }

        internal IlWeavedValuesArray(object[] values)
        {
            _values = values;
            NewValue = null;
            ChangeType = TranspiledMethodDefinitions.FullTrackedMethodNameToChangeTypeMap[values[3] as string];
            ValueBeforeChange = null;
        }
    }
}
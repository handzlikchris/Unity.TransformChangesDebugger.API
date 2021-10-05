using TransformChangesDebugger.API;
using TransformChangesDebugger.API.Patches;
using UnityEngine;

public class CustomCallback : MonoBehaviour
{
    [SerializeField] private TrackTransformChanges InterceptFor;
    
    void Start()
    {
        RegisterCustomCallbacks();
    }

    private void RegisterCustomCallbacks()
    {
        TransformChangesDebuggerManager.RegisterCallbackForPositionChanges(InterceptFor,
            (ilWeavedValues, newValue) => { PrintInterceptionDebugMessage(newValue, ilWeavedValues, "POSITION"); }
        );

        TransformChangesDebuggerManager.RegisterCallbackForRotationChanges(InterceptFor,
            (ilWeavedValues, newValue) => { PrintInterceptionDebugMessage(newValue, ilWeavedValues, "ROTATION"); }
        );

        TransformChangesDebuggerManager.RegisterCallbackForScaleChanges(InterceptFor,
            (ilWeavedValues, newValue) => { PrintInterceptionDebugMessage(newValue, ilWeavedValues, "scale"); }
        );
    }

    private static void PrintInterceptionDebugMessage(object newValue, IlWeavedValuesArray ilWeavedValues, string type)
    {
        if (Time.frameCount % 100 == 0)
        {
            Debug.Log($"Intercepted {type} change: {newValue} {ilWeavedValues.CallingFromMethodName} {ilWeavedValues.CallingObject?.name ?? "StaticCall"} {ilWeavedValues.ChangeType}");
        }
    }
}
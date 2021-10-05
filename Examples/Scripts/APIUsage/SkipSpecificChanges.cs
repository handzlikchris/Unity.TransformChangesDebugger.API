using System;
using TransformChangesDebugger.API;
using UnityEngine;

public class SkipSpecificChanges : MonoBehaviour
{
    [SerializeField] private string SkipChangesToObjectNamed = "TrackedObject-1";
    [SerializeField] private string SkipChangesIfCallingFromMethodName = "CircleMotion.UpdatePositionMethodNameSoLongItGetsTrimmedInModifiersPanel()";
    
    private void Awake()
    {
        SetupSkipSpecificChanges();
    }

    private void SetupSkipSpecificChanges()
    {
        var samePredicateNeededToUnregister = TransformChangesDebuggerManager.SkipTransformChangesFor((ilWeavedValuesArray, changingComponent) =>
        {
            if (changingComponent.name == SkipChangesToObjectNamed && ilWeavedValuesArray.CallingFromMethodName == SkipChangesIfCallingFromMethodName)
            {
                return true; //true will indicate that change should be skipped
            }
            return false;
        });
        
        //To unregister you simply pass same predicate (which is also return value) to RemoveSkipTransformChangesFor
        // TransformChangesDebuggerManager.RemoveSkipTransformChangesFor(samePredicateNeededToUnregister);
    }
}
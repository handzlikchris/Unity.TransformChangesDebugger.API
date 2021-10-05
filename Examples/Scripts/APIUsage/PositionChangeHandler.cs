using TransformChangesDebugger.API;
using UnityEngine;

public class PositionChangeHandler : MonoBehaviour
{
    public void HandleTransformChange(TransformChange transformChange)
    {
        if(transformChange.WasChangeSkipped && Time.frameCount % 100 == 0)
            Debug.Log($"Change: from {transformChange.CallingObject.name} was skipped");
    }
}
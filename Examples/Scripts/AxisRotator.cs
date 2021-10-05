using System.Collections.Generic;
using System.Linq;
using TransformChangesDebugger.API;
using UnityEngine;

public class AxisRotator : MonoBehaviour
{
    private List<Transform> ObjectsToRotate = new List<Transform>();
    
    void Start()
    {
        ObjectsToRotate = GameObject.FindObjectsOfType<TrackTransformChanges>().Select(t => t.transform).ToList();
    }

    void LateUpdate()
    {
        if(Time.frameCount % 2 == 0) return;
    
        foreach (var objectToRotate in ObjectsToRotate)
        {
            UpdateRotation(objectToRotate);
        }
    }

    private void UpdateRotation(Transform objectToRotate)
    {
        var currentRotation = objectToRotate.rotation.eulerAngles;
        objectToRotate.rotation = Quaternion.AngleAxis(currentRotation.y + 1, Vector3.up);
    }
}

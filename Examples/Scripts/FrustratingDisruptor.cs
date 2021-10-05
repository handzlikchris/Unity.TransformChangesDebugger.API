using System.Collections;
using UnityEngine;

public class FrustratingDisruptor : MonoBehaviour
{
    [SerializeField] private Transform ObjectToChange;
    [SerializeField] private int ChangePositionEveryNFrames;

    [SerializeField] private float StartDisruptionAfterNSeconds = 0;

    private bool _isDisruptionEnabled = false;
    
    void LateUpdate()
    {
        if(!_isDisruptionEnabled) return;
        
        if (Time.frameCount % ChangePositionEveryNFrames == 0)
        {
            var objectToChangePosition = ObjectToChange.position;
            ObjectToChange.position = new Vector3(
                objectToChangePosition.x + 1, 
                objectToChangePosition.y, 
                objectToChangePosition.x
            );
        }
    }

    private void Start()
    {
        StartCoroutine(StartDisruption());
    }

    IEnumerator StartDisruption()
    {
        yield return new WaitForSeconds(StartDisruptionAfterNSeconds);

        _isDisruptionEnabled = true;
    }
}

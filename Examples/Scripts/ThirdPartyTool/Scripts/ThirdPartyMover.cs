using UnityEngine;

public class ThirdPartyMover : MonoBehaviour
{
    void LateUpdate()
    {
        if (Time.frameCount % 3 == 0)
        {
            var gameObject = GameObject.Find("TrackedObject-1");
            gameObject.transform.Translate(
                gameObject.transform.position.x + 3,
                gameObject.transform.position.y + 3,
                gameObject.transform.position.z + 3
            );
        }
    }
}

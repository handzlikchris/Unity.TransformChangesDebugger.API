using UnityEngine;

public class CircleMotion : MonoBehaviour
{
    private Quaternion _rotation;
    private Vector3 _radius = new Vector3(3,0,0);
    private float _currentRotation = 0.0f;
    private Vector3 _initialPosition;
    
    [SerializeField] private Transform MoveObject;

    private void Awake()
    {
        _initialPosition = MoveObject.position;
    }

    void Update()
    {
        UpdatePositionMethodNameSoLongItGetsTrimmedInModifiersPanel();
    }

    private void UpdatePositionMethodNameSoLongItGetsTrimmedInModifiersPanel()
    {
        _currentRotation += 1;
        _rotation.eulerAngles = new Vector3(0, _currentRotation, 0);
        MoveObject.position = (_rotation * _radius) + _initialPosition;
    }
}
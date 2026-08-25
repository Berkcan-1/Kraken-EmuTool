using UnityEngine;
using UnityEngine.InputSystem;

public class EmulatorCameraController : MonoBehaviour
{
    [Header("Hedef (Telefon Prefabı)")]
    public Transform target; 

    [Header("Yakınlaştırma (Fare Tekerleği)")]
    public float zoomSpeed = 0.001f;
    public float minScale = 0.5f;  
    public float maxScale = 2.5f;  

    [Header("Döndürme (Sağ Tık Basılıyken)")]
    public float rotationSpeed = 0.4f;
    public float smoothSpeed = 10f; 
    
    public float xMinLimit = -45f; 
    public float xMaxLimit = 45f;

    
    private float targetX = 0.0f;
    private float targetY = 0.0f;

    
    private float currentX = 0.0f;
    private float currentY = 0.0f;

    void Start()
    {
        if (target != null)
        {
            Vector3 angles = target.eulerAngles;
            targetX = currentX = angles.y;
            targetY = currentY = angles.x;
        }
    }

    
    public void SetRotation(float xAngle, float yAngle)
    {
        targetX = xAngle;
        targetY = yAngle;
    }

    void LateUpdate()
    {
        if (target == null || Mouse.current == null) return;

        
        if (Mouse.current.rightButton.isPressed)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            
            targetX -= mouseDelta.x * rotationSpeed;
            targetY += mouseDelta.y * rotationSpeed;
            targetY = Mathf.Clamp(targetY, xMinLimit, xMaxLimit);
        }

        
        currentX = Mathf.LerpAngle(currentX, targetX, Time.deltaTime * smoothSpeed);
        currentY = Mathf.LerpAngle(currentY, targetY, Time.deltaTime * smoothSpeed);
        target.rotation = Quaternion.Euler(currentY, currentX, 0);

        
        float scrollDelta = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scrollDelta) > 0.01f)
        {
            Vector3 currentScale = target.localScale;
            float newScale = currentScale.x + (scrollDelta * zoomSpeed);
            newScale = Mathf.Clamp(newScale, minScale, maxScale);
            target.localScale = new Vector3(newScale, newScale, newScale);
        }
    }
}
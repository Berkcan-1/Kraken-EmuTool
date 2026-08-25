using UnityEngine;
using System.Collections.Generic;

public class HardwareNode : MonoBehaviour
{
    [Header("Parça Kimliği")]
    public string partName = "Bilinmeyen Parça";
    
    [Tooltip("Bu parça sistem için kritik mi? (İşlemci, Batarya vb. için işaretle)")]
    public bool isCritical = false;
    public HardwareComponent componentType = HardwareComponent.Screen; 

 [Header("Fiziksel Kurallar (Bağımlılıklar)")]
    [Tooltip("Bu parçayı SÖKEBİLMEK için nelerin sökülmüş olması şart?")]
    public List<HardwareNode> blockersToRemove = new List<HardwareNode>(); 

    [Tooltip("Bu parçayı GERİ TAKABİLMEK için nelerin yerinde olması şart?")]
    public List<HardwareNode> requiredToRestore = new List<HardwareNode>(); 

    
    public bool CanBeRemoved()
    {
        foreach (var blocker in blockersToRemove)
        {
            
            if (blocker != null && blocker.gameObject.activeInHierarchy) return false;
        }
        return true;
    }

    
    public bool CanBeRestored()
    {
        foreach (var req in requiredToRestore)
        {
            
            if (req != null && !req.gameObject.activeInHierarchy) return false;
        }
        return true;
    }
}
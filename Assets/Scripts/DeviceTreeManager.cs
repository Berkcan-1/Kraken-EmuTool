using UnityEngine;
using System.Collections; 

public enum HardwareComponent { Battery, CPU, eMMC, CameraMain, Screen }

public class DeviceTreeManager : MonoBehaviour
{
    [Header("Bağlantılar")]
    public AndroidScreenConnector emulatorConnector;
    public EmulatorUIController uiController;

    [Header("Sistem Sesleri")]
    public AudioSource sysAudioSource;
    public AudioClip bootVibrationClip;

    [Header("Donanım Durumları (Otomatik)")]
    public bool isDeviceDead = false; 
    public bool hasBattery = true;
    public bool hasCPU = true;
    public bool hasEMMC = true;

    
    void Start()
    {
        if (emulatorConnector == null) emulatorConnector = FindObjectOfType<AndroidScreenConnector>();
        if (uiController == null) uiController = FindObjectOfType<EmulatorUIController>();

        LoadHardwareState();
    }

    private void LoadHardwareState()
    {
        string currentAvd = PlayerPrefs.GetString("SelectedAVDName", "Auto_Android12_AOSP");
        HardwareNode[] allNodes = FindObjectsOfType<HardwareNode>(true); 

        foreach (var node in allNodes)
        {
            
            int isInstalled = PlayerPrefs.GetInt($"HW_{currentAvd}_{node.partName}", 1);

            if (isInstalled == 0)
            {
                node.gameObject.SetActive(false);
                UpdateInternalState(node, false);
                
                
                if (uiController != null) uiController.AddRemovedPartToUI(node);
            }
            else
            {
                node.gameObject.SetActive(true);
                UpdateInternalState(node, true);
            }
        }
        UnityEngine.Debug.Log($"[DEVICE TREE] {currentAvd} makinesi için donanım profili başarıyla yüklendi.");
    }

    
    private void UpdateInternalState(HardwareNode node, bool isInstalled)
    {
        if (node.componentType == HardwareComponent.Battery) hasBattery = isInstalled;
        else if (node.componentType == HardwareComponent.CPU) { hasCPU = isInstalled; isDeviceDead = !isInstalled; }
        else if (node.componentType == HardwareComponent.eMMC) hasEMMC = isInstalled;
    }

    public void PlayBootSound()
    {
        if (sysAudioSource != null && bootVibrationClip != null)
        {
            sysAudioSource.PlayOneShot(bootVibrationClip);
        }
    }

    public void NotifyPartRemoved(HardwareNode node)
    {
        
        string currentAvd = PlayerPrefs.GetString("SelectedAVDName", "Auto_Android12_AOSP");
        PlayerPrefs.SetInt($"HW_{currentAvd}_{node.partName}", 0);
        PlayerPrefs.Save();

        if (uiController != null) uiController.AddRemovedPartToUI(node);

        switch (node.componentType)
        {
            case HardwareComponent.Battery:
                hasBattery = false;
                UnityEngine.Debug.LogWarning("[DEVICE TREE] BATARYA ÇEKİLDİ! Elektrik anında kesildi.");
                ForceKillSystem(); 
                break;

            case HardwareComponent.CPU:
                hasCPU = false;
                isDeviceDead = true; 
                UnityEngine.Debug.LogError("[DEVICE TREE] FATAL: İŞLEMCİ SÖKÜLDÜ! Kernel Panic Tetikleniyor...");
                
                if (emulatorConnector != null && emulatorConnector.IsEmulatorOn())
                {
                    emulatorConnector.SendAdbShellCommand("su -c 'echo c > /proc/sysrq-trigger'");
                    StartCoroutine(DelayedKill(2.0f)); 
                }
                break;

            case HardwareComponent.eMMC:
                hasEMMC = false;
                UnityEngine.Debug.LogWarning("[DEVICE TREE] eMMC söküldü! Depolama G/Ç hatası, System Server çöküyor...");
                
                if (emulatorConnector != null && emulatorConnector.IsEmulatorOn())
                {
                    emulatorConnector.SendAdbShellCommand("su -c 'killall vold'");
                    emulatorConnector.SendAdbShellCommand("su -c 'killall system_server'");
                    StartCoroutine(DelayedKill(4.0f)); 
                }
                break;
        }
    }

    public void NotifyPartRestored(HardwareNode node)
    {
        
        string currentAvd = PlayerPrefs.GetString("SelectedAVDName", "Auto_Android12_AOSP");
        PlayerPrefs.SetInt($"HW_{currentAvd}_{node.partName}", 1);
        PlayerPrefs.Save();

        UpdateInternalState(node, true);
        UnityEngine.Debug.Log($"[DEVICE TREE] {node.componentType} geri takıldı ve kaydedildi.");
    }

    private void ForceKillSystem()
    {
        if (emulatorConnector != null && emulatorConnector.IsEmulatorOn()) 
            emulatorConnector.PowerOffEmulator(); 
    }

    private IEnumerator DelayedKill(float delay)
    {
        yield return new WaitForSeconds(delay);
        ForceKillSystem();
    }

    
    public bool CanDeviceBoot()
    {
        if (isDeviceDead) { UnityEngine.Debug.LogWarning("[DONANIM] İşlemci ölü, boot iptal edildi."); return false; }
        if (!hasBattery) { UnityEngine.Debug.LogWarning("[DONANIM] Batarya yok, boot iptal edildi."); return false; }
        if (!hasEMMC) { UnityEngine.Debug.LogWarning("[DONANIM] eMMC takılı değil, boot iptal edildi."); return false; }
        return true; 
    }

    public bool CanEnterEDL()
    {
        return !isDeviceDead; 
    }
}
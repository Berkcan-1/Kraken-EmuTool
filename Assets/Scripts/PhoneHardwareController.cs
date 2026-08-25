using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PhoneHardwareController : MonoBehaviour
{
    [Header("Bağlantılar")]
    public AndroidScreenConnector emulatorConnector;
    public EmulatorUIController uiController;
    public DeviceTreeManager deviceTree;
    public BootMenuController bootMenu; 
    
    [Header("Aksiyon Ayarları")]
    public float coverHoldTime = 1.0f; 
    public Image circularProgressBar; 
    public float powerLongPressTime = 1.5f;

    [Header("Kıvılcım Sesleri")]
    public AudioSource sfxAudioSource;
    public AudioClip sparkClip;

    private float currentHoldTime = 0f;
    private Transform currentTarget = null;
    private bool isActionTriggered = false;
    private Vector2 dragStartPos;

    void Start()
    {
        if (deviceTree == null) deviceTree = FindObjectOfType<DeviceTreeManager>();
        if (uiController == null) uiController = FindObjectOfType<EmulatorUIController>();
        if (emulatorConnector == null) emulatorConnector = FindObjectOfType<AndroidScreenConnector>();
        if (bootMenu == null) bootMenu = FindObjectOfType<BootMenuController>();
    }

    void Update()
    {
        if (Mouse.current == null) return;

        
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            dragStartPos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(dragStartPos);
            
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                currentTarget = hit.transform;
                currentHoldTime = 0f;
                isActionTriggered = false;
                
                if (currentTarget.CompareTag("TP_EDL"))
                {
                    if (!emulatorConnector.IsEmulatorOn() && (deviceTree == null || deviceTree.CanEnterEDL()))
                    {
                        if (uiController != null) uiController.TriggerEDLMode();
                    }
                }
                else if (currentTarget.CompareTag("TP_Restart"))
                {
                    if (emulatorConnector.IsEmulatorOn())
                    {
                        emulatorConnector.SendAdbShellCommand("reboot");
                        if (deviceTree != null) deviceTree.PlayBootSound();
                    }
                }
                else if (currentTarget.CompareTag("TP_Spark"))
                {
                    if (sfxAudioSource != null && sparkClip != null) sfxAudioSource.PlayOneShot(sparkClip);
                    if (emulatorConnector.IsEmulatorOn()) emulatorConnector.PowerOffEmulator();
                }
                
                bool isButton = currentTarget.CompareTag("BtnPower") || currentTarget.CompareTag("BtnVolUp") || currentTarget.CompareTag("BtnVolDown");
                
                if (!isButton && currentTarget.GetComponent<HardwareNode>() != null && circularProgressBar != null)
                {
                    circularProgressBar.fillAmount = 0;
                    circularProgressBar.gameObject.SetActive(true);
                    circularProgressBar.transform.position = Mouse.current.position.ReadValue();
                }
            }
        }

        
        if (Mouse.current.leftButton.isPressed && currentTarget != null && !isActionTriggered)
        {
            currentHoldTime += Time.deltaTime;
            bool isButton = currentTarget.CompareTag("BtnPower") || currentTarget.CompareTag("BtnVolUp") || currentTarget.CompareTag("BtnVolDown");

            if (isButton)
            {
                
                if (Vector2.Distance(Mouse.current.position.ReadValue(), dragStartPos) > 40f)
                {
                    int keyCode = 0; string keyName = "";
                    if (currentTarget.CompareTag("BtnPower")) { keyCode = 26; keyName = "Güç Tuşu"; }
                    else if (currentTarget.CompareTag("BtnVolUp")) { keyCode = 24; keyName = "Ses Açma (Vol+)"; }
                    else if (currentTarget.CompareTag("BtnVolDown")) { keyCode = 25; keyName = "Ses Kısma (Vol-)"; }

                    if (uiController != null) uiController.AddMacroKey(keyName, keyCode);

                    currentTarget = null;
                    currentHoldTime = 0f;
                    return;
                }

                
                if (currentTarget.CompareTag("BtnPower") && currentHoldTime >= powerLongPressTime)
                {
                    isActionTriggered = true;
                    if (emulatorConnector.IsEmulatorOn())
                    {
                        emulatorConnector.SendAdbShellCommand("input keyevent --longpress 26");
                    }
                    else if (bootMenu != null && bootMenu.currentMode == BootMode.Fastboot)
                    {
                        UnityEngine.Debug.Log("[DONANIM] Fastboot'ta Güç tuşuna uzun basıldı, sistem başlatılıyor...");
                        bootMenu.Hide();
                        emulatorConnector.PowerOnEmulator();
                        if (deviceTree != null) deviceTree.PlayBootSound();
                    }
                    else if (bootMenu != null && bootMenu.currentMode == BootMode.Recovery)
                    {
                        
                    }
                    else if (deviceTree == null || deviceTree.CanDeviceBoot()) 
                    {
                        if (uiController != null && uiController.IsMacroActive(24)) 
                        {
                            if (bootMenu != null) bootMenu.ShowRecovery();
                            if (deviceTree != null) deviceTree.PlayBootSound();
                        }
                        else if (uiController != null && uiController.IsMacroActive(25)) 
                        {
                            if (bootMenu != null) bootMenu.ShowFastboot();
                            if (deviceTree != null) deviceTree.PlayBootSound();
                        }
                        else
                        {
                            emulatorConnector.PowerOnEmulator();
                            if (deviceTree != null) deviceTree.PlayBootSound();
                        }
                    }
                }
            }
            else 
            {
                HardwareNode node = currentTarget.GetComponent<HardwareNode>();
                if (node != null)
                {
                    if (circularProgressBar != null)
                    {
                        circularProgressBar.transform.position = Mouse.current.position.ReadValue();
                        circularProgressBar.fillAmount = currentHoldTime / coverHoldTime; 
                    }

                    if (currentHoldTime >= coverHoldTime)
                    {
                        isActionTriggered = true;
                        if (circularProgressBar != null) circularProgressBar.gameObject.SetActive(false);

                        if (!node.CanBeRemoved()) return;

                        node.gameObject.SetActive(false);
                        
                        if (node.isCritical && deviceTree != null) deviceTree.NotifyPartRemoved(node);
                        else if (uiController != null) uiController.AddRemovedPartToUI(node);
                    }
                }
            }
        }

        
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            if (circularProgressBar != null) circularProgressBar.gameObject.SetActive(false);

            if (currentTarget != null && !isActionTriggered)
            {
                if (bootMenu != null && bootMenu.currentMode != BootMode.None)
                {
                    if (currentTarget.CompareTag("BtnPower")) bootMenu.OnPower();
                    else if (currentTarget.CompareTag("BtnVolUp")) bootMenu.OnVolUp();
                    else if (currentTarget.CompareTag("BtnVolDown")) bootMenu.OnVolDown();
                }
                else if (emulatorConnector.IsEmulatorOn())
                {
                    if (currentTarget.CompareTag("BtnPower"))
                        emulatorConnector.SendAdbShellCommand("input keyevent 26");
                    else if (currentTarget.CompareTag("BtnVolUp"))
                        emulatorConnector.SendAdbShellCommand("input keyevent 24"); 
                    else if (currentTarget.CompareTag("BtnVolDown"))
                        emulatorConnector.SendAdbShellCommand("input keyevent 25"); 
                }
            }
            currentTarget = null;
            currentHoldTime = 0f;
        }
    }
}
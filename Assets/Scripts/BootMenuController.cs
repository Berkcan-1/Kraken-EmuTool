using UnityEngine;
using UnityEngine.UI;
using System.IO;

public enum BootMode { None, Fastboot, Recovery }

public class BootMenuController : MonoBehaviour
{
    [Header("Bağlantılar")]
    public AndroidScreenConnector adbConnector;
    public DeviceTreeManager deviceTree;
    public Transform screenQuad; 

    [Header("Görseller (Inspector'dan Ata)")]
    public Texture2D fastbootImage;
    public Font recoveryFont; 

    public BootMode currentMode = BootMode.None;

    private GameObject bootCanvasObj;
    private RawImage bgImage;
    private Text recoveryText;

    private int selectedIndex = 0;
    private string[] recoveryOptions = { "Reboot system now", "Wipe data/factory reset", "Reboot to bootloader", "Power off" };

    void Start()
    {
        if (screenQuad == null) screenQuad = this.transform;
        CreateBootCanvas();
    }

    void CreateBootCanvas()
    {
        bootCanvasObj = new GameObject("BootCanvas");
        bootCanvasObj.transform.SetParent(screenQuad, false);
        Canvas canvas = bootCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        
        RectTransform rect = bootCanvasObj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1080, 2340);
        rect.localScale = new Vector3(1f / 1080f, 1f / 2340f, 1f); 
        rect.localPosition = new Vector3(0, 0, -0.001f); 
        rect.localRotation = Quaternion.identity;

        bgImage = bootCanvasObj.AddComponent<RawImage>();
        bgImage.color = Color.black;

        GameObject textObj = new GameObject("RecoveryText");
        textObj.transform.SetParent(bootCanvasObj.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.offsetMin = new Vector2(50, 80); textRect.offsetMax = new Vector2(-50, -50);

        recoveryText = textObj.AddComponent<Text>();
        recoveryText.font = recoveryFont != null ? recoveryFont : Resources.GetBuiltinResource<Font>("Arial.ttf");

        bootCanvasObj.SetActive(false);
    }

    public void ShowFastboot()
    {
        currentMode = BootMode.Fastboot;
        bootCanvasObj.SetActive(true);
        bgImage.texture = fastbootImage;
        bgImage.color = Color.white;
        
        UpdateFastbootText();
        UnityEngine.Debug.Log("[BOOT] Fastboot Modu Açıldı!");
        
        
        FindObjectOfType<EmulatorUIController>()?.OpenFastbootPanel();
    }

    public void UpdateFastbootText()
    {
        string avdName = PlayerPrefs.GetString("SelectedAVDName", "Auto_Android12_AOSP");
        int isUnlocked = PlayerPrefs.GetInt("Unlocked_" + avdName, 0);
        string state = isUnlocked == 1 ? "unlocked" : "locked";
        string color = isUnlocked == 1 ? "red" : "green";

        recoveryText.alignment = TextAnchor.LowerCenter;
        recoveryText.fontSize = 32;
        recoveryText.text = $"\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n<color=white>PRODUCT_NAME - lito\nVARIANT - SM_ UFS\nSECURE BOOT - yes\nDEVICE STATE - </color><color={color}>{state}</color>";
    }

    public void ShowRecovery()
    {
        currentMode = BootMode.Recovery;
        bootCanvasObj.SetActive(true);
        bgImage.texture = null;
        bgImage.color = Color.black;
        selectedIndex = 0;

        recoveryText.alignment = TextAnchor.UpperLeft;
        recoveryText.fontSize = 55;
        UpdateRecoveryUI();
        UnityEngine.Debug.Log("[BOOT] Recovery Modu Açıldı!");
    }

    public void Hide()
    {
        currentMode = BootMode.None;
        bootCanvasObj.SetActive(false);
        FindObjectOfType<EmulatorUIController>()?.CloseFastbootPanel();
    }

    public void OnVolUp()
    {
        if (currentMode == BootMode.Recovery) { selectedIndex--; if (selectedIndex < 0) selectedIndex = recoveryOptions.Length - 1; UpdateRecoveryUI(); }
    }

    public void OnVolDown()
    {
        if (currentMode == BootMode.Recovery) { selectedIndex++; if (selectedIndex >= recoveryOptions.Length) selectedIndex = 0; UpdateRecoveryUI(); }
    }

    public void OnPower()
    {
        if (currentMode == BootMode.Recovery) ExecuteRecoveryOption();
    }

    private void UpdateRecoveryUI()
    {
        string menu = "<color=yellow>Android Recovery\nUse volume up/down and power.</color>\n\n";
        for (int i = 0; i < recoveryOptions.Length; i++)
        {
            if (i == selectedIndex) menu += $"<color=#00FF00>-> {recoveryOptions[i]}</color>\n";
            else menu += $"   {recoveryOptions[i]}\n";
        }
        recoveryText.text = menu;
    }

    private void ExecuteRecoveryOption()
    {
        string option = recoveryOptions[selectedIndex];

        if (option == "Reboot system now")
        {
            Hide();
            adbConnector.PowerOnEmulator();
            if (deviceTree != null) deviceTree.PlayBootSound();
        }
        else if (option == "Wipe data/factory reset")
        {
            recoveryText.text = "<color=red>\n-- Wiping data...\nFormatting /data...\nFormatting /cache...\nData wipe complete.</color>";
            FindObjectOfType<EmulatorUIController>()?.WipeDataDirectly();
            Invoke("ShowRecovery", 2.5f); 
        }
        else if (option == "Reboot to bootloader") ShowFastboot();
        else if (option == "Power off") Hide();
    }
}
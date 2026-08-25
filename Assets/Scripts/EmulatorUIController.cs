using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine.SceneManagement;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

public class EmulatorUIController : MonoBehaviour
{
    [Header("Bağlantılar")]
    public EmulatorCameraController cameraController;
    public AndroidScreenConnector adbConnector;

    private string adbPath;

    
    private VisualElement[] panels;
    private Button[] tabBtns;

    
    private ScrollView appListScroll;
    private Label lblAutoLog;
    private ScrollView partsListScroll;

    
    private int simulatedBattery = 100;
    private bool isWifiOn = true;
    private bool isDataOn = true;
    
    
    private ScrollView macroListScroll;
    private HashSet<int> activeMacros = new HashSet<int>();
    private Coroutine macroCoroutine = null;

    
    private VisualElement edlSlidePanel;
    private TextField romSourceInput;
    private Label lblFlashLog;
    public bool isEDLMode = false; 
    private volatile string pendingFlashLog = null;

    void Start()
    {
        adbPath = Path.Combine(Application.streamingAssetsPath, "scrcpy", "adb.exe");
    }

    void Update()
    {
        if (pendingFlashLog != null)
        {
            if (lblFlashLog != null) lblFlashLog.text = pendingFlashLog;
            pendingFlashLog = null;
        }
    }

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        tabBtns = new Button[] { 
            root.Q<Button>("btnTabHard"), root.Q<Button>("btnTabCam"), 
            root.Q<Button>("btnTabTerm"), root.Q<Button>("btnTabAuto"), 
            root.Q<Button>("btnTabParts"), root.Q<Button>("btnTabMacro") 
        };
        panels = new VisualElement[] { 
            root.Q<VisualElement>("panelHard"), root.Q<VisualElement>("panelCam"), 
            root.Q<VisualElement>("panelTerm"), root.Q<VisualElement>("panelAuto"), 
            root.Q<VisualElement>("panelParts"), root.Q<VisualElement>("panelMacro") 
        };

        for (int i = 0; i < tabBtns.Length; i++)
        {
            int index = i; 
            tabBtns[index].clicked += () => SwitchTab(index);
        }

        
        root.Q<Button>("btnPower").clicked += () => adbConnector.SendAdbShellCommand("input keyevent 26");
        root.Q<Button>("btnHome").clicked += () => adbConnector.SendAdbShellCommand("input keyevent 3");
        root.Q<Button>("btnVolUp").clicked += () => adbConnector.SendAdbShellCommand("input keyevent 24");
        root.Q<Button>("btnVolDown").clicked += () => adbConnector.SendAdbShellCommand("input keyevent 25");
        root.Q<Button>("btnExit").clicked += ExitToMainMenu;
        
        
        root.Q<Button>("btnBatPlus").clicked += () => ChangeBattery(5);
        root.Q<Button>("btnBatMinus").clicked += () => ChangeBattery(-5);
        
        
        root.Q<Button>("btnBatCharge").clicked += () => {
            adbConnector.SendAdbShellCommand("dumpsys battery set ac 1");
            adbConnector.SendAdbShellCommand("dumpsys battery set status 2"); 
        };
        root.Q<Button>("btnBatUnplug").clicked += () => {
            adbConnector.SendAdbShellCommand("dumpsys battery set ac 0");
            adbConnector.SendAdbShellCommand("dumpsys battery set status 3"); 
        };
        
        
        root.Q<Button>("btnBatReset").clicked += () => {
            simulatedBattery = 100;
            adbConnector.SendAdbShellCommand("dumpsys battery reset");
        };

        
        root.Q<Button>("btnWifiToggle").clicked += () => {
            isWifiOn = !isWifiOn;
            adbConnector.SendAdbShellCommand(isWifiOn ? "svc wifi enable" : "svc wifi disable");
        };
        root.Q<Button>("btnDataToggle").clicked += () => {
            isDataOn = !isDataOn;
            adbConnector.SendAdbShellCommand(isDataOn ? "svc data enable" : "svc data disable");
        };

        
        root.Q<Button>("btnDarkMode").clicked += () => adbConnector.SendAdbShellCommand("cmd uimode night yes");
        root.Q<Button>("btnLightMode").clicked += () => adbConnector.SendAdbShellCommand("cmd uimode night no");

        
        edlSlidePanel = root.Q<VisualElement>("edlSlidePanel");
        romSourceInput = root.Q<TextField>("romSourceInput");
        lblFlashLog = root.Q<Label>("lblFlashLog");

        root.Q<Button>("btnFlashRom").clicked += StartRomFlashing;
        root.Q<Button>("btnBrowseFolder").clicked += () => {
            string selectedFolder = WindowsFolderBrowser.ShowDialog();
            if (!string.IsNullOrEmpty(selectedFolder)) romSourceInput.value = selectedFolder;
        };

        root.Q<Button>("btnCloseEdl").clicked += () => {
            edlSlidePanel.RemoveFromClassList("open");
            isEDLMode = false;
            lblFlashLog.text = "Cihaz yeniden başlatılıyor...";
        };

        
        root.Q<Button>("btnCamFront").clicked += () => cameraController.SetRotation(0, 0);
        root.Q<Button>("btnCamBack").clicked += () => cameraController.SetRotation(180, 0);
        root.Q<Button>("btnCamLeft").clicked += () => cameraController.SetRotation(90, 0);
        root.Q<Button>("btnCamRight").clicked += () => cameraController.SetRotation(-90, 0);

      
        var adbInput = root.Q<TextField>("adbInput");
        var lblTerminalLog = root.Q<Label>("lblTerminalLog");
        
        
        adbInput.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                ExecuteAdbCommand(adbInput, lblTerminalLog);
            }
        });

        
        root.Q<Button>("btnSendAdb").clicked += () => ExecuteAdbCommand(adbInput, lblTerminalLog);

        
        appListScroll = root.Q<ScrollView>("appListScroll");
        lblAutoLog = root.Q<Label>("lblAutoLog");
        root.Q<Button>("btnRefreshApps").clicked += () => StartCoroutine(RefreshAppList());

        
        partsListScroll = root.Q<ScrollView>("partsListScroll");
        macroListScroll = root.Q<ScrollView>("macroListScroll");

        
        VisualElement fastbootSlidePanel = root.Q<VisualElement>("fastbootSlidePanel");
        TextField fbInput = root.Q<TextField>("fbInput");
        Label lblFbLog = root.Q<Label>("lblFbLog");
        
        if(root.Q<Button>("btnSendFb") != null)
        {
            root.Q<Button>("btnSendFb").clicked += () => SendFastbootCmd(fbInput.value, lblFbLog, fastbootSlidePanel);
        }
        
    } 

    
    
    
    private void ExitToMainMenu()
    {
        UnityEngine.Debug.Log("Ana Menüye Dönülüyor...");
        
        
        if (adbConnector != null)
        {
            adbConnector.SendAdbShellCommand("reboot -p"); 
        }

        
        try { foreach (var p in Process.GetProcessesByName("qemu-system-x86_64")) { try { p.Kill(); } catch { } } } catch { }
        try { foreach (var p in Process.GetProcessesByName("scrcpy")) { try { p.Kill(); } catch { } } } catch { }
        
        
        SceneManager.LoadScene("mainmenu"); 
    }

    
    private void ChangeBattery(int amount)
    {
        simulatedBattery = Mathf.Clamp(simulatedBattery + amount, 1, 100);
        adbConnector.SendAdbShellCommand("dumpsys battery unplug"); 
        adbConnector.SendAdbShellCommand($"dumpsys battery set level {simulatedBattery}"); 
    }

    void SwitchTab(int activeIndex)
    {
        for (int i = 0; i < panels.Length; i++)
        {
            if (i == activeIndex)
            {
                panels[i].style.display = DisplayStyle.Flex;
                tabBtns[i].AddToClassList("active");
            }
            else
            {
                panels[i].style.display = DisplayStyle.None;
                tabBtns[i].RemoveFromClassList("active");
            }
        }
    }

    
    
    
    public void AddMacroKey(string keyName, int keyCode) 
    {
        if (activeMacros.Contains(keyCode)) return; 
        activeMacros.Add(keyCode);
        
        VisualElement row = new VisualElement(); 
        row.AddToClassList("part-item");
        
        Label nameLbl = new Label($"[KİLİTLİ] {keyName}"); 
        nameLbl.AddToClassList("part-name");
        
        Button stopBtn = new Button(); 
        stopBtn.text = "Serbest Bırak"; 
        stopBtn.AddToClassList("app-del-btn");
        
        stopBtn.clicked += () => { 
            activeMacros.Remove(keyCode); 
            if (macroListScroll.Contains(row)) macroListScroll.Remove(row); 
        };
        
        row.Add(nameLbl); 
        row.Add(stopBtn); 
        macroListScroll.Add(row);
        
        SwitchTab(5);
        
        if (macroCoroutine == null) macroCoroutine = StartCoroutine(MacroSpamRoutine());
    }

    private IEnumerator MacroSpamRoutine() 
    {
        while (true) 
        {
            if (activeMacros.Count == 0) { macroCoroutine = null; yield break; }
            if (adbConnector.IsEmulatorOn()) 
            { 
                foreach (int key in activeMacros) adbConnector.SendAdbShellCommand($"input keyevent {key}"); 
            }
            yield return new WaitForSeconds(0.35f); 
        }
    }

    public bool IsMacroActive(int keyCode) 
    { 
        return activeMacros.Contains(keyCode); 
    }

    
    
    
    public void AddRemovedPartToUI(HardwareNode node)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("part-item");

        Label nameLbl = new Label(node.partName);
        nameLbl.AddToClassList("part-name");

        Button restoreBtn = new Button();
        restoreBtn.text = "Geri Tak";
        restoreBtn.AddToClassList("part-restore-btn");

        restoreBtn.clicked += () => {
            if (!node.CanBeRestored())
            {
                UnityEngine.Debug.LogWarning($"[HATA] {node.partName} takılamaz! Altındaki zemin/şase henüz takılmamış.");
                restoreBtn.text = "Zemin Eksik!";
                return;
            }

            node.gameObject.SetActive(true);
            if (partsListScroll.Contains(row)) partsListScroll.Remove(row);
            
            if (node.isCritical)
            {
                DeviceTreeManager dt = FindObjectOfType<DeviceTreeManager>();
                if (dt != null) dt.NotifyPartRestored(node);
            }
            UnityEngine.Debug.Log($"[DONANIM] {node.partName} cihaza geri takıldı!");
        };

        row.Add(nameLbl);
        row.Add(restoreBtn);
        partsListScroll.Add(row);
    }

    
    
    
    public void OpenFastbootPanel() 
    { 
        var root = GetComponent<UIDocument>().rootVisualElement;
        root.Q<VisualElement>("fastbootSlidePanel")?.AddToClassList("open"); 
    }

    public void CloseFastbootPanel() 
    { 
        var root = GetComponent<UIDocument>().rootVisualElement;
        root.Q<VisualElement>("fastbootSlidePanel")?.RemoveFromClassList("open"); 
    }

    private void SendFastbootCmd(string cmd, Label lblFbLog, VisualElement fastbootSlidePanel)
    {
        cmd = cmd.Trim().ToLower();
        string avdName = PlayerPrefs.GetString("SelectedAVDName", "Auto_Android12_AOSP");
        BootMenuController bootMenu = FindObjectOfType<BootMenuController>();

        if (cmd == "fastboot oem unlock") 
        {
            PlayerPrefs.SetInt("Unlocked_" + avdName, 1); PlayerPrefs.Save();
            WipeDataDirectly();
            if (bootMenu != null) bootMenu.UpdateFastbootText();
            lblFbLog.text = "OKAY [0.050s]\nDevice unlocked & wiped! System is now WRITABLE.";
        }
        else if (cmd == "fastboot oem lock") 
        {
            PlayerPrefs.SetInt("Unlocked_" + avdName, 0); PlayerPrefs.Save();
            WipeDataDirectly();
            if (bootMenu != null) bootMenu.UpdateFastbootText();
            lblFbLog.text = "OKAY [0.060s]\nDevice locked & wiped! Writable mode disabled.";
        }
        else if (cmd == "fastboot reboot") 
        {
            lblFbLog.text = "Rebooting..."; 
            if (bootMenu != null) bootMenu.Hide();
            adbConnector.PowerOnEmulator(); 
            FindObjectOfType<DeviceTreeManager>()?.PlayBootSound();
        }
        else if (cmd == "fastboot reboot recovery") 
        { 
            if (bootMenu != null) bootMenu.ShowRecovery(); 
        }
        else 
        { 
            lblFbLog.text = $"FAILED (remote: unknown command '{cmd}')"; 
        }
    }

    public void WipeDataDirectly()
    {
        try {
            string targetAvdDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".android", "avd", PlayerPrefs.GetString("SelectedAVDName", "Auto_Android12_AOSP") + ".avd");
            if (Directory.Exists(targetAvdDir)) {
                string[] wipeFiles = Directory.GetFiles(targetAvdDir, "*qemu*", SearchOption.TopDirectoryOnly);
                foreach (string f in wipeFiles) File.Delete(f);
            }
        } catch {}
    }

    
    
    
    public void TriggerEDLMode()
    {
        edlSlidePanel.AddToClassList("open");
        lblFlashLog.text = "ROM dosyası bekleniyor...";
    }

    private void StartRomFlashing()
    {
        string sourceDir = romSourceInput.value;
        if (string.IsNullOrWhiteSpace(sourceDir)) { lblFlashLog.text = "[HATA] Geçerli bir kaynak klasörü girin!"; return; }
        StartCoroutine(FlashRomCoroutine(sourceDir));
    }

    private IEnumerator FlashRomCoroutine(string sourceDir)
    {
        int targetApi = 31; 
        Match match = Regex.Match(sourceDir, @"android-(\d+)");
        if (match.Success) targetApi = int.Parse(match.Groups[1].Value);

        lblFlashLog.text = $"[ADIM 1] EDL Bağlantısı Kesiliyor... (Hedef ROM: Android API {targetApi})";
        adbConnector.PowerOffEmulator();
        yield return new WaitForSeconds(2f);

        foreach (var p in Process.GetProcessesByName("qemu-system-x86_64")) { try { p.Kill(); } catch { } }
        yield return new WaitForSeconds(1f); 

        lblFlashLog.text = "[ADIM 2] Eski Anakart Partitionları (Wipe) Siliniyor...";
        yield return new WaitForSeconds(1.5f); 

        string sdkPath = adbConnector.androidSdkPath;
        string avdName = PlayerPrefs.GetString("SelectedAVDName", "Auto_Android12_AOSP");
        string aospImage = $"system-images;android-{targetApi};default;x86_64";
        string cmdlineToolsPath = null;
        string basePath = Path.Combine(sdkPath, "cmdline-tools");
        if (Directory.Exists(basePath))
        {
            foreach (string dir in Directory.GetDirectories(basePath))
            {
                string binPath = Path.Combine(dir, "bin");
                if (File.Exists(Path.Combine(binPath, "avdmanager.bat"))) { cmdlineToolsPath = binPath; break; }
            }
        }

        if (cmdlineToolsPath == null) { lblFlashLog.text = "[HATA] cmdline-tools bulunamadı! Android Studio'dan yükleyin."; yield break; }

        string avdManagerPath = Path.Combine(cmdlineToolsPath, "avdmanager.bat");
        string batPath = Path.Combine(Application.temporaryCachePath, "flash_rom.bat");
        string[] batLines = new string[] { "@echo off", $"echo y | \"{avdManagerPath}\" delete avd -n {avdName}", $"echo no | \"{avdManagerPath}\" create avd -n {avdName} -k \"{aospImage}\" --device \"pixel\" --force" };
        File.WriteAllLines(batPath, batLines);

        lblFlashLog.text = "[ADIM 3] Yeni ROM Yazılıyor... (Lütfen Cihazı Sökmeyin)";
        ProcessStartInfo createInfo = new ProcessStartInfo { FileName = batPath, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        createInfo.EnvironmentVariables["ANDROID_SDK_ROOT"] = sdkPath; createInfo.EnvironmentVariables["ANDROID_HOME"] = sdkPath;
        string javaHomePath = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (string.IsNullOrEmpty(javaHomePath))
        {
            string[] possibleJavaPaths = { @"C:\Program Files\Android\Android Studio\jbr", @"C:\Program Files\Android\Android Studio\jre" };
            foreach (string jPath in possibleJavaPaths) { if (Directory.Exists(jPath)) { javaHomePath = jPath; break; } }
        }
        if (!string.IsNullOrEmpty(javaHomePath)) createInfo.EnvironmentVariables["JAVA_HOME"] = javaHomePath;

        Process createProcess = Process.Start(createInfo);
        while (!createProcess.HasExited) yield return null; 
        if (File.Exists(batPath)) File.Delete(batPath);

        lblFlashLog.text = "[ADIM 4] ROM Başarıyla Flashlandı! Cihaz başlatılıyor...";
        yield return new WaitForSeconds(2.5f);

        edlSlidePanel.RemoveFromClassList("open");
        isEDLMode = false;
        adbConnector.PowerOnEmulator();
        FindObjectOfType<DeviceTreeManager>()?.PlayBootSound();
    }

    
    
    
private void ExecuteAdbCommand(TextField adbInput, Label lblTerminalLog)
    {
        string cmd = adbInput.value.Trim();
        if (!string.IsNullOrEmpty(cmd))
        {
            
            if (cmd.StartsWith("adb ")) cmd = cmd.Substring(4); 
            
            StartCoroutine(RunAdbCommandAndRead(cmd, lblTerminalLog));
            adbInput.value = "";
            adbInput.Focus(); 
        }
    }

    IEnumerator RunAdbCommandAndRead(string command, Label logLabel)
    {
        logLabel.text = $"Running: adb {command}...";
        
        
        ProcessStartInfo startInfo = new ProcessStartInfo { 
            FileName = adbPath, 
            Arguments = command, 
            CreateNoWindow = true, 
            UseShellExecute = false, 
            RedirectStandardOutput = true, 
            RedirectStandardError = true 
        };
        
        Process process = Process.Start(startInfo);
        string output = ""; 
        string error = "";
        
        process.OutputDataReceived += (sender, args) => { if (args.Data != null) output += args.Data + "\n"; };
        process.ErrorDataReceived += (sender, args) => { if (args.Data != null) error += args.Data + "\n"; };
        
        process.BeginOutputReadLine(); 
        process.BeginErrorReadLine();
        
        while (!process.HasExited) yield return null;
        
        
        logLabel.text = string.IsNullOrWhiteSpace(error) 
            ? (string.IsNullOrWhiteSpace(output) ? "Success (No output)." : output.Trim()) 
            : $"[ERROR]\n{error.Trim()}";
    }

    IEnumerator RefreshAppList()
    {
        lblAutoLog.text = "Uygulamalar taranıyor...";
        appListScroll.Clear();
        ProcessStartInfo listInfo = new ProcessStartInfo { FileName = adbPath, Arguments = "shell pm list packages", CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        Process listProcess = Process.Start(listInfo);
        System.Collections.Generic.List<string> packages = new System.Collections.Generic.List<string>();
        listProcess.OutputDataReceived += (sender, args) => { if (!string.IsNullOrEmpty(args.Data) && args.Data.StartsWith("package:")) { packages.Add(args.Data.Substring(8).Trim()); } };
        listProcess.BeginOutputReadLine();
        while (!listProcess.HasExited) yield return null;
        int count = 0; foreach(string pkg in packages) { CreateAppListItem(pkg); count++; }
        lblAutoLog.text = count > 0 ? $"{count} adet uygulama listelendi." : "Uygulama bulunamadı.";
    }

    void CreateAppListItem(string pkgName)
    {
        string displayName = pkgName; string[] parts = pkgName.Split('.'); if (parts.Length > 0) displayName = parts[parts.Length - 1];
        VisualElement row = new VisualElement(); row.AddToClassList("app-item");
        VisualElement iconBox = new VisualElement(); iconBox.AddToClassList("app-icon-box");
        float hue = Mathf.Abs(pkgName.GetHashCode() % 360) / 360f; iconBox.style.backgroundColor = Color.HSVToRGB(hue, 0.6f, 0.8f); 
        Label iconLetter = new Label(displayName.Substring(0, 1).ToUpper()); iconLetter.AddToClassList("app-icon-letter"); iconBox.Add(iconLetter);
        VisualElement textContainer = new VisualElement(); textContainer.AddToClassList("app-text-container");
        Label nameLbl = new Label(displayName); nameLbl.AddToClassList("app-name"); Label pkgLbl = new Label(pkgName); pkgLbl.AddToClassList("app-package-name");
        textContainer.Add(nameLbl); textContainer.Add(pkgLbl);
        Button delBtn = new Button(); delBtn.text = "Sil"; delBtn.AddToClassList("app-del-btn");
        delBtn.clicked += () => StartCoroutine(UninstallApp(pkgName, row));
        row.Add(iconBox); row.Add(textContainer); row.Add(delBtn); appListScroll.Add(row);
    }

    IEnumerator UninstallApp(string pkgName, VisualElement rowElement)
    {
        lblAutoLog.text = $"{pkgName} siliniyor...";
        ProcessStartInfo uninstallInfo = new ProcessStartInfo { FileName = adbPath, Arguments = $"shell pm uninstall --user 0 {pkgName}", CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        Process uProcess = Process.Start(uninstallInfo);
        string output = ""; uProcess.OutputDataReceived += (sender, args) => { if (args.Data != null) output += args.Data; }; uProcess.BeginOutputReadLine();
        while (!uProcess.HasExited) yield return null;
        if (output.Contains("Success")) { lblAutoLog.text = $"{pkgName} kaldırıldı!"; if (appListScroll.Contains(rowElement)) appListScroll.Remove(rowElement); }
        else { lblAutoLog.text = $"[HATA]: {output}"; }
        
    }
}



public static class WindowsFolderBrowser
{
    public delegate int BrowseCallbackProc(IntPtr hwnd, int uMsg, IntPtr lParam, IntPtr lpData);
    [StructLayout(LayoutKind.Sequential)] public struct BROWSEINFO { public IntPtr hwndOwner; public IntPtr pidlRoot; public IntPtr pszDisplayName; [MarshalAs(UnmanagedType.LPTStr)] public string lpszTitle; public uint ulFlags; public BrowseCallbackProc lpfn; public IntPtr lParam; public int iImage; }
    [DllImport("shell32.dll")] static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);
    [DllImport("shell32.dll", CharSet = CharSet.Auto)] static extern bool SHGetPathFromIDList(IntPtr pidl, IntPtr pszPath);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr pv);

    public static string ShowDialog()
    {
        BROWSEINFO bi = new BROWSEINFO(); bi.lpszTitle = "Cihaza yüklenecek (Kopyalanacak) ROM veya SDK Klasörünü Seçin"; bi.ulFlags = 0x00000040 | 0x00000010; 
        IntPtr pidl = SHBrowseForFolder(ref bi);
        if (pidl != IntPtr.Zero)
        {
            IntPtr pathPtr = Marshal.AllocHGlobal(260 * Marshal.SystemDefaultCharSize);
            if (SHGetPathFromIDList(pidl, pathPtr)) { string path = Marshal.PtrToStringAuto(pathPtr); Marshal.FreeHGlobal(pathPtr); CoTaskMemFree(pidl); return path; }
            Marshal.FreeHGlobal(pathPtr); CoTaskMemFree(pidl);
        }
        return "";
    }

    
}
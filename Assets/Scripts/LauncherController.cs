using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

public class LauncherController : MonoBehaviour
{
    [Header("Splash Screen Ayarları")]
    public Texture2D appLogo; 

    private VisualElement appWindow;
    private VisualElement setupOverlay;
    private Button btnCheckAgain;

    private DropdownField existingAvdDropdown;
    private DropdownField newVersionDropdown;
    private Button launchBtn;
    private Button createBtn;
    private Label loadingDetails;
    
    private VisualElement splashContainer;
    private VisualElement splashLogo;
    private VisualElement splashSpinner;
    private VisualElement statusSpinner;

    private string androidSdkPath = "";
    private string emulatorPath = "";
    private string volatileLog = "System Ready. Waiting for user input...";
    private bool isLoading = false;
    private float rotationAngle = 0f;

    private readonly List<(string name, int api)> androidVersions = new List<(string, int)>
    {
        ("Android 17", 37), ("Android 16", 36), ("Android 15", 35),
        ("Android 14", 34), ("Android 13", 33), ("Android 12", 31),
        ("Android 11", 30), ("Android 10", 29), ("Android 9", 28),
        ("Android 8.1", 27), ("Android 7.0", 24), ("Android 6.0", 23),
        ("Android 5.0", 21)
    };

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        appWindow = root.Q<VisualElement>("appWindow");
        setupOverlay = root.Q<VisualElement>("setupOverlay");
        btnCheckAgain = root.Q<Button>("btnCheckAgain");

        existingAvdDropdown = root.Q<DropdownField>("existingAvdDropdown");
        newVersionDropdown = root.Q<DropdownField>("newVersionDropdown");
        launchBtn = root.Q<Button>("launchBtn");
        createBtn = root.Q<Button>("createBtn");
        loadingDetails = root.Q<Label>("loadingDetails");
        statusSpinner = root.Q<VisualElement>("statusSpinner");

        splashContainer = root.Q<VisualElement>("splashContainer");
        splashLogo = root.Q<VisualElement>("splashLogo");
        splashSpinner = root.Q<VisualElement>("splashSpinner");

        launchBtn.clicked += OnLaunchClicked;
        createBtn.clicked += OnCreateClicked;
        
        
        btnCheckAgain.clicked += VerifyAndInitialize;

        root.Q<Button>("btnGithub")?.RegisterCallback<ClickEvent>(e => Application.OpenURL("https://github.com/Berkcan-1"));
        root.Q<Button>("btnLinkedin")?.RegisterCallback<ClickEvent>(e => Application.OpenURL("https://www.linkedin.com/in/aerolondev/"));

        newVersionDropdown.RegisterValueChangedCallback(evt => {
            if (evt.newValue.Contains("[Not Available Yet]")) createBtn.SetEnabled(false);
            else createBtn.SetEnabled(true);
        });

        if (appLogo != null) splashLogo.style.backgroundImage = new StyleBackground(appLogo);

        
        appWindow.style.display = DisplayStyle.None;

        StartCoroutine(SplashSequenceCoroutine());
    }

    void Update()
    {
        if (loadingDetails.text != volatileLog) loadingDetails.text = volatileLog;

        rotationAngle += Time.deltaTime * 360f; 
        if (splashSpinner != null && splashSpinner.resolvedStyle.opacity > 0)
        {
            splashSpinner.style.rotate = new StyleRotate(new Rotate(new Angle(rotationAngle, AngleUnit.Degree)));
        }
        if (isLoading && statusSpinner != null)
        {
            statusSpinner.style.rotate = new StyleRotate(new Rotate(new Angle(rotationAngle, AngleUnit.Degree)));
        }
    }

    IEnumerator SplashSequenceCoroutine()
    {
        splashSpinner.style.opacity = 0f;
        yield return new WaitForSeconds(1.0f);
        splashSpinner.style.opacity = 1f;
        
        
        FindSdkPaths();
        
        yield return new WaitForSeconds(2.0f);

        
        splashContainer.style.opacity = 0f;
        yield return new WaitForSeconds(0.8f); 
        splashContainer.style.display = DisplayStyle.None; 

        
        VerifyAndInitialize();
    }

    
    void VerifyAndInitialize()
    {
        FindSdkPaths();

        bool hasSdkFolder = Directory.Exists(androidSdkPath);
        bool hasEmulator = File.Exists(emulatorPath);
        bool hasCmdlineTools = GetDynamicCmdlineToolsPath() != null;

        if (hasSdkFolder && hasEmulator && hasCmdlineTools)
        {
            
            setupOverlay.style.display = DisplayStyle.None;
            appWindow.style.display = DisplayStyle.Flex;

            PopulateExistingAvds();
            PopulateNewVersions();
        }
        else
        {
            
            appWindow.style.display = DisplayStyle.None;
            setupOverlay.style.display = DisplayStyle.Flex;
        }
    }

    void FindSdkPaths()
    {
        string defaultSdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk");
        if (Directory.Exists(defaultSdk))
        {
            androidSdkPath = defaultSdk;
            emulatorPath = Path.Combine(androidSdkPath, @"emulator\emulator.exe");
        }
    }

    string GetDynamicCmdlineToolsPath()
    {
        if (string.IsNullOrEmpty(androidSdkPath)) return null;
        
        string basePath = Path.Combine(androidSdkPath, "cmdline-tools");
        if (!Directory.Exists(basePath)) return null;
        
        foreach (string dir in Directory.GetDirectories(basePath))
        {
            string binPath = Path.Combine(dir, "bin");
            if (Directory.Exists(binPath) && File.Exists(Path.Combine(binPath, "sdkmanager.bat"))) return binPath;
        }
        return null;
    }

    void PopulateExistingAvds()
    {
        if (!File.Exists(emulatorPath)) return;

        ProcessStartInfo listInfo = new ProcessStartInfo { FileName = emulatorPath, Arguments = "-list-avds", CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };

        using (Process process = Process.Start(listInfo))
        {
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            List<string> avds = new List<string>(output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            List<string> filteredAvds = new List<string>();

            foreach (string avd in avds)
            {
                if (avd.Trim().StartsWith("Auto_Android")) filteredAvds.Add(avd.Trim());
            }
            
            if (filteredAvds.Count > 0)
            {
                existingAvdDropdown.choices = filteredAvds;
                existingAvdDropdown.index = 0;
                launchBtn.SetEnabled(true);
            }
            else
            {
                existingAvdDropdown.choices = new List<string> { "No dedicated devices found" };
                launchBtn.SetEnabled(false);
            }
        }
    }

    void PopulateNewVersions()
    {
        List<string> dropdownOptions = new List<string>();
        foreach (var ver in androidVersions)
        {
            string imageType = ver.api <= 31 ? "default" : "google_apis";
            string expectedPath = Path.Combine(androidSdkPath, $"system-images\\android-{ver.api}\\{imageType}\\x86_64");
            string label = $"{ver.name} (API {ver.api})";
            
            if (ver.api == 37) label += " [Not Available Yet]";
            else if (Directory.Exists(expectedPath)) label += " [✓ Installed]"; 
            
            dropdownOptions.Add(label);
        }
        newVersionDropdown.choices = dropdownOptions;
        newVersionDropdown.index = 0; 

        if (dropdownOptions.Count > 0 && dropdownOptions[0].Contains("[Not Available Yet]"))
            createBtn.SetEnabled(false);
    }

    void OnLaunchClicked()
    {
        if (existingAvdDropdown.value != "No dedicated devices found" && existingAvdDropdown.value != "SDK Not Found!")
        {
            PlayerPrefs.SetString("SelectedAVDName", existingAvdDropdown.value);
            PlayerPrefs.Save();
            SceneManager.LoadScene("Mainmulator"); 
        }
    }

    void OnCreateClicked()
    {
        isLoading = true;
        statusSpinner.style.display = DisplayStyle.Flex;
        createBtn.SetEnabled(false);
        launchBtn.SetEnabled(false);
        StartCoroutine(CreateAndDownloadAvdCoroutine());
    }

    IEnumerator CreateAndDownloadAvdCoroutine()
    {
        string selectedVersion = newVersionDropdown.value;
        int targetApi = 31; 
        foreach (var ver in androidVersions) { if (selectedVersion.Contains($"API {ver.api}")) { targetApi = ver.api; break; } }

        string imageType = targetApi <= 31 ? "default" : "google_apis";
        string aospImage = $"system-images;android-{targetApi};{imageType};x86_64";
        string avdName = $"Auto_Android{targetApi}_AOSP";
        
        string cmdlineToolsPath = GetDynamicCmdlineToolsPath();
        if (cmdlineToolsPath == null)
        {
            volatileLog = "[Error] cmdline-tools not found! Install via Android Studio.";
            ResetUI();
            yield break;
        }

        string sdkManagerPath = Path.Combine(cmdlineToolsPath, "sdkmanager.bat");
        string avdManagerPath = Path.Combine(cmdlineToolsPath, "avdmanager.bat");
        string batPath = Path.Combine(Application.temporaryCachePath, "create_emulator.bat");

        string[] batLines = new string[]
        {
            "@echo off",
            "echo [PROCESS] Accepting SDK licenses...",
            $"echo y | call \"{sdkManagerPath}\" --sdk_root=\"{androidSdkPath}\" --licenses",
            
            $"echo [PROCESS] Downloading image for API {targetApi} (This might take a while)...",
            $"echo y | call \"{sdkManagerPath}\" --sdk_root=\"{androidSdkPath}\" \"{aospImage}\"",
            
            $"echo [PROCESS] Creating Virtual Machine ({avdName})...",
            $"echo no | call \"{avdManagerPath}\" create avd -n {avdName} -k \"{aospImage}\" --device \"pixel\" --force",
            "echo [PROCESS] Done!"
        };
        File.WriteAllLines(batPath, batLines);

        ProcessStartInfo createInfo = new ProcessStartInfo { FileName = batPath, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        createInfo.EnvironmentVariables["ANDROID_SDK_ROOT"] = androidSdkPath;
        createInfo.EnvironmentVariables["ANDROID_HOME"] = androidSdkPath;

        string javaHomePath = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (string.IsNullOrEmpty(javaHomePath))
        {
            string[] possibleJavaPaths = { @"C:\Program Files\Android\Android Studio\jbr", @"C:\Program Files\Android\Android Studio\jre" };
            foreach (string jPath in possibleJavaPaths) { if (Directory.Exists(jPath)) { javaHomePath = jPath; break; } }
        }
        if (!string.IsNullOrEmpty(javaHomePath)) createInfo.EnvironmentVariables["JAVA_HOME"] = javaHomePath;

        Process createProcess = new Process { StartInfo = createInfo };
        
        createProcess.OutputDataReceived += (sender, args) => { 
            if (!string.IsNullOrWhiteSpace(args.Data)) 
            {
                string line = args.Data.Trim();
                if (line.StartsWith("[PROCESS]")) volatileLog = line.Replace("[PROCESS]", "").Trim();
                else if (line.Contains("%")) volatileLog = "Downloading: " + line; 
            }
        };
        
        createProcess.ErrorDataReceived += (sender, args) => { 
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                string line = args.Data.Trim().ToLowerInvariant();
                if (!line.Contains("warning")) 
                {
                    if (line.Contains("%")) volatileLog = "Downloading: " + args.Data.Trim();
                    else volatileLog = args.Data.Trim();
                }
            }
        };

        createProcess.Start();
        createProcess.BeginOutputReadLine();
        createProcess.BeginErrorReadLine();
        
        while (!createProcess.HasExited) yield return null; 
        if (File.Exists(batPath)) File.Delete(batPath);

        volatileLog = "Process completed successfully! Updating device list...";
        yield return new WaitForSeconds(1.5f);

        PopulateExistingAvds();
        PopulateNewVersions(); 
        existingAvdDropdown.value = avdName;
        
        ResetUI();
        volatileLog = "System Ready. Waiting for user input...";
    }

    void ResetUI()
    {
        isLoading = false;
        statusSpinner.style.display = DisplayStyle.None;
        createBtn.SetEnabled(true);
        if (existingAvdDropdown.choices.Count > 0 && existingAvdDropdown.choices[0] != "No dedicated devices found") launchBtn.SetEnabled(true);
    }
}
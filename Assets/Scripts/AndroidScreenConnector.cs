using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

public class AndroidScreenConnector : MonoBehaviour
{
    [Header("Android SDK & Emülatör Ayarları")]
    public bool autoStartEmulator = true;
    public string avdName = "Auto_Android12_AOSP";
    public string androidSdkPath = ""; 
    
    [Header("Malzeme (Material)")]
    public Material targetMaterial;

    [Header("Boot Ekranı Resimleri (YENİ)")]
    public Texture2D bootLogoLocked;
    public Texture2D bootLogoUnlocked;

    [Header("Cihaz Çözünürlüğü")]
    public int androidWidth = 1080;
    public int androidHeight = 2340;

    [Header("Yakalama Boyutu")]
    public int captureWidth = 540;
    public int captureHeight = 1170;

    [Header("Scrcpy Ayarları")]
    public int scrcpyMaxFps = 60;
    public string scrcpyBitRate = "8M";
    public string deviceSerial = "";
    public bool useBitBltFallback = false;
    public string emulatorGpuMode = "host";

    [Header("Stabilizasyon")]
    public bool forceSoftwareRenderer = true;
    public bool preventExclusiveFullscreen = true;
    public bool useHighResolutionTimer = true;
    public int captureThreadPriorityBoost = 1;

    private const string WINDOW_TITLE = "UnityMirrorWindow";
    private string AOSP_IMAGE = "system-images;android-31;default;x86_64";
    
    private Process emulatorProcess;
    private bool isIntentionalShutdown = false;

    private Thread captureThread;
    private Texture2D screenTexture;
    private byte[] latestFrameBytes;
    private byte[] workBuffer;
    private bool isFrameReady = false;
    private bool isReconnecting = false;
    private bool isRunning = true;
    
    private string adbPath;
    private string scrcpyPath;
    private string emulatorPath;
    
    private Camera mainCamera;
    private Process scrcpyProcess;
    private IntPtr targetHwnd = IntPtr.Zero;

    private bool isDragging = false;
    private Vector2 dragStartAndroidPos;
    private float dragStartTime;

    private Process adbShellProcess;
    private StreamWriter adbShellWriter;
    private readonly object adbLock = new object(); 

    #region Win32 Interop
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd); 
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, [Out] byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uMilliseconds);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uMilliseconds);

    private const uint SRCCOPY = 0x00CC0020;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    private const uint DIB_RGB_COLORS = 0;
    private const uint CAPTUREBLT = 0x40000000;
    private const uint BI_RGB = 0;
    
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_BORDER = 0x00800000;
    private const int WS_SIZEBOX = 0x00040000;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint LWA_ALPHA = 0x00000002;
    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly object frameLock = new object();
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount; public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter; public uint biClrUsed; public uint biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }
    
    private IntPtr gdiHdcScreen = IntPtr.Zero;
    private IntPtr gdiHdcMem = IntPtr.Zero;
    private IntPtr gdiHBitmap = IntPtr.Zero;
    private IntPtr gdiHOldBitmap = IntPtr.Zero;
    private bool gdiReady = false;
    #endregion

    void Awake()
    {
        CleanZombieProcesses();
        Application.runInBackground = true;

        // BUILD'DE KASMA FIX 1: Sadece "Exclusive Fullscreen" modunu engelliyoruz (DWM compositing'i
        // kapattığı için PrintWindow yakalamasını bozar). Windowed veya FullScreenWindow modunu
        // olduğu gibi bırakıyoruz — kullanıcının bilinçli seçimini asla ezmiyoruz.
        if (preventExclusiveFullscreen && Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen)
        {
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
        }

        // BUILD'DE KASMA FIX 2: VSync'i Unity'nin kendi ayarına bırakmayıp elle kapatıp
        // hedef FPS'i scrcpy'nin FPS'i ile hizalıyoruz; aksi halde Update() thread'i
        // beklenmedik şekilde bloklanıp yakalanan kareleri geç uyguluyor.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Mathf.Max(60, scrcpyMaxFps);

        // BUILD'DE KASMA FIX 3: Windows'un varsayılan sistem zamanlayıcı çözünürlüğü ~15.6ms'dir.
        // Editor process'i genelde bunu zaten yükseltilmiş bulundurur (başka araçlar sebebiyle),
        // ama derlenmiş bir Player bunu yapmaz. Bu da capture thread'deki Thread.Sleep(5) gibi
        // küçük bekletmelerin build'de 15ms'ye kadar uzamasına, yani yakalama FPS'inin
        // yarı yarıya düşmesine sebep olur. 1ms çözünürlük isteyerek bunu düzeltiyoruz.
        if (useHighResolutionTimer)
        {
            try { timeBeginPeriod(1); } catch { }
        }

        avdName = PlayerPrefs.GetString("SelectedAVDName", "Auto_Android12_AOSP");
        AOSP_IMAGE = PlayerPrefs.GetString("SelectedAOSPImage", "system-images;android-31;default;x86_64");

        string defaultSdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk");
        if (Directory.Exists(defaultSdk)) androidSdkPath = defaultSdk;
    }

    void CleanZombieProcesses()
    {
        try { foreach (var p in Process.GetProcessesByName("qemu-system-x86_64")) { try { p.Kill(); } catch { } } foreach (var p in Process.GetProcessesByName("scrcpy")) { try { p.Kill(); } catch { } } } catch { }
    }

    void OnEnable() { if (Keyboard.current != null) Keyboard.current.onTextInput += OnTextInput; }
    void OnDisable() { if (Keyboard.current != null) Keyboard.current.onTextInput -= OnTextInput; }

    void Start()
    {
        mainCamera = Camera.main;
        string scrcpyFolder = Path.Combine(Application.streamingAssetsPath, "scrcpy");
        adbPath = Path.Combine(scrcpyFolder, "adb.exe");
        scrcpyPath = Path.Combine(scrcpyFolder, "scrcpy.exe");
        emulatorPath = Path.Combine(androidSdkPath, @"emulator\emulator.exe");

        if (autoStartEmulator) PowerOnEmulator();
    }

    public bool IsEmulatorOn() { return emulatorProcess != null && !emulatorProcess.HasExited; }

   public void PowerOnEmulator()
    {
        if (IsEmulatorOn()) return; 

        
        DeviceTreeManager dt = FindObjectOfType<DeviceTreeManager>();
        if (dt != null && !dt.CanDeviceBoot())
        {
            UnityEngine.Debug.LogWarning("[DONANIM REDDİ] Kritik parçalar eksik. Sistem başlatılamıyor!");
            return; 
        }

        isIntentionalShutdown = false;

        int isUnlocked = PlayerPrefs.GetInt("Unlocked_" + avdName, 0);
        Texture2D selectedLogo = (isUnlocked == 1 && bootLogoUnlocked != null) ? bootLogoUnlocked : bootLogoLocked;
        
        if (targetMaterial != null && selectedLogo != null)
        {
            targetMaterial.mainTexture = selectedLogo;
        }

        StartCoroutine(BootRoutine());
    }

    public void PowerOffEmulator()
    {
        if (!IsEmulatorOn()) return;
        isIntentionalShutdown = true;
        try { if (adbShellWriter != null) adbShellWriter.Close(); } catch { }
        try { if (adbShellProcess != null && !adbShellProcess.HasExited) adbShellProcess.Kill(); } catch { }
        SendAdbCommandAsync("reboot -p"); 
        isFrameReady = false;
        
        
        Texture2D blackTex = new Texture2D(2, 2);
        for (int i=0; i<4; i++) blackTex.SetPixel(i%2, i/2, Color.black);
        blackTex.Apply();
        if (targetMaterial != null) targetMaterial.mainTexture = blackTex;
    }

    IEnumerator BootRoutine()
    {
        StartAndroidEmulator();
        yield return new WaitForSeconds(8f);
        int retryCount = 0;
        while (!FetchDeviceResolution() && retryCount < 30) { yield return new WaitForSeconds(3f); retryCount++; }
        if (!FetchDeviceResolution()) yield break; 

        float aspectRatio = (float)androidWidth / (float)androidHeight;
        captureWidth = 540; 
        captureHeight = Mathf.RoundToInt(captureWidth / aspectRatio);
        screenTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.BGRA32, false);
        workBuffer = new byte[captureWidth * captureHeight * 4];
        
        
        
        isRunning = true;
        StartScrcpy();
        StartPersistentAdbShell();

        if (captureThread == null || !captureThread.IsAlive)
        {
            captureThread = new Thread(WindowCaptureLoop) { IsBackground = true };
            // BUILD'DE KASMA FIX 4: Editor'de bu thread genelde yeterince CPU zamanı bulur,
            // ama build'de ana render thread tüm çekirdekleri daha agresif kullanabilir ve
            // varsayılan (Normal) öncelikli capture thread'i aç bırakabilir. Önceliği artırıyoruz.
            try
            {
                captureThread.Priority = captureThreadPriorityBoost >= 2 ? System.Threading.ThreadPriority.Highest
                    : captureThreadPriorityBoost == 1 ? System.Threading.ThreadPriority.AboveNormal
                    : System.Threading.ThreadPriority.Normal;
            }
            catch { }
            captureThread.Start();
        }
    }

    void StartAndroidEmulator()
    {
        
        int isUnlocked = PlayerPrefs.GetInt("Unlocked_" + avdName, 0);
        string writableArg = isUnlocked == 1 ? "-writable-system " : "";

        ProcessStartInfo startInfo = new ProcessStartInfo {
            FileName = emulatorPath,
            Arguments = $"-avd {avdName} {writableArg}-no-window -no-snapshot-save -gpu {emulatorGpuMode} -accel on -no-boot-anim",
            CreateNoWindow = true, UseShellExecute = false
        };
        startInfo.EnvironmentVariables["ANDROID_SDK_ROOT"] = androidSdkPath; startInfo.EnvironmentVariables["ANDROID_HOME"] = androidSdkPath;
        try { emulatorProcess = new Process(); emulatorProcess.StartInfo = startInfo; emulatorProcess.Start(); } catch { }
    }

    void Update()
    {
        if (scrcpyProcess != null && scrcpyProcess.HasExited)
        {
            scrcpyProcess = null;
            isFrameReady = false;
            
            Texture2D blackTex = new Texture2D(2, 2);
            for (int i = 0; i < 4; i++) blackTex.SetPixel(i % 2, i / 2, Color.black);
            blackTex.Apply();
            if (targetMaterial != null) targetMaterial.mainTexture = blackTex;

            if (IsEmulatorOn() && !isIntentionalShutdown && !isReconnecting)
            {
                isReconnecting = true; 
                UnityEngine.Debug.Log("[EKRAN] Bağlantı koptu, güvenli yeniden bağlanma başlatılıyor...");
                StartCoroutine(ReconnectScrcpyRoutine());
            }
        }

        if (isFrameReady)
        {
            lock (frameLock)
            {
                try
                {
                    if (latestFrameBytes != null && screenTexture != null)
                    {
                        screenTexture.LoadRawTextureData(latestFrameBytes);
                        screenTexture.Apply(false);

                        
                        if (targetMaterial != null && targetMaterial.mainTexture != screenTexture)
                        {
                            targetMaterial.mainTexture = screenTexture;
#if UNITY_EDITOR
                            // Build'de Debug.Log diske senkron yazdığı için bu satır Editor'e özel bırakıldı.
                            UnityEngine.Debug.Log("[EKRAN] Görüntü cihaza başarıyla yansıtıldı!");
#endif
                        }
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"[EKRAN] Kare yüklenemedi, atlanıyor: {e.Message}");
                }
                isFrameReady = false;
            }
        }
        HandleMouseInput(); 
        HandleKeyboardInput();
    }

    IEnumerator ReconnectScrcpyRoutine()
    {
        try { if (adbShellWriter != null) adbShellWriter.Close(); } catch { }
        try { if (adbShellProcess != null && !adbShellProcess.HasExited) adbShellProcess.Kill(); } catch { }
        try { foreach (var p in Process.GetProcessesByName("scrcpy")) { try { p.Kill(); } catch { } } } catch { }

        yield return new WaitForSeconds(5f); 
        
        int retryCount = 0;
        while (!FetchDeviceResolution() && retryCount < 30)
        {
            yield return new WaitForSeconds(3f);
            retryCount++;
        }

        if (FetchDeviceResolution() && isRunning && !isIntentionalShutdown)
        {
            StartScrcpy();
            StartPersistentAdbShell();
            
            SendAdbShellCommand("input keyevent 224"); 
            SendAdbShellCommand("input keyevent 82");  

            UnityEngine.Debug.Log("[EKRAN] Yeniden bağlantı başarılı!");
        }
        else
        {
            UnityEngine.Debug.LogError("[EKRAN] Yeniden bağlanma zaman aşımına uğradı veya cihaz kapandı.");
        }
        isReconnecting = false;
    }

    void StartPersistentAdbShell()
    {
        try {
            string args = string.IsNullOrEmpty(deviceSerial) ? "shell" : $"-s {deviceSerial} shell";
            ProcessStartInfo startInfo = new ProcessStartInfo { FileName = adbPath, Arguments = args, CreateNoWindow = true, UseShellExecute = false, RedirectStandardInput = true };
            adbShellProcess = Process.Start(startInfo); adbShellWriter = adbShellProcess.StandardInput;
        } catch { }
    }

    public void SendAdbShellCommand(string command)
    {
        if ((adbShellProcess == null || adbShellProcess.HasExited) && isRunning && !isIntentionalShutdown)
        {
            StartPersistentAdbShell();
        }

        if (adbShellWriter != null && adbShellProcess != null && !adbShellProcess.HasExited)
        {
            ThreadPool.QueueUserWorkItem(_ => 
            {
                try 
                { 
                    lock(adbLock)
                    {
                        adbShellWriter.WriteLine(command); 
                        adbShellWriter.Flush(); 
                    }
                } 
                catch { }
            });
        }
    }

    bool FetchDeviceResolution()
    {
        ProcessStartInfo startInfo = new ProcessStartInfo { FileName = adbPath, Arguments = string.IsNullOrEmpty(deviceSerial) ? "shell wm size" : $"-s {deviceSerial} shell wm size", CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        try { using (Process process = Process.Start(startInfo)) { string output = process.StandardOutput.ReadToEnd(); process.WaitForExit(); if (output.Contains("Physical size:")) { string[] parts = output.Replace("Physical size:", "").Trim().Split('x'); if (parts.Length == 2) { androidWidth = int.Parse(parts[0]); androidHeight = int.Parse(parts[1]); return true; } } } } catch { }
        return false;
    }

    void StartScrcpy()
    {
        int maxSize = Mathf.Max(captureWidth, captureHeight);
        string serialArg = string.IsNullOrEmpty(deviceSerial) ? "" : $"--serial={deviceSerial} ";
        string renderDriverArg = forceSoftwareRenderer ? "--render-driver=software " : "";
        string args = serialArg + renderDriverArg + $"--window-title=\"{WINDOW_TITLE}\" --window-x=-3200 --window-y=-3200 --window-width={captureWidth} --window-height={captureHeight} --window-borderless --max-size={maxSize} --max-fps={scrcpyMaxFps} --video-bit-rate={scrcpyBitRate} --no-audio --disable-screensaver --stay-awake";
        ProcessStartInfo startInfo = new ProcessStartInfo { FileName = scrcpyPath, Arguments = args, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(scrcpyPath) };
        startInfo.EnvironmentVariables["ADB"] = adbPath;
        try { scrcpyProcess = Process.Start(startInfo); } catch { }
    }

    bool AcquireWindowHandle()
    {
        targetHwnd = FindWindow(null, WINDOW_TITLE);
        if (targetHwnd != IntPtr.Zero)
        {
            int style = GetWindowLong(targetHwnd, GWL_STYLE);
            SetWindowLong(targetHwnd, GWL_STYLE, style & ~WS_CAPTION & ~WS_BORDER & ~WS_SIZEBOX);

            int exStyle = GetWindowLong(targetHwnd, GWL_EXSTYLE);
            SetWindowLong(targetHwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
            SetLayeredWindowAttributes(targetHwnd, 0, 1, LWA_ALPHA); 
           SetWindowPos(targetHwnd, HWND_BOTTOM, -3200, -3200, captureWidth, captureHeight, SWP_NOACTIVATE);
            return true;
        }
        return false;
    }

    void InitGdiResources()
    {
        gdiHdcScreen = GetDC(IntPtr.Zero); gdiHdcMem = CreateCompatibleDC(gdiHdcScreen); gdiHBitmap = CreateCompatibleBitmap(gdiHdcScreen, captureWidth, captureHeight); gdiHOldBitmap = SelectObject(gdiHdcMem, gdiHBitmap); gdiReady = true;
    }

    void ReleaseGdiResources()
    {
        if (!gdiReady) return; SelectObject(gdiHdcMem, gdiHOldBitmap); DeleteObject(gdiHBitmap); DeleteDC(gdiHdcMem); ReleaseDC(IntPtr.Zero, gdiHdcScreen); gdiReady = false;
    }

    void WindowCaptureLoop()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (isRunning)
        {
            if (targetHwnd == IntPtr.Zero || !IsWindow(targetHwnd))
            {
                ReleaseGdiResources();
                targetHwnd = IntPtr.Zero;
                while (isRunning && !AcquireWindowHandle()) { Thread.Sleep(500); }
                if (!isRunning) break;
                InitGdiResources();
            }

            long frameStartMs = stopwatch.ElapsedMilliseconds;
            try { CaptureWindowFrame(); } catch { }
            long elapsedMs = stopwatch.ElapsedMilliseconds - frameStartMs;
            
            int remainingMs = (int)((1000.0 / Mathf.Max(1, scrcpyMaxFps)) - elapsedMs);
            Thread.Sleep(Mathf.Max(5, remainingMs)); 
        }
        ReleaseGdiResources();
    }

    bool CaptureWindowFrame()
    {
        if (!gdiReady) return false;
        bool success;
        
        if (useBitBltFallback) 
        { 
            IntPtr hdcWindow = GetDC(targetHwnd);
            success = BitBlt(gdiHdcMem, 0, 0, captureWidth, captureHeight, hdcWindow, 0, 0, SRCCOPY | CAPTUREBLT); 
            ReleaseDC(targetHwnd, hdcWindow);
        }
        else 
        { 
            success = PrintWindow(targetHwnd, gdiHdcMem, PW_RENDERFULLCONTENT); 
        }

        if (success)
        {
            BITMAPINFO bmi = new BITMAPINFO(); 
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER)); 
            bmi.bmiHeader.biWidth = captureWidth; 
            bmi.bmiHeader.biHeight = captureHeight; 
            bmi.bmiHeader.biPlanes = 1; 
            bmi.bmiHeader.biBitCount = 32; 
            bmi.bmiHeader.biCompression = BI_RGB;
            
            int copiedLines = GetDIBits(gdiHdcMem, gdiHBitmap, 0, (uint)captureHeight, workBuffer, ref bmi, DIB_RGB_COLORS);
            if (copiedLines > 0)
            {
                lock (frameLock) 
                { 
                    if (latestFrameBytes == null || latestFrameBytes.Length != workBuffer.Length) 
                        latestFrameBytes = new byte[workBuffer.Length]; 
                    
                    Buffer.BlockCopy(workBuffer, 0, latestFrameBytes, 0, workBuffer.Length); 
                    isFrameReady = true; 
                }
            }
            else
            {
                success = false;
            }
        }
        return success;
    }

    void HandleMouseInput()
    {
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame) 
        { 
            Vector2 androidPos = GetAndroidPixelPosition(); 
            if (androidPos.x >= 0f) 
            { 
                isDragging = true; 
                dragStartAndroidPos = androidPos; 
                
                
                
                dragStartTime = Time.realtimeSinceStartup; 
            } 
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame && isDragging)
        {
            isDragging = false; 
            Vector2 dragEndAndroidPos = GetAndroidPixelPosition();
            
            if (dragEndAndroidPos.x >= 0f) 
            {
                float dragDurationMs = (Time.realtimeSinceStartup - dragStartTime) * 1000f; 
                float distance = Vector2.Distance(dragStartAndroidPos, dragEndAndroidPos);
                
                
                if (distance > 10f) 
                { 
                    
                    int duration = Mathf.Clamp(Mathf.RoundToInt(dragDurationMs), 50, 400); 
                    SendAdbShellCommand($"input swipe {Mathf.RoundToInt(dragStartAndroidPos.x)} {Mathf.RoundToInt(dragStartAndroidPos.y)} {Mathf.RoundToInt(dragEndAndroidPos.x)} {Mathf.RoundToInt(dragEndAndroidPos.y)} {duration}"); 
                }
                else 
                {
                    
                    if (dragDurationMs > 400f) 
                    {
                        int longPressDuration = Mathf.Clamp(Mathf.RoundToInt(dragDurationMs), 401, 2000);
                        SendAdbShellCommand($"input swipe {Mathf.RoundToInt(dragStartAndroidPos.x)} {Mathf.RoundToInt(dragStartAndroidPos.y)} {Mathf.RoundToInt(dragStartAndroidPos.x)} {Mathf.RoundToInt(dragStartAndroidPos.y)} {longPressDuration}");
                    }
                    else 
                    { 
                        
                        SendAdbShellCommand($"input tap {Mathf.RoundToInt(dragStartAndroidPos.x)} {Mathf.RoundToInt(dragStartAndroidPos.y)}"); 
                    }
                }
            }
        }
    }

    private void OnTextInput(char ch) { if (ch == '\b' || ch == '\n' || ch == '\r' || ch == '\u001b') return; if (ch == ' ') SendAdbShellCommand("input keyevent 62"); else SendAdbShellCommand($"input text \"{ch}\""); }
    void HandleKeyboardInput() { if (Keyboard.current == null) return; if (Keyboard.current.backspaceKey.wasPressedThisFrame) SendAdbShellCommand("input keyevent 67"); if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame) SendAdbShellCommand("input keyevent 66"); if (Keyboard.current.escapeKey.wasPressedThisFrame) SendAdbShellCommand("input keyevent 4"); if (Keyboard.current.upArrowKey.wasPressedThisFrame) SendAdbShellCommand("input keyevent 19"); if (Keyboard.current.downArrowKey.wasPressedThisFrame) SendAdbShellCommand("input keyevent 20"); if (Keyboard.current.leftArrowKey.wasPressedThisFrame) SendAdbShellCommand("input keyevent 21"); if (Keyboard.current.rightArrowKey.wasPressedThisFrame) SendAdbShellCommand("input keyevent 22"); }

    Vector2 GetAndroidPixelPosition()
    {
        if (mainCamera == null) mainCamera = Camera.main; if (mainCamera == null) return Vector2.one * -1;
        Vector2 mousePos = Mouse.current.position.ReadValue(); Ray ray = mainCamera.ScreenPointToRay(mousePos); Plane quadPlane = new Plane(transform.forward, transform.position); float enter;
        if (!quadPlane.Raycast(ray, out enter)) return Vector2.one * -1;
        Vector3 hitPoint = ray.GetPoint(enter); Vector3 localHitPoint = transform.InverseTransformPoint(hitPoint);
        float rawUV_X = localHitPoint.x + 0.5f; float rawUV_Y = localHitPoint.y + 0.5f;
        if (rawUV_X < 0f || rawUV_X > 1f || rawUV_Y < 0f || rawUV_Y > 1f) return Vector2.one * -1;
        int androidX = Mathf.RoundToInt(rawUV_X * androidWidth); int androidY = Mathf.RoundToInt((1.0f - rawUV_Y) * androidHeight); return new Vector2(androidX, androidY);
    }

    void SendAdbCommandAsync(string arguments) { if (!string.IsNullOrEmpty(deviceSerial)) arguments = $"-s {deviceSerial} {arguments}"; ThreadPool.QueueUserWorkItem(_ => { try { ProcessStartInfo startInfo = new ProcessStartInfo { FileName = adbPath, Arguments = arguments, CreateNoWindow = true, UseShellExecute = false }; using (Process process = Process.Start(startInfo)) { process.WaitForExit(); } } catch { } }); }

    void OnApplicationQuit()
    {
        isRunning = false;
        try { if (adbShellWriter != null) { adbShellWriter.Close(); } } catch { }
        try { if (adbShellProcess != null && !adbShellProcess.HasExited) { adbShellProcess.Kill(); } } catch { }
        if (captureThread != null && captureThread.IsAlive) captureThread.Join(500);
        try { ReleaseGdiResources(); } catch { }
        try { if (scrcpyProcess != null && !scrcpyProcess.HasExited) scrcpyProcess.Kill(); } catch { }
        if (useHighResolutionTimer) { try { timeEndPeriod(1); } catch { } }
        SendAdbCommandAsync("emu kill"); 
    }
}
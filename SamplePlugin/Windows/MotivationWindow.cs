using JumpKhaunter67;
using System;
using System.IO;
using System.Reflection;
using System.Numerics;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Bindings.ImGui;
using System.Threading.Tasks;

namespace JumpKhaunter67.Windows;

public class MotivationWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private float displayTimer = 0f;
    private ISharedImmediateTexture? currentMemeTexture;
    private readonly Random random;
    private readonly string pluginDirectory;

    // GIF animation support
    private List<ISharedImmediateTexture> frameTextures = new();
    private List<int> frameDelaysMs = new();
    private List<string> frameTempFiles = new();
    private float animationElapsed = 0f;
    private int currentFrameIndex = 0;
    private readonly object textureLock = new();
    private Dalamud.Bindings.ImGui.ImTextureID lastTextureHandle = default;
    private bool lastTextureValid = false;

    // Windows MCI audio playback (winmm.dll, built-in, no external dependencies)
    private const string MciAlias = "jk67_audio";
    private bool audioPlaying = false;

    [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int mciSendString(string command, StringBuilder? returnString, int returnLength, IntPtr hwndCallback);

    // Debug report guard to avoid chat spam
    private bool debugReported = false;

    // Simple file logger for diagnostics (disabled)
    private void WriteLog(string msg) { }

    public MotivationWindow(Plugin plugin) : base(
        "##JumpKhaunter67_Secret_Overlay",
        ImGuiWindowFlags.NoDecoration | 
        ImGuiWindowFlags.NoBackground | 
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoMouseInputs |
        ImGuiWindowFlags.NoNavFocus |
        ImGuiWindowFlags.NoSavedSettings)
    {
        this.plugin = plugin;
        this.IsOpen = false; 
        this.random = new Random();
        this.pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
    }

    public void TriggerMilestone(int milestone)
    {
        this.displayTimer = 5.0f;
        string finalAssetPath = string.Empty;

        try
        {
            // Search upward from the assembly directory for an "images" folder (checks up to 5 levels)
            var dir = this.pluginDirectory;
            int depth = 0;
            while (!string.IsNullOrEmpty(dir) && depth < 6)
            {
                var candidate = Path.Combine(dir, "images");
                if (Directory.Exists(candidate))
                {
                    var allFiles = Directory.GetFiles(candidate);
                    var gifFiles = Array.FindAll(allFiles, f => string.Equals(Path.GetExtension(f), ".gif", StringComparison.OrdinalIgnoreCase));
                    var imgFiles = Array.FindAll(allFiles, f =>
                        string.Equals(Path.GetExtension(f), ".png", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetExtension(f), ".jpg", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetExtension(f), ".jpeg", StringComparison.OrdinalIgnoreCase));

                    if (gifFiles.Length > 0) finalAssetPath = gifFiles[this.random.Next(gifFiles.Length)];
                    else if (imgFiles.Length > 0) finalAssetPath = imgFiles[this.random.Next(imgFiles.Length)];

                    if (!string.IsNullOrEmpty(finalAssetPath))
                    {
                        break;
                    }
                }

                var parent = Directory.GetParent(dir);
                dir = parent?.FullName ?? string.Empty;
                depth++;
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }

        if (string.IsNullOrEmpty(finalAssetPath))
        {
            // Try to extract an embedded image/gif resource from the assembly first
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var resources = asm.GetManifestResourceNames();
                var candidates = Array.FindAll(resources, r => r.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || r.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || r.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || r.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
                if (candidates.Length > 0)
                {
                    var pick = candidates[this.random.Next(candidates.Length)];
                    var ext = Path.GetExtension(pick);
                    var tempImg = Path.Combine(Path.GetTempPath(), $"jk67_emb_{Guid.NewGuid()}{ext}");
                    using (var rs = asm.GetManifestResourceStream(pick))
                    {
                        if (rs != null) { using var fs = File.OpenWrite(tempImg); rs.CopyTo(fs); finalAssetPath = tempImg; frameTempFiles.Add(tempImg); }
                    }
                }
            }
            catch { }


            if (string.IsNullOrEmpty(finalAssetPath))
                finalAssetPath = Path.Combine(this.pluginDirectory, "images", "Shrek.gif.gif");
        }

        try
        {
            // Null previous texture reference
            this.currentMemeTexture = null;


            System.Diagnostics.Trace.WriteLine($"JK67: finalAssetPath={finalAssetPath} exists={File.Exists(finalAssetPath)}");
            if (File.Exists(finalAssetPath))
            {
                if (string.Equals(Path.GetExtension(finalAssetPath), ".gif", StringComparison.OrdinalIgnoreCase))
                {
                    // Load first frame synchronously to avoid long freeze, decode rest in background
                    try
                    {
                        frameTextures.Clear();
                        frameDelaysMs.Clear();
                        animationElapsed = 0f;
                        currentFrameIndex = 0;

                        using (var img = Image.FromFile(finalAssetPath))
                        {
                            FrameDimension dims;
                            int frameCount = 1;
                            try
                            {
                                var fdList = img.FrameDimensionsList;
                                if (fdList != null && fdList.Length > 0)
                                {
                                    dims = new FrameDimension(fdList[0]);
                                    frameCount = img.GetFrameCount(dims);
                                }
                                else
                                {
                                    dims = new FrameDimension(Guid.Empty);
                                    frameCount = 1;
                                }
                            }
                            catch { dims = new FrameDimension(Guid.Empty); frameCount = 1; }

                            int[] delays = new int[frameCount];
                            System.Drawing.Imaging.PropertyItem? prop = null;
                            try { prop = img.GetPropertyItem(0x5100); } catch { prop = null; }
                            try
                            {
                                var raw = prop?.Value;
                                if (raw != null && raw.Length >= 4)
                                {
                                    delays = new int[raw.Length / 4];
                                    for (int i = 0; i < delays.Length; i++)
                                        delays[i] = BitConverter.ToInt32(raw, i * 4) * 10; // to ms
                                }
                                else
                                {
                                    for (int i = 0; i < frameCount; i++) delays[i] = 100;
                                }
                            }
                            catch { for (int i = 0; i < frameCount; i++) delays[i] = 100; }

                            // Extract and load first frame quickly
                            img.SelectActiveFrame(dims, 0);
                            using (var bmp = new Bitmap(img))
                            {
                                string temp0 = Path.Combine(Path.GetTempPath(), $"jk67_{Guid.NewGuid()}_0.png");
                                bmp.Save(temp0, ImageFormat.Png);
                                var tex0 = Plugin.TextureProvider.GetFromFile(temp0);
                                lock (frameTextures) { frameTextures.Add(tex0); frameDelaysMs.Add(delays.Length>0?delays[0]:100); frameTempFiles.Add(temp0); }
                                // keep temp0 until Dispose to ensure TextureProvider can finish loading; will delete later (frameTempFiles list stores it)
                            }

                            // Start background decoding for remaining frames
                            if (frameCount > 1)
                            {
                                _ = Task.Run(() =>
                                {
                                    try
                                    {
                                        for (int i = 1; i < frameCount; i++)
                                        {
                                            try
                                            {
                                                using var img2 = Image.FromFile(finalAssetPath);
                                                img2.SelectActiveFrame(dims, i);
                                                using var bmp2 = new Bitmap(img2);
                                                string temp = Path.Combine(Path.GetTempPath(), $"jk67_{Guid.NewGuid()}_{i}.png");
                                                bmp2.Save(temp, ImageFormat.Png);
                                                var tex = Plugin.TextureProvider.GetFromFile(temp);
                                                lock (frameTextures) { frameTextures.Add(tex); frameDelaysMs.Add(delays.Length>i?delays[i]:100); frameTempFiles.Add(temp); }
                                                // keep temp until Dispose to ensure TextureProvider can finish loading; will delete later (frameTempFiles list stores it)
                                            }
                                            catch (Exception fex) { System.Diagnostics.Trace.WriteLine(fex.Message); }
                                        }
                                    }
                                    catch (Exception ex2) { System.Diagnostics.Trace.WriteLine(ex2.Message); }
                                });
                            }
                        }
                    }
                    catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
                }
                else
                {
                    this.currentMemeTexture = Plugin.TextureProvider.GetFromFile(finalAssetPath);
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }

        // Milestone header image support disabled — prefer showing the selected meme GIF only.
        PlayRandomMilestoneAudio();
        this.IsOpen = true;
        // Reset debug flag for this display so we can report any display-time issues once
        this.debugReported = false;
    }

    private string LogPath => Path.Combine(Path.GetTempPath(), "jk67_audio.log");

    private void AudioLog(string msg)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); } catch { }
    }

    private void PlayRandomMilestoneAudio()
    {
        try
        {
            AudioLog("=== PlayRandomMilestoneAudio ===");
            string selectedAudioPath = string.Empty;

            var dir = this.pluginDirectory;
            AudioLog($"pluginDirectory={dir}");
            int depth = 0;
            while (!string.IsNullOrEmpty(dir) && depth < 6)
            {
                var candidate = Path.Combine(dir, "audio");
                AudioLog($"  checking dir={candidate} exists={Directory.Exists(candidate)}");
                if (Directory.Exists(candidate))
                {
                    var mp3Files = Directory.GetFiles(candidate, "*.mp3");
                    AudioLog($"  found {mp3Files.Length} mp3s");
                    if (mp3Files.Length > 0)
                    {
                        selectedAudioPath = mp3Files[this.random.Next(0, mp3Files.Length)];
                        break;
                    }
                }

                var parent = Directory.GetParent(dir);
                dir = parent?.FullName ?? string.Empty;
                depth++;
            }

            if (string.IsNullOrEmpty(selectedAudioPath))
            {
                AudioLog("no audio folder found, trying embedded resources");
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var res = asm.GetManifestResourceNames();
                    var mp3s = Array.FindAll(res, r => r.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));
                    AudioLog($"embedded mp3s: {mp3s.Length}");
                    if (mp3s.Length > 0)
                    {
                        var pick = mp3s[this.random.Next(mp3s.Length)];
                        var ext = Path.GetExtension(pick);
                        var tempMp3 = Path.Combine(Path.GetTempPath(), $"jk67_emb_{Guid.NewGuid()}{ext}");
                        using (var rs = asm.GetManifestResourceStream(pick))
                        {
                            if (rs != null) { using var fs = File.OpenWrite(tempMp3); rs.CopyTo(fs); selectedAudioPath = tempMp3; }
                        }
                        AudioLog($"extracted embedded mp3 to {selectedAudioPath}");
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(selectedAudioPath)) { AudioLog("No audio found at all"); return; }
            }

            this.audioPlaying = false;
            StopMciAudio();

            AudioLog($"selectedAudioPath={selectedAudioPath} exists={File.Exists(selectedAudioPath)}");
            if (!File.Exists(selectedAudioPath)) { AudioLog("FILE DOES NOT EXIST"); return; }

            // Try MCI with different type specifiers
            bool played = false;
            foreach (var typeSpec in new[] { "", "type mpegvideo", "type MPEGVideo", "type waveaudio" })
            {
                var cmd = string.IsNullOrEmpty(typeSpec)
                    ? $"open \"{selectedAudioPath}\" alias {MciAlias}"
                    : $"open \"{selectedAudioPath}\" {typeSpec} alias {MciAlias}";
                AudioLog($"MCI open: {cmd}");
                int ret = mciSendString(cmd, null, 0, IntPtr.Zero);
                AudioLog($"  ret={ret}");
                if (ret == 0)
                {
                    int retPlay = mciSendString($"play {MciAlias}", null, 0, IntPtr.Zero);
                    AudioLog($"  play ret={retPlay}");
                    if (retPlay == 0)
                    {
                        played = true;
                        this.audioPlaying = true;
                        AudioLog("  SUCCESS - audio playing via MCI");
                        break;
                    }
                    else
                    {
                        mciSendString($"close {MciAlias}", null, 0, IntPtr.Zero);
                    }
                }
            }

            if (!played)
            {
                AudioLog("MCI failed, trying Process.Start fallback");
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(selectedAudioPath)
                    {
                        UseShellExecute = true,
                        Verb = "open",
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    System.Diagnostics.Process.Start(psi);
                    AudioLog("Process.Start launched");
                    this.audioPlaying = true;
                }
                catch (Exception pex)
                {
                    AudioLog($"Process.Start failed: {pex.Message}");
                }
            }
        }
        catch (Exception ex) { AudioLog($"EXCEPTION: {ex}"); }
    }

    private void StopMciAudio()
    {
        try
        {
            var sb = new StringBuilder(32);
            int ret = mciSendString($"status {MciAlias} ready", sb, sb.Capacity, IntPtr.Zero);
            if (ret == 0 && sb.ToString().Trim() == "true")
            {
                mciSendString($"stop {MciAlias}", null, 0, IntPtr.Zero);
                mciSendString($"close {MciAlias}", null, 0, IntPtr.Zero);
            }
        }
        catch { }
    }

    private bool IsMciAudioPlaying()
    {
        try
        {
            var sb = new StringBuilder(32);
            int ret = mciSendString($"status {MciAlias} mode", sb, sb.Capacity, IntPtr.Zero);
            if (ret == 0)
                return string.Equals(sb.ToString().Trim(), "playing", StringComparison.OrdinalIgnoreCase);
        }
        catch { }
        return false;
    }

    public void Dispose()
    {
        StopMciAudio();
        this.audioPlaying = false;
        this.currentMemeTexture = null;
        foreach (var t in frameTextures) { try { /* no dispose available */ } catch { } }
        frameTextures.Clear();
        frameDelaysMs.Clear();
        foreach (var f in frameTempFiles) { try { File.Delete(f); } catch { } }
        frameTempFiles.Clear();
    }

    public override void Draw()
    {
        try
        {
            // Bring overlay to front while visible so config window doesn't cover it
            ImGui.SetNextWindowFocus();

            // Update animation timing
            float dt = ImGui.GetIO().DeltaTime;
            // Ensure first decoded frame is used for display if currentMemeTexture wasn't set during decode
            if (this.currentMemeTexture == null && this.frameTextures.Count > 0)
            {
                this.currentMemeTexture = this.frameTextures[0];
            }

            // Diagnostics: report once if rendering can't proceed
            IDalamudTextureWrap? wrapTmp = null;
            bool tryWrap = false;
            lock (this.textureLock)
            {
                if (this.currentMemeTexture != null)
                    tryWrap = this.currentMemeTexture.TryGetWrap(out wrapTmp, out _);
                if (tryWrap && wrapTmp != null && wrapTmp.Handle != IntPtr.Zero)
                {
                    this.lastTextureHandle = wrapTmp.Handle;
                    this.lastTextureValid = true;
                }
            }

            if (!this.debugReported)
            {
                if (this.currentMemeTexture == null)
                {
                    try { } catch { }
                }
                else if (!tryWrap)
                {
                    try { } catch { }
                }
                else if (wrapTmp == null)
                {
                    try { } catch { }
                }
                else if (wrapTmp.Handle == IntPtr.Zero)
                {
                    try { } catch { }
                }
                this.debugReported = true;
            }

            if (frameTextures.Count > 1)
            {
                animationElapsed += dt * 1000f;
                int delay = frameDelaysMs[currentFrameIndex];
                if (animationElapsed >= delay)
                {
                    animationElapsed -= delay;
                    currentFrameIndex = (currentFrameIndex + 1) % frameTextures.Count;
                    currentMemeTexture = frameTextures[currentFrameIndex];
                }
            }

            // Close overlay when timer expired and no audio playing
            if (this.displayTimer > 0f) this.displayTimer -= dt;
            this.audioPlaying = IsMciAudioPlaying();

            if (this.displayTimer <= 0f && !this.audioPlaying)
            {
                this.IsOpen = false;
                StopMciAudio();
                return;
            }

            var viewport = ImGui.GetMainViewport();
            // Increase GIF size by 40% (previous scale 0.25 -> now 0.35)
            float scale = 0.504f;
            Vector2 imageSize = new Vector2(400, 400) * scale;

            // compute window size and position centered near top � add larger padding so image isn't clipped
            Vector2 totalSize = imageSize + new Vector2(80, 80);
            ImGui.SetNextWindowSize(totalSize, ImGuiCond.Always);
            // Also set Dalamud window size and position so the WindowSystem doesn't clip content
            this.Size = totalSize;
            this.SizeCondition = ImGuiCond.Always;
            var pos = new Vector2(viewport.WorkPos.X + (viewport.WorkSize.X - totalSize.X) / 2, viewport.WorkPos.Y + 20);
            this.Position = pos;
            this.PositionCondition = ImGuiCond.Always;
            ImGui.SetNextWindowPos(pos, ImGuiCond.Always);

            // Prefer to draw the active wrap; fall back to lastTextureHandle if the current texture hasn't produced a wrap yet
            IDalamudTextureWrap? wrapToDraw = null;
            bool haveWrap = false;
            lock (this.textureLock)
            {
            if (this.currentMemeTexture != null)
                haveWrap = this.currentMemeTexture.TryGetWrap(out wrapToDraw, out _);
            if (!haveWrap && this.lastTextureValid)
            {
                // use last known handle directly, centered
                ImGui.SetCursorPosX((totalSize.X - imageSize.X) / 2f);
                ImGui.Image(this.lastTextureHandle, imageSize);
                return;
            }
            }

            // Draw meme image (gif) centered in the window (milestone header removed)
            ImGui.SetCursorPosX((totalSize.X - imageSize.X) / 2f);
            if (wrapToDraw != null)
            {
                ImGui.Image(wrapToDraw.Handle, imageSize);
            }
            else if (this.lastTextureValid)
            {
                ImGui.Image(this.lastTextureHandle, imageSize);
            }
        }
        catch (Exception ex)
        {
            try { System.Diagnostics.Trace.WriteLine(ex.ToString()); } catch { }
            this.IsOpen = false;
        }
    }
}





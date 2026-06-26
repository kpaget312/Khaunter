using JumpKhaunter67;
using System;
using System.IO;
using System.Reflection;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Bindings.ImGui;

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

    // Windows MCI audio playback (winmm.dll)
    private const string MciAlias = "jk67_audio";
    private bool audioPlaying = false;

    [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int mciSendString(string command, StringBuilder? returnString, int returnLength, IntPtr hwndCallback);

    private bool debugReported = false;
    private int frameGeneration = 0;

    public MotivationWindow(Plugin plugin) : base(
        "##JumpKhaunter67_Secret_Overlay",
        ImGuiWindowFlags.NoDecoration | 
        ImGuiWindowFlags.NoBackground | 
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoInputs | 
        ImGuiWindowFlags.NoNav |
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

        // Search upward from the assembly directory for an "images" folder
        try
        {
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

                    if (!string.IsNullOrEmpty(finalAssetPath)) break;
                }
                var parent = Directory.GetParent(dir);
                dir = parent?.FullName ?? string.Empty;
                depth++;
            }
        }
        catch { }

        if (string.IsNullOrEmpty(finalAssetPath))
        {
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
            this.currentMemeTexture = null;

            if (File.Exists(finalAssetPath))
            {
                if (string.Equals(Path.GetExtension(finalAssetPath), ".gif", StringComparison.OrdinalIgnoreCase))
                {
                    CleanupFrames();
                    animationElapsed = 0f;
                    currentFrameIndex = 0;

                    int capturedGen = ++this.frameGeneration;

                    using (var img = Image.FromFile(finalAssetPath))
                    {
                        var dims = new FrameDimension(Guid.Empty);
                        int frameCount = 1;
                        try
                        {
                            var fdList = img.FrameDimensionsList;
                            if (fdList != null && fdList.Length > 0)
                            {
                                dims = new FrameDimension(fdList[0]);
                                frameCount = img.GetFrameCount(dims);
                            }
                        }
                        catch { }

                        int[] delays = new int[frameCount];
                        try
                        {
                            var prop = img.GetPropertyItem(0x5100);
                            var raw = prop?.Value;
                            if (raw != null && raw.Length >= 4)
                            {
                                int count = Math.Min(delays.Length, raw.Length / 4);
                                for (int i = 0; i < count; i++)
                                    delays[i] = Math.Max(BitConverter.ToInt32(raw, i * 4) * 10, 10);
                            }
                        }
                        catch { }

                        // Decode first frame synchronously
                        img.SelectActiveFrame(dims, 0);
                        using (var bmp0 = new Bitmap(img))
                        {
                            string temp0 = Path.Combine(Path.GetTempPath(), $"jk67_{Guid.NewGuid()}_0.png");
                            bmp0.Save(temp0, ImageFormat.Png);
                            var tex0 = Plugin.TextureProvider.GetFromFile(temp0);
                            lock (frameTextures)
                            {
                                frameTextures.Add(tex0);
                                frameDelaysMs.Add(delays.Length > 0 && delays[0] > 0 ? delays[0] : 100);
                                frameTempFiles.Add(temp0);
                            }
                            this.currentMemeTexture = tex0;
                        }

                        // Decode remaining frames in background
                        if (frameCount > 1)
                        {
                            var bgDims = dims;
                            var bgDelays = delays;
                            var bgPath = finalAssetPath;
                            _ = Task.Run(() =>
                            {
                                for (int i = 1; i < frameCount; i++)
                                {
                                    try
                                    {
                                        using var img2 = Image.FromFile(bgPath);
                                        img2.SelectActiveFrame(bgDims, i);
                                        using var bmp2 = new Bitmap(img2);
                                        string temp = Path.Combine(Path.GetTempPath(), $"jk67_{Guid.NewGuid()}_{i}.png");
                                        bmp2.Save(temp, ImageFormat.Png);
                                        var tex = Plugin.TextureProvider.GetFromFile(temp);
                                        lock (frameTextures)
                                        {
                                            if (this.frameGeneration == capturedGen)
                                            {
                                                frameTextures.Add(tex);
                                                frameDelaysMs.Add(bgDelays.Length > i && bgDelays[i] > 0 ? bgDelays[i] : 100);
                                                frameTempFiles.Add(temp);
                                            }
                                            else
                                            {
                                                try { File.Delete(temp); } catch { }
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            });
                        }
                    }
                }
                else
                {
                    this.currentMemeTexture = Plugin.TextureProvider.GetFromFile(finalAssetPath);
                }
            }
        }
        catch { }

        PlayRandomMilestoneAudio();
        this.IsOpen = true;
        this.debugReported = false;
    }

    private void CleanupFrames()
    {
        this.currentMemeTexture = null;
        this.lastTextureValid = false;
        this.frameGeneration++;
        lock (frameTextures)
        {
            frameTextures.Clear();
            frameDelaysMs.Clear();
        }
        var stale = frameTempFiles;
        frameTempFiles = new List<string>();
        foreach (var f in stale) { try { File.Delete(f); } catch { } }
    }

    private void PlayRandomMilestoneAudio()
    {
        try
        {
            string selectedAudioPath = string.Empty;

            var dir = this.pluginDirectory;
            int depth = 0;
            while (!string.IsNullOrEmpty(dir) && depth < 6)
            {
                var candidate = Path.Combine(dir, "audio");
                if (Directory.Exists(candidate))
                {
                    var audioExts = new[] { "*.mp3", "*.m4a" };
                    var audioFiles = audioExts.SelectMany(e => Directory.GetFiles(candidate, e)).ToArray();
                    if (audioFiles.Length > 0)
                    {
                        selectedAudioPath = audioFiles[this.random.Next(0, audioFiles.Length)];
                        break;
                    }
                }

                var parent = Directory.GetParent(dir);
                dir = parent?.FullName ?? string.Empty;
                depth++;
            }

            if (string.IsNullOrEmpty(selectedAudioPath))
            {
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var res = asm.GetManifestResourceNames();
                    var audioRes = Array.FindAll(res, r => r.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || r.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase));
                    if (audioRes.Length > 0)
                    {
                        var pick = audioRes[this.random.Next(audioRes.Length)];
                        var ext = Path.GetExtension(pick);
                        var tempFile = Path.Combine(Path.GetTempPath(), $"jk67_emb_{Guid.NewGuid()}{ext}");
                        using (var rs = asm.GetManifestResourceStream(pick))
                        {
                            if (rs != null) { using var fs = File.OpenWrite(tempFile); rs.CopyTo(fs); selectedAudioPath = tempFile; }
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(selectedAudioPath)) return;
            }

            this.audioPlaying = false;
            StopMciAudio();

            foreach (var typeSpec in new[] { "", "type mpegvideo", "type waveaudio" })
            {
                var cmd = string.IsNullOrEmpty(typeSpec)
                    ? $"open \"{selectedAudioPath}\" alias {MciAlias}"
                    : $"open \"{selectedAudioPath}\" {typeSpec} alias {MciAlias}";
                int ret = mciSendString(cmd, null, 0, IntPtr.Zero);
                if (ret == 0)
                {
                    int retPlay = mciSendString($"play {MciAlias}", null, 0, IntPtr.Zero);
                    if (retPlay == 0)
                    {
                        this.audioPlaying = true;
                        SetVolume(this.plugin.Configuration.Volume);
                        break;
                    }
                    else
                    {
                        mciSendString($"close {MciAlias}", null, 0, IntPtr.Zero);
                    }
                }
            }
        }
        catch { }
    }

    public void SetVolume(int volume)
    {
        try
        {
            var clamped = Math.Clamp(volume, 0, 100);
            mciSendString($"setaudio {MciAlias} volume to {clamped * 10}", null, 0, IntPtr.Zero);
        }
        catch { }
    }

    public void PlayTestAudio()
    {
        try
        {
            string selectedAudioPath = string.Empty;

            var dir = this.pluginDirectory;
            int depth = 0;
            while (!string.IsNullOrEmpty(dir) && depth < 6)
            {
                var candidate = Path.Combine(dir, "audio");
                if (Directory.Exists(candidate))
                {
                    var audioExts = new[] { "*.mp3", "*.m4a" };
                    var audioFiles = audioExts.SelectMany(e => Directory.GetFiles(candidate, e)).ToArray();
                    if (audioFiles.Length > 0)
                    {
                        selectedAudioPath = audioFiles[this.random.Next(0, audioFiles.Length)];
                        break;
                    }
                }

                var parent = Directory.GetParent(dir);
                dir = parent?.FullName ?? string.Empty;
                depth++;
            }

            if (string.IsNullOrEmpty(selectedAudioPath))
            {
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var res = asm.GetManifestResourceNames();
                    var audioRes = Array.FindAll(res, r => r.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || r.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase));
                    if (audioRes.Length > 0)
                    {
                        var pick = audioRes[this.random.Next(audioRes.Length)];
                        var ext = Path.GetExtension(pick);
                        var tempFile = Path.Combine(Path.GetTempPath(), $"jk67_emb_{Guid.NewGuid()}{ext}");
                        using (var rs = asm.GetManifestResourceStream(pick))
                        {
                            if (rs != null) { using var fs = File.OpenWrite(tempFile); rs.CopyTo(fs); selectedAudioPath = tempFile; }
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(selectedAudioPath)) return;
            }

            StopMciAudio();
            this.audioPlaying = false;

            foreach (var typeSpec in new[] { "", "type mpegvideo", "type waveaudio" })
            {
                var cmd = string.IsNullOrEmpty(typeSpec)
                    ? $"open \"{selectedAudioPath}\" alias {MciAlias}"
                    : $"open \"{selectedAudioPath}\" {typeSpec} alias {MciAlias}";
                int ret = mciSendString(cmd, null, 0, IntPtr.Zero);
                if (ret == 0)
                {
                    int retPlay = mciSendString($"play {MciAlias}", null, 0, IntPtr.Zero);
                    if (retPlay == 0)
                    {
                        SetVolume(this.plugin.Configuration.Volume);
                        break;
                    }
                    else
                    {
                        mciSendString($"close {MciAlias}", null, 0, IntPtr.Zero);
                    }
                }
            }
        }
        catch { }
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
        CleanupFrames();
    }

    public override void Draw()
    {
        try
        {
            // Animation timing
            float dt = ImGui.GetIO().DeltaTime;
            // Ensure first decoded frame is used for display if currentMemeTexture wasn't set during decode
            lock (frameTextures)
            {
                if (this.currentMemeTexture == null && this.frameTextures.Count > 0)
                {
                    this.currentMemeTexture = this.frameTextures[0];
                }
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

            lock (frameTextures)
            {
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
            }

            // Close overlay when timer expired and no audio playing
            if (this.displayTimer > 0f) this.displayTimer -= dt;
            this.audioPlaying = IsMciAudioPlaying();

            if (this.displayTimer <= 0f && !this.audioPlaying)
            {
                this.IsOpen = false;
                StopMciAudio();
                CleanupFrames();
                return;
            }

            var viewport = ImGui.GetMainViewport();
            // GIF Size
            float scale = 0.504f;
            Vector2 imageSize = new Vector2(400, 400) * scale;

            // compute window size and position
            Vector2 totalSize = imageSize + new Vector2(80, 80);
            ImGui.SetNextWindowSize(totalSize, ImGuiCond.Always);
            this.Size = totalSize;
            this.SizeCondition = ImGuiCond.Always;
            var pos = new Vector2(viewport.WorkPos.X + (viewport.WorkSize.X - totalSize.X) / 2, viewport.WorkPos.Y + 20);
            this.Position = pos;
            this.PositionCondition = ImGuiCond.Always;
            ImGui.SetNextWindowPos(pos, ImGuiCond.Always);

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

            // Draw Shrek
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





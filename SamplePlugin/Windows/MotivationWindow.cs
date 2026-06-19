using JumpKhaunter67;
using System;
using System.IO;
using System.Reflection;
using System.Numerics;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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

    private CancellationTokenSource? decodeCts;

    private bool debugReported = false;

    // Simple file logger for diagnostics (disabled)
    private void WriteLog(string msg) { }

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
                    // Cancel any previous GIF decoding task
                    try { this.decodeCts?.Cancel(); } catch { }
                    this.decodeCts = new CancellationTokenSource();
                    var ct = this.decodeCts.Token;

                    try
                    {
                        lock (frameTextures)
                        {
                            frameTextures.Clear();
                            frameDelaysMs.Clear();
                        }
                        animationElapsed = 0f;
                        currentFrameIndex = 0;

                        var decoder = BitmapDecoder.Create(new Uri(finalAssetPath, UriKind.Absolute),
                            BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        int frameCount = decoder.Frames.Count;

                        int[] delays = new int[frameCount];
                        for (int i = 0; i < frameCount; i++)
                        {
                            try
                            {
                                var meta = decoder.Frames[i].Metadata as BitmapMetadata;
                                if (meta != null)
                                {
                                    var rawDelay = meta.GetQuery("/grctlext/Delay");
                                    if (rawDelay is ushort u) delays[i] = Math.Max(u * 10, 10);
                                    else if (rawDelay is short s) delays[i] = Math.Max(s * 10, 10);
                                    else delays[i] = 100;
                                }
                                else delays[i] = 100;
                            }
                            catch { delays[i] = 100; }
                        }

                        using (var ms = new MemoryStream())
                        {
                            var enc = new PngBitmapEncoder();
                            enc.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
                            enc.Save(ms);
                            string temp0 = Path.Combine(Path.GetTempPath(), $"jk67_{Guid.NewGuid()}_0.png");
                            File.WriteAllBytes(temp0, ms.ToArray());
                            var tex0 = Plugin.TextureProvider.GetFromFile(temp0);
                            lock (frameTextures) { frameTextures.Add(tex0); frameDelaysMs.Add(delays.Length > 0 ? delays[0] : 100); frameTempFiles.Add(temp0); }
                        }

                        if (frameCount > 1 && !ct.IsCancellationRequested)
                        {
                            var frozenFrames = new BitmapFrame[frameCount];
                            for (int i = 0; i < frameCount; i++)
                                frozenFrames[i] = BitmapFrame.Create(decoder.Frames[i]);

                            _ = Task.Run(() =>
                            {
                                try
                                {
                                    for (int i = 1; i < frameCount; i++)
                                    {
                                        if (ct.IsCancellationRequested) break;
                                        try
                                        {
                                            using var ms = new MemoryStream();
                                            var enc = new PngBitmapEncoder();
                                            enc.Frames.Add(frozenFrames[i]);
                                            enc.Save(ms);
                                            string temp = Path.Combine(Path.GetTempPath(), $"jk67_{Guid.NewGuid()}_{i}.png");
                                            File.WriteAllBytes(temp, ms.ToArray());
                                            var tex = Plugin.TextureProvider.GetFromFile(temp);
                                            lock (frameTextures) { frameTextures.Add(tex); frameDelaysMs.Add(delays.Length > i ? delays[i] : 100); frameTempFiles.Add(temp); }
                                        }
                                        catch (Exception fex) { System.Diagnostics.Trace.WriteLine(fex.Message); }
                                    }
                                }
                                catch (Exception ex2) { System.Diagnostics.Trace.WriteLine(ex2.Message); }
                            }, ct);
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
                    var mp3Files = Directory.GetFiles(candidate, "*.mp3");
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
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var res = asm.GetManifestResourceNames();
                    var mp3s = Array.FindAll(res, r => r.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));
                    if (mp3s.Length > 0)
                    {
                        var pick = mp3s[this.random.Next(mp3s.Length)];
                        var ext = Path.GetExtension(pick);
                        var tempMp3 = Path.Combine(Path.GetTempPath(), $"jk67_emb_{Guid.NewGuid()}{ext}");
                        using (var rs = asm.GetManifestResourceStream(pick))
                        {
                            if (rs != null) { using var fs = File.OpenWrite(tempMp3); rs.CopyTo(fs); selectedAudioPath = tempMp3; }
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
        try { this.decodeCts?.Cancel(); } catch { }
        this.decodeCts?.Dispose();
        StopMciAudio();
        this.audioPlaying = false;
        this.currentMemeTexture = null;
        lock (frameTextures)
        {
            foreach (var t in frameTextures) { try { /* no dispose available */ } catch { } }
            frameTextures.Clear();
            frameDelaysMs.Clear();
        }
        foreach (var f in frameTempFiles) { try { File.Delete(f); } catch { } }
        frameTempFiles.Clear();
    }

    public override void Draw()
    {
        try
        {
            // Update animation timing
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





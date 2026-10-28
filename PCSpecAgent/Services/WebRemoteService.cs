using System.Diagnostics;
using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PCSpecAgent.Forms;
using PCSpecAgent.Models;
using DrawingEncoder = System.Drawing.Imaging.Encoder;

namespace PCSpecAgent.Services;

public class WebRemoteService
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    private const int CURSOR_SHOWING = 0x00000001;

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(out CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern bool DrawIcon(IntPtr hDC, int X, int Y, IntPtr hIcon);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_LWIN = 0x5B;
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_TAB = 0x09;
    private const byte VK_MENU = 0x12;
    private const byte VK_CONTROL = 0x11;

    private readonly AppConfig _config;
    private readonly HttpClient _http;
    private bool _isSessionActive = false;
    private readonly object _lock = new();

    private int _capturedWidth = 1920;
    private int _capturedHeight = 1080;

    public WebRemoteService(AppConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("X-PC-TOKEN", _config.OrgToken);
    }

    private string NormalizeUrl(string endpoint)
    {
        string baseUrl = _config.ServerUrl.TrimEnd('/');
        return $"{baseUrl}/{endpoint.TrimStart('/')}";
    }

    public async Task CheckAndHandleIncomingSessionAsync()
    {
        lock (_lock)
        {
            if (_isSessionActive) return;
        }

        try
        {
            string url = NormalizeUrl($"api/remote.php?action=check_request&machine_id={Uri.EscapeDataString(_config.MachineId)}");
            var res = await _http.GetAsync(url);
            if (!res.IsSuccessStatusCode) return;

            string json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("has_request", out var hasReq) && hasReq.GetBoolean())
            {
                string token = root.GetProperty("session_token").GetString() ?? "";
                string status = root.GetProperty("status").GetString() ?? "";
                string adminName = root.TryGetProperty("admin_name", out var an) ? an.GetString() ?? "Admin IT Pusat" : "Admin IT Pusat";

                if (status == "pending_consent" && !string.IsNullOrEmpty(token))
                {
                    await HandleConsentPromptAsync(token, adminName);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Remote check error: {ex.Message}");
        }
    }

    private async Task HandleConsentPromptAsync(string sessionToken, string adminName)
    {
        lock (_lock)
        {
            if (_isSessionActive) return;
            _isSessionActive = true;
        }

        string generatedPin = Random.Shared.Next(100000, 999999).ToString();
        bool isApproved = false;

        // Show Consent Prompt Dialog on desktop
        try
        {
            using var promptForm = new ConsentPromptForm(adminName, generatedPin);
            promptForm.ShowDialog();
            isApproved = promptForm.IsApproved;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Consent dialog error: {ex.Message}");
        }

        // Send consent response to server
        try
        {
            string url = NormalizeUrl("api/remote.php?action=consent_response");
            var payload = new
            {
                session_token = sessionToken,
                response = isApproved ? "approved" : "rejected",
                pin_code = generatedPin
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await _http.PostAsync(url, content);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Send consent response error: {ex.Message}");
        }

        if (isApproved)
        {
            // Start streaming and control loop
            _ = Task.Run(async () => await RunStreamingLoopAsync(sessionToken, adminName));
        }
        else
        {
            lock (_lock)
            {
                _isSessionActive = false;
            }
        }
    }

    private async Task RunStreamingLoopAsync(string sessionToken, string adminName)
    {
        RemoteFloatingBarForm? floatingBar = null;

        // Show floating top banner on UI thread
        try
        {
            var syncContext = SynchronizationContext.Current;
            floatingBar = new RemoteFloatingBarForm(adminName);
            floatingBar.OnDisconnectRequested += () =>
            {
                _isSessionActive = false;
                _ = EndSessionOnServerAsync(sessionToken);
            };

            // Run in dedicated thread so it doesn't block capture
            var barThread = new Thread(() =>
            {
                Application.Run(floatingBar);
            });
            barThread.SetApartmentState(ApartmentState.STA);
            barThread.Start();
        }
        catch { }

        try
        {
            // Run Frame Capture/Upload and Input Polling concurrently in two decoupled tasks
            var frameTask = RunFrameCaptureLoopAsync(sessionToken);
            var inputTask = RunInputPollingLoopAsync(sessionToken);
            await Task.WhenAll(frameTask, inputTask);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Streaming loop error: {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                _isSessionActive = false;
            }

            try
            {
                if (floatingBar != null && !floatingBar.IsDisposed)
                {
                    floatingBar.Invoke(new Action(() => floatingBar.Close()));
                }
            }
            catch { }
        }
    }

    private async Task RunFrameCaptureLoopAsync(string sessionToken)
    {
        var encoder = GetEncoder(ImageFormat.Jpeg);
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(DrawingEncoder.Quality, 48L); // 48% JPEG gives crystal-clear text while keeping size under 25KB

        bool isUploading = false;

        while (_isSessionActive)
        {
            long startTick = Environment.TickCount64;

            if (isUploading)
            {
                // Network is still transmitting previous frame; skip this cycle to avoid bufferbloat & latency
                await Task.Delay(25);
                continue;
            }

            try
            {
                var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

                // Target streaming resolution: downscale to 1280 wide max if screen is larger
                int targetWidth = screen.Width;
                int targetHeight = screen.Height;
                if (targetWidth > 1280)
                {
                    targetHeight = (int)((double)targetHeight * 1280 / targetWidth);
                    targetWidth = 1280;
                }

                _capturedWidth = targetWidth;
                _capturedHeight = targetHeight;

                using var rawBmp = new Bitmap(screen.Width, screen.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(rawBmp))
                {
                    g.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size, CopyPixelOperation.SourceCopy);

                    // Draw Hardware Mouse Cursor Overlay
                    try
                    {
                        var cursorInfo = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
                        if (GetCursorInfo(out cursorInfo) && cursorInfo.flags == CURSOR_SHOWING && cursorInfo.hCursor != IntPtr.Zero)
                        {
                            IntPtr hdc = g.GetHdc();
                            try
                            {
                                int cursorX = cursorInfo.ptScreenPos.x - screen.Left;
                                int cursorY = cursorInfo.ptScreenPos.y - screen.Top;
                                DrawIcon(hdc, cursorX, cursorY, cursorInfo.hCursor);
                            }
                            finally
                            {
                                g.ReleaseHdc(hdc);
                            }
                        }
                    }
                    catch { }
                }

                Bitmap bmpToSave = rawBmp;
                Bitmap? scaledBmp = null;
                if (targetWidth != screen.Width || targetHeight != screen.Height)
                {
                    scaledBmp = new Bitmap(rawBmp, new Size(targetWidth, targetHeight));
                    bmpToSave = scaledBmp;
                }

                byte[] jpegBytes;
                try
                {
                    using var ms = new MemoryStream();
                    bmpToSave.Save(ms, encoder, encoderParams);
                    jpegBytes = ms.ToArray();
                }
                finally
                {
                    scaledBmp?.Dispose();
                }

                string frameUrl = NormalizeUrl($"api/remote.php?action=upload_frame&session_token={Uri.EscapeDataString(sessionToken)}");

                isUploading = true;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var byteContent = new ByteArrayContent(jpegBytes);
                        byteContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

                        var frameRes = await _http.PostAsync(frameUrl, byteContent);
                        if (!frameRes.IsSuccessStatusCode)
                        {
                            var body = await frameRes.Content.ReadAsStringAsync();
                            if (body.Contains("ended") || body.Contains("tidak aktif"))
                            {
                                _isSessionActive = false;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Upload frame error: {ex.Message}");
                    }
                    finally
                    {
                        isUploading = false;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Frame capture error: {ex.Message}");
            }

            long elapsed = Environment.TickCount64 - startTick;
            int delay = (int)Math.Max(25, 75 - elapsed); // Aim for ~13 FPS
            await Task.Delay(delay);
        }
    }

    private async Task RunInputPollingLoopAsync(string sessionToken)
    {
        int lastInputId = 0;
        while (_isSessionActive)
        {
            try
            {
                string inputUrl = NormalizeUrl($"api/remote.php?action=fetch_inputs&session_token={Uri.EscapeDataString(sessionToken)}&last_id={lastInputId}");
                var inputRes = await _http.GetAsync(inputUrl);
                if (inputRes.IsSuccessStatusCode)
                {
                    string inJson = await inputRes.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(inJson);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("session_status", out var ss) && ss.GetString() == "ended")
                    {
                        _isSessionActive = false;
                        break;
                    }

                    if (root.TryGetProperty("inputs", out var inputsArr) && inputsArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in inputsArr.EnumerateArray())
                        {
                            int id = item.GetProperty("id").GetInt32();
                            if (id > lastInputId) lastInputId = id;

                            string type = item.GetProperty("type").GetString() ?? "";
                            var data = item.GetProperty("data");

                            DispatchInputEvent(type, data);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Input error: {ex.Message}");
            }

            await Task.Delay(35); // Poll inputs every ~35ms for responsive input
        }
    }

    private void DispatchInputEvent(string type, JsonElement data)
    {
        try
        {
            var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

            if (type == "mouse_move")
            {
                int x = data.GetProperty("x").GetInt32();
                int y = data.GetProperty("y").GetInt32();
                int realX = screen.Left + (int)((double)x / Math.Max(1, _capturedWidth) * screen.Width);
                int realY = screen.Top + (int)((double)y / Math.Max(1, _capturedHeight) * screen.Height);
                SetCursorPos(realX, realY);
            }
            else if (type == "mouse_down")
            {
                int x = data.GetProperty("x").GetInt32();
                int y = data.GetProperty("y").GetInt32();
                int button = data.GetProperty("button").GetInt32();
                int realX = screen.Left + (int)((double)x / Math.Max(1, _capturedWidth) * screen.Width);
                int realY = screen.Top + (int)((double)y / Math.Max(1, _capturedHeight) * screen.Height);
                SetCursorPos(realX, realY);

                uint flag = button switch
                {
                    0 => MOUSEEVENTF_LEFTDOWN,
                    1 => MOUSEEVENTF_MIDDLEDOWN,
                    2 => MOUSEEVENTF_RIGHTDOWN,
                    _ => MOUSEEVENTF_LEFTDOWN
                };
                mouse_event(flag, 0, 0, 0, 0);
            }
            else if (type == "mouse_up")
            {
                int x = data.GetProperty("x").GetInt32();
                int y = data.GetProperty("y").GetInt32();
                int button = data.GetProperty("button").GetInt32();
                int realX = screen.Left + (int)((double)x / Math.Max(1, _capturedWidth) * screen.Width);
                int realY = screen.Top + (int)((double)y / Math.Max(1, _capturedHeight) * screen.Height);
                SetCursorPos(realX, realY);

                uint flag = button switch
                {
                    0 => MOUSEEVENTF_LEFTUP,
                    1 => MOUSEEVENTF_MIDDLEUP,
                    2 => MOUSEEVENTF_RIGHTUP,
                    _ => MOUSEEVENTF_LEFTUP
                };
                mouse_event(flag, 0, 0, 0, 0);
            }
            else if (type == "wheel")
            {
                int deltaY = (int)data.GetProperty("deltaY").GetDouble();
                // Windows WHEEL delta is typically 120 per click, negative is down
                int winDelta = deltaY > 0 ? -120 : 120;
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)winDelta, 0);
            }
            else if (type == "special_key")
            {
                string key = data.GetProperty("key").GetString() ?? "";
                if (key == "win_key")
                {
                    keybd_event(VK_LWIN, 0, 0, 0);
                    keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, 0);
                }
                else if (key == "esc")
                {
                    keybd_event(VK_ESCAPE, 0, 0, 0);
                    keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, 0);
                }
                else if (key == "alt_tab")
                {
                    keybd_event(VK_MENU, 0, 0, 0);
                    keybd_event(VK_TAB, 0, 0, 0);
                    keybd_event(VK_TAB, 0, KEYEVENTF_KEYUP, 0);
                    keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0);
                }
            }
            else if (type == "key_down")
            {
                if (data.TryGetProperty("keyCode", out var kc))
                {
                    byte bVk = (byte)kc.GetInt32();
                    keybd_event(bVk, 0, 0, 0);
                }
            }
            else if (type == "key_up")
            {
                if (data.TryGetProperty("keyCode", out var kc))
                {
                    byte bVk = (byte)kc.GetInt32();
                    keybd_event(bVk, 0, KEYEVENTF_KEYUP, 0);
                }
            }
        }
        catch { }
    }

    private async Task EndSessionOnServerAsync(string sessionToken)
    {
        try
        {
            string url = NormalizeUrl("api/remote.php?action=end_session");
            var content = new StringContent(JsonSerializer.Serialize(new { session_token = sessionToken }), Encoding.UTF8, "application/json");
            await _http.PostAsync(url, content);
        }
        catch { }
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
        foreach (ImageCodecInfo codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return codecs[0];
    }
}

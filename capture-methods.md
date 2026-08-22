# Capturing a Windows 11 Application Window with True Alpha Corners (and Optionally the DWM Shadow)

## TL;DR
- **For pixel-perfect results including the real DWM drop shadow, use the classic dual-background (white/black) alpha-recovery technique** on the `DWMWA_EXTENDED_FRAME_BOUNDS` rect plus shadow padding — this is exactly how ShareX's "Capture window with transparency" works and is the only method that recovers the true semi-transparent shadow and anti-aliased corners for an arbitrary third-party window.
- **For the simplest robust result (corners only, no real shadow), use Windows.Graphics.Capture (WGC)**: capturing a single window with `B8G8R8A8UIntNormalized` preserves the rounded corners as genuinely transparent (premultiplied-alpha) pixels for free — but the capture region equals the shadow-excluded frame bounds, so the window's drop shadow is NOT included; synthesize a shadow if you need one.
- **Avoid relying on `PrintWindow`/`BitBlt` alone**: they produce square, opaque corners with no alpha on Windows 11. Always run the capturing process as Per-Monitor-v2 DPI-aware so coordinates and the 8px-at-96-DPI corner radius scale correctly.

## Key Findings

1. **WGC preserves corner transparency but not the window shadow.** A single-window WGC capture returns the DWM-composited surface, whose Windows 11 rounded corners already carry alpha=0. But the captured region matches `DWMWA_EXTENDED_FRAME_BOUNDS` (shadow excluded), so a normal top-level window's drop shadow is outside the frame and is not captured. Menu/popup/taskbar translucency IS captured because it lives in those windows' own surfaces.
2. **WGC delivers premultiplied BGRA alpha.** In premultiplied alpha a fully transparent pixel is (0,0,0,0) — transparent and black are the same bytes. This is why OBS and other tools that flatten onto an opaque pipeline show "black corners" (obsproject/obs-studio issue #6413): the alpha is present but ignored.
3. **The dual-background method is the gold standard for shadow + corners.** Place a solid white full-screen window just behind the target, capture; swap to black, capture; recover alpha = 1 − (white − black) per channel and un-blend the color. This recovers the true semi-transparent DWM shadow and anti-aliased corners. ShareX's `Screenshot_Transparent.cs` implements exactly this.
4. **Windows 11 corner radius is 8px at 96 DPI (100%), scaling linearly with DPI.** Per Microsoft Learn's "Guidance for Rounded Display Bezels": *"Windows 11 ships with two radii available for rounding – 4 px for controls such as buttons and input fields, and 8 px for flyout menus and main app windows... The design of software rounding in Windows 11 is based on an 8 px radius at 96 DPI, and this scales linearly."* That yields ≈10px @125%, ≈12px @150%, 16px @200%. There is no API to read the actual rounded-corner geometry (`GetWindowRgn` returns ERROR on Win11), so a manual mask must reproduce the 8px-logical radius.
5. **`PrintWindow`+`PW_RENDERFULLCONTENT` renders square corners and no alpha** but is the most reliable in-process/opaque grab for DirectComposition/UWP/WPF content; combine with a synthetic rounded mask.
6. **In-process the plugin can temporarily set `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_DONOTROUND`** on its host window to capture a clean square window, then apply a synthetic mask/shadow.

## Details

### 1. Windows.Graphics.Capture (WGC / WinRT)

WGC (`Windows.Graphics.Capture`) first shipped in Windows 10 version 1803 / build 17134 (the `GraphicsCaptureSession` class is documented as "introduced in 10.0.17134.0", UniversalApiContract v6.0). For a single window you use the Win32 interop `IGraphicsCaptureItemInterop::CreateForWindow(hwnd, …)`, whose minimum supported client is Windows 10 version 1903 / build 18362. You then create a `Direct3D11CaptureFramePool` with `DirectXPixelFormat.B8G8R8A8UIntNormalized`, create a `GraphicsCaptureSession`, and pull one frame.

**Alpha behavior:** the captured surface carries the DWM per-pixel alpha, so Windows 11 rounded corners come out with alpha=0 *for free* — no manual corner masking needed. The alpha is **premultiplied** (the default pairing for this format is `B8G8R8A8UIntNormalized` + `CanvasAlphaMode.Premultiplied`; this is high-confidence inference from DWM/DXGI composition conventions and the observed transparent=black-when-flattened behavior, rather than a single verbatim spec sentence). To save a correct PNG you must be careful: PNG stores straight (non-premultiplied) alpha, so you should un-premultiply (divide RGB by A) before/at save time, or use an imaging pipeline (Win2D/WIC) that handles the conversion. The reference implementation of "perfect Windows 11 screenshots" this way is rednoah's **CaptureGraphicsWin2D**, which uses Microsoft.Graphics.Win2D on top of WGC and saves 32-bit PNGs with the alpha preserved; it relies on WGC's native alpha, not chroma-keying. (Its shadow examples are context menus, pop-ups, and the taskbar — windows whose shadow/translucency lives in their own surface — not ordinary app-window DWM shadows.)

**Shadow:** because the WGC window-capture region equals the extended frame bounds (confirmed by robmikh/Win32CaptureSample issue #26, where a content-size mismatch with the DWM frame bounds is treated as an API bug), the surrounding DWM drop shadow of a normal window is not part of the frame. If you need a shadow with WGC you must synthesize it.

**Session options and version gates:**
- `IsCursorCaptureEnabled` (hide cursor): Windows 10 version 2004 / build 19041 (UniversalApiContract v10.0).
- `IsBorderRequired` (remove the yellow capture border): Windows 10 version 2104 / build 20348 (UniversalApiContract v12.0), and in practice **only usable on Windows 11**. It requires calling `GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless)` and declaring the `graphicsCaptureWithoutBorder` capability (which itself requires a minimum OS build of 19645 in the uap11 manifest schema). On Windows 10 client builds (19045) the property throws E_NOINTERFACE — gate it with `ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired")`.
- `IncludeSecondaryWindows` and `DirtyRegionMode`: newer additions; capturing secondary popups/dialogs that intersect the main window is a Windows 11 24H2 capability.

**Calling from .NET Framework 4.8:** WGC is WinRT. Add the `Microsoft.Windows.SDK.Contracts` NuGet package (supports .NET Framework 4.6+ / .NET Core 3.0+) to project the WinRT types. You still need a hand-written COM import to cast the capture item to `IGraphicsCaptureItemInterop` (interop isn't projected), and `CreateDirect3D11DeviceFromDXGIDevice` / `Direct3D11CaptureFramePool` for the D3D↔WinRT surface bridge. A pragmatic alternative is a small C++/WinRT helper DLL (use robmikh's Win32CaptureSample as the template) that you P/Invoke — this sidesteps the fiddly .NET Framework WinRT interop entirely.

**Python:** use the community `winsdk`/`winrt-*` packages (`create_for_window(hwnd)` is exposed in `winsdk.windows.graphics.capture.interop`), or the Rust-backed `windows-capture` pip package which is simpler (`draw_border=False`, `cursor_capture=False`, then `frame.save_as_image("out.png")`).

### 2. PrintWindow with PW_RENDERFULLCONTENT

`PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT=0x00000002)` asks the window to render itself into your DC. `PW_RENDERFULLCONTENT` (Windows 8.1+, historically undocumented but ubiquitous) is required to capture DirectComposition/GPU-composited content (Chrome, UWP, WPF, WinUI) that otherwise prints as a black rectangle. Behavior on Windows 11:
- It renders the window as a **rectangle with square corners** — the corner pixels contain real window/background content, not transparency.
- It produces **no meaningful alpha** (the DC is opaque).
- It is synchronous/blocking and can be slow; the target app processes the `WM_PRINT`/`WM_PRINTCLIENT` message.

It's the best "opaque grab" for occluded or GPU-composited windows. Combine with a synthetic rounded mask (Section 5). Because it returns square corners with real content, masking cleanly removes exactly the corner overhang.

### 3. DWM APIs

- **`DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS=9, &rect, sizeof(rect))`** returns the true visible window bounds *excluding* the drop shadow and invisible resize borders. Critically, unlike `GetWindowRect`, these bounds are **not** DPI-virtualized. `GetWindowRect` on Vista+ *includes* the shadow/invisible border area. So: use `GetWindowRect` to know the outer rect (with shadow), `DWMWA_EXTENDED_FRAME_BOUNDS` to know the visible window rect; the difference is the shadow/resize-border band. (Note: extended frame bounds can only be read after the window has been shown at least once.)
- **`DWMWA_WINDOW_CORNER_PREFERENCE = 33`** (Windows 11) with the `DWM_WINDOW_CORNER_PREFERENCE` enum (`DWMWCP_DEFAULT=0, DWMWCP_DONOTROUND=1, DWMWCP_ROUND=2, DWMWCP_ROUNDSMALL=3`) lets you read/set rounding via `DwmGet/SetWindowAttribute`. Reading returns only the *preference*, not whether the window is actually rounded (snapped/maximized/VM/AVD windows aren't rounded regardless of preference).
- **`GetWindowRgn` returns ERROR on Windows 11** — the rounded region is not exposed, so you cannot get exact corner geometry from it (confirmed by the Greenshot maintainer on Microsoft Q&A).
- **DWM thumbnails (`DwmRegisterThumbnail`)** render a live preview of a window into your own window's area but **cannot be read back into a bitmap** by your process — the composition happens in DWM, above your content. (One Microsoft Q&A shows reading thumbnail-like content via a bespoke DWM-window→HBITMAP route with D3D9 `D3DXSaveSurfaceToFileInMemory`, but that is not the public thumbnail API.) Treat thumbnails as non-capturable.

C# P/Invoke for corner preference:
```csharp
[DllImport("dwmapi.dll", PreserveSig = true)]
static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
const int DWMWCP_DONOTROUND = 1, DWMWCP_DEFAULT = 0;
int pref = DWMWCP_DONOTROUND;
DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
```

### 4. The dual-background (white/black) alpha-recovery technique — RECOMMENDED for shadow + corners

This technique was popularized by Kenny Kerr's Window Clippings and documented by Ian Griffiths in his 30 January 2007 post "Calculating Transparency of Vista Windows" (*"I recently wrote about Kenny Kerr's Window Clippings utility... how it generates transparent screenshots... Kenny and I both came up with the same idea independently"*). It is what ShareX's `CaptureWindowTransparent` implements today.

**Math.** For a window pixel with true straight color C and alpha A composited over a background of value k, the observed value is `obs = A·C + (1−A)·k`. Capturing over black (k=0) gives `blackObs = A·C`. Capturing over white (k=1, i.e., 255 normalized to 1) gives `whiteObs = A·C + (1−A)`. Subtracting:
```
A     = 1 − (whiteObs − blackObs)      // per channel; the three estimates should agree
Color = blackObs / A                    // un-premultiply to straight color (guard A=0)
```
Work on 0..1 normalized values. `blackObs` is exactly the premultiplied color, so you can save `Color = blackObs/A` with straight alpha A, or store premultiplied directly if your consumer expects it.

**Implementation steps (matching ShareX):**
1. Get the target rect via `DWMWA_EXTENDED_FRAME_BOUNDS`; if capturing the shadow, `rect.Inflate(shadowOffset, shadowOffset)` and intersect with the union of all screen bounds (ShareX: `Screen.AllScreens.Select(x => x.Bounds).Where(x => x.IntersectsWith(rect)).Combine()`), and only if `!IsZoomed(handle) && IsDWMEnabled()`.
2. Create a borderless topmost backdrop window sized to that rect, positioned just behind the target with `SetWindowPos(backdrop, target, …, SWP_NOACTIVATE)` (insert the backdrop directly *after* the target in Z-order) — or use two sequential fullscreen backdrops.
3. Paint it solid white; call `DwmFlush()` / wait for composition to settle; `BitBlt` or `Graphics.CopyFromScreen` the rect.
4. Paint it solid black; flush; capture again.
5. Compute per-pixel alpha and color with the formula above; auto-trim fully-transparent margins.

**Pitfalls:**
- **Mica/Acrylic backdrops in the *target* app** sample whatever is behind the window. Because your white and black backdrops change what's behind, the target's own translucent material changes color between the two captures, so the alpha math produces artifacts on those regions. Detect via `DWMWA_SYSTEMBACKDROP_TYPE` / `DWMWA_USE_HOSTBACKDROPBRUSH`; if the window uses a system backdrop, prefer WGC + synthetic mask instead, or accept color error there. This is the root cause of ShareX's intermittent "white/gray background instead of transparent" reports on Windows 11 (issues #2099, #7491), and of the "sometimes 2-3 corners work" flakiness (#5951).
- **Timing/animation:** wait for open/resize animations to finish and call `DwmFlush` before each grab; a moving window desyncs the two captures.
- **Occlusion:** the target must be unobstructed and foreground; bring it forward first.
- **Windows 11's shadow is larger than Windows 10's** (ShareX #5674), so the older ShareX default `ShadowOffset` clipped it on Win11; use a generous offset (~40px) then auto-trim.
- **Anti-aliased corners** are recovered correctly — they're just partially-transparent pixels handled by the same math.

**Screen-grab method:** `BitBlt` from the screen DC or `Graphics.CopyFromScreen` both work. Desktop Duplication (DXGI) also works but is overkill; whichever you use, it captures the *composited* desktop (opaque), which is exactly what the dual-background math requires.

### 5. Manual masking approach (opaque capture + synthetic rounded corners + optional synthetic shadow)

Capture opaque via WGC/PrintWindow/BitBlt of the `DWMWA_EXTENDED_FRAME_BOUNDS` rect, then apply an anti-aliased rounded-rectangle alpha mask. The radius must be the Windows 11 value: **8px at 96 DPI, scaled linearly**: `radiusPx = round(8 * dpi / 96)` (8 @100%, 10 @125%, 12 @150%, 16 @200%). Use `GetDpiForWindow(hwnd)` to get the target's DPI.

C#/GDI+ (anti-aliased):
```csharp
static GraphicsPath RoundedRect(Rectangle r, int radius) {
    int d = radius * 2; var p = new GraphicsPath();
    p.AddArc(r.X, r.Y, d, d, 180, 90);
    p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    p.CloseFigure(); return p;
}
static Bitmap ApplyRoundedCorners(Bitmap src, int radius) {
    var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(dst)) {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using (var path = RoundedRect(new Rectangle(0, 0, src.Width, src.Height), radius))
        using (var brush = new TextureBrush(src))
            g.FillPath(brush, path);
    }
    return dst;
}
```
For a synthetic shadow, draw a black rounded rect offset and Gaussian-blurred on a larger transparent canvas underneath — approximating (not matching exactly) DWM's shadow.

Python/PIL: build an `'L'` mask with `ImageDraw.rounded_rectangle`, render it ~4× oversized and downscale for anti-aliasing, then `img.putalpha(mask)`.

### 6. Desktop Duplication API (DXGI)

`IDXGIOutputDuplication` captures the fully composed, **opaque** desktop of a monitor. It gives you no per-window alpha by itself; to get alpha you must still run the dual-background recovery. Its interop from C#/Python (SharpDX/Vortice/comtypes) is heavier than BitBlt with no benefit for this use case. Use it only if you already have a Direct3D pipeline or need high-FPS video (note: robmikh reports a Desktop-Duplication capture-behavior change in 24H2 tied to dirty-region handling).

### 7. In-process options (plugin running inside the host)

- **If the host is WPF:** `RenderTargetBitmap` (`PixelFormats.Pbgra32`) can render any `Visual` to a bitmap *with* alpha, entirely in-process, no screen capture. Caveats: it is software-rendered only; it won't capture child HWNDs / hosted DirectX / `WindowsFormsHost` content (the WPF/Win32 "airspace" rule — each pixel belongs to exactly one technology); and it renders the app's *visual tree*, not the DWM frame/shadow. Excellent for capturing the plugin's own widget; not for the host's non-client shadow.
- **If the host is WinForms:** `Control.DrawToBitmap` renders a control to a bitmap but is opaque, doesn't handle GPU/ActiveX content, and has documented quirks (RichTextBox draws only the border; hidden TextBoxes are skipped; containers render children in reverse order). Weak for high-fidelity capture.
- **Temporarily disabling rounding:** from in-process the plugin can call `DwmSetWindowAttribute(hostHwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND)`, capture a clean square window (any method), then restore `DWMWCP_DEFAULT` and apply a synthetic mask/shadow. Risks: it briefly changes the host's appearance (user-visible flicker) and modifies a window you don't own — acceptable for a transient capture, but restore promptly and only on a window you can guarantee to reset.

### DPI awareness (mandatory)

Run the capturing process as **Per-Monitor-v2 DPI aware** (manifest `<dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>`, or `SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` before any window is created; `SetProcessDpiAwarenessContext` requires Windows 10 1607+ for the finer-grained value). Otherwise Windows virtualizes coordinates, `GetWindowRect`/screen grabs return scaled/blurry results, and your corner radius will be wrong. Remember `DWMWA_EXTENDED_FRAME_BOUNDS` is already in physical pixels (not virtualized), while `GetWindowRect` is virtualized — mixing them under the wrong awareness causes offset bugs. For multi-monitor, always intersect capture rects with the union of `Screen.AllScreens` bounds and read per-monitor DPI with `GetDpiForWindow`/`GetDpiForMonitor`.

## Comparison Table

| Technique | Alpha corners | Real DWM shadow | Robustness (3rd-party window) | Complexity | OS requirement |
|---|---|---|---|---|---|
| Dual-background (white/black) recovery | ✅ true anti-aliased | ✅ true semi-transparent | High (must be foreground, unobstructed) | Medium-High | Any (Vista+ for DWM) |
| WGC (`B8G8R8A8UIntNormalized`) | ✅ true (premultiplied) | ❌ excluded from frame | High | Medium (WinRT interop) | Win10 1903+; borderless = Win11 |
| WGC + synthetic shadow | ✅ | ⚠️ synthetic only | High | Medium | Win10 1903+ |
| PrintWindow(PW_RENDERFULLCONTENT) + mask | ⚠️ synthetic mask | ❌ / synthetic | High (works when occluded) | Low-Medium | Win8.1+ |
| BitBlt/CopyFromScreen + mask | ⚠️ synthetic mask | ❌ / synthetic | Medium (must be visible) | Low | Any |
| Desktop Duplication + recovery | ✅ via recovery | ✅ via recovery | High | High | Win8+ |
| WPF RenderTargetBitmap (in-proc) | ✅ (tree alpha) | ❌ | Only own WPF tree | Low | Any |
| DwmRegisterThumbnail | n/a | n/a | Cannot read back | — | — |

## Recommendations

**Primary recommendation (best fidelity, corners + real shadow): dual-background alpha recovery.**
1. Manifest the capture tool as PerMonitorV2 DPI-aware.
2. Bring the target window to the foreground; wait for animations; `DwmFlush`.
3. Read `DWMWA_EXTENDED_FRAME_BOUNDS`; inflate by a generous shadow margin (~40px) and clamp to the screen union.
4. Show a borderless backdrop window directly behind the target (`SetWindowPos` with `hWndInsertAfter = target`, `SWP_NOACTIVATE`), fill white, flush, `BitBlt` the rect; fill black, flush, `BitBlt` again.
5. Recover per-pixel alpha/color; auto-trim fully transparent edges; save straight-alpha PNG.
6. **Detection gate:** query `DWMWA_SYSTEMBACKDROP_TYPE`; if the target uses Mica/Acrylic, fall back to the secondary recommendation for correct color.

**Secondary recommendation (simplest, corners only): WGC.** If you don't need the real shadow, capture the single window with WGC + `B8G8R8A8UIntNormalized`, un-premultiply, save PNG — corners are transparent automatically. Add a synthetic Gaussian rounded-rect shadow if desired. Use a small C++/WinRT helper if the .NET Framework WinRT interop is painful.

**Tertiary (fallback for occluded/GPU content): `PrintWindow(PW_RENDERFULLCONTENT)` + synthetic rounded mask** at radius `round(8*dpi/96)`, plus optional synthetic shadow.

**In-process shortcut:** if your plugin owns or can safely mutate the host window and the host is WPF, `RenderTargetBitmap` of the relevant visual is the cleanest way to get alpha for your own widget; for the whole host frame, temporarily set `DWMWCP_DONOTROUND`, capture, restore, mask.

**Thresholds that change the recommendation:** need real shadow → dual-background; target uses Mica/Acrylic → WGC + synthetic (dual-background color math breaks); window can be occluded/off-screen → PrintWindow; capturing your own WPF widget → RenderTargetBitmap.

## Caveats
- **Premultiplied vs straight alpha when saving PNG:** WGC and the dual-background "black capture" both yield premultiplied color; PNG expects straight alpha — divide RGB by A (guarding A=0) or dark fringes/artifacts appear.
- **Mica/Acrylic** target backdrops break dual-background color recovery and make even WGC's captured colors depend on what's behind the window.
- **Animations, occlusion, and Z-order races** desync the two captures; always flush and ensure the target is foreground and unobstructed.
- **DPI:** wrong awareness → wrong coordinates, blurry grabs, wrong corner radius; `EXTENDED_FRAME_BOUNDS` is physical, `GetWindowRect` is virtualized.
- **No exact corner geometry API:** `GetWindowRgn` fails on Win11; synthetic masks approximate the 8px-logical radius but may be a pixel off versus DWM's actual curve.
- **WGC yellow border** appears unless removed (`IsBorderRequired=false`, Win11 + user consent + `graphicsCaptureWithoutBorder` capability) — otherwise it pollutes the capture edges.
- **`DWMWCP_DONOTROUND` on a third-party host** briefly alters its appearance; restore it promptly.
- **Premultiplied-alpha conclusion for WGC** is a high-confidence inference from DWM/DXGI conventions and observed behavior, not a single verbatim Microsoft spec line — verify empirically on your target build (22H2/23H2/24H2 behave the same for corners; 24H2 adds secondary-window capture and changed some Desktop-Duplication dirty-region behavior).
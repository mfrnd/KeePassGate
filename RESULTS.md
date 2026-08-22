# What was measured, and what was decided

Three methods were tried in order, each because the one before it was wrong. Everything below was
measured on the nine dialogs KeePassGate's documentation is written around, by generating the set
twice and comparing the files byte for byte, then locating the differing pixels.

| Method | Corners | Shadow | Background in the file | Verdict |
| --- | --- | --- | --- | --- |
| Screen copy over the window rectangle | the desktop shows through | included, blended | **yes, in every file** | wrong |
| `PrintWindow` with `PW_RENDERFULLCONTENT` | square and opaque | absent | none | shape had to be asserted |
| **Two backdrops, solved** | measured | measured | none | **chosen** |

## 1. A screen copy is not a picture of the window

The rectangle came from the accessibility tree and the bitmap was `Format32bppRgb`, which has no
alpha channel at all, so every pixel was opaque and every pixel was whatever the screen held.

Measured on the committed set: the bottom corners of `entry-access.png` were **165,165,164** and
**171,171,170**. The dialog's face is 240 and its title bar 243, so neither corner was the window;
they were the drop shadow blended over whatever was behind it. The two corners differing from each
other is the giveaway, a shadow gradient rather than a flat window pixel.

Between two runs of the same fixture, `entry-access.png` differed by **375 pixels**, all in the
bottom three rows.

## 2. `PrintWindow` removes the background and the shape with it

`PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)` asks the window to draw itself instead of reading the
screen. Measured against the screen copy on all nine dialogs:

- **the same size to the pixel**, so the accessibility rectangle and `GetWindowRect` agree here
- the DWM composited **caption renders correctly**
- the corner pixel comes back **243**, the window's own color, rather than 242, the blend
- alpha comes back valid and opaque, so no invisible-PNG trap
- none of the nine renders blank
- about **2.4%** of pixels differ from the screen copy, spread around the frame: the drop shadow and
  DWM's final blend, which it does not include

The corners are square, because rounding is the compositor's and a window draws itself square. So
the corners had to be put back, and putting them back meant writing down a radius of eight pixels
and an antialiasing rule that belong to Windows.

**That is what ruled it out.** A picture whose shape this repository asserts goes quietly wrong the
day Windows changes it, with nothing to catch it.

## 3. Two backdrops, solved

A pixel composited over a background is `color * alpha + background * (1 - alpha)`. Capture the
same window over black and over white, and the background is known at both ends:

```text
alpha = 1 - (white - black)
color = black / alpha
```

Solved per channel and averaged, because the three should agree and averaging keeps a rounding error
in one of them from showing as a colored fringe.

The backdrop must not take the focus: a window that loses it draws a different title bar.
`ShowWithoutActivation` and `WS_EX_NOACTIVATE` between them see to that, and `SetWindowPos` puts the
backdrop directly behind the target rather than merely somewhere below it.

Measured on the committed set: alpha now spans **0 to 255**, corners come back at **A=13**, and
about **3,150 pixels per image** carry partial alpha where before every pixel was opaque.

This is the method `capture-methods.md` recommends first, and the same one ShareX uses for its
"capture window with transparency".

## What else was nondeterministic, and what it was

Getting the background out was necessary and not sufficient. Three more differences survived, all
found the same way and none of them about the window's contents:

- **The path in the dialog's banner**, twice over: a session identifier and a `keepass-<identifier>`
  directory. KeePass puts the open database's path in the settings dialog's banner and six of the
  nine pictures are of that dialog.
- **The port**, taken free from the operating system and published by the status window.
- **Keyboard cues.** `profiles.png` differed by **66 pixels at every other x on one row**, which is a
  dotted focus rectangle, and later by **8 contiguous black pixels**, which is the underline under a
  button's letter. Windows hides both until somebody navigates by keyboard and shows them
  afterwards, so which a fixture catches is an accident of whether it last clicked or last typed.
  `WM_CHANGEUISTATE` with `UISF_HIDEFOCUS | UISF_HIDEACCEL` settles both.

## Where it landed

| | before | after |
| --- | --- | --- |
| identical between two runs | 3 of 9 | 5 of 9 |
| pixels from behind the window | every corner of every file | none |
| alpha | none, the format had no channel | measured, 0 to 255 |

The four that still differ do so only inside a value that is genuinely new each run: an authority
fingerprint and its two validity dates, two timestamps, an invitation code, and a fingerprint on the
approval dialog. Those are a question about seeding the database, not about capture.

## Not tried, and why

**Windows.Graphics.Capture** is the modern answer and `capture-methods.md` rates it second. It
returns the DWM composited surface, so Windows 11's rounded corners arrive already carrying alpha,
with no backdrop and no second exposure. It was not taken because it is WinRT with Direct3D 11
device interop from .NET Framework 4.8: a large amount of machinery and a new dependency surface for
a test fixture, against a project that does not take dependencies lightly. If the fixture ever needs
the shadow as well, note that a single-window capture excludes it, so that would not help either.

**Synthetic corners** are the manual masking approach in the survey. Rejected above.

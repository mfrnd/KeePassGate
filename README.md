# KeePassGate: how to screenshot one window

**Working out, kept for reference and never merged.** KeePassGate's documentation is written around
screenshots a fixture takes by driving a real KeePass. They are committed and published, so a
picture holding part of somebody's desktop publishes it, and a picture that changes on every
regeneration makes its own diff useless for seeing what changed.

This branch is taken from `master` and holds nothing else: no plugin, no tests, no design documents.
The mainline keeps the method that was chosen and the reason it was chosen; what is here is
everything that was ruled out and why.

## What was asked

Windows 11 rounds the corners of a top-level window when it composes the desktop. So a screen copy
over the window's rectangle is not a picture of the window: the corners hold whatever was behind it,
and so does the drop shadow around the frame. The question was how to get a file that holds the
window, nothing else, and the same bytes every time nothing on screen has changed.

## What is here

| | |
| --- | --- |
| [`RESULTS.md`](RESULTS.md) | what was measured on the real dialogs, and what was decided |
| [`capture-methods.md`](capture-methods.md) | a survey of every route Windows offers, gathered rather than measured |

## The short answer

**Capture the window twice over a backdrop of a known color, once black and once white, and solve
for the transparency.** A pixel composited over a background is `color * alpha + background * (1 -
alpha)`, so with the background known at both ends `alpha = 1 - (white - black)` and the color is
`black / alpha`. Nothing about the shape has to be written down, which is the whole point: a corner
radius asserted here would go quietly wrong the day Windows changed it.

`RESULTS.md` has the two that were tried first and what each measured.

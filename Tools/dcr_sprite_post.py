#!/usr/bin/env python3
"""
DCR sprite post-process and validator.

Reads RGBA PNGs out of ComfyUI, normalises subject value range, optionally
crops to content, re-quantises, and prints a per-sprite report so a bad
sprite is visible in seconds instead of at import time.

Originals are never modified: output goes to a sibling folder.

Why this exists: measured across test renders, roll-to-roll brightness
variance (median luminance 44 to 63) was as large as the entire gain from
rewriting the prompt tail. Prompting can shift the average but cannot make it
consistent, and inconsistent value range across a roster reads as some
monsters being mud and others fine, at random. This pass makes value range
deterministic.

Usage:
    python3 dcr_sprite_post.py IN_DIR [-o OUT_DIR] [options]
    python3 dcr_sprite_post.py IN_DIR --report-only     # measure, write nothing

Common options:
    --lo 45 --hi 235      target luminance floor / ceiling for the subject
    --median 115          target median subject luminance (0 disables the gamma)
    --dark 0.65           per-file "keep it dark" factor, see --dark-list
    --dark-list FILE      newline-separated name fragments to treat as dark
                          subjects (Umbral, Void, Shadow, ...): these are
                          normalised into a compressed, lower range so they
                          stay dark but still carry internal contrast
    --no-crop             skip crop-to-content (keep the model's framing)
    --margin 0.04         fraction of the subject's larger side kept as padding
    --colors 24           re-quantise after processing (0 = leave alone)
    --alpha 128           alpha threshold that defines "subject"
"""

import argparse
import os
import sys

try:
    import numpy as np
    from PIL import Image
except ImportError:
    sys.exit("Needs pillow and numpy:  pip install pillow numpy")


LUMA = (0.2126, 0.7152, 0.0722)


def luminance(rgb):
    return LUMA[0] * rgb[..., 0] + LUMA[1] * rgb[..., 1] + LUMA[2] * rgb[..., 2]


def measure(rgba, alpha_thresh):
    """Subject-only stats. Returns None when the image has no subject."""
    mask = rgba[..., 3] > alpha_thresh
    if mask.sum() < 4:
        return None
    lum = luminance(rgba[..., :3])[mask]
    ys, xs = np.nonzero(mask)
    h, w = rgba.shape[:2]
    bw = int(xs.max() - xs.min() + 1)
    bh = int(ys.max() - ys.min() + 1)
    colours = len(set(map(tuple, rgba[..., :3][mask].astype(int))))
    return {
        "median": float(np.median(lum)),
        "p10": float(np.percentile(lum, 10)),
        "p90": float(np.percentile(lum, 90)),
        "dark_share": float((lum < 60).mean()),
        "bbox": (bw, bh),
        "fill": float(bw * bh) / (w * h),
        "vfill": float(bh) / h,
        "colours": colours,
        "mask": mask,
    }


def normalise(rgba, mask, lo, hi, target_median=None):
    """Stretch subject luminance into [lo, hi], preserving hue.

    RGB channels are scaled together rather than per-channel so the sprite's
    colours do not shift; only its value range moves. The 2nd/98th percentiles
    anchor the stretch so a couple of stray bright pixels (an eye highlight)
    cannot flatten the whole sprite.
    """
    out = rgba.astype(np.float64).copy()
    lum = luminance(out[..., :3])
    sub = lum[mask]
    src_lo, src_hi = np.percentile(sub, 2), np.percentile(sub, 98)
    if src_hi - src_lo < 1.0:
        return rgba  # flat image, nothing meaningful to stretch
    scale = (hi - lo) / (src_hi - src_lo)
    rgb = np.clip((out[..., :3] - src_lo) * scale + lo, 0, 255)

    # A linear stretch fixes the RANGE but not the DISTRIBUTION: a subject whose
    # mass sits at the dark end (a bat, a rotting ghoul) still reads dark after
    # it, because only the two endpoints moved. A midtone gamma placed on the
    # median is what actually makes brightness consistent across a roster of
    # subjects the model draws at wildly different keys.
    if target_median is not None:
        norm = np.clip((rgb - lo) / max(hi - lo, 1e-6), 1e-4, 1.0)
        cur = float(np.median(luminance(norm[mask] * 255.0)) / 255.0)
        want = float(np.clip((target_median - lo) / max(hi - lo, 1e-6), 0.02, 0.98))
        if 0.01 < cur < 0.99:
            gamma = np.log(want) / np.log(cur)
            gamma = float(np.clip(gamma, 0.2, 5.0))  # keep it sane on extremes
            rgb = np.power(norm, gamma) * (hi - lo) + lo

    out[..., :3] = np.clip(rgb, 0, 255)
    out[~mask, :3] = rgba[~mask, :3]  # leave transparent pixels untouched
    return out.astype(np.uint8)


def crop_to_content(rgba, mask, margin, target_wh):
    """Crop to the subject, keep the target aspect, re-scale to target size.

    Recovers resolution the model wastes on empty canvas. Nearest sampling on
    the way back up keeps the pixels hard; the crop is computed on the mask so
    a soft alpha edge cannot drag the box outward.
    """
    tw, th = target_wh
    ys, xs = np.nonzero(mask)
    y0, y1, x0, x1 = int(ys.min()), int(ys.max()), int(xs.min()), int(xs.max())
    bw, bh = x1 - x0 + 1, y1 - y0 + 1
    pad = int(round(max(bw, bh) * margin))
    x0, x1 = x0 - pad, x1 + pad
    y0, y1 = y0 - pad, y1 + pad

    # Grow the shorter axis so the crop matches the target aspect exactly,
    # otherwise the rescale below would distort the sprite.
    cw, ch = x1 - x0 + 1, y1 - y0 + 1
    want = tw / th
    if cw / ch < want:
        need = int(round(ch * want)) - cw
        x0 -= need // 2
        x1 += need - need // 2
    else:
        need = int(round(cw / want)) - ch
        y0 -= need // 2
        y1 += need - need // 2

    h, w = mask.shape
    canvas = np.zeros((y1 - y0 + 1, x1 - x0 + 1, 4), dtype=np.uint8)
    sx0, sy0 = max(x0, 0), max(y0, 0)
    sx1, sy1 = min(x1, w - 1), min(y1, h - 1)
    canvas[sy0 - y0:sy1 - y0 + 1, sx0 - x0:sx1 - x0 + 1] = rgba[sy0:sy1 + 1, sx0:sx1 + 1]
    return np.asarray(
        Image.fromarray(canvas, "RGBA").resize((tw, th), Image.NEAREST))


def quantise(rgba, colours):
    """Re-quantise RGB while preserving the alpha channel exactly."""
    rgb = Image.fromarray(rgba[..., :3], "RGB")
    q = rgb.quantize(colors=colours, dither=Image.Dither.NONE).convert("RGB")
    out = np.dstack([np.asarray(q), rgba[..., 3]])
    return out.astype(np.uint8)


def is_dark_subject(name, fragments):
    low = name.lower()
    return any(f and f in low for f in fragments)


def main():
    ap = argparse.ArgumentParser(description="DCR sprite post-process and validator")
    ap.add_argument("in_dir")
    ap.add_argument("-o", "--out-dir", default=None)
    ap.add_argument("--lo", type=float, default=45.0)
    ap.add_argument("--hi", type=float, default=235.0)
    ap.add_argument("--median", type=float, default=115.0,
                    help="target median subject luminance (0 = range-only, no gamma)")
    ap.add_argument("--dark", type=float, default=0.65,
                    help="range multiplier applied to dark-list subjects")
    ap.add_argument("--dark-list", default=None)
    ap.add_argument("--no-crop", action="store_true")
    ap.add_argument("--margin", type=float, default=0.04)
    ap.add_argument("--colors", type=int, default=24)
    ap.add_argument("--alpha", type=int, default=128)
    ap.add_argument("--report-only", action="store_true")
    a = ap.parse_args()

    fragments = []
    if a.dark_list and os.path.isfile(a.dark_list):
        fragments = [l.strip().lower() for l in open(a.dark_list) if l.strip()]

    out_dir = a.out_dir or (a.in_dir.rstrip("/\\") + "_post")
    if not a.report_only:
        os.makedirs(out_dir, exist_ok=True)

    files = sorted(f for f in os.listdir(a.in_dir) if f.lower().endswith(".png"))
    if not files:
        sys.exit(f"No PNGs in {a.in_dir}")

    print(f"{'file':<34} {'size':>9}  {'median':>14}  {'p90':>13}  "
          f"{'<60':>11}  {'fill':>11}  cols  flags")
    print("-" * 118)

    warned = 0
    for fn in files:
        path = os.path.join(a.in_dir, fn)
        try:
            im = Image.open(path).convert("RGBA")
        except Exception as e:
            print(f"{fn:<34} !! unreadable: {e}")
            warned += 1
            continue

        rgba = np.asarray(im)
        before = measure(rgba, a.alpha)
        if before is None:
            print(f"{fn:<34} !! no subject (alpha empty) - check the matte")
            warned += 1
            continue

        target = im.size
        work = rgba
        mask = before["mask"]

        if not a.no_crop:
            work = crop_to_content(work, mask, a.margin, target)
            m2 = measure(work, a.alpha)
            mask = m2["mask"] if m2 else mask

        lo, hi = a.lo, a.hi
        dark = is_dark_subject(fn, fragments)
        if dark:
            # Keep dark subjects dark, but still give them a real internal
            # value spread: compress the target range toward the floor rather
            # than skipping normalisation, or they stay unreadable.
            hi = lo + (hi - lo) * a.dark
        tgt = None if a.median <= 0 else (lo + (a.median - a.lo) * (hi - lo) / max(a.hi - a.lo, 1e-6))
        work = normalise(work, mask, lo, hi, tgt)

        if a.colors > 0:
            work = quantise(work, a.colors)

        after = measure(work, a.alpha)

        flags = []
        if dark:
            flags.append("dark-list")
        if after and after["vfill"] < 0.55:
            flags.append("LOW-VFILL")
        if after and after["colours"] < 6:
            flags.append("FEW-COLOURS")
        if before["fill"] < 0.25:
            flags.append("TINY-SUBJECT")
        if flags and any(f.isupper() for f in flags):
            warned += 1

        print(f"{fn:<34} {im.size[0]:>4}x{im.size[1]:<4} "
              f"{before['median']:>6.0f} -> {after['median']:>4.0f}  "
              f"{before['p90']:>5.0f} -> {after['p90']:>4.0f}  "
              f"{100*before['dark_share']:>4.0f}% -> {100*after['dark_share']:>3.0f}%  "
              f"{100*before['fill']:>4.0f}% -> {100*after['fill']:>3.0f}%  "
              f"{after['colours']:>4}  {' '.join(flags)}")

        if not a.report_only:
            Image.fromarray(work, "RGBA").save(os.path.join(out_dir, fn))

    print("-" * 118)
    print(f"{len(files)} sprite(s); {warned} flagged."
          + ("" if a.report_only else f"  Written to {out_dir}"))


if __name__ == "__main__":
    main()

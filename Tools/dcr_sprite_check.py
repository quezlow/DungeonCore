#!/usr/bin/env python3
"""
DCR sprite artefact checker.

Flags the STRUCTURAL signatures that generated sprites produce and hand-drawn
pixel art almost never does. It does not judge whether a creature looks right --
that stays an eye call. It narrows where to look.

What it catches, and why each is a signal:

  ORPHANS      Pixel islands disconnected from the main body. Generators leave
               stray specks; a human drawing a 46x32 sprite does not.
  HOLES        Enclosed transparent regions inside the silhouette. Sometimes
               deliberate (a gap under a wing), often a matting failure.
  ASYMMETRY    For front/back views, left and right halves should roughly
               mirror. Large scores mean one side was drawn differently from
               the other -- the classic generated-art tell.
  TOPOLOGY     Component or hole count changing across an animation set means
               something appeared or vanished between frames.
  OUTLIERS     Colours far from the sprite's own palette clusters, present in
               only a few pixels. Usually resampling debris.
  EDGE NOISE   Single-pixel spurs on the silhouette boundary.

Usage:
    python3 dcr_sprite_check.py PATH [--symmetric] [--alpha 128] [-v]

PATH may be one image, a folder of frames, or a creature folder containing
direction subfolders. Frame sets are additionally checked for topology jumps.
"""

import argparse
import os
import sys

try:
    import numpy as np
    from PIL import Image
    from scipy import ndimage
except ImportError:
    sys.exit("Needs pillow, numpy and scipy:  pip install pillow numpy scipy")


def load_mask(path, alpha_thresh):
    im = Image.open(path).convert("RGBA")
    a = np.asarray(im)
    alpha = a[..., 3]
    if alpha.min() >= 250:                       # no real alpha: key the corner
        bg = a[0, 0, :3].astype(int)
        m = (np.abs(a[..., :3].astype(int) - bg).sum(axis=2) > 45)
    else:
        m = alpha > alpha_thresh
    return a, m


def orphans(mask, min_frac=0.02):
    """Components smaller than min_frac of the largest are suspect."""
    lab, n = ndimage.label(mask, structure=np.ones((3, 3)))
    if n <= 1:
        return 0, 0, n
    sizes = ndimage.sum(mask, lab, range(1, n + 1))
    big = sizes.max()
    small = [s for s in sizes if s < big * min_frac]
    return len(small), int(sum(small)), n


def holes(mask):
    """Transparent regions fully enclosed by subject."""
    inv = ~mask
    lab, n = ndimage.label(inv, structure=np.ones((3, 3)))
    if n == 0:
        return 0, 0
    border = set(lab[0, :]) | set(lab[-1, :]) | set(lab[:, 0]) | set(lab[:, -1])
    border.discard(0)
    enclosed = [i for i in range(1, n + 1) if i not in border]
    px = int(sum((lab == i).sum() for i in enclosed))
    return len(enclosed), px


def asymmetry(mask):
    """Mean mismatch between the silhouette and its mirror, about the centroid.

    Only meaningful for head-on views. Scored 0 (perfect mirror) to 1.
    """
    ys, xs = np.nonzero(mask)
    if len(xs) == 0:
        return 1.0
    sub = mask[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    best = 1.0
    for shift in (-1, 0, 1):                     # tolerate a 1px centring error
        s = np.roll(sub, shift, axis=1)
        mir = s[:, ::-1]
        inter = (s & mir).sum()
        union = (s | mir).sum()
        if union:
            best = min(best, 1.0 - inter / union)
    return best


def edge_spurs(mask):
    """Subject pixels attached to the body by a single neighbour."""
    nb = ndimage.convolve(mask.astype(int), np.ones((3, 3), int),
                          mode="constant", cval=0) - mask.astype(int)
    return int(((nb <= 1) & mask).sum())


def colour_outliers(a, mask, dist=90, max_px=4):
    """Rare colours far from every populous colour in the same sprite."""
    from collections import Counter
    cnt = Counter(map(tuple, a[..., :3][mask].astype(int)))
    if not cnt:
        return 0
    populous = np.array([c for c, n in cnt.items() if n >= 8], dtype=float)
    if len(populous) == 0:
        return 0
    bad = 0
    for c, n in cnt.items():
        if n > max_px:
            continue
        d = np.abs(populous - np.array(c, dtype=float)).sum(axis=1).min()
        if d > dist:
            bad += 1
    return bad


def check_one(path, alpha_thresh, symmetric):
    a, m = load_mask(path, alpha_thresh)
    n_orph, orph_px, n_comp = orphans(m)
    n_hole, hole_px = holes(m)
    spurs = edge_spurs(m)
    outl = colour_outliers(a, m)
    asym = asymmetry(m) if symmetric else None
    flags = []
    if n_orph:
        flags.append(f"ORPHANS({n_orph}/{orph_px}px)")
    if n_hole:
        flags.append(f"HOLES({n_hole}/{hole_px}px)")
    if spurs > max(3, 0.02 * m.sum()):
        flags.append(f"EDGE-NOISE({spurs})")
    if outl:
        flags.append(f"OUTLIER-COLS({outl})")
    if asym is not None and asym > 0.22:
        flags.append(f"ASYMMETRY({asym:.2f})")
    return dict(name=os.path.basename(path), comps=n_comp, holes=n_hole,
                spurs=spurs, asym=asym, flags=flags, area=int(m.sum()))


def main():
    ap = argparse.ArgumentParser(description="DCR sprite artefact checker")
    ap.add_argument("path")
    ap.add_argument("--symmetric", action="store_true",
                    help="score left/right mirror symmetry (front/back views only)")
    ap.add_argument("--alpha", type=int, default=128)
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    if os.path.isfile(args.path):
        targets = [(os.path.dirname(args.path) or ".", [os.path.basename(args.path)])]
    else:
        targets = []
        for root, _dirs, files in os.walk(args.path):
            pngs = sorted(f for f in files if f.lower().endswith(".png"))
            if pngs:
                targets.append((root, pngs))

    total_flagged = 0
    for root, pngs in targets:
        print(f"\n== {root}  ({len(pngs)} image(s))")
        hdr = f"{'file':<22}{'area':>6}{'comps':>7}{'holes':>7}{'spurs':>7}"
        if args.symmetric:
            hdr += f"{'asym':>7}"
        print(hdr + "  flags")
        results = []
        for f in pngs:
            r = check_one(os.path.join(root, f), args.alpha, args.symmetric)
            results.append(r)
            line = (f"{r['name']:<22}{r['area']:>6}{r['comps']:>7}"
                    f"{r['holes']:>7}{r['spurs']:>7}")
            if args.symmetric:
                line += f"{r['asym']:>7.2f}"
            print(line + "  " + " ".join(r["flags"]))
            if r["flags"]:
                total_flagged += 1

        # Topology stability across a frame set: counts should not jump about.
        if len(results) > 2:
            comps = [r["comps"] for r in results]
            hol = [r["holes"] for r in results]
            areas = [r["area"] for r in results]
            if max(comps) - min(comps) > 1:
                print(f"   !! TOPOLOGY JUMP: component count varies {min(comps)}-{max(comps)} "
                      f"across frames (something appears/vanishes)")
            if max(hol) - min(hol) > 1:
                print(f"   !! HOLE COUNT varies {min(hol)}-{max(hol)} across frames")
            spread = (max(areas) - min(areas)) / max(np.mean(areas), 1)
            print(f"   frame area spread: {100*spread:.0f}% of mean "
                  f"({'expected for a flap' if spread > 0.15 else 'low - little motion'})")

    print(f"\n{total_flagged} image(s) carry at least one flag. "
          f"Flags mark candidates for review, not verdicts.")


if __name__ == "__main__":
    main()

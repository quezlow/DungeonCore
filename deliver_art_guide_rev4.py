#!/usr/bin/env python3
"""Art Authoring Guide Rev 4 delivery -- Dungeon Core: Rebirth.

Adds the surface camp art set and the findings of a full hidden-sprite sweep:

  3d. Surface camps (22) -- every prefab slot in SurfaceZoneProfile.campTiers is
      {fileID: 0}: commerce anchors (cart/stall/shop), framing and ruin variants,
      three tent variants, palisade segment + corner, the four resource node stubs,
      and the city gate. Profile prefab slots are unruled by Audit Art Debt, so
      this guide is their only ledger.
  3e. Den remains marker (1) -- DenTunnelProfile.remainsMarkerPrefab is null; the
      code is null-safe but Commands surfaces the gap because a robbed remains
      leaves no visible hole without it (canon 42's legibility rule).
  6.  Register additions -- slots the sweep confirmed intentionally null, recorded
      so no future chat re-flags them: FlagInteractable.portrait (optional
      narration field, 21 nulls), PartyBannerPrefab bar renderer (colour-index
      tinted), LootPolicyPanelRowPrefab row Image (flat colour).

Four anchored edits, count == 1 each, staged in memory, against the guide at HEAD
(Rev 3). Checkbox ids: 46 new mints (dcr-art-v1-camp-*, -node-*, -denremains-*);
existing ids untouched, so ticked state survives.

Usage: python deliver_art_guide_rev4.py [repo_path]
"""
import os, sys

SUMMARY_ANCHOR = '<details><summary>3. Objects and props -- 24 icons</summary>'
SUMMARY_NEW = '<details><summary>3. Objects and props -- 24 icons + 23 world props</summary>'

JUNCTION_ANCHOR = ('a banner scrap pinned to it, front orthographic view</code></div></div>'
                   '</div></details>\n<details><summary>4. UI emblems')

REV3_TAIL = 'ticked state survives.</div>'
REV4_LINE = ('<br>\nRev 4 (2026-08-15): chapters 3d (surface camps, 22 -- every campTiers prefab slot on '
             'SurfaceZoneProfile is empty; profile prefab slots are unruled by Audit Art Debt, this guide is '
             'their ledger) and 3e (den remains marker) added after a full serialized-sprite and null-renderer '
             'sweep; the sweep\'s intentionally-null findings recorded in chapter 6. Checkbox ids unchanged; '
             'ticked state survives.</div>')

REGISTER_ANCHOR = ('<li><code>Definition icons | DivineAudienceScript.deities.backdrop</code>: 6</li></ul>')
REGISTER_ADD = ('<li><code>FlagInteractable.portrait</code>: 21 -- optional narration field; prologue props '
                'narrate portrait-less by design</li>'
                '<li><code>PartyBannerPrefab bar SpriteRenderer</code>: 1 -- colour-index tinted, no sprite '
                'wanted</li>'
                '<li><code>LootPolicyPanelRowPrefab row Image</code>: 1 -- flat colour row background</li></ul>'
                '<p class="small">The three rows above were confirmed intentional by the Rev 4 sweep of every '
                'serialized Sprite field and null SpriteRenderer in the project; they are recorded here so the '
                'next sweep does not re-open them.</p>')

# name, inspector/asset slot, purpose, prompt, target
CAMP_ITEMS = [
 ("Camp_Cart", "campTiers[Waystation].commercePrefab", "Waystation commerce anchor; the lone cart that got wind of a new dungeon. Faces the way home; the merchant's eventual dock.",
  "a wooden travelling cart with a canvas cover and a small goods crate, front orthographic view", "56x44 (object)"),
 ("Camp_Stall", "campTiers[Camp].commercePrefab", "Camp commerce anchor.",
  "a wooden market stall with a striped awning and counter goods, front orthographic view", "60x48 (object)"),
 ("Camp_Shop", "campTiers[Settlement].commercePrefab", "Settlement commerce anchor.",
  "a small timber shop with a plank sign and a shuttered window, front orthographic view", "62x62 (object, large)"),
 ("Camp_Stall_Framing", "campTiers[Camp].framingCommercePrefab", "Construction-site stall; rises beside the cart at 70 percent of the Camp threshold.",
  "a half-built market stall, bare post frame and rolled awning, a sawhorse beside it, front orthographic view", "60x48 (object)"),
 ("Camp_Shop_Framing", "campTiers[Settlement].framingCommercePrefab", "Construction-site shop; rises beside the stall approaching Settlement.",
  "a half-built timber shop, wall studs and roof beams open to the sky, a ladder against the frame, front orthographic view", "62x62 (object, large)"),
 ("Camp_Cart_Ruin", "campTiers[Waystation].ruinCommercePrefab", "Raid-displacement ruin; takes the anchor spot.",
  "a wrecked travelling cart, snapped axle, scattered boards and torn canvas, front orthographic view", "56x40 (object)"),
 ("Camp_Stall_Ruin", "campTiers[Camp].ruinCommercePrefab", "Raid-displacement ruin.",
  "a collapsed market stall, fallen awning and broken counter, front orthographic view", "60x40 (object)"),
 ("Camp_Shop_Ruin", "campTiers[Settlement].ruinCommercePrefab", "Raid-displacement ruin.",
  "a burnt-out timber shop, charred beams and a fallen sign, front orthographic view", "62x56 (object, large)"),
 ("Camp_Tent_A", "campTiers[Camp/Settlement].props", "Camp prop; Settlement re-uses at higher count.",
  "a small canvas ridge tent with guy ropes, front orthographic view", "48x40 (object)"),
 ("Camp_Tent_B", "campTiers[Camp/Settlement].props", "Second tent variant for visual density.",
  "a patched round-topped canvas tent with a lantern pole, front orthographic view", "46x42 (object)"),
 ("Camp_Tent_C", "campTiers[Settlement].props", "Third tent variant, Settlement density.",
  "a lean-to canvas tent against a timber frame, a bedroll visible inside, front orthographic view", "48x36 (object)"),
 ("Camp_Tent_Framing", "campTiers[*].framingProps (shared by all tent variants)", "One framing look shared across the tent variants; a bare frame reads alike for all three.",
  "a bare tent frame of lashed poles with a rolled canvas bundle at its foot, front orthographic view", "46x38 (object)"),
 ("Camp_Tent_Ruin", "campTiers[*].ruinProps (shared by all tent variants)", "One ruin look shared across the tent variants.",
  "a collapsed torn tent, canvas lying flat over broken poles, front orthographic view", "48x28 (object, low)"),
 ("Camp_Palisade_Segment", "campTiers[Settlement].props", "Settlement palisade piece.",
  "a straight palisade wall segment of sharpened timber stakes, front orthographic view", "56x36 (object)"),
 ("Camp_Palisade_Corner", "campTiers[Settlement].props", "Right-angle piece, in case position hashing reads gap-toothed with segments alone.",
  "a right-angled palisade corner of sharpened timber stakes, front orthographic view", "48x40 (object)"),
 ("Camp_Palisade_Framing", "campTiers[Settlement].framingProps (shared by segment and corner)", "Shared framing look for both palisade pieces.",
  "a row of freshly set palisade posts at half height with an earth mound at their base, front orthographic view", "56x28 (object, low)"),
 ("Camp_Palisade_Ruin", "campTiers[Settlement].ruinProps (shared by segment and corner)", "Shared ruin look for both palisade pieces.",
  "a breached palisade, splintered leaning stakes around a burnt gap, front orthographic view", "56x32 (object)"),
 ("Node_Wood", "nodeTypes[node.wood].stubPrefab", "Wood resource node stub.",
  "a log pile beside a fresh tree stump with an axe struck in it, front orthographic view", "44x32 (object, low)"),
 ("Node_Stone", "nodeTypes[node.stone].stubPrefab", "Stone resource node stub.",
  "a grey rock outcrop with quarried faces and loose chips, front orthographic view", "44x32 (object, low)"),
 ("Node_Herb", "nodeTypes[node.herb].stubPrefab", "Herb resource node stub.",
  "a patch of leafy herbs with small white flowers among stones, front orthographic view", "40x28 (object, low)"),
 ("Node_Exotic", "nodeTypes[node.exotic].stubPrefab", "Exotic resource node stub.",
  "a cluster of pale glowing growths on dark moss with faint light motes, front orthographic view", "40x32 (object, low)"),
 ("Camp_CityGate", "SurfaceZoneProfile.gatePrefab", "The city gate at the road's outer edge; currently an invisible trigger volume.",
  "a timber city gate with two watch posts spanning a dirt road, doors standing open, front orthographic view", "64x60 (object, large)"),
]

REMAINS_ITEM = ("DenRemainsMarker", "DenTunnelProfile.remainsMarkerPrefab",
 "Stands in every robbed den remains the player can see; without it the loss has no visible hole (canon 42). Code is null-safe; Commands flags the unassigned slot.",
 "a picked-over bone pile in a shallow scrape of earth, gnaw marks on the bones, front orthographic view", "36x24 (floor, flat)")

PREFIX = 'dcr-art-v1'


def one_item(name, slot, purpose, prompt, target):
    sid = name.lower().replace('_', '-')
    did = PREFIX + '-' + sid + '-d'
    rid = PREFIX + '-' + sid + '-r'
    return ('<div class="item"><div class="boxes">'
            '<span class="box"><input type="checkbox" id="' + did + '"><label for="' + did + '">Done</label></span>'
            '<span class="box"><input type="checkbox" id="' + rid + '"><label for="' + rid + '">Revisit</label></span>'
            '</div><div class="meta"><b>' + name + '</b>'
            '<span class="path">' + slot + '</span>'
            '<span class="purpose">' + purpose + '</span>'
            '<span class="target">target ' + target + '</span>'
            '<code class="slot">' + prompt + '</code>'
            '</div></div>')


def build_3d_3e():
    parts = ['<h3>3d. Surface camps (22) -- canon 25 tiers, canon 26 ruins</h3>'
             '<p class="small">Every prefab slot in <code>SurfaceZoneProfile.campTiers</code> is empty, plus '
             'the four node stubs and the city gate -- the camps currently grow as invisible markers. Profile '
             'prefab slots are <b>unruled by Audit Art Debt</b>; this section is their whole ledger. Object '
             'contract (1b). Sprites here become prefabs Brad wires into the profile; the slot column names '
             'the destination. No Waystation framing exists: growth can never approach tier 0, so its framing '
             'row is unreachable by construction. Framing and ruin looks are shared across tent variants and '
             'across palisade pieces -- the arrays are per-prop, so the shared sprite simply rides in more '
             'than one prefab.</p>']
    for it in CAMP_ITEMS:
        parts.append(one_item(*it))
    parts.append('<h3>3e. Den remains marker (1)</h3>')
    parts.append(one_item(*REMAINS_ITEM))
    return ''.join(parts)


def find_repo():
    if len(sys.argv) > 1:
        return sys.argv[1]
    env = os.environ.get('DCR_REPO')
    if env:
        return env
    d = os.getcwd()
    while True:
        if os.path.isdir(os.path.join(d, 'Assets')) and os.path.isfile(os.path.join(d, 'Docs', 'ART_DEBT.md')):
            return d
        nd = os.path.dirname(d)
        if nd == d:
            break
        d = nd
    sys.exit('ABORT: repo not found. Pass the path or set DCR_REPO.')


def main():
    repo = find_repo()
    path = os.path.join(repo, 'Docs', 'DCR_Guide_Art_Authoring.html')
    raw = open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    if bom:
        raw = raw[3:]
    text = raw.decode('utf-8')
    crlf = '\r\n' in text
    text = text.replace('\r\n', '\n')

    # ---- idempotency guard ----
    if 'Rev 4 (2026-08-15)' in text or '3d. Surface camps' in text:
        sys.exit('ABORT: guide already carries Rev 4 -- delivery already applied.')
    if 'Rev 3 (2026-08-15)' not in text:
        sys.exit('ABORT: guide is not at Rev 3 -- apply deliver_art_guide_rev3.py first. Nothing written.')

    # ---- anchor assertions before any write ----
    anchors = (('summary', SUMMARY_ANCHOR), ('junction', JUNCTION_ANCHOR),
               ('rev tail', REV3_TAIL), ('register', REGISTER_ANCHOR))
    for label, a in anchors:
        n = text.count(a)
        if n != 1:
            sys.exit('ABORT: ' + label + ' anchor count == ' + str(n)
                     + ' (expected 1). Guide moved; re-anchor. Nothing written.')

    body = build_3d_3e()
    for ch in body + REV4_LINE + REGISTER_ADD:
        if ord(ch) > 127:
            sys.exit('ABORT: non-ASCII in inserted text; nothing written.')

    # ---- stage all edits ----
    new = text.replace(SUMMARY_ANCHOR, SUMMARY_NEW, 1)
    # The junction's final </div> closes the chapter's .body padding div; 3d/3e go
    # inside it, so the new sections keep the same padding as 3a-3c.
    new = new.replace(JUNCTION_ANCHOR,
                      JUNCTION_ANCHOR.replace('</div></details>\n<details><summary>4. UI emblems',
                                              body + '</div></details>\n<details><summary>4. UI emblems'),
                      1)
    new = new.replace(REV3_TAIL, REV3_TAIL[:-len('</div>')] + REV4_LINE, 1)
    new = new.replace(REGISTER_ANCHOR, REGISTER_ANCHOR[:-len('</ul>')] + REGISTER_ADD, 1)

    # ---- validate staged result ----
    import re
    ids = re.findall(r'input type="checkbox" id="([^"]+)"', new)
    if len(ids) != len(set(ids)):
        sys.exit('ABORT: duplicate checkbox ids in staged result; nothing written.')
    if len(ids) != len(re.findall(r'input type="checkbox"', text)) + 46:
        sys.exit('ABORT: staged checkbox count wrong; nothing written.')
    labels = re.findall(r'<label for="([^"]+)"', new)
    if sorted(ids) != sorted(labels):
        sys.exit('ABORT: label/id parity broken; nothing written.')
    if new.count('class="target"') != text.count('class="target"') + 23:
        sys.exit('ABORT: target-line delta wrong; nothing written.')
    for tag in ('div', 'details', 'span', 'code', 'h3', 'p', 'ul', 'li'):
        do = len(re.findall('<' + tag + '(?=[ >])', new)) - len(re.findall('<' + tag + '(?=[ >])', text))
        dc = new.count('</' + tag + '>') - text.count('</' + tag + '>')
        if do != dc:
            sys.exit('ABORT: ' + tag + ' balance delta mismatch; nothing written.')

    # ---- write, then report ----
    out = new
    if crlf:
        out = out.replace('\n', '\r\n')
    data = out.encode('utf-8')
    if bom:
        data = b'\xef\xbb\xbf' + data
    with open(path, 'wb') as f:
        f.write(data)

    sys.stdout.write('APPLIED\n')
    sys.stdout.write('  ~ Docs/DCR_Guide_Art_Authoring.html Rev 3 -> Rev 4\n')
    sys.stdout.write('    3d: 22 surface camp sprites (anchors, framing, ruins, tents x3, palisade + corner, 4 nodes, gate)\n')
    sys.stdout.write('    3e: den remains marker\n')
    sys.stdout.write('    ch6: 3 sweep-confirmed intentional nulls registered\n')
    sys.stdout.write('    46 new checkboxes; existing ids untouched\n')


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""
apply_cast_tab_button.py -- the CAST tab becomes a real scene button.

  EDIT  Assets/Scripts/UI/ActionBarHUD.cs   (serialized castTabButton; clone removed)
  EDIT  Docs/DESIGN_CANON.md                (entry 38 access paragraph)

The runtime clone existed to spare a scene edit. That trade is off now that
the spell row is a designed panel: the tab is a fifth Button assigned in the
Inspector like the other four, so it can be placed, styled and sized rather
than inheriting whatever Summon happens to look like.

NO SILENT FALLBACK. The clone is not kept as a "if unassigned, clone one"
path -- "empty means clone one for me" is indistinguishable in the Inspector
from "not filled in yet", which is the ambiguous-default trap this project
has a standing rule against. Unassigned is a NAMED fault from
ValidateSpellRowWiring instead.

Run:  python3 apply_cast_tab_button.py [path-to-repo-root]
"""

import os
import re
import sys

FAILURES = []


class Doc:
    def __init__(self, path):
        with open(path, "rb") as f:
            raw = f.read()
        self.path = path
        self.bom = raw.startswith(b"\xef\xbb\xbf")
        if self.bom:
            raw = raw[3:]
        self.crlf = b"\r\n" in raw
        self.text = raw.replace(b"\r\n", b"\n").decode("utf-8")

    def to_bytes(self):
        out = self.text.encode("utf-8")
        if self.crlf:
            out = out.replace(b"\n", b"\r\n")
        if self.bom:
            out = b"\xef\xbb\xbf" + out
        return out


def sub_once(doc, old, new, label):
    n = doc.text.count(old)
    if n != 1:
        FAILURES.append("%s: anchor count %d (expected 1) in %s" % (label, n, doc.path))
        return
    doc.text = doc.text.replace(old, new, 1)


def check_no_duplicate_members(doc, names, label):
    for name in names:
        hits = re.findall(
            r"^\s*(?:\[[^\]]*\]\s*)*(?:public|private|protected|internal)\s+"
            r"(?:static\s+|virtual\s+|override\s+|sealed\s+|async\s+)*"
            r"[\w<>,\[\]\.\?() ]+\s+" + re.escape(name) + r"\s*\(",
            doc.text, re.M)
        if len(hits) != 1:
            FAILURES.append("%s: '%s' defined %d times in %s (expected 1)"
                            % (label, name, len(hits), doc.path))


def check_fields(doc, names, label):
    for name in names:
        if not re.search(
                r"^\s*(?:\[[^\]]*\]\s*)*(?:public|private|protected|internal)\s+"
                r"(?:readonly\s+)?[\w<>,\[\]\.\?() ]+\s+" + re.escape(name)
                + r"\s*(?:=|;|=>)", doc.text, re.M):
            FAILURES.append("%s: '%s' used but never declared in %s"
                            % (label, name, doc.path))


def main():
    root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else ".")

    def P(rel):
        return os.path.join(root, rel.replace("/", os.sep))

    hud_rel = "Assets/Scripts/UI/ActionBarHUD.cs"
    canon_rel = "Docs/DESIGN_CANON.md"

    for rel in (hud_rel, canon_rel):
        if not os.path.isfile(P(rel)):
            FAILURES.append("missing target: " + rel)
    if FAILURES:
        die()

    hud = Doc(P(hud_rel))
    if "EnsureCastTab" not in hud.text:
        FAILURES.append("IDEMPOTENCY: the cast tab is already a serialized button.")
    if "spellEntryContainer" not in hud.text:
        FAILURES.append("PRECONDITION: the spell row is not merged in -- run "
                        "apply_spell_row_merge.py first.")
    if FAILURES:
        die()

    canon = Doc(P(canon_rel))

    # -- The field joins its four siblings ---------------------------------
    sub_once(hud,
             "    [SerializeField] private Button summonTabButton;\n"
             "\n"
             "    // Not serialized: cloned from a sibling at runtime by EnsureCastTab.\n"
             "    private Button castTabButton;",
             "    [SerializeField] private Button summonTabButton;\n"
             "\n"
             "    [Tooltip(\"The CAST tab (canon 38). Hidden by RefreshCastTabVisibility until \" +\n"
             "             \"the core holds any working, so it may be left active in the scene.\")]\n"
             "    [SerializeField] private Button castTabButton;",
             "castTabButton serialized")

    sub_once(hud,
             "        summonTabButton?.onClick.AddListener(OnSummonTabClicked);",
             "        summonTabButton?.onClick.AddListener(OnSummonTabClicked);\n"
             "        castTabButton?.onClick.AddListener(OnCastTabClicked);",
             "Start wires cast tab")

    # -- The clone goes ----------------------------------------------------
    sub_once(hud,
             "        EnsureCastTab();\n"
             "        SpellBook.OnRosterChanged += HandleSpellRosterChanged;",
             "        SpellBook.OnRosterChanged += HandleSpellRosterChanged;",
             "drop EnsureCastTab call")

    sub_once(hud,
             "    /// <summary>\n"
             "    /// The CAST tab is CLONED from an existing tab at runtime rather than added\n"
             "    /// to the scene. The tab row is a HorizontalLayoutGroup, and the four tab\n"
             "    /// Buttons carry no persistent onClick calls (verified against\n"
             "    /// Dungeon_Level_0 -- every listener is wired here in Start), so a clone\n"
             "    /// arrives inert and takes only the listener given to it. A scene edit\n"
             "    /// would be a manual step, and a forgotten manual step means the feature\n"
             "    /// simply is not on the bar -- the same failure the Wall entry above dodges.\n"
             "    /// </summary>\n"
             "    private void EnsureCastTab()\n"
             "    {\n"
             "        if (castTabButton != null) return;\n"
             "        var donor = summonTabButton != null ? summonTabButton : mineTabButton;\n"
             "        if (donor == null || donor.transform.parent == null) return;\n"
             "\n"
             "        castTabButton = Instantiate(donor, donor.transform.parent);\n"
             "        castTabButton.name = \"CastTab\";\n"
             "        castTabButton.onClick.RemoveAllListeners();   // defensive: a future Inspector wiring\n"
             "        castTabButton.onClick.AddListener(OnCastTabClicked);\n"
             "        castTabButton.gameObject.SetActive(true);\n"
             "    }\n"
             "\n",
             "",
             "remove EnsureCastTab")

    # -- Visibility comment now describes a scene button -------------------
    sub_once(hud,
             "    /// <summary>The tab appears once the core holds ANY working -- not once the\n"
             "    /// Sorcery trunk is researched. A god's grant at a tier-up must be castable\n"
             "    /// by a core that never took the trunk, or the audience hands over a power\n"
             "    /// with no way to reach it.</summary>",
             "    /// <summary>The tab appears once the core holds ANY working -- not once the\n"
             "    /// Sorcery trunk is researched. A god's grant at a tier-up must be castable\n"
             "    /// by a core that never took the trunk, or the audience hands over a power\n"
             "    /// with no way to reach it.\n"
             "    ///\n"
             "    /// This drives the button's active state, so leave it ACTIVE in the scene --\n"
             "    /// it is switched off here on the first call and back on when a working\n"
             "    /// arrives. A tab left inactive in the scene still works; it simply will not\n"
             "    /// be seen until the first roster change.</summary>",
             "visibility comment")

    # -- The validator covers the new slot ---------------------------------
    sub_once(hud,
             "        var faults = new List<string>();\n"
             "        if (spellEntryContainer == null)\n"
             "            faults.Add(\"spellEntryContainer is not assigned -- the spell row can never show.\");",
             "        var faults = new List<string>();\n"
             "        if (castTabButton == null)\n"
             "            faults.Add(\"castTabButton is not assigned -- there is no CAST tab on the bar. \"\n"
             "                     + \"The hotkey still works; the button does not exist.\");\n"
             "        if (spellEntryContainer == null)\n"
             "            faults.Add(\"spellEntryContainer is not assigned -- the spell row can never show.\");",
             "validator covers cast tab")

    # -- Header comment ----------------------------------------------------
    sub_once(hud,
             "///   Cast   [Q] — toggles the spell row (canon 38). A FIFTH tab, cloned from a sibling\n"
             "///                at runtime, hidden until the core holds any working. Its row is a\n"
             "///                third sub-menu built exactly like Mine's, off the same entry prefab.",
             "///   Cast   [Q] — toggles the spell row (canon 38). A FIFTH tab, assigned in the\n"
             "///                Inspector like the other four and hidden until the core holds any\n"
             "///                working. Its row is a third sub-menu built exactly like Mine's,\n"
             "///                off the same entry prefab.",
             "header cast tab note")

    # ======================================================================
    # Canon
    # ======================================================================
    sub_once(canon,
             "fifth CAST tab is CLONED from an existing tab button at runtime rather than\n"
             "added to the scene (the four tab Buttons carry no persistent onClick calls),\n"
             "so the whole feature costs one component drop and no Inspector wiring. The\n"
             "tab lights on `SpellBook.AnySpellKnown` rather than on the trunk node, so a\n"
             "god's grant is reachable by a core that never researched the trunk.",
             "fifth CAST tab is a scene Button assigned in the Inspector beside the other\n"
             "four. It was a runtime clone of a sibling tab at first, to spare a scene\n"
             "edit; that trade came off once the spell row became a designed panel, since\n"
             "a tab that can be placed and styled beats one inheriting whatever Summon\n"
             "happens to look like. There is deliberately NO clone-if-unassigned fallback\n"
             "-- \"empty means make one for me\" reads in the Inspector exactly like \"not\n"
             "filled in yet\", which is the ambiguous-default trap; an unassigned tab is a\n"
             "named fault from `ValidateSpellRowWiring` instead. The tab lights on\n"
             "`SpellBook.AnySpellKnown` rather than on the trunk node, so a god's grant is\n"
             "reachable by a core that never researched the trunk, and its visibility is\n"
             "driven from code -- leave the button ACTIVE in the scene.",
             "canon access paragraph")

    # ======================================================================
    # Validate before writing.
    # ======================================================================
    check_no_duplicate_members(hud, [
        "OnCastTabClicked", "RefreshCastTabVisibility", "ValidateSpellRowWiring",
        "HandleSpellRosterChanged", "RefreshShortcutLabels", "UpdateTabHighlights",
    ], "ActionBarHUD")
    check_fields(hud, ["castTabButton", "spellEntryContainer", "spellDetailLabel"],
                 "ActionBarHUD")

    if "EnsureCastTab" in hud.text:
        FAILURES.append("EnsureCastTab survived removal in " + hud_rel)
    if "Instantiate(donor" in hud.text:
        FAILURES.append("the tab clone survived removal in " + hud_rel)

    for o, c in (("{", "}"), ("(", ")"), ("[", "]")):
        if hud.text.count(o) != hud.text.count(c):
            FAILURES.append("%s: unbalanced %s%s" % (hud_rel, o, c))

    if FAILURES:
        die()

    for path, blob in ((P(hud_rel), hud.to_bytes()), (P(canon_rel), canon.to_bytes())):
        with open(path, "wb") as f:
            f.write(blob)

    print("apply_cast_tab_button: OK\n"
          "  EDIT: " + hud_rel + "\n"
          "  EDIT: " + canon_rel + "\n"
          "\n"
          "  In Unity: add a fifth Button to the tab row (duplicate SUMMON is the\n"
          "  quickest start), clear any onClick entries the duplicate carried, and\n"
          "  assign it to ActionBarHUD -> Cast Tab Button. Leave it ACTIVE -- the\n"
          "  component hides it until the core holds a working. The label text is\n"
          "  written for you as \"CAST (Q)\".\n"
          "\n"
          "  Then: Commands -> Validate Spell Picker Wiring.")


def die():
    sys.stderr.write("apply_cast_tab_button: ABORTED -- tree untouched.\n")
    for f in FAILURES:
        sys.stderr.write("  " + f + "\n")
    sys.exit(1)


if __name__ == "__main__":
    main()

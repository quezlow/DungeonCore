using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Validates every hand-authored site plan on an AncientSiteProfile WITHOUT
/// entering play mode or generating a floor.
///
/// This exists because the constraint that governs site geometry is invisible
/// while you are drawing: a wall's rendered face is TWO cells tall and drapes
/// over the open floor south of it, and those cells are solid to the
/// pathfinder. A room can therefore look perfectly enterable in the text file
/// and behave as a wall in game. Worse, the drape is always in world +Y, so a
/// plan can be fine on one quarter turn and impassable on another.
///
/// The validator applies the real rule to all eight orientations and reports
/// the WORST case, which is the only number that matters -- the generator will
/// eventually roll that orientation.
/// </summary>
public static class SitePlanValidator
{
    /// <summary>Mirrors AncientSiteBuilder.MinWalkableCells. If that constant
    /// moves, move this one.</summary>
    private const int MinWalkableCells = 16;

    [MenuItem("Dungeon Core/Validate Site Plans")]
    public static void Validate()
    {
        var profiles = new List<AncientSiteProfile>();
        foreach (var guid in AssetDatabase.FindAssets("t:AncientSiteProfile"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var p = AssetDatabase.LoadAssetAtPath<AncientSiteProfile>(path);
            if (p != null) profiles.Add(p);
        }

        if (profiles.Count == 0)
        {
            Debug.LogWarning("[SitePlanValidator] No AncientSiteProfile asset found.");
            return;
        }

        int totalPlans = 0, failures = 0;

        // Names rather than a count: the roll-call is a to-do list for
        // annotation, and a number tells you there is work without telling you
        // where.
        //
        // METHOD scope, beside the counters it is printed with. It began inside
        // the per-profile loop, where it did not compile -- and would have
        // under-reported even if it had, because the roll-call prints once for
        // the whole run while plans can span more than one profile.
        var unmarked = new List<string>();
        var sb = new StringBuilder();

        foreach (var profile in profiles)
        {
            var plans = profile.GetAuthoredPlans();

            // Decor entries: each must name a real authored plan, and that plan
            // must be @rotate: no -- a decor prefab does not rotate with the
            // site, so a rotatable decorated plan WILL misalign on three of four
            // quarter turns. Checked before the walkability loop so a decor
            // failure is visible even when every plan passes.
            if (profile.SiteDecor != null)
                foreach (var entry in profile.SiteDecor)
                {
                    if (entry == null || entry.prefab == null) continue;
                    AuthoredSitePlan match = null;
                    foreach (var p in plans)
                        if (p != null && p.name == entry.planName) { match = p; break; }
                    if (match == null)
                    {
                        failures++;
                        sb.Append("  DECOR FAIL: entry '").Append(entry.planName)
                          .Append("' names no authored plan on this profile.\n");
                        continue;
                    }
                    if (match.allowRotation)
                    {
                        failures++;
                        sb.Append("  DECOR FAIL: '").Append(entry.planName)
                          .Append("' allows rotation; a decorated plan must be @rotate: no.\n");
                    }
                }
            sb.Append("\n=== ").Append(profile.name).Append(" -- ")
              .Append(plans.Count).Append(" authored plan(s) ===\n");

            if (plans.Count == 0)
            {
                sb.Append("  (none assigned; all sites will use the procedural recipes)\n");
                continue;
            }

            foreach (var plan in plans)
            {
                totalPlans++;

                Debug.Log($"PLAN LOADED: '{plan.sourceName}' floor={plan.floor.Count} wall={plan.wall.Count}");


                // THE LANE. Checked by PATHFIND rather than by width, because
                // width is a proxy and the thing that matters is whether a
                // walker can actually get from one door to another through the
                // lane in every orientation this plan is allowed to take.
                if (plan.lane.Count > 0)
                {
                    var laneDoors = new List<DoorRun>();
                    var laneSet = new HashSet<Vector2Int>(plan.lane);
                    foreach (var dr in plan.doorRuns)
                        if (TouchesLane(dr, laneSet)) laneDoors.Add(dr);

                    if (LanePieces(laneSet) != 1)
                    {
                        failures++;
                        sb.Append("  LANE FAIL: '").Append(plan.sourceName)
                          .Append("' has a lane in ").Append(LanePieces(laneSet))
                          .Append(" pieces. A road is a path, not an archipelago.\n");
                    }
                    else if (laneDoors.Count < 2)
                    {
                        sb.Append("  lane warn: '").Append(plan.sourceName)
                          .Append("' has a lane reaching ").Append(laneDoors.Count)
                          .Append(" door(s). It is decorative floor until it ")
                          .Append("reaches two -- no road will thread it.\n");
                    }
                    else
                    {
                        // Every orientation this plan may take, and no more.
                        // Failing a @rotate: no vault for a quarter turn it never
                        // takes is the false positive that retired the first gate.
                        int rots = plan.allowRotation ? 4 : 1;
                        int mirs = plan.allowRotation ? 2 : 1;
                        for (int rr = 0; rr < rots; rr++)
                        for (int mm = 0; mm < mirs; mm++)
                        {
                            var walk = WalkableSet(plan.floor, rr, mm == 1);
                            if (LaneCarries(plan, laneDoors, walk, rr, mm == 1)) continue;
                            failures++;
                            sb.Append("  LANE FAIL: '").Append(plan.sourceName)
                              .Append("' lane carries no walker between its doors at ")
                              .Append("rot ").Append(rr).Append(mm == 1 ? " mirrored" : "")
                              .Append(".\n         -> three cells wide minimum. A floor ")
                              .Append("cell is walkable only when y+1 AND y+2 are")
                              .Append("\n            also floor, so one and two wide seal ")
                              .Append("under the drape.\n");
                            rr = rots; break;
                        }

                        // A road entering north and leaving east orphans
                        // everything behind it. Warned at authoring time, where
                        // it is a one-cell edit.
                        foreach (var a in laneDoors)
                        {
                            bool opposed = false;
                            foreach (var b in laneDoors)
                            {
                                if (b.mid == a.mid) continue;
                                if (a.outward + b.outward == Vector2Int.zero) { opposed = true; break; }
                            }
                            if (!opposed)
                                sb.Append("  lane warn: '").Append(plan.sourceName)
                                  .Append("' door at ").Append(a.mid)
                                  .Append(" is on the lane with nothing opposite it. ")
                                  .Append("A road arriving there would have to turn.\n");
                            if (LaneWidthAt(a, laneSet) < a.length)
                                sb.Append("  lane warn: '").Append(plan.sourceName)
                                  .Append("' lane is narrower than the ").Append(a.length)
                                  .Append("-wide door at ").Append(a.mid)
                                  .Append(". Legal, but the street pinches at the gate.\n");
                        }
                    }
                }

                // THE DOOR RULE, back and on a footing that cannot repeat the
                // first gate's failure. It reads ONLY '+' cells, so the 815
                // stall gaps, grave rows and column spacings that the old
                // inference flagged are exempt by construction rather than by
                // exception. A plan that declares nothing is checked for
                // nothing -- and says so in the roll-call.
                if (plan.doorPolicy == DoorPolicy.Absent)
                {
                    failures++;
                    sb.Append("  DOORS FAIL: '").Append(plan.sourceName)
                      .Append("' has no '@doors:' header.")
                      .Append("\n         -> write one of: unmarked (not yet annotated),")
                      .Append("\n            none (genuinely has none), marked (doors are '+').")
                      .Append("\n            Omission is not a third answer.\n");
                }
                else if (plan.doorPolicy == DoorPolicy.None && plan.door.Count > 0)
                {
                    failures++;
                    sb.Append("  DOORS FAIL: '").Append(plan.sourceName)
                      .Append("' says '@doors: none' but draws ")
                      .Append(plan.door.Count).Append(" '+' cell(s).\n");
                }
                else if (plan.doorPolicy == DoorPolicy.Marked)
                {
                    if (plan.door.Count == 0)
                    {
                        failures++;
                        sb.Append("  DOORS FAIL: '").Append(plan.sourceName)
                          .Append("' says '@doors: marked' and draws no '+' cells.")
                          .Append("\n         -> use 'none' if it genuinely has no doors.\n");
                    }
                    int shortRuns = 0, runs = plan.doorRuns.Count, worstRun = int.MaxValue;
                    foreach (var dr in plan.doorRuns)
                    {
                        if (dr.length < worstRun) worstRun = dr.length;
                        if (dr.length < 3) shortRuns++;
                    }
                    if (worstRun == int.MaxValue) worstRun = 0;

                    // Door anchoring needs a normal, and a run with none is one
                    // the placement will silently skip. Say so here instead.
                    if (plan.anchorOnDoor)
                    {
                        int usable = 0;
                        foreach (var dr in plan.doorRuns)
                            if (dr.outward != Vector2Int.zero) usable++;
                        if (usable == 0)
                        {
                            failures++;
                            sb.Append("  DOORS FAIL: '").Append(plan.sourceName)
                              .Append("' is '@anchor_on: door' but no door run faces ")
                              .Append("outward.")
                              .Append("\n         -> a door must open onto rock. Check it is ")
                              .Append("on the plan's edge, not an interior wall.\n");
                        }
                    }
                    if (shortRuns > 0)
                    {
                        failures++;
                        sb.Append("  DOORS FAIL: '").Append(plan.sourceName)
                          .Append("' has ").Append(shortRuns).Append(" of ").Append(runs)
                          .Append(" door run(s) under three cells (shortest ")
                          .Append(worstRun).Append(").")
                          .Append("\n         -> three. Two seals in the drawn orientation:")
                          .Append("\n            the drape needs y+1 AND y+2 clear, and a")
                          .Append("\n            two-cell run never gives the bottom cell both.\n");
                    }
                }
                else if (plan.doorPolicy == DoorPolicy.Unmarked)
                {
                    unmarked.Add(plan.sourceName);
                }

                // THE HEART. The old door-rule gate that used to sit here is gone:
                // it failed twelve shipped, working plans by flagging every
                // decorative niche as a sealed passage. A site is MINED into
                // rather than walked into through a door, so neither a narrow
                // alcove nor a fragmented interior is fatal, and a gate that
                // calls working plans broken only teaches you to skip the
                // report. What survives is the connectivity figure, printed on
                // every plan line as information.
                if (plan.heart.Count > 1)
                {
                    failures++;
                    sb.Append("  HEART FAIL: '").Append(plan.sourceName)
                      .Append("' has ").Append(plan.heart.Count)
                      .Append(" heart cells ('X'); at most one is allowed.\n");
                }
                if (plan.heart.Count == 1 && !HeartHasClearance(plan))
                {
                    failures++;
                    sb.Append("  HEART FAIL: '").Append(plan.sourceName)
                      .Append("' has a heart with fewer than three clear cells on one side.")
                      .Append("\n         -> a free-standing heart needs a chamber of at")
                      .Append("\n            least seven by seven around it.\n");
                }


                // THE PLATFORM AND ITS STAIRS. All three faults below are
                // invisible in the grid and only bite once something has to walk
                // the geometry -- which, with the avatar system, will be a player.
                if (plan.platform.Count > 0)
                {
                    int leaks = CountPlatformLeaks(plan);
                    if (leaks > 0)
                    {
                        failures++;
                        sb.Append("  PLATFORM FAIL: '").Append(plan.sourceName)
                          .Append("' has ").Append(leaks)
                          .Append(" platform edge(s) opening onto ordinary floor.")
                          .Append("\n         -> a raised edge must be masonry except at stairs,")
                          .Append("\n            or the platform is walkable from anywhere.\n");
                    }
                    if (plan.stairs.Count == 0)
                    {
                        failures++;
                        sb.Append("  PLATFORM FAIL: '").Append(plan.sourceName)
                          .Append("' has a raised platform and no stairs.")
                          .Append("\n         -> nothing can reach it once the edge is solid.\n");
                    }
                }

                int narrowStair = NarrowStairRun(plan);
                if (narrowStair > 0)
                {
                    failures++;
                    sb.Append("  STAIR FAIL: '").Append(plan.sourceName)
                      .Append("' has a stair run ").Append(narrowStair).Append(" cells wide.")
                      .Append("\n         -> three, the door rule where it genuinely applies:")
                      .Append("\n            this is a real passage and everything must path it.\n");
                }

                int worst = int.MaxValue, best = 0;
                int worstRot = 0;
                bool worstMirror = false;

                int rotations = plan.allowRotation ? 4 : 1;
                for (int rot = 0; rot < rotations; rot++)
                {
                    int mirrors = plan.allowRotation ? 2 : 1;
                    for (int m = 0; m < mirrors; m++)
                    {
                        int w = CountWalkable(plan.floor, rot, m == 1);
                        if (w > best) best = w;
                        if (w < worst)
                        {
                            worst = w;
                            worstRot = rot;
                            worstMirror = m == 1;
                        }
                    }
                }

                int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
                foreach (var c in plan.floor) Extend(c, ref minX, ref maxX, ref minY, ref maxY);
                foreach (var c in plan.wall) Extend(c, ref minX, ref maxX, ref minY, ref maxY);

                bool ok = worst >= MinWalkableCells;
                if (!ok) failures++;

                sb.Append(ok ? "  OK   " : "  FAIL ")
                  .Append(plan.sourceName.PadRight(34))
                  .Append(plan.archetype.ToString().PadRight(17))
                  .Append((maxX - minX + 1).ToString()).Append('x').Append((maxY - minY + 1).ToString())
                  .Append("  carved ").Append(plan.floor.Count)
                  .Append("  masonry ").Append(plan.wall.Count)
                  .Append("  walkable ").Append(worst).Append("..").Append(best);

                // Connectivity of the WORST orientation, as information only.
                // A single piece is ideal; anything lower says the interior
                // does not hang together under the drape, which is usually
                // what a drawing that did not come out right looks like in
                // numbers. It gates nothing -- see WalkableConnectivity.
                WalkableConnectivity(WalkableSet(plan.floor, worstRot, worstMirror),
                                     out int pieces, out int largestPiece);
                sb.Append("  linked ").Append(pieces).Append("pc/")
                  .Append(worst > 0 ? Mathf.RoundToInt(100f * largestPiece / worst) : 100)
                  .Append('%');

                if (!plan.allowRotation) sb.Append("  [rotation off]");
                if (plan.hasAnchorOverride) sb.Append("  [anchor ").Append(plan.anchorOverride).Append(']');

                if (!ok)
                {
                    sb.Append("\n         worst orientation: rot ").Append(worstRot * 90)
                      .Append(worstMirror ? " mirrored" : "")
                      .Append(" gives ").Append(worst).Append(" walkable cells, need ")
                      .Append(MinWalkableCells)
                      .Append("\n         -> widen the interiors. A room needs THREE rows of open floor")
                      .Append("\n            before one cell is walkable, and about five to be usable.");
                }
                sb.Append('\n');
            }
        }

        sb.Append("\n").Append(totalPlans).Append(" plan(s) checked, ")
          .Append(failures).Append(" failure(s).\n");

        // THE ROLL-CALL. Not a failure -- an unannotated plan is not a broken
        // one, it is one nobody has looked at yet, and the whole point of the
        // third policy state is that those two are different. Printed so the
        // annotation backlog is visible instead of implied.
        if (unmarked.Count > 0)
        {
            sb.Append(unmarked.Count).Append(" plan(s) still '@doors: unmarked' -- ")
              .Append("the door rule checks nothing on these:\n");
            foreach (var n in unmarked) sb.Append("    ").Append(n).Append('\n');
            sb.Append("  -> set 'marked' and draw the '+' cells, or 'none' if there ")
              .Append("are genuinely no doors.\n");
        }

        if (failures > 0)
            Debug.LogError("[SitePlanValidator]" + sb);
        else
            Debug.Log("[SitePlanValidator]" + sb);
    }


    /// <summary>Platform cells with a neighbour that is neither platform, nor
    /// masonry, nor stairs -- an opening in the raised edge. One is a hole the
    /// whole design leaks through, so this counts rather than returning a bool:
    /// a count tells you whether you mis-drew one cell or the entire edge.</summary>
    private static int CountPlatformLeaks(AuthoredSitePlan plan)
    {
        var plat = new HashSet<Vector2Int>(plan.platform);
        var solid = new HashSet<Vector2Int>(plan.wall);
        var steps = new HashSet<Vector2Int>(plan.stairs);
        var dirs = new[]
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        int leaks = 0;
        foreach (var p in plat)
            foreach (var d in dirs)
            {
                var q = new Vector2Int(p.x + d.x, p.y + d.y);
                if (plat.Contains(q) || solid.Contains(q) || steps.Contains(q)) continue;
                leaks++;
            }
        return leaks;
    }

    /// <summary>The width of the narrowest stair run, or 0 if every run is three
    /// or more. Measured on both axes because a stair reads along whichever axis
    /// it was drawn, and a plan may carry stairs on all four sides.</summary>
    private static int NarrowStairRun(AuthoredSitePlan plan)
    {
        if (plan.stairs.Count == 0) return 0;
        var steps = new HashSet<Vector2Int>(plan.stairs);

        for (int axis = 0; axis < 2; axis++)
        {
            var lines = new Dictionary<int, List<int>>();
            foreach (var c in steps)
            {
                int key = axis == 0 ? c.y : c.x;
                int val = axis == 0 ? c.x : c.y;
                if (!lines.TryGetValue(key, out var list)) lines[key] = list = new List<int>();
                list.Add(val);
            }

            foreach (var kv in lines)
            {
                var vs = kv.Value;
                vs.Sort();
                int start = 0;
                for (int i = 1; i <= vs.Count; i++)
                {
                    if (i < vs.Count && vs[i] == vs[i - 1] + 1) continue;
                    int len = i - start;
                    // A single cell on this axis is a run seen edge-on -- the
                    // three-wide run on the OTHER axis. Only two is unambiguously
                    // a narrow stair.
                    if (len == 2) return 2;
                    start = i;
                }
            }
        }
        return 0;
    }

    /// <summary>Three clear floor cells beyond the heart's own SOLID COMPONENT,
    /// on all four sides.
    ///
    /// The component matters and the first version of this check missed it. A
    /// heart set in a plinth -- `#####` over `##X##` over `#####` -- has masonry
    /// for its immediate neighbours by design, so measuring from the heart CELL
    /// failed six of the eight plans the rule was written to protect, while the
    /// two with a free-standing stone passed. Flooding the solid blob first and
    /// measuring from its edge treats both shapes correctly and reduces to the
    /// old behaviour when the blob is a single cell.
    ///
    /// A heart set into a WALL rather than standing in a room is exempt: past
    /// the size cutoff the blob is the building, not a plinth, and there is
    /// nothing meaningful to measure clearance around.</summary>
    private static bool HeartHasClearance(AuthoredSitePlan plan)
    {
        var open = new HashSet<Vector2Int>(plan.floor);
        var solid = new HashSet<Vector2Int>(plan.wall);
        var dirs = new[]
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        var blob = new HashSet<Vector2Int> { plan.heart[0] };
        var stack = new Stack<Vector2Int>();
        stack.Push(plan.heart[0]);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            foreach (var d in dirs)
            {
                var q = new Vector2Int(p.x + d.x, p.y + d.y);
                if (solid.Contains(q) && blob.Add(q)) stack.Push(q);
            }
        }

        // Nine by nine. Anything larger is a wall the heart was set into.
        if (blob.Count > 81) return true;

        foreach (var d in dirs)
        {
            var edge = plan.heart[0];
            int bestDot = int.MinValue;
            foreach (var p in blob)
            {
                int dot = p.x * d.x + p.y * d.y;
                if (dot > bestDot) { bestDot = dot; edge = p; }
            }
            for (int i = 1; i <= 3; i++)
                if (!open.Contains(new Vector2Int(edge.x + d.x * i, edge.y + d.y * i)))
                    return false;
        }
        return true;
    }

    /// <summary>How many disconnected pieces the drape-filtered walkable set
    /// falls into, and how much of it the largest piece holds.
    ///
    /// REPORTED, NEVER FAILED. This started life as a DOOR RULE check and the
    /// data retired it: SunkenPlaza_TheCountingFloor fragments into seven pieces
    /// with the largest at 33 per cent, TollHouse_TheWeighingHouse sits at 41,
    /// and both ship and both work -- because a site is MINED into rather than
    /// walked into through a door, so the player carves their own way and
    /// internal fragmentation is not fatal. A gate that calls twelve working
    /// plans broken is worse than no gate, because it teaches you to skip the
    /// report.
    ///
    /// It stays visible because it is still worth seeing. A low percentage says
    /// the interior does not hang together under the wall drape, which is
    /// usually what "this drawing did not come out right" looks like in
    /// numbers.</summary>
    /// <summary>The drape-filtered walkable set at one orientation. Same rule as
    /// CountWalkable, which counts it -- kept as two methods rather than one so
    /// the hot count path does not allocate a set it never uses.</summary>
    /// <summary>Every cell of a door run, reconstructed from its middle, length
    /// and facing. The run lies across its own outward normal.</summary>
    private static IEnumerable<Vector2Int> DoorRunCells(DoorRun dr)
    {
        var along = new Vector2Int(dr.outward.y, dr.outward.x);
        if (along == Vector2Int.zero)
        {
            yield return dr.mid;
            yield break;
        }
        int half = dr.length / 2;
        for (int k = -half; k <= half; k++)
            yield return new Vector2Int(dr.mid.x + along.x * k, dr.mid.y + along.y * k);
    }

    /// <summary>Whether a door run's middle sits on or beside the lane.</summary>
    private static bool TouchesLane(DoorRun dr, HashSet<Vector2Int> lane)
    {
        if (lane.Contains(dr.mid)) return true;
        var inward = -dr.outward;
        for (int k = 1; k <= 2; k++)
            if (lane.Contains(new Vector2Int(dr.mid.x + inward.x * k,
                                             dr.mid.y + inward.y * k))) return true;
        return false;
    }

    /// <summary>Connected pieces of the lane, four-neighbour.</summary>
    private static int LanePieces(HashSet<Vector2Int> lane)
    {
        var seen = new HashSet<Vector2Int>();
        int pieces = 0;
        foreach (var start in lane)
        {
            if (seen.Contains(start)) continue;
            pieces++;
            var stack = new Stack<Vector2Int>();
            stack.Push(start);
            seen.Add(start);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                var n0 = new Vector2Int(c.x + 1, c.y);
                var n1 = new Vector2Int(c.x - 1, c.y);
                var n2 = new Vector2Int(c.x, c.y + 1);
                var n3 = new Vector2Int(c.x, c.y - 1);
                if (lane.Contains(n0) && seen.Add(n0)) stack.Push(n0);
                if (lane.Contains(n1) && seen.Add(n1)) stack.Push(n1);
                if (lane.Contains(n2) && seen.Add(n2)) stack.Push(n2);
                if (lane.Contains(n3) && seen.Add(n3)) stack.Push(n3);
            }
        }
        return pieces;
    }

    /// <summary>How wide the lane is where it meets this door, measured across
    /// the door's own run direction.</summary>
    private static int LaneWidthAt(DoorRun dr, HashSet<Vector2Int> lane)
    {
        var across = new Vector2Int(dr.outward.y, dr.outward.x);
        if (across == Vector2Int.zero) return 0;
        var probe = dr.mid - dr.outward;
        int w = lane.Contains(probe) ? 1 : 0;
        for (int s = -1; s <= 1; s += 2)
            for (int k = 1; k < 32; k++)
            {
                var c = new Vector2Int(probe.x + across.x * k * s, probe.y + across.y * k * s);
                if (!lane.Contains(c)) break;
                w++;
            }
        return w;
    }

    /// <summary>
    /// Can a walker get from one of these doors to another ALONG THE LANE, in
    /// this orientation?
    ///
    /// Two things are deliberately separate here. Walkability is judged over the
    /// whole floor, because the drape is physics and does not care what a cell
    /// is for -- a one-wide lane painted across an open plaza IS walkable, since
    /// the open floor above it supplies the clearance. But the PATH is confined
    /// to the lane, because the question is whether the ROUTE carries a road,
    /// not whether some route exists.
    ///
    /// The first version of this check ran the search over all floor and passed
    /// a one-wide lane through an open room -- it found a way between the doors
    /// through the room and never touched the lane. That is also why "three
    /// cells wide" is not the rule: three is only needed where the lane is cut
    /// through solid masonry, and stating it as a blanket minimum would have had
    /// perfectly good plans widened for nothing.
    /// </summary>
    private static bool LaneCarries(AuthoredSitePlan plan, List<DoorRun> doors,
                                    HashSet<Vector2Int> walk, int rot, bool mirror)
    {
        if (doors.Count < 2) return false;

        // The corridor the search may use: lane cells, plus each door's own
        // threshold so a route can start and finish at the wall.
        var route = new HashSet<Vector2Int>();
        foreach (var c in plan.lane) route.Add(Transform(c, rot, mirror));
        foreach (var d in doors)
        {
            route.Add(Transform(d.mid, rot, mirror));
            for (int k = 1; k <= 4; k++)
                route.Add(Transform(d.mid - d.outward * k, rot, mirror));
        }
        walk = new HashSet<Vector2Int>(walk);
        walk.IntersectWith(route);

        // Start from the first door's inward cell, and see which other doors the
        // walkable set reaches. Any two is enough: a road only needs one route.
        // Start cells: EVERY cell of the first door's run, stepped inward --
        // not merely its middle. In a three-tall corridor only the BOTTOM row is
        // walkable, because the two above it are buried under the drape, so a
        // probe that walks in from the middle finds nothing and reports a
        // perfectly good lane as impassable. That is a false negative, and false
        // negatives on working plans are what retired the first door gate.
        var starts = new List<Vector2Int>();
        foreach (var c in DoorRunCells(doors[0]))
            for (int k = 1; k <= 4; k++)
            {
                var p = Transform(c - doors[0].outward * k, rot, mirror);
                if (walk.Contains(p)) starts.Add(p);
            }
        if (starts.Count == 0) return false;

        var targets = new HashSet<Vector2Int>();
        for (int i = 1; i < doors.Count; i++)
            foreach (var c in DoorRunCells(doors[i]))
                for (int k = 1; k <= 4; k++)
                    targets.Add(Transform(c - doors[i].outward * k, rot, mirror));

        var seen = new HashSet<Vector2Int>();
        var stack = new Stack<Vector2Int>();
        foreach (var s in starts)
            if (seen.Add(s)) stack.Push(s);
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            if (targets.Contains(c)) return true;
            var n0 = new Vector2Int(c.x + 1, c.y);
            var n1 = new Vector2Int(c.x - 1, c.y);
            var n2 = new Vector2Int(c.x, c.y + 1);
            var n3 = new Vector2Int(c.x, c.y - 1);
            if (walk.Contains(n0) && seen.Add(n0)) stack.Push(n0);
            if (walk.Contains(n1) && seen.Add(n1)) stack.Push(n1);
            if (walk.Contains(n2) && seen.Add(n2)) stack.Push(n2);
            if (walk.Contains(n3) && seen.Add(n3)) stack.Push(n3);
        }
        return false;
    }

    private static HashSet<Vector2Int> WalkableSet(List<Vector2Int> cells, int rot, bool mirror)
    {
        var set = new HashSet<Vector2Int>();
        foreach (var c in cells) set.Add(Transform(c, rot, mirror));

        var walk = new HashSet<Vector2Int>();
        foreach (var c in set)
        {
            if (!set.Contains(new Vector2Int(c.x, c.y + 1))) continue;
            if (!set.Contains(new Vector2Int(c.x, c.y + 2))) continue;
            walk.Add(c);
        }
        return walk;
    }

    private static void WalkableConnectivity(HashSet<Vector2Int> walkable,
                                             out int pieces, out int largest)
    {
        pieces = 0;
        largest = 0;
        var seen = new HashSet<Vector2Int>();
        var dirs = new[]
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        foreach (var start in walkable)
        {
            if (!seen.Add(start)) continue;
            pieces++;
            int size = 0;
            var stack = new Stack<Vector2Int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                size++;
                foreach (var d in dirs)
                {
                    var q = new Vector2Int(p.x + d.x, p.y + d.y);
                    if (walkable.Contains(q) && seen.Add(q)) stack.Push(q);
                }
            }
            if (size > largest) largest = size;
        }
    }

    private static void Extend(Vector2Int c, ref int minX, ref int maxX, ref int minY, ref int maxY)
    {
        if (c.x < minX) minX = c.x;
        if (c.x > maxX) maxX = c.x;
        if (c.y < minY) minY = c.y;
        if (c.y > maxY) maxY = c.y;
    }

    /// <summary>The pathfinder's rule: a cell is walkable only if the two cells
    /// directly north of it are also open, because that is the wall face
    /// draping down over it.</summary>
    private static int CountWalkable(List<Vector2Int> cells, int rot, bool mirror)
    {
        var set = new HashSet<Vector2Int>();
        foreach (var c in cells) set.Add(Transform(c, rot, mirror));

        int n = 0;
        foreach (var c in set)
        {
            if (!set.Contains(new Vector2Int(c.x, c.y + 1))) continue;
            if (!set.Contains(new Vector2Int(c.x, c.y + 2))) continue;
            n++;
        }
        return n;
    }

    private static Vector2Int Transform(Vector2Int p, int rot, bool mirror)
    {
        int x = mirror ? -p.x : p.x;
        int y = p.y;
        switch (rot & 3)
        {
            case 1: return new Vector2Int(-y, x);
            case 2: return new Vector2Int(-x, -y);
            case 3: return new Vector2Int(y, -x);
            default: return new Vector2Int(x, y);
        }
    }
}

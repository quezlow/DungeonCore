using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The gatehouse-to-village route derivation, extracted VERBATIM from
/// DwarvenCaravanController.BuildRoutes so the relief cycle (canon 46, stage
/// E2) does not grow a second copy free to drift from the first. Finds the two
/// floors by their sites, builds both road graphs, pairs the rim ends by
/// bearing, and lays the outbound cell runs. Deterministic for a given world,
/// so nothing about a route ever needs a save field. The caravan calls this
/// too; its behaviour is unchanged. Canon 51 adds BuildToSite beside it:
/// the pilgrimage's outpost-to-deep-site derivation, sharing the rim
/// pairing through BearingMatchedPair so neither copy is free to drift.
/// </summary>
public static class DwarvenJourneyRoutes
{
    /// <summary>Everything a two-leg journey needs, in one derivation.</summary>
    public class RouteSet
    {
        public FloorRoot gateFloor;
        public FloorRoot villageFloor;
        public DeepRoadGraph.Graph gateGraph;
        public DeepRoadGraph.Graph villageGraph;
        public List<Vector3Int> gateRouteOut;      // outpost -> gate-floor rim
        public List<Vector3Int> villageRouteOut;   // village-floor rim -> village
        public int gateRimRail = -1;
        public int gateRimIndex = -1;
        public int villageRimRail = -1;
        public int villageRimIndex = -1;
    }

    public static bool Build(out RouteSet routes)
    {
        routes = null;
        var set = new RouteSet();

        var fm = FloorManager.Instance;
        if (fm == null) return false;
        foreach (var floor in fm.AllFloors)
        {
            var features = floor?.FeatureGenerator;
            if (features == null || !features.HasGenerated) continue;
            if (features.GetOutpostSite() != null) set.gateFloor = floor;
            if (features.GetVillageSite() != null) set.villageFloor = floor;
        }
        if (set.gateFloor == null || set.villageFloor == null) return false;

        set.gateGraph = DeepRoadGraph.Build(set.gateFloor.FeatureGenerator.FeatureData.roads);
        set.villageGraph = DeepRoadGraph.Build(set.villageFloor.FeatureGenerator.FeatureData.roads);

        var gateRims = DeepRoadGraph.RimEnds(set.gateGraph);
        var villageRims = DeepRoadGraph.RimEnds(set.villageGraph);
        if (gateRims.Count == 0 || villageRims.Count == 0) return false;

        // Bearing-matched pair: the walkers leave and arrive on "the same" road.
        BearingMatchedPair(gateRims, villageRims, out var gateRim, out var villageRim);

        var outpost = set.gateFloor.FeatureGenerator.GetOutpostSite();
        var village = set.villageFloor.FeatureGenerator.GetVillageSite();
        if (outpost == null || village == null) return false;

        if (!DeepRoadGraph.NearestWalkCell(set.gateGraph, outpost.anchorCell.ToVector3Int(),
                out int oRail, out int oIdx)) return false;
        if (!DeepRoadGraph.NearestWalkCell(set.villageGraph, village.anchorCell.ToVector3Int(),
                out int vRail, out int vIdx)) return false;

        int gRimIdx = TerminusIndex(set.gateGraph, gateRim);
        int vRimIdx = TerminusIndex(set.villageGraph, villageRim);
        set.gateRimRail = gateRim.railIndex; set.gateRimIndex = gRimIdx;
        set.villageRimRail = villageRim.railIndex; set.villageRimIndex = vRimIdx;

        set.gateRouteOut = DeepRoadGraph.Route(set.gateGraph, oRail, oIdx,
                                               gateRim.railIndex, gRimIdx);
        set.villageRouteOut = DeepRoadGraph.Route(set.villageGraph, villageRim.railIndex,
                                                  vRimIdx, vRail, vIdx);
        if (set.gateRouteOut.Count <= 1 || set.villageRouteOut.Count <= 1) return false;

        routes = set;
        return true;
    }

    /// <summary>Walk index of a rim terminus on its rail. Moved here with the
    /// derivation; the caravan's private copy is deleted rather than left as an
    /// orphan for a sweep to find.</summary>
    public static int TerminusIndex(DeepRoadGraph.Graph g, DeepRoadGraph.RimEnd rim)
    {
        var rail = g.rails[rim.railIndex];
        return rail.walk[0] == rim.walkTerminus ? 0 : rail.walk.Count - 1;
    }

    /// <summary>The rim pairing extracted from Build so the pilgrimage's
    /// derivation (canon 51) shares it rather than growing a copy free to
    /// drift -- the same reason this file exists at all. Behaviour is
    /// byte-for-byte the loop Build carried: minimum bearing delta wins,
    /// first pair on ties.</summary>
    public static void BearingMatchedPair(
        List<DeepRoadGraph.RimEnd> endsA, List<DeepRoadGraph.RimEnd> endsB,
        out DeepRoadGraph.RimEnd rimA, out DeepRoadGraph.RimEnd rimB)
    {
        float best = float.MaxValue;
        rimA = endsA[0]; rimB = endsB[0];
        foreach (var a in endsA)
            foreach (var b in endsB)
            {
                float d = DeepRoadGraph.BearingDelta(a.bearingDegrees, b.bearingDegrees);
                if (d < best) { best = d; rimA = a; rimB = b; }
            }
    }

    /// <summary>Everything the one-way pilgrimage needs (canon 51): the
    /// caravan's outbound gate leg, then a rim-to-site run on the pinned
    /// destination floor. Deterministic for a given world and pin, so the
    /// save carries only (destFloorIndex, destSiteId) and re-derives.</summary>
    public class PilgrimRouteSet
    {
        public FloorRoot gateFloor;
        public FloorRoot destFloor;
        public DeepRoadGraph.Graph gateGraph;
        public DeepRoadGraph.Graph destGraph;
        public List<Vector3Int> gateRouteOut;   // outpost -> gate-floor rim
        public List<Vector3Int> destRouteOut;   // dest-floor rim -> site
    }

    /// <summary>Derives the pilgrimage route to a PINNED site. The pin is
    /// the caller's (chosen at departure, saved), because a floor dug
    /// mid-journey must not shift the destination under a walking column --
    /// which is exactly what a re-run of the "deepest floor" pick would do.
    /// Returns false while floors are still standing up after a load; the
    /// controller retries lazily, the funeral's own guard.</summary>
    public static bool BuildToSite(int destFloorIndex, int destSiteId,
                                   out PilgrimRouteSet routes)
    {
        routes = null;
        var set = new PilgrimRouteSet();

        var fm = FloorManager.Instance;
        if (fm == null) return false;
        foreach (var floor in fm.AllFloors)
        {
            var features = floor?.FeatureGenerator;
            if (features == null || !features.HasGenerated) continue;
            if (features.GetOutpostSite() != null) set.gateFloor = floor;
            if (floor.FloorIndex == destFloorIndex) set.destFloor = floor;
        }
        if (set.gateFloor == null || set.destFloor == null) return false;

        var site = set.destFloor.FeatureGenerator.GetSiteById(destSiteId);
        if (site == null) return false;

        set.gateGraph = DeepRoadGraph.Build(set.gateFloor.FeatureGenerator.FeatureData.roads);
        set.destGraph = DeepRoadGraph.Build(set.destFloor.FeatureGenerator.FeatureData.roads);

        var gateRims = DeepRoadGraph.RimEnds(set.gateGraph);
        var destRims = DeepRoadGraph.RimEnds(set.destGraph);
        if (gateRims.Count == 0 || destRims.Count == 0) return false;

        BearingMatchedPair(gateRims, destRims, out var gateRim, out var destRim);

        var outpost = set.gateFloor.FeatureGenerator.GetOutpostSite();
        if (outpost == null) return false;

        if (!DeepRoadGraph.NearestWalkCell(set.gateGraph, outpost.anchorCell.ToVector3Int(),
                out int oRail, out int oIdx)) return false;
        // The pilgrims leave the road for the shrine unseen: the route ends
        // at the graph's nearest walk cell to the site anchor, however far
        // the last stretch off the carriageway runs.
        if (!DeepRoadGraph.NearestWalkCell(set.destGraph, site.anchorCell.ToVector3Int(),
                out int sRail, out int sIdx)) return false;

        int gRimIdx = TerminusIndex(set.gateGraph, gateRim);
        int dRimIdx = TerminusIndex(set.destGraph, destRim);

        set.gateRouteOut = DeepRoadGraph.Route(set.gateGraph, oRail, oIdx,
                                               gateRim.railIndex, gRimIdx);
        set.destRouteOut = DeepRoadGraph.Route(set.destGraph, destRim.railIndex,
                                               dRimIdx, sRail, sIdx);
        if (set.gateRouteOut.Count <= 1 || set.destRouteOut.Count <= 1) return false;

        routes = set;
        return true;
    }
}

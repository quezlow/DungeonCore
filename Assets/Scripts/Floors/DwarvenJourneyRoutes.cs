using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The gatehouse-to-village route derivation, extracted VERBATIM from
/// DwarvenCaravanController.BuildRoutes so the relief cycle (canon 46, stage
/// E2) does not grow a second copy free to drift from the first. Finds the two
/// floors by their sites, builds both road graphs, pairs the rim ends by
/// bearing, and lays the outbound cell runs. Deterministic for a given world,
/// so nothing about a route ever needs a save field. The caravan calls this
/// too; its behaviour is unchanged.
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
        float best = float.MaxValue;
        DeepRoadGraph.RimEnd gateRim = gateRims[0], villageRim = villageRims[0];
        foreach (var a in gateRims)
            foreach (var b in villageRims)
            {
                float d = DeepRoadGraph.BearingDelta(a.bearingDegrees, b.bearingDegrees);
                if (d < best) { best = d; gateRim = a; villageRim = b; }
            }

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
}

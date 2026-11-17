using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Display;

// Transient UI selection; a failed route remains explainable after the place card replaces its image.
public static class WorldTravelSelection
{
    public static void Clear(DisplayConfig display)
    {
        display.WorldTarget=null;
        display.WorldRoute=null;
        display.WorldRouteInfo=null;
        display.WorldRouteUnavailable=false;
    }

    public static void Select(WorldState state, DisplayConfig display, WorldMap visibleWorld, (int x,int y) target)
    {
        Clear(display);
        var origin=GameWorld.HeroTile(state);
        bool heroTile=origin==target;
        var localPlace=LocalPlaceEntry.Find(state,target,visibleWorld);
        var plan=heroTile?null:TravelService.PlanTo(state,target);
        display.WorldTarget=plan!=null||localPlace!=null?target:null;
        display.WorldRoute=plan?.Route.Path;
        display.WorldRouteInfo=plan==null?null:(plan.TargetPlace??WorldAtlas.TileName(visibleWorld,target.x,target.y),plan.Route.Days,plan.Route.RoadShare);
        display.WorldRouteUnavailable=plan==null&&!heroTile&&origin!=null;
    }
}

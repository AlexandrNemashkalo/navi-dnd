using NaviDnD.Clients;
using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class EnemyTacticsTests
{
    [Fact]
    public void CasterTurnContextIncludesRealSpellsActionsAndIndividualTactics()
    {
        var s=new Storage();var ws=s.WorldState;
        ws.Hero=new(){Symbol="HRO",Position=[2,2]};ws.Combat=new(){Active=true};
        var druid=MonsterDatabase.Find("Druid");Assert.NotNull(druid);
        ws.Map.Entities=[new(){Name="Шаман",Symbol="SHM",MonsterKey=druid!.Name,Position=[7,2],SpeedMax=30,AiInfo=["Защищает идола, держится за охраной"]}];
        ws.Combat!.CurrentTurn="SHM";ws.Combat.Initiative=[new(){Symbol="SHM",Score=18},new(){Symbol="HRO",Score=12}];
        string context=new AiContextBuilder(ws).CombatTurnState();
        Assert.Contains("Защищает идола",context);
        Assert.Contains(druid.Spells,context);
        Assert.Contains(druid.Actions[0].Text,context);
        Assert.Contains("speedFt:30",context);
    }
}

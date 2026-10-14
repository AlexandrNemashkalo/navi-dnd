using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class EnemyTacticsPromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ExhaustedShamanUsesKnownRangedCantripInsteadOfMelee()
    {
        var s=PromptScenario.LoadLocalFixture("door_interaction_game.json");var ws=s.WorldState;
        ws.Hero.Position=[9,3];ws.Hero.Hp="50/50";ws.Hero.Light=new(){On=true,Ft=60};
        ws.Map.Rooms=[new(){Name="Святилище",Positions=[..Enumerable.Range(1,15).SelectMany(c=>Enumerable.Range(1,6).Select(r=>new List<int>{c,r}))]}];
        ws.Map.Cols=15;ws.Map.Rows=6;ws.Map.Doors=[];
        var druid=MonsterDatabase.Find("Druid")!;
        ws.Map.Entities=[new(){Symbol="SHM",Name="Шаман",MonsterKey=druid.Name,Position=[3,3],Hp="27/27",SpeedMax=30,DarkvisionFt=0,AiInfo=["Охраняет святилище, держит героя в пределах дальности своих заговоров, избегает ближнего боя. Все ячейки заклинаний уже потрачены; концентрации нет. Свои известные заговоры доступны."]}];
        ws.Combat=new(){Active=true,CurrentTurn="SHM",Initiative=[new(){Symbol="SHM",Score=18},new(){Symbol="HRO",Score=12}]};
        var result=await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e","SendAction",s,new AiContextBuilder(ws).CombatTurnState());
        output.WriteLine(result.RawResponse);
        var parsed=PromptScenario.ApplyAndParse(s,result.RawResponse);
        Assert.Contains("calculate_movement",result.ToolTrace);
        Assert.True(TargetGeometry.DistanceFt(ws.Hero.Position[0],ws.Hero.Position[1],ws.Map.Entities[0].Position[0],ws.Map.Entities[0].Position[1])>=15);
        Assert.Contains(parsed.HistoryTexts,t=>t.Contains("плам",StringComparison.OrdinalIgnoreCase));
        Assert.Equal("HRO",ws.Combat!.CurrentTurn);
        Assert.Equal("27/27",ws.Map.Entities[0].Hp);
    }
}

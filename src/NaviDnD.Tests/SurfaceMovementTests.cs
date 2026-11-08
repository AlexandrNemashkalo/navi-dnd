using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Clients;
using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class SurfaceMovementTests
{
    private static Storage Scene(char surface,int speed=30)
    {
        var s=new Storage();var ws=s.WorldState;
        ws.Hero=new(){Name="Hero",Symbol="HRO",Position=[2,2],SpeedMax=30,SpeedLeft=speed};
        ws.Combat=new(){Active=true,CurrentTurn="HRO"};
        ws.Map.Chunks=[new(){Theme="forest",Terrain=Enumerable.Repeat(new string(surface,MapChunk.Cols),MapChunk.Rows).ToList()}];
        return s;
    }

    [Theory]
    [InlineData('"')]
    [InlineData('~')]
    [InlineData(':')]
    [InlineData('g')]
    public void HeroCannotOverspendLastFiveFeetOnDifficultTerrain(char surface)
    {
        var s=Scene(surface,5);
        new MovementHandler(s.WorldState,s,new DisplayConfig()).TryMove("MoveEast",out _,out _,out _,out _);
        Assert.Equal(new[]{2,2},s.WorldState.Hero!.Position);
        Assert.Equal(5,s.WorldState.Hero.SpeedLeft);
    }

    [Theory]
    [InlineData('.',5)]
    [InlineData('"',10)]
    [InlineData('~',10)]
    [InlineData(':',10)]
    [InlineData('g',10)]
    public void HeroAndEnemyPayIdenticalSurfaceAndDiagonalCosts(char surface,int expected)
    {
        var s=Scene(surface);var ws=s.WorldState;
        var handler=new MovementHandler(ws,s,new DisplayConfig());
        handler.TryMove("MoveNorthEast",out _,out _,out _,out _);
        Assert.Equal(30-expected,ws.Hero!.SpeedLeft);
        Assert.Equal(expected,MovementCalculator.GetStepCost(ws,3,3,true,false));
        handler.TryMove("MoveNorthEast",out _,out _,out _,out _);
        Assert.Equal(30-expected*2-5,ws.Hero.SpeedLeft);
        Assert.Equal(expected+5,MovementCalculator.GetStepCost(ws,4,4,true,true));
        ws.Hero.Position=[10,10];
        ws.Map.Entities=[new(){Symbol="ENM",Position=[2,2],SpeedMax=30}];
        var reachable=MovementCalculator.Dijkstra(ws,2,2,expected-1,0);
        Assert.DoesNotContain(reachable.Keys,k=>k.col==3&&k.row==2);
        reachable=MovementCalculator.Dijkstra(ws,2,2,expected,0);
        Assert.Contains(reachable,k=>k.Key.col==3&&k.Key.row==2&&k.Value.cost==expected);
    }

    [Theory]
    [InlineData('.',5)]
    [InlineData('"',10)]
    [InlineData('~',10)]
    [InlineData(':',10)]
    [InlineData('g',10)]
    public void EnemyContinuationCannotSpendSameMovementTwice(char surface,int cost)
    {
        var s=Scene(surface);var ws=s.WorldState;ws.Map.Rows=1;
        ws.Hero!.Position=[10,1];ws.Map.Entities=[new(){Symbol="ENM",Position=[2,1],SpeedMax=30}];
        var first=MovementCalculator.Calculate(ws,new(){EntityId=0,SpeedFt=cost+2,Intents=[new(){Intent="approach",Target="hero"}]});
        Assert.Equal(new[]{3,1},first.Steps[^1]);Assert.Equal(2,first.RemainingSpeedFt);
        var second=MovementCalculator.Calculate(ws,new(){EntityId=0,FromPosition=first.Steps[^1],SpeedFt=first.RemainingSpeedFt,DiagonalsUsed=first.DiagonalsUsed,Intents=[new(){Intent="approach",Target="hero"}]});
        Assert.Equal(new[]{3,1},second.Steps[^1]);Assert.Equal(2,second.RemainingSpeedFt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void InvalidAreaCostCannotCreateFreeMovementOrNegativePathEdges(int invalid)
    {
        var s=Scene('.');s.WorldState.Map.Area=[new(){Positions=[[3,2]],StepCostFt=invalid}];
        Assert.Equal(5,MovementCalculator.GetStepCost(s.WorldState,3,2));
    }

    [Fact]
    public void MagicAreaCostIsSameForHeroAndEnemyAndDeletedAreaDoesNotApply()
    {
        var s=Scene('.');var ws=s.WorldState;
        ws.Map.Area=[new(){Positions=[[3,2]],StepCostFt=20}];
        new MovementHandler(ws,s,new DisplayConfig()).TryMove("MoveEast",out _,out _,out _,out _);
        Assert.Equal(10,ws.Hero!.SpeedLeft);
        Assert.Equal(20,MovementCalculator.GetStepCost(ws,3,2));
        ws.Map.Area[0].Deleted=true;
        Assert.Equal(5,MovementCalculator.GetStepCost(ws,3,2));
    }

    [Fact]
    public void FirstDiagonalAfterAutomaticNewRoundKeepsFiveFootDiscount()
    {
        var s=Scene('.',5);s.WorldState.Combat=null;
        s.ApplyClientMovement(5,true);
        new MovementHandler(s.WorldState,s,new DisplayConfig()).TryMove("MoveNorthEast",out _,out _,out _,out _);
        Assert.Equal(2,s.WorldState.Time.TotalRounds);
        Assert.Equal(25,s.WorldState.Hero!.SpeedLeft);
        Assert.Equal(1,s.DiagonalUsed);
    }

    [Fact]
    public void SqueezingPastBystanderRequiresBothStepsWorthOfMovement()
    {
        var s=Scene('.',5);var ws=s.WorldState;
        ws.Map.Entities=[new(){Symbol="NPC",Name="Friend",Position=[3,2],Hp="10/10"}];
        var handler=new MovementHandler(ws,s,new DisplayConfig());
        handler.TryMove("MoveEast",out _,out _,out _,out _);
        handler.TryMove("MoveEast",out _,out _,out _,out _);
        Assert.Equal(new[]{2,2},ws.Hero!.Position);Assert.Equal(5,ws.Hero.SpeedLeft);
    }

    [Fact]
    public void ExpensiveDiagonalCannotOverspendAndOutOfCombatMovementStartsNewRound()
    {
        var s=Scene('.',10);s.ApplyClientMovement(5,true);
        new MovementHandler(s.WorldState,s,new DisplayConfig()).TryMove("MoveNorthEast",out _,out _,out _,out _);
        Assert.Equal(new[]{2,2},s.WorldState.Hero!.Position);Assert.Equal(5,s.WorldState.Hero.SpeedLeft);
        s=Scene('~',5);s.WorldState.Combat=null;
        new MovementHandler(s.WorldState,s,new DisplayConfig()).TryMove("MoveEast",out _,out _,out _,out _);
        Assert.Equal(new[]{3,2},s.WorldState.Hero!.Position);Assert.Equal(20,s.WorldState.Hero.SpeedLeft);
        Assert.Equal(2,s.WorldState.Time.TotalRounds);
    }
}

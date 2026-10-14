using NaviDnD.Clients;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

public class ThirdPassPromptTests(ITestOutputHelper output)
{
    private static Storage Scene()
    {
        var s=PromptScenario.LoadLocalFixture("door_interaction_game.json");
        s.WorldState.Narrative=new();
        s.WorldState.Hero.Position=[3,3];
        s.WorldState.Hero.DarkvisionFt=0;
        s.WorldState.Map.Doors=[];
        s.WorldState.Time.PartOfDay=PartsOfDay.Night;
        s.WorldState.Hero.Stats=[new(){Key=StatKeys.Class,Name="Класс",Value="Волшебник"},new(){Key=StatKeys.ArmorClass,Name="КД",Value="12"}];
        return s;
    }

    private async Task<AutoAnswerResult> Act(Storage s,string action,string extra="",(int,int)? target=null)
    {
        var result=await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e","SendAction",s,
            "lang:ru\nspeechEnabled:false\n"+new AiContextBuilder(s.WorldState).MinimalState(action)+"\n"+extra,targetPoint:target);
        output.WriteLine(result.RawResponse);
        PromptScenario.ApplyAndParse(s,result.RawResponse);
        return result;
    }

    [Fact]
    public async Task StealthBeatsWatcherWithoutStartingCombat()
    {
        var s=Scene(); s.WorldState.Hero.Stealth=25;
        var e=new LivingEntity{Name="Страж",Symbol="GRD",Position=[5,3],Hp="10/10",DarkvisionFt=60,Triggers=new(){Spotted=new(){Effect="Патрулирует, пассивное Восприятие 10; герой пока не обнаружен"}}};
        s.WorldState.Map.Entities=[e];
        await Act(s,"продолжаю тихо ждать в укрытии",new AiContextBuilder(s.WorldState).TriggerState([(e,"spotted")],[],[]));
        Assert.NotEqual(true,s.WorldState.Combat?.Active);
        Assert.Equal(25,s.WorldState.Hero.Stealth);
    }

    [Fact]
    public async Task LightCantripCreatesLightWithoutSpendingSpellSlot()
    {
        var s=Scene();
        s.WorldState.Hero.Inventory=[new(){Name="Посох",EquipmentedSlot=["П Рука"]}];
        s.WorldState.Hero.Spells=[new(){Name="Свет",Level=0,Prepared=true}];
        s.WorldState.Hero.Resources=[new(){Name="Ячейки 1 ур.",Value="2/2",Category=ResourceCategories.Spells}];
        await Act(s,"накладываю Свет на навершие своего посоха и держу его в руке");
        Assert.True(s.WorldState.Hero.Light?.On);
        Assert.True(s.WorldState.Hero.Light!.Ft>=20);
        Assert.Equal("2/2",s.WorldState.Hero.Resources[0].Value);
    }

    [Fact]
    public async Task BurningHandsUsesSelectedConeAndDamagesAllyToo()
    {
        var s=Scene(); s.WorldState.Hero.Light=new(){On=true,Ft=60};
        s.WorldState.Hero.Spells=[new(){Name="Огненные ладони",Level=1,Prepared=true}];
        s.WorldState.Hero.Resources=[new(){Name="Ячейки 1 ур.",Value="2/2",Category=ResourceCategories.Spells}];
        s.WorldState.Map.Entities=[new(){Name="Разбойник",Symbol="BND",Position=[5,3],Hp="30/30",AiInfo=["КД 12, Ловкость +0"]},new(){Name="Союзник",Symbol="ALY",Position=[4,3],Hp="30/30",AiInfo=["Друг героя, Ловкость +0"]}];
        var result=await Act(s,"накладываю Огненные ладони на восток; я знаю, что союзник на линии огня, всё равно применяю",target:(5,3));
        Assert.True(result.SelectTargetTriggered);
        Assert.Equal("1/2",s.WorldState.Hero.Resources[0].Value);
        Assert.All(s.WorldState.Map.Entities,e=>Assert.NotEqual("30/30",e.Hp));
    }

    [Fact]
    public async Task WorldExitCannotBeForcedOpen()
    {
        var s=Scene(); s.WorldState.Map.Doors=[new(){From=[3,3],To=[4,3],IsDoor=true,IsWorldExit=true,IsDoorOpen=false}];
        await Act(s,"открываю дверь выхода на карту мира прямо передо мной");
        Assert.False(s.WorldState.Map.Doors[0].IsDoorOpen);
        Assert.True(s.WorldState.Map.Doors[0].IsWorldExit);
    }

    [Fact]
    public async Task EquippingShieldUpdatesAcAndKeepsInventory()
    {
        var s=Scene();
        s.WorldState.Hero.Stats[0].Value="Воин";
        s.WorldState.Hero.Inventory=[new(){Name="Щит",Description="Обычный щит, +2 КД",EquipmentedSlot=[]},new(){Name="Кинжал",EquipmentedSlot=["П Рука"]}];
        await Act(s,"надеваю щит на свободную левую руку");
        Assert.Equal(2,s.WorldState.Hero.Inventory.Count);
        Assert.NotEmpty(s.WorldState.Hero.Inventory[0].EquipmentedSlot!);
        Assert.Equal("14",s.WorldState.Hero.Stat(StatKeys.ArmorClass)!.Value);
        Assert.Contains("П Рука",s.WorldState.Hero.Inventory[1].EquipmentedSlot!);
    }

    [Fact]
    public async Task TravelReportConsumesFoodOnceWithoutMovingTimeAgain()
    {
        var s=Scene();
        s.WorldState.Time.Day=4; s.WorldState.Time.PartOfDay=PartsOfDay.Evening;
        s.WorldState.Hero.Inventory=[new(){Name="Дорожные рационы",Quantity=5}];
        await Act(s,"путь завершён, описать прибытие", "## ПУТЕШЕСТВИЕ (движок уже переместил героя и сдвинул время — time и world.place не меняй)\nВ пути: 2 дня. Встреч нет. Отдых уже завершён, хиты и действия полные. Еда: в пути 2 дня — по 1 дневной порции за день. В инвентаре: Дорожные рационы (id 0) ×5. Спиши съеденное патчем hero.inventory.");
        Assert.Equal(3,s.WorldState.Hero.Inventory[0].Quantity);
        Assert.Equal(4,s.WorldState.Time.Day);
    }

    [Fact]
    public async Task NpcUsesStoredMemoryAndAddsNewPromise()
    {
        var s=Scene();
        s.WorldState.Narrative.Npcs=[new(){Name="Мира",Met=true,Attitude="нейтральная",Notes="Трактирщица",Memory=["Герой сказал: пароль — Синяя сова"]}];
        var result=await Act(s,"Мира, напомни мой пароль. Обещаю завтра принести тебе лекарство.");
        var npc=s.WorldState.Narrative.Npcs.Single();
        Assert.Contains(npc.Memory!,m=>m.Contains("Синяя сова"));
        Assert.Contains(npc.Memory!,m=>m.Contains("лекарств",StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Синяя сова",result.RawResponse);
    }

    [Fact]
    public async Task HiddenAmbusherRevealsItselfAndSurprisesHero()
    {
        var s=Scene();s.WorldState.Hero.Light=new(){On=true,Ft=30};
        s.WorldState.Hero.Skills=[new(){Name="Восприятие",Value="-2"}];
        var enemy=new LivingEntity{Name="Гоблин",Symbol="GOB",Hp="7/7",Position=[4,3],Hidden=true,DarkvisionFt=60,AiInfo=["Засада: Скрытность 20 против пассивного Восприятия героя 8"],Triggers=new(){Spotted=new(){Effect="Нападает из засады"}}};
        s.WorldState.Map.Entities=[enemy];
        var result=await Act(s,"жду",new AiContextBuilder(s.WorldState).TriggerState([(enemy,"spotted")],[],[]));
        Assert.False(s.WorldState.Map.Entities[0].Hidden);
        Assert.True(s.WorldState.Combat?.Active);
        Assert.True(result.RollDiceTriggered);
        Assert.Contains(s.WorldState.Hero.Effects??[],e=>e.Deleted!=true && e.Name.Contains("врасплох",StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0,s.WorldState.Hero.SpeedLeft);
    }

    [Fact]
    public async Task MatchingKeyOpensRevealedSecretDoorWithoutConsumingKey()
    {
        var s=Scene();
        s.WorldState.Hero.Light=new(){On=true,Ft=30};
        s.WorldState.Hero.Inventory=[new(){Name="Латунный ключ",Quantity=1,Description="От секретной двери с совой"}];
        s.WorldState.Map.Doors=[new(){From=[3,3],To=[4,3],IsDoor=true,Hidden=true,IsDoorOpen=false,AiInfo=["Секретная дверь уже найдена героем под тканью. Заперта обычным замком; латунный ключ героя точно подходит. Ловушек нет."]}];
        await Act(s,"отодвигаю ткань, показываю найденную секретную дверь, отпираю её латунным ключом и открываю");
        Assert.False(s.WorldState.Map.Doors[0].Hidden);
        Assert.True(s.WorldState.Map.Doors[0].IsDoorOpen);
        Assert.Contains(s.WorldState.Hero.Inventory,i=>i.Name=="Латунный ключ" && i.Deleted!=true);
    }

    [Fact]
    public async Task HideThenActiveSearchClearsStealth()
    {
        var s=Scene();
        s.WorldState.Hero.Skills=[new(){Name="Скрытность",Value="+3"}];
        s.WorldState.Map.Objects=[new(){Name="Большие ящики",Symbol="[C]",Position=[4,3],AiInfo=["Дают герою полное укрытие для скрытности, наблюдателей пока нет"]}];
        s.WorldState.Map.Rooms![0].AiInfo=["Тёмный зал, много больших ящиков, за которыми можно спрятаться. Никто сейчас не смотрит на героя."];
        var hide=await Act(s,"прячусь за большими ящиками в темноте");
        Assert.True(hide.RollDiceTriggered);
        Assert.True(s.WorldState.Hero.Stealth>=15);
        var enemy=new LivingEntity{Name="Ищейка",Symbol="SRH",Position=[5,3],DarkvisionFt=60,Hp="20/20",AiInfo=["Восприятие +30; активно ищет героя, результат проверки заведомо выше его Скрытности"],Triggers=new(){OnRound=new(){Effect="Активно обыскивает ящики и обнаруживает героя успешной проверкой Восприятия"}}};
        s.WorldState.Map.Entities=[enemy];
        await Act(s,"жду",new AiContextBuilder(s.WorldState).TriggerState([(enemy,"onRound")],[],[]));
        Assert.Null(s.WorldState.Hero.Stealth);
    }

    [Fact]
    public async Task LootAndDropCoinsDoesNotDuplicateOrLoseThem()
    {
        var s=Scene();s.WorldState.Hero.Light=new(){On=true,Ft=30};
        s.WorldState.Map.Objects=[new(){Name="Тело разбойника",Symbol="(B)",Position=[4,3],AiInfo=["В кошеле ровно 10 золотых монет. Других предметов нет."]}];
        await Act(s,"беру с тела четыре золотых монеты, из этих четырёх три кладу отдельной кучкой на пол рядом с собой; одну оставляю себе");
        var coins=s.WorldState.Hero.Inventory.Where(i=>i.Deleted!=true && i.Name.Contains("монет",StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(coins);Assert.Equal(1,coins[0].Quantity??1);
        Assert.Equal(2,s.WorldState.Map.Objects!.Count(o=>o.Deleted!=true));
        Assert.Contains(s.WorldState.Map.Objects,o=>o.Deleted!=true && o.Symbol!="(B)" && o.AiInfo!.Any(n=>n.Contains("3")||n.Contains("три",StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(s.WorldState.Map.Objects[0].AiInfo!,n=>n.Contains("6")||n.Contains("шесть",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PickingLockRequiresRollAndKeepsTools()
    {
        var s=Scene();s.WorldState.Hero.Light=new(){On=true,Ft=30};
        s.WorldState.Hero.Inventory=[new(){Name="Воровские инструменты",Description="Набор отмычек, герой владеет"}];
        s.WorldState.Hero.Skills=[new(){Name="Ловкость рук",Value="+6"}];
        s.WorldState.Map.Doors=[new(){From=[3,3],To=[4,3],IsDoor=true,IsDoorOpen=false,AiInfo=["Заперта обычным замком СЛ 10. После удачного взлома может быть открыта. Без ловушек."]}];
        var result=await Act(s,"вскрываю замок воровскими инструментами, затем открываю дверь");
        Assert.True(result.RollDiceTriggered);
        Assert.True(s.WorldState.Map.Doors[0].IsDoorOpen);
        Assert.Contains(s.WorldState.Hero.Inventory,i=>i.Name=="Воровские инструменты" && i.Deleted!=true);
    }

    [Fact]
    public async Task NpcRemembersFirstReplyAndThanksHeroForMedicineInSecondReply()
    {
        var s=Scene();s.WorldState.Narrative.Npcs=[new(){Name="Мира",Gender="female",Met=true,Attitude="нейтральная",Notes="Просила лекарство для сына"}];
        s.WorldState.Narrative.PlotThreads=[new(){Name="Лекарство",Description="Принести Мире лекарство",Status="активна"}];
        s.WorldState.Hero.Inventory=[new(){Name="Лекарство",Quantity=1}];
        await Act(s,"Мира, мой любимый цвет — янтарный. Запомни это.");
        Assert.Contains(s.WorldState.Narrative.Npcs[0].Memory??[],n=>n.Contains("янтар",StringComparison.OrdinalIgnoreCase));
        await Act(s,"Мира, какой мой любимый цвет? Передаю тебе обещанное лекарство для сына, этим выполняю твою просьбу.");
        Assert.DoesNotContain(s.WorldState.Hero.Inventory,i=>i.Deleted!=true && i.Name=="Лекарство");
        Assert.NotEqual("нейтральная",s.WorldState.Narrative.Npcs[0].Attitude);
        Assert.Equal("завершена",s.WorldState.Narrative.PlotThreads[0].Status);
        Assert.NotEmpty(s.WorldState.Narrative.PlotThreads[0].Steps??[]);
        Assert.Contains(s.WorldState.Narrative.Npcs[0].Memory??[],n=>n.Contains("лекарств",StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("sphere","Огненный шар",3,20)]
    [InlineData("cube","Волна грома",1,15)]
    public async Task AreaSpellUsesSelectionAndSpendsSlot(string shape,string spell,int level,int size)
    {
        var s=Scene();s.WorldState.Hero.Light=new(){On=true,Ft=60};
        s.WorldState.Hero.Spells=[new(){Name=spell,Level=level,Prepared=true}];
        s.WorldState.Hero.Resources=[new(){Name=$"Ячейки {level} ур.",Value="2/2",Category=ResourceCategories.Spells}];
        s.WorldState.Map.Entities=[new(){Name="Разбойник",Symbol="BND",Position=[4,3],Hp="100/100",AiInfo=["КД 12, спасброски +0"]}];
        var result=await Act(s,$"применяю {spell} в сторону разбойника; согласен, что область может задеть меня",target:(4,3));
        Assert.True(result.SelectTargetTriggered);
        Assert.Contains($"shape={shape}",result.ToolTrace);
        Assert.Equal("1/2",s.WorldState.Hero.Resources[0].Value);
        Assert.NotEqual("100/100",s.WorldState.Map.Entities[0].Hp);
    }

    [Fact]
    public async Task UpperFloorPopulationStaysOnRequestedFloor()
    {
        var s=Scene(); s.WorldState.Map.Cols=40;
        var upper=new MapChunk{X=1,Y=0,Z=1,Theme="building",Purpose="Тихий чердак с пустыми ящиками, без существ и NPC"};
        s.WorldState.Map.Chunks=[new(){X=0,Y=0,Z=0,Theme="building",Populated=true},upper];
        s.WorldState.Map.Rooms!.Add(new(){Name="Чердак",Positions=[[23,3],[24,3],[23,4],[24,4]]});
        var ctx=new AiContextBuilder(s.WorldState);
        var raw=await PromptScenario.CallRealAiAsync("Dnd5e","PopulateChunk",s,"lang:ru\nspeechEnabled:false\n"+ctx.PopulationState()+"\n"+ctx.LocationPlan(upper)+"\n"+ctx.ChunkState(upper));
        output.WriteLine(raw); PromptScenario.ApplyAndParse(s,raw);
        Assert.Equal(new[]{3,3},s.WorldState.Hero.Position);
        Assert.All((s.WorldState.Map.Entities??[]).Cast<CellEntity>().Concat(s.WorldState.Map.Objects??[]),e=>Assert.Equal(1,s.WorldState.Map.FloorAt(e.Position[0],e.Position[1])));
        Assert.Equal(0,s.WorldState.Map.FloorAt(3,3));
        Assert.False(string.IsNullOrWhiteSpace(s.WorldState.Map.Rooms[1].Name));
    }
}

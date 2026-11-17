using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using NaviDnD.Data.Models;
using NaviDnD.Display;

namespace NaviDnD.Tests;

public class ForestLegendRenderingTests
{
    [Fact]
    public void ForestLegendSeparatesFoliageTrunkAndReadableNameColors()
    {
        var render=typeof(LegendDisplay).GetMethod("WriteTerrainColumn",BindingFlags.NonPublic|BindingFlags.Static)!;
        var rows=new List<string>();
        foreach(char code in new[]{'T','F'}) foreach(bool hover in new[]{false,true})
        {
            var kind=TerrainCatalog.Get(code)!;
            var previous=Console.Out; using var output=new StringWriter();
            try { Console.SetOut(output); render.Invoke(null,[kind,hover?kind:null,36,new List<int>{18,20,24}]); }
            finally { Console.SetOut(previous); }
            var ansi=output.ToString(); rows.Add(ansi);
            var plain=Regex.Replace(ansi,"\u001b\\[[0-9;]*m","");
            Assert.Equal(36,plain.Length);
            Assert.StartsWith(kind.LegendGlyph,plain);
            Assert.Equal(hover,plain.EndsWith("←"));
            Assert.True(Regex.Matches(ansi,"\u001b\\[38;2;[0-9;]+m").Select(m=>m.Value).Distinct().Count()>=3);
        }
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null&&!File.Exists(Path.Combine(root.FullName,"scripts","build-release.ps1"))) root=root.Parent;
        Assert.NotNull(root);
        var path=Path.Combine(root.FullName,"artifacts","forest-polish-after"); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path,"legend.json"),JsonSerializer.Serialize(rows));
    }
}

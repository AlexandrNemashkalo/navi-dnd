using NaviDnD.Data.Art;

namespace NaviDnD.Tests;

public class ArtCatalogTests
{
    private static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static ArtAsset Asset(string id, string? family = null, string gender = "female", string usage = "portrait", string skin = "green") =>
        new(id, $"portraits/{id}.png", "pixel-32", family ?? id, "A pointed-eared face.",
            new HashSet<string> { usage }, new Dictionary<string, string> { ["gender"] = gender, ["skin"] = skin }, true, true, true, Hash(id));

    private static ArtRequest Request(string id, bool exclusive = true, string? gender = "female", string usage = "portrait") =>
        new(id, usage, "pixel-32", gender == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["gender"] = gender },
            new Dictionary<string, string>(), exclusive);

    [Theory]
    [InlineData("../portrait.png")]
    [InlineData("/portrait.png")]
    [InlineData("C:/portrait.png")]
    [InlineData("portraits\\face.png")]
    [InlineData("portraits//face.png")]
    [InlineData("portraits/./face.png")]
    [InlineData("portraits/face?.png")]
    [InlineData("portraits/CON.png")]
    [InlineData("portraits/lpt1.png")]
    [InlineData("portraits./face.png")]
    [InlineData("portraits /face.png")]
    public void UnsafePathsRejected(string path) =>
        Assert.Throws<ArgumentException>(() => new ArtCatalog("r1", [Asset("a") with { RelativePath = path }]));

    [Fact]
    public void DuplicateIdsRejected() => Assert.Throws<ArgumentException>(() => new ArtCatalog("r1", [Asset("a"), Asset("a")]));

    [Fact]
    public void CaseOnlyPathsRejectedForCrossPlatformPackages() =>
        Assert.Throws<ArgumentException>(() => new ArtCatalog("r1", [Asset("a"), Asset("b") with { RelativePath = "PORTRAITS/A.PNG" }]));

    [Fact]
    public void CatalogSnapshotsCallerOwnedTraitsAndUsages()
    {
        var traits = new Dictionary<string, string> { ["gender"] = "female" };
        var usages = new HashSet<string> { "portrait" };
        var catalog = new ArtCatalog("r1", [Asset("a") with { Traits = traits, Usages = usages }]);
        traits["gender"] = "male";
        usages.Clear();
        Assert.Equal(ArtSelectionStatus.Assigned, new ArtAssignmentBook(catalog, "world").Assign(Request("npc")).Status);
    }

    [Fact]
    public void StrictTraitsAndFramingNeverRelaxOnEmptyPool()
    {
        var catalog = new ArtCatalog("r1", [Asset("unknown", gender: "unknown"), Asset("man", gender: "male"), Asset("fullbody", usage: "map_actor")]);
        var book = new ArtAssignmentBook(catalog, "world");
        Assert.Equal(ArtSelectionStatus.Missing, book.Assign(Request("npc")).Status);
        Assert.Empty(book.Snapshot());
    }

    [Fact]
    public void UnknownCanBeUsedOnlyWhenNotARequiredFact()
    {
        var book = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("a", gender: "unknown")]), "world");
        Assert.Equal(ArtSelectionStatus.Assigned, book.Assign(Request("npc", gender: null)).Status);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void EveryApprovalIsRequired(bool technical, bool semantic, bool art)
    {
        var asset = Asset("a") with { TechnicalApproved = technical, SemanticApproved = semantic, ArtApproved = art };
        Assert.Equal(ArtSelectionStatus.Missing, new ArtAssignmentBook(new ArtCatalog("r1", [asset]), "w").Assign(Request("npc")).Status);
    }

    [Fact]
    public void StyleCannotBeOverriddenByPreference()
    {
        var book = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("a") with { Style = "other" }]), "w");
        Assert.Equal(ArtSelectionStatus.Missing, book.Assign(Request("npc")).Status);
    }

    [Fact]
    public void RecolorsCannotBypassExclusiveIdentity()
    {
        var book = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("a", "same-face"), Asset("b", "same-face")]), "w");
        Assert.Equal(ArtSelectionStatus.Assigned, book.Assign(Request("npc1")).Status);
        Assert.Equal("matching_identities_occupied", book.Assign(Request("npc2")).Reason);
        Assert.Single(book.Snapshot());
    }

    [Fact]
    public void ExistingAssignmentSurvivesRetryAndUnrelatedAssignments()
    {
        var book = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("a"), Asset("b"), Asset("c")]), "w");
        var original = book.Assign(Request("npc1")).Assignment;
        book.Assign(Request("npc2"));
        var retry = book.Assign(Request("npc1"));
        Assert.Equal(ArtSelectionStatus.Existing, retry.Status);
        Assert.Equal(original, retry.Assignment);
        Assert.Equal(2, book.Snapshot().Count);
    }

    [Fact]
    public void ContradictoryNewFactsDoNotReplaceAnExistingFace()
    {
        var book = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("a"), Asset("b", gender: "male")]), "w");
        var original = book.Assign(Request("npc")).Assignment;
        Assert.Equal(ArtSelectionStatus.Conflict, book.Assign(Request("npc", gender: "male")).Status);
        Assert.Equal(original, Assert.Single(book.Snapshot()));
    }

    [Fact]
    public void ItemsMayReuseButBalanceEquallySuitableFamilies()
    {
        var book = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("a", usage: "item"), Asset("b", usage: "item")]), "w");
        for (int i = 0; i < 20; i++) book.Assign(Request($"sword-{i}", false, usage: "item"));
        var counts = book.Snapshot().GroupBy(a => a.IdentityFamily).Select(g => g.Count()).ToArray();
        Assert.Equal(new[] { 10, 10 }, counts);
    }

    [Fact]
    public void ExclusiveReservationCannotBeBypassedWithNonexclusiveRequest()
    {
        var book = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("a")]), "w");
        book.Assign(Request("npc1"));
        Assert.Equal(ArtSelectionStatus.Missing, book.Assign(Request("npc2", false)).Status);
    }

    [Fact]
    public void PreferenceWinsOverUnusedButLessSuitableAsset()
    {
        var book = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("green"), Asset("blue", skin: "blue")]), "w");
        for (int i = 0; i < 5; i++)
        {
            var request = Request($"npc-{i}", false) with { Preferred = new Dictionary<string, string> { ["skin"] = "green" } };
            Assert.Equal("green", book.Assign(request).Assignment!.AssetId);
        }
    }

    [Fact]
    public void SelectionIndependentOfCatalogEnumerationOrder()
    {
        var assets = Enumerable.Range(0, 20).Select(i => Asset($"face-{i}")).ToArray();
        var first = new ArtAssignmentBook(new ArtCatalog("r1", assets), "world").Assign(Request("npc"));
        var second = new ArtAssignmentBook(new ArtCatalog("r1", assets.Reverse()), "world").Assign(Request("npc"));
        Assert.Equal(first.Assignment, second.Assignment);
    }

    [Fact]
    public void RestoredAssignmentsDoNotChangeWhenCatalogGrows()
    {
        var old = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("a"), Asset("b")]), "w");
        var assignment = old.Assign(Request("npc")).Assignment;
        var restored = new ArtAssignmentBook(new ArtCatalog("r2", [Asset("a"), Asset("b"), Asset("c")]), "w", old.Snapshot());
        Assert.Equal(assignment, restored.Assign(Request("npc")).Assignment);
    }

    [Fact]
    public void RemovedAssetStaysReservedInsteadOfSilentlyChanging()
    {
        var historic = new ArtAssignment("npc", "portrait", "removed", "family", "r0", true, Hash("removed"));
        var restored = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("recolor", "family")]), "w", [historic]);
        Assert.Equal("assigned_asset_unavailable", restored.Assign(Request("npc")).Reason);
        Assert.Equal(ArtSelectionStatus.Missing, restored.Assign(Request("new-npc")).Status);
        Assert.Equal(historic, Assert.Single(restored.Snapshot()));
    }

    [Fact]
    public void FailedBatchDoesNotLeakReservations()
    {
        var book = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("a")]), "w");
        var result = book.AssignBatch([Request("npc1"), Request("npc2")]);
        Assert.DoesNotContain(result, r => r.Status == ArtSelectionStatus.Assigned);
        Assert.Empty(book.Snapshot());
        Assert.Equal(ArtSelectionStatus.Assigned, book.Assign(Request("npc3")).Status);
    }

    [Fact]
    public void BatchOrderingAndRetryDoNotChangeAssignments()
    {
        var catalog = new ArtCatalog("r1", [Asset("a"), Asset("b"), Asset("c")]);
        var first = new ArtAssignmentBook(catalog, "w");
        var second = new ArtAssignmentBook(catalog, "w");
        first.AssignBatch([Request("npc2"), Request("npc1")]);
        second.AssignBatch([Request("npc1"), Request("npc2")]);
        Assert.Equal(first.Snapshot(), second.Snapshot());
        Assert.All(first.AssignBatch([Request("npc1"), Request("npc2")]), r => Assert.Equal(ArtSelectionStatus.Existing, r.Status));
    }

    [Fact]
    public void DuplicateBatchAndRestoreAreRejected()
    {
        var catalog = new ArtCatalog("r1", [Asset("a")]);
        var book = new ArtAssignmentBook(catalog, "w");
        Assert.Throws<ArgumentException>(() => book.AssignBatch([Request("npc"), Request("npc")]));
        var a = new ArtAssignment("one", "portrait", "a", "a", "r1", true, Hash("a"));
        Assert.Throws<ArgumentException>(() => new ArtAssignmentBook(catalog, "w", [a, a]));
        Assert.Throws<ArgumentException>(() => new ArtAssignmentBook(catalog, "w", [a, a with { SubjectId = "two" }]));
    }

    [Fact]
    public void ConcurrentAssignmentsCannotShareExclusiveFaces()
    {
        var book = new ArtAssignmentBook(new ArtCatalog("r1", Enumerable.Range(0, 8).Select(i => Asset($"face-{i}"))), "w");
        var results = new System.Collections.Concurrent.ConcurrentBag<ArtSelection>();
        Parallel.For(0, 30, i => results.Add(book.Assign(Request($"npc-{i}"))));
        Assert.Equal(8, results.Count(r => r.Status == ArtSelectionStatus.Assigned));
        Assert.Equal(8, book.Snapshot().Select(a => a.IdentityFamily).Distinct().Count());
    }

    [Fact]
    public void BatchReservesScarceMatchBeforeUnconstrainedRequest()
    {
        var book = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("female"), Asset("male", gender: "male")]), "w");
        var results = book.AssignBatch([Request("a-any", gender: null), Request("z-woman")]);
        Assert.All(results, r => Assert.Equal(ArtSelectionStatus.Assigned, r.Status));
        Assert.Equal("female", results[1].Assignment!.AssetId);
        Assert.Equal("male", results[0].Assignment!.AssetId);
    }

    [Fact]
    public void BatchCanShareIdentityAcrossSlotsOfSameSubject()
    {
        var asset = Asset("same") with { Usages = new HashSet<string> { "portrait", "journal" } };
        var book = new ArtAssignmentBook(new ArtCatalog("r1", [asset]), "w");
        Assert.All(book.AssignBatch([Request("npc"), Request("npc", usage: "journal")]), r => Assert.Equal(ArtSelectionStatus.Assigned, r.Status));
    }

    [Fact]
    public void SearchBudgetFailureDoesNotPretendPoolIsEmptyOrLeakFaces()
    {
        var book = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("a"), Asset("b")]), "w");
        Assert.All(book.AssignBatch([Request("one"), Request("two")], maxSearchSteps: 1),
            r => Assert.Equal("batch_search_budget_exhausted", r.Reason));
        Assert.Empty(book.Snapshot());
    }

    [Fact]
    public void ChangedImageBytesCannotSilentlyReplaceAnAssignedFace()
    {
        var original = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("a")]), "w");
        var assigned = original.Assign(Request("npc")).Assignment;
        var changed = Asset("a") with { ContentHash = Hash("different pixels") };
        var restored = new ArtAssignmentBook(new ArtCatalog("r2", [changed]), "w", original.Snapshot());
        var result = restored.Assign(Request("npc"));
        Assert.Equal(ArtSelectionStatus.Conflict, result.Status);
        Assert.Equal("assigned_asset_content_changed", result.Reason);
        Assert.Equal(assigned, result.Assignment);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ru-RU")]
    [InlineData("ar-SA")]
    public void StableShuffleUsesAnExplicitCrossProcessCultureIndependentKey(string culture)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var book = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("a"), Asset("b"), Asset("c")]), "world-42");
            Assert.Equal("b", book.Assign(Request("npc-17")).Assignment!.AssetId);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void CatalogJsonLoadsItsOwnStrictVersionedContract()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1, revision = "r1", assets = new[] { new
            {
                id = "face", relativePath = "faces/face.png", style = "pixel-32", identityFamily = "face",
                description = "A face.", usages = new[] { "portrait" }, traits = new { gender = "female" },
                technicalApproved = true, semanticApproved = true, artApproved = true, contentHash = Hash("pixels"),
            } },
        });
        var catalog = ArtCatalog.Parse(json);
        Assert.Equal("face", new ArtAssignmentBook(catalog, "w").Assign(Request("npc")).Assignment!.AssetId);
        Assert.Throws<System.Text.Json.JsonException>(() => ArtCatalog.Parse(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2")));
        Assert.Throws<System.Text.Json.JsonException>(() => ArtCatalog.Parse(json.Replace("\"revision\":\"r1\"", "\"revision\":\"r1\",\"unexpected\":true")));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("Unknown")]
    [InlineData("UNKNOWN")]
    public void UnknownNeverCountsAsAConfirmedTrait(string unknown)
    {
        var book = new ArtAssignmentBook(new ArtCatalog("r1", [Asset("face", gender: unknown)]), "world");
        Assert.Equal(ArtSelectionStatus.Missing, book.Assign(Request("npc", gender: unknown)).Status);
    }
}

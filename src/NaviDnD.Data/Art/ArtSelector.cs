namespace NaviDnD.Data.Art;

// Required traits are facts; preferences may affect ranking but can never override a fact.
public sealed record ArtRequest(
    string SubjectId, string Usage, string Style,
    IReadOnlyDictionary<string, string> Required,
    IReadOnlyDictionary<string, string> Preferred,
    bool ExclusiveIdentity = false);

public enum ArtSelectionStatus { Assigned, Existing, Missing, Conflict }

public sealed record ArtAssignment(
    string SubjectId, string Usage, string AssetId, string IdentityFamily,
    string CatalogRevision, bool ExclusiveIdentity, string ContentHash);

public sealed record ArtSelection(
    ArtSelectionStatus Status, ArtAssignment? Assignment, string Reason);

// One instance per world/draft; the caller commits Snapshot together with the game patch.
// No disk I/O, no model calls, no release-on-death, no process-dependent random seed.
public sealed class ArtAssignmentBook
{
    private readonly object _gate = new();
    private readonly ArtCatalog _catalog;
    private readonly string _worldSeed;
    private Dictionary<(string Subject, string Usage), ArtAssignment> _assignments = [];

    public ArtAssignmentBook(ArtCatalog catalog, string worldSeed, IEnumerable<ArtAssignment>? restored = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldSeed);
        _catalog = catalog;
        _worldSeed = worldSeed;
        foreach (var entry in restored ?? [])
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.SubjectId);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Usage);
            // A removed asset remains reserved: never silently reassign a historic face.
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.AssetId);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.IdentityFamily);
            if (entry.ContentHash == null || entry.ContentHash.Length != 64 || !entry.ContentHash.All(Uri.IsHexDigit))
                throw new ArgumentException($"Invalid assigned art content hash: {entry.AssetId}");
            if (catalog.Assets.TryGetValue(entry.AssetId, out var asset) && asset.IdentityFamily != entry.IdentityFamily)
                throw new ArgumentException($"Changed art identity family: {entry.AssetId}");
            if (!_assignments.TryAdd((entry.SubjectId, entry.Usage), entry))
                throw new ArgumentException($"Duplicate art assignment: {entry.SubjectId}/{entry.Usage}");
        }
        // Exclusive identity is world-wide, including across different visual slots.
        foreach (var family in _assignments.Values.GroupBy(a => a.IdentityFamily))
            if (family.Any(a => a.ExclusiveIdentity) && family.Select(a => a.SubjectId).Distinct(StringComparer.Ordinal).Count() > 1)
                throw new ArgumentException($"Duplicate exclusive art identity: {family.Key}");
    }

    public IReadOnlyList<ArtAssignment> Snapshot()
    {
        lock (_gate)
            return _assignments.Values.OrderBy(a => a.SubjectId, StringComparer.Ordinal)
                .ThenBy(a => a.Usage, StringComparer.Ordinal).ToArray();
    }

    public ArtSelection Assign(ArtRequest request)
    {
        lock (_gate) return AssignInto(_assignments, Freeze(request));
    }

    // All-or-nothing draft: validation or pool exhaustion cannot leak reservations.
    public IReadOnlyList<ArtSelection> AssignBatch(IEnumerable<ArtRequest> requests, int maxSearchSteps = 10000)
    {
        var batch = requests.Take(65).Select(Freeze).ToArray();
        if (batch.Length > 64 || maxSearchSteps < 1)
            throw new ArgumentException("Art batch exceeds supported limits.");
        if (batch.Select(r => (r.SubjectId, r.Usage)).Distinct().Count() != batch.Length)
            throw new ArgumentException("Duplicate subject/usage in art batch.");
        lock (_gate)
        {
            var draft = new Dictionary<(string, string), ArtAssignment>(_assignments);
            var results = new Dictionary<(string, string), ArtSelection>();
            var remaining = new List<ArtRequest>();
            foreach (var request in batch)
            {
                if (draft.ContainsKey((request.SubjectId, request.Usage)))
                    results[(request.SubjectId, request.Usage)] = AssignInto(draft, request);
                else remaining.Add(request);
            }
            if (results.Values.Any(r => r.Status != ArtSelectionStatus.Existing))
            {
                foreach (var request in remaining)
                    results[(request.SubjectId, request.Usage)] = new(ArtSelectionStatus.Conflict, null, "batch_existing_assignment_conflict");
                return batch.Select(r => results[(r.SubjectId, r.Usage)]).ToArray();
            }

            var occupancy = draft.Values.GroupBy(a => a.IdentityFamily)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            // Precompute image eligibility, preference score and stable shuffle once.
            var candidates = remaining.ToDictionary(r => (r.SubjectId, r.Usage), r =>
                _catalog.Assets.Values.Where(a => Eligible(a, r)).Select(a =>
                    (Asset: a, Score: r.Preferred.Count(t => a.Traits.TryGetValue(t.Key, out var v) && v == t.Value),
                     Shuffle: ArtCatalog.StableKey("art-v2-1", _worldSeed, r.SubjectId, r.Usage, a.Id))).ToArray());
            bool Available(ArtAsset asset, ArtRequest request) =>
                !occupancy.TryGetValue(asset.IdentityFamily, out var uses) ||
                !uses.Any(u => u.SubjectId != request.SubjectId && (request.ExclusiveIdentity || u.ExclusiveIdentity));
            int Uses(ArtAsset asset) => occupancy.TryGetValue(asset.IdentityFamily, out var uses) ? uses.Count : 0;
            int steps = 0;
            bool exhausted = false;
            bool Solve()
            {
                if (remaining.Count == 0) return true;
                // Reserve scarce matches first, and backtrack if another subject
                // needs a family chosen earlier. Alphabetical greedy assignment
                // can otherwise report "missing" despite a valid complete match.
                var request = remaining.OrderBy(r => candidates[(r.SubjectId, r.Usage)]
                        .Where(c => Available(c.Asset, r)).Select(c => c.Asset.IdentityFamily).Distinct(StringComparer.Ordinal).Count())
                    .ThenBy(r => r.SubjectId, StringComparer.Ordinal).ThenBy(r => r.Usage, StringComparer.Ordinal).First();
                var key = (request.SubjectId, request.Usage);
                var options = candidates[key].Where(c => Available(c.Asset, request))
                    .OrderByDescending(c => c.Score).ThenBy(c => Uses(c.Asset))
                    .ThenBy(c => c.Shuffle, StringComparer.Ordinal).ThenBy(c => c.Asset.Id, StringComparer.Ordinal).ToArray();
                if (options.Length == 0) return false;
                remaining.Remove(request);
                foreach (var option in options)
                {
                    if (steps++ >= maxSearchSteps) { exhausted = true; break; }
                    var asset = option.Asset;
                    var assignment = new ArtAssignment(request.SubjectId, request.Usage, asset.Id, asset.IdentityFamily, _catalog.Revision, request.ExclusiveIdentity, asset.ContentHash);
                    draft.Add(key, assignment);
                    if (!occupancy.TryGetValue(asset.IdentityFamily, out var uses)) occupancy[asset.IdentityFamily] = uses = [];
                    uses.Add(assignment);
                    if (Solve()) return true;
                    uses.RemoveAt(uses.Count - 1);
                    draft.Remove(key);
                    if (exhausted) break;
                }
                remaining.Add(request);
                return false;
            }

            bool solved = Solve();
            foreach (var request in batch.Where(r => !results.ContainsKey((r.SubjectId, r.Usage))))
            {
                var key = (request.SubjectId, request.Usage);
                results[key] = solved ? new(ArtSelectionStatus.Assigned, draft[key], "approved_batch_match") :
                    new(exhausted ? ArtSelectionStatus.Conflict : ArtSelectionStatus.Missing, null,
                        exhausted ? "batch_search_budget_exhausted" : "batch_no_feasible_assignment");
            }
            if (solved) _assignments = draft;
            return batch.Select(r => results[(r.SubjectId, r.Usage)]).ToArray();
        }
    }

    private ArtSelection AssignInto(Dictionary<(string Subject, string Usage), ArtAssignment> assignments, ArtRequest request)
    {
        var key = (request.SubjectId, request.Usage);
        if (assignments.TryGetValue(key, out var existing))
        {
            if (!_catalog.Assets.TryGetValue(existing.AssetId, out var oldAsset))
                return new(ArtSelectionStatus.Missing, existing, "assigned_asset_unavailable");
            if (!string.Equals(existing.ContentHash, oldAsset.ContentHash, StringComparison.OrdinalIgnoreCase))
                return new(ArtSelectionStatus.Conflict, existing, "assigned_asset_content_changed");
            if (!Eligible(oldAsset, request) || request.ExclusiveIdentity != existing.ExclusiveIdentity)
                return new(ArtSelectionStatus.Conflict, existing, "existing_assignment_conflicts_with_request");
            return new(ArtSelectionStatus.Existing, existing, "existing_assignment_preserved");
        }

        var eligible = _catalog.Assets.Values.Where(a => Eligible(a, request)).ToArray();
        if (eligible.Length == 0) return new(ArtSelectionStatus.Missing, null, "no_approved_match");
        var familyUses = assignments.Values.GroupBy(a => a.IdentityFamily)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var available = eligible.Where(a => !familyUses.TryGetValue(a.IdentityFamily, out var used) ||
            !used.Any(u => u.SubjectId != request.SubjectId && (request.ExclusiveIdentity || u.ExclusiveIdentity))).ToArray();
        if (available.Length == 0) return new(ArtSelectionStatus.Missing, null, "matching_identities_occupied");
        int PreferenceScore(ArtAsset a) => request.Preferred.Count(t =>
            a.Traits.TryGetValue(t.Key, out var value) && value == t.Value);
        int Uses(ArtAsset a) => familyUses.TryGetValue(a.IdentityFamily, out var used) ? used.Length : 0;
        // Only equal-quality candidates compete on usage; novelty never defeats known requirements.
        var selected = available.OrderByDescending(PreferenceScore).ThenBy(Uses)
            .ThenBy(a => ArtCatalog.StableKey("art-v2-1", _worldSeed, request.SubjectId, request.Usage, a.Id), StringComparer.Ordinal)
            .ThenBy(a => a.Id, StringComparer.Ordinal).First();
        var assignment = new ArtAssignment(request.SubjectId, request.Usage, selected.Id,
            selected.IdentityFamily, _catalog.Revision, request.ExclusiveIdentity, selected.ContentHash);
        assignments.Add(key, assignment);
        return new(ArtSelectionStatus.Assigned, assignment, "approved_match");
    }

    private static bool Eligible(ArtAsset asset, ArtRequest request) =>
        asset.TechnicalApproved && asset.SemanticApproved && asset.ArtApproved &&
        asset.Style == request.Style && asset.Usages.Contains(request.Usage) &&
        request.Required.All(t => !string.Equals(t.Value, "unknown", StringComparison.OrdinalIgnoreCase) &&
            asset.Traits.TryGetValue(t.Key, out var value) && value == t.Value);

    private static ArtRequest Freeze(ArtRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Required);
        ArgumentNullException.ThrowIfNull(request.Preferred);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Usage);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Style);
        if (request.Required.Concat(request.Preferred).Any(t => string.IsNullOrWhiteSpace(t.Key) || string.IsNullOrWhiteSpace(t.Value)))
            throw new ArgumentException("Empty art request trait.");
        return request with
        {
            Required = request.Required.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal),
            Preferred = request.Preferred.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal),
        };
    }
}

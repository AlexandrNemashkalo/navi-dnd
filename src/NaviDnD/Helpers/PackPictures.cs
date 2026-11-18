namespace NaviDnD.Helpers;

// The engine's own pictures (terrain, furniture, doors, places and biomes of the world map, journal) are written
// as game-icons slugs. In a game of the Dungeon Crawl pack they are drawn by its tiles - the whole screen in one
// style. Only what the pack has: there is no furniture, fence or bridge in it - those stay game-icons. Games of
// the game-icons pack (and all old games) are not affected.
public static class PackPictures
{
    private static readonly Dictionary<string, string> Dcss = new()
    {
        // Terrain cells (Terrain.Images) and furniture (Furniture.Kinds).
        ["delapouite/berry-bush"] = "dcss/monster/fungi_plants/bush_2",
        ["lorc/oak"] = "dcss/dungeon/trees/mangrove_1",
        ["delapouite/forest"] = "dcss/dungeon/trees/tree_2_yellow",
        ["lorc/pine-tree"] = "dcss/dungeon/trees/tree_1_red",
        ["delapouite/stone-stack"] = "dcss/item/misc/misc_stone_old",
        ["delapouite/falling-rocks"] = "dcss/dungeon/boulder",
        ["lorc/ribcage"] = "dcss/item/food/bone",
        ["delapouite/carrot"] = "dcss/monster/fungi_plants/plant",
        ["delapouite/cactus"] = "dcss/monster/fungi_plants/plant",
        ["delapouite/well"] = "dcss/dungeon/blue_fountain",
        ["delapouite/shop"] = "dcss/dungeon/shops/shop_general",
        ["delapouite/bookshelf"] = "dcss/dungeon/shops/shop_books",
        ["delapouite/barrel"] = "dcss/dungeon/large_box",
        ["delapouite/fireplace"] = "dcss/dungeon/wall/torches/torch_0",
        ["delapouite/star-altar"] = "dcss/dungeon/altars/altar_new",
        ["delapouite/3d-stairs"] = "dcss/dungeon/gateways/stone_stairs_down",
        ["delapouite/stalactites"] = "dcss/dungeon/statues/granite_stump_new",
        ["delapouite/ancient-columns"] = "dcss/dungeon/statues/crumbled_column",
        // Doors, gates and exits (MouseUiHelper.DoorImage).
        ["delapouite/door"] = "dcss/dungeon/doors/closed_door",
        ["lorc/doorway"] = "dcss/dungeon/doors/open_door",
        ["delapouite/gate"] = "dcss/dungeon/vaults/grate",
        ["delapouite/window-bars"] = "dcss/dungeon/vaults/grate",
        ["delapouite/open-gate"] = "dcss/dungeon/gateways/stone_arch",
        ["delapouite/exit-door"] = "dcss/dungeon/gateways/entrance",
        ["delapouite/underground-cave"] = "dcss/dungeon/gateways/rock_stairs_down",
        ["delapouite/mountain-road"] = "dcss/dungeon/gateways/enter",
        ["delapouite/forest-entrance"] = "dcss/dungeon/trees/mangrove_1",
        // Places of the world map (WorldMapView).
        ["delapouite/castle"] = "dcss/dungeon/gateways/enter_zot_closed_new",
        ["delapouite/tower-flag"] = "dcss/dungeon/gateways/enter_vaults_closed",
        ["delapouite/watchtower"] = "dcss/dungeon/gateways/entrance",
        ["delapouite/family-house"] = "dcss/dungeon/gateways/enter_orc",
        ["delapouite/village"] = "dcss/dungeon/shops/shop_general",
        ["delapouite/broken-wall"] = "dcss/dungeon/statues/crumbled_column",
        ["delapouite/crypt-entrance"] = "dcss/dungeon/gateways/enter_crypt",
        ["delapouite/cave-entrance"] = "dcss/dungeon/gateways/enter_lair",
        ["delapouite/greek-temple"] = "dcss/dungeon/altars/altar_new",
        // Journal and default pictures.
        ["lorc/scroll-unfurled"] = "dcss/item/scroll/scroll-brown",
        ["lorc/quill-ink"] = "dcss/item/book/book_dog_eared",
        ["lorc/open-book"] = "dcss/item/book/book_dog_eared",
        ["delapouite/spell-book"] = "dcss/item/book/book_indigo",
        ["delapouite/person"] = "dcss/monster/human_new",
    };

    // The picture of the engine in the pack of the current game (CatalogSearch.IconPack).
    public static string ForPack(string slug) =>
        CatalogSearch.IconPack != CatalogSearch.DcssPack ? slug :
        ArtPictures.Current?.EngineIcon(slug) ?? (Dcss.TryGetValue(slug, out var tile)
            && File.Exists(Path.Combine(CatalogSearch.DcssBasePath, tile[CatalogSearch.DcssPrefix.Length..] + ".png")) ? tile : slug);

    // For the test: all tiles of the table exist.
    public static IEnumerable<string> DcssTiles => Dcss.Values;
}

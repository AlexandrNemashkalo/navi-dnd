namespace NaviDnD;

// Version of data file format (saves, draft, hero cards, worlds with locations, notes) - field formatVersion.
// New files are written by the current version; old ones are rewritten by NaviDnD.Migrations.StorageMigration
// (when updating the game). Changed the data format - increase Current and add a migration step.
public static class StorageFormat
{
    public const int Current = 3;
}

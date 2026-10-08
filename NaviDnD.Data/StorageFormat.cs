namespace NaviDnD;

// Версия формата файлов данных (сохранения, черновик, карточки героев, миры с локациями, заметки) — поле
// formatVersion. Новые файлы пишутся текущей версией; старые переписывает NaviDnD.Migrations.StorageMigration
// (при обновлении игры). Изменил формат данных — увеличь Current и добавь шаг миграции.
public static class StorageFormat
{
    public const int Current = 1;
}

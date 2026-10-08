// Язык интерфейса и мира (L), сохранения и папка Storage — общие на процесс: тесты, которые их меняют, не должны
// идти параллельно с остальными. Набор быстрый — последовательно.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

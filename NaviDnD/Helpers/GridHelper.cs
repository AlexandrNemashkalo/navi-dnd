namespace NaviDnD.Helpers;

public class GridHelper
{
    public static bool HasAdjacentTwos(List<int> arr)
    {
        int n = arr.Count;

        // Проверяем все соседние пары
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n; // Следующий индекс с замыканием на начало

            if (arr[i] == 2 && arr[next] == 2)
                return true;
        }

        return false;
    }

    public static List<int> Transform1112List(List<int> list)
    {
        // Находим индекс двойки
        int twoIndex = list.IndexOf(2);
        if (twoIndex == -1) return list;

        // Копируем список
        List<int> result = new List<int>(list);

        // Проходим по всем индексам
        for (int i = 0; i < 4; i++)
        {
            // Пропускаем двойку и её соседей
            if (i == twoIndex ||
                i == (twoIndex + 1) % 4 ||
                i == (twoIndex + 3) % 4)  // (twoIndex - 1 + 4) % 4
            {
                continue;
            }

            // Если нашли единицу не рядом - меняем на 0
            if (result[i] == 1)
            {
                result[i] = 0;
                return result;
            }
        }

        return result;
    }
}

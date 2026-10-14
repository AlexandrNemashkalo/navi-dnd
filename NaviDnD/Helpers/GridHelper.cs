namespace NaviDnD.Helpers;

public class GridHelper
{
    public static bool HasAdjacentTwos(List<int> arr)
    {
        int n = arr.Count;

        // Checking all neighboring pairs
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n; // Next index with loop to start

            if (arr[i] == 2 && arr[next] == 2)
                return true;
        }

        return false;
    }

    public static List<int> Transform1112List(List<int> list)
    {
        // Finding the index of two
        int twoIndex = list.IndexOf(2);
        if (twoIndex == -1) return list;

        // Copying the list
        List<int> result = new List<int>(list);

        // We go through all indexes
        for (int i = 0; i < 4; i++)
        {
            // We skip the deuce and its neighbors
            if (i == twoIndex ||
                i == (twoIndex + 1) % 4 ||
                i == (twoIndex + 3) % 4)  // (twoIndex - 1 + 4) % 4
            {
                continue;
            }

            // If you find the unit is not nearby, change it to 0
            if (result[i] == 1)
            {
                result[i] = 0;
                return result;
            }
        }

        return result;
    }
}

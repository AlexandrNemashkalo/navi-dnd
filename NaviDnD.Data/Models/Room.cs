namespace NaviDnD.Data.Models;

public class Room : Area
{
    // Passage (corridor, manhole, gallery) - places a generator; The name is in the language of the world and
    // may change. The passages are not signed in the legend; partition doors are not placed in them and they
    // are not selected for starting.
    public bool? Passage { get; set; }
}

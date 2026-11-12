using Aether.Core.Playback;

namespace Aether.Core.Tests;

public class PlaylistStoreTests
{
    private static PlaylistStore Loaded(string current, params string[] items)
    {
        var store = new PlaylistStore(new Random(7));
        store.Load(items, current);
        return store;
    }

    private static List<string> PlayThrough(PlaylistStore store, int count)
    {
        var played = new List<string> { store.Current! };
        for (int i = 1; i < count; i++)
        {
            played.Add(store.Next(ended: true)!);
        }
        return played;
    }

    [Fact]
    public void Load_Selects_The_Current_Item_Ignoring_Case()
    {
        PlaylistStore store = Loaded(@"C:\SHOW\B.MP4", @"C:\show\a.mp4", @"C:\show\b.mp4");

        Assert.Equal(@"C:\show\b.mp4", store.Current);
        Assert.Equal(1, store.CurrentIndex);
    }

    [Fact]
    public void Load_Without_The_Current_Item_Starts_Nowhere()
    {
        PlaylistStore store = Loaded("gone.mp4", "a.mp4", "b.mp4");

        Assert.Null(store.Current);
        Assert.Equal(-1, store.CurrentIndex);
        Assert.Equal("a.mp4", store.Next(ended: false));
    }

    [Fact]
    public void Next_And_Previous_Walk_The_List_And_Stop_At_The_Ends()
    {
        PlaylistStore store = Loaded("a.mp4", "a.mp4", "b.mp4", "c.mp4");

        Assert.False(store.CanGoPrevious);
        Assert.Null(store.Previous());
        Assert.Equal("b.mp4", store.Next(ended: false));
        Assert.Equal("c.mp4", store.Next(ended: true));
        Assert.False(store.CanGoNext);
        Assert.Null(store.Next(ended: true));
        Assert.Equal("c.mp4", store.Current);
        Assert.Equal("b.mp4", store.Previous());
        Assert.True(store.CanGoPrevious);
        Assert.True(store.CanGoNext);
    }

    [Fact]
    public void Repeat_All_Wraps_Both_Ways()
    {
        PlaylistStore store = Loaded("c.mp4", "a.mp4", "b.mp4", "c.mp4");
        store.Repeat = RepeatMode.All;

        Assert.True(store.CanGoNext);
        Assert.Equal("a.mp4", store.Next(ended: true));
        Assert.Equal("c.mp4", store.Previous());
    }

    [Fact]
    public void Repeat_One_Replays_At_The_End_Of_The_File_But_A_Skip_Moves_On()
    {
        PlaylistStore store = Loaded("a.mp4", "a.mp4", "b.mp4");
        store.Repeat = RepeatMode.One;

        Assert.Equal("a.mp4", store.Next(ended: true));
        Assert.Equal("b.mp4", store.Next(ended: false));
        Assert.Equal("b.mp4", store.Next(ended: true));
    }

    [Fact]
    public void Repeat_One_Wraps_A_Skip_From_The_Last_Item()
    {
        PlaylistStore store = Loaded("b.mp4", "a.mp4", "b.mp4");
        store.Repeat = RepeatMode.One;

        Assert.Equal("a.mp4", store.Next(ended: false));
    }

    [Fact]
    public void Empty_Queue_Goes_Nowhere()
    {
        var store = new PlaylistStore();

        Assert.Null(store.Next(ended: true));
        Assert.Null(store.Previous());
        Assert.False(store.CanGoNext);
        Assert.False(store.CanGoPrevious);
        Assert.Empty(store.Items);
    }

    [Fact]
    public void Shuffle_Plays_Every_Item_Once_Starting_From_The_Current_One()
    {
        string[] items = Enumerable.Range(1, 20).Select(i => $"ep{i}.mkv").ToArray();
        PlaylistStore store = Loaded("ep5.mkv", items);

        store.Shuffle = true;
        List<string> played = PlayThrough(store, items.Length);

        Assert.Equal("ep5.mkv", played[0]);
        Assert.Equal(items.Order(), played.Order());
        Assert.NotEqual(items.Skip(4).Concat(items.Take(4)), played);
        Assert.Null(store.Next(ended: true));
    }

    [Fact]
    public void Shuffle_With_Repeat_All_Reshuffles_Each_Round_Without_Replaying_The_Last_Item()
    {
        string[] items = Enumerable.Range(1, 6).Select(i => $"{i}.mp4").ToArray();
        for (int seed = 0; seed < 50; seed++)
        {
            var store = new PlaylistStore(new Random(seed));
            store.Load(items, "1.mp4");
            store.Shuffle = true;
            store.Repeat = RepeatMode.All;

            List<string> first = PlayThrough(store, items.Length);
            string next = store.Next(ended: true)!;
            List<string> second = [next, .. PlayThrough(store, items.Length).Skip(1)];

            Assert.Equal(items.Order(), first.Order());
            Assert.Equal(items.Order(), second.Order());
            Assert.NotEqual(first[^1], second[0]);
        }
    }

    [Fact]
    public void Previous_Retraces_The_Shuffled_Order()
    {
        PlaylistStore store = Loaded("a.mp4", "a.mp4", "b.mp4", "c.mp4", "d.mp4");
        store.Shuffle = true;
        List<string> played = PlayThrough(store, 4);

        Assert.Equal(played[2], store.Previous());
        Assert.Equal(played[1], store.Previous());
        Assert.Equal(played[0], store.Previous());
        Assert.Null(store.Previous());
    }

    [Fact]
    public void Turning_Shuffle_Off_Continues_In_List_Order_From_The_Current_Item()
    {
        PlaylistStore store = Loaded("b.mp4", "a.mp4", "b.mp4", "c.mp4", "d.mp4");
        store.Shuffle = true;
        store.Shuffle = false;

        Assert.Equal("c.mp4", store.Next(ended: true));
        Assert.Equal("b.mp4", store.Previous());
    }

    [Fact]
    public void Reloading_The_Same_Listing_Keeps_The_Shuffled_Order()
    {
        string[] items = Enumerable.Range(1, 10).Select(i => $"{i}.mp4").ToArray();
        PlaylistStore store = Loaded("1.mp4", items);
        store.Shuffle = true;
        string second = store.Next(ended: true)!;
        string third = store.Next(ended: true)!;

        store.Load(items.Select(i => i.ToUpperInvariant()), second);

        Assert.Equal(second, store.Current);
        Assert.Equal(third, store.Next(ended: true));
    }

    [Fact]
    public void Reloading_A_Changed_Listing_Starts_A_New_Order()
    {
        PlaylistStore store = Loaded("a.mp4", "a.mp4", "b.mp4");

        store.Load(["a.mp4", "b.mp4", "c.mp4"], "b.mp4");

        Assert.Equal(["a.mp4", "b.mp4", "c.mp4"], store.Items);
        Assert.Equal("c.mp4", store.Next(ended: true));
    }

    [Fact]
    public void The_Same_File_Can_Be_Queued_Twice()
    {
        PlaylistStore store = Loaded("a.mp4", "a.mp4", "b.mp4");
        store.Enqueue(["a.mp4"]);

        Assert.Equal(["a.mp4", "b.mp4", "a.mp4"], store.Items);
        Assert.Equal("b.mp4", store.Next(ended: true));
        Assert.Equal("a.mp4", store.Next(ended: true));
        Assert.Equal(2, store.CurrentIndex);
    }

    [Fact]
    public void Enqueue_Into_An_Empty_Store_Leaves_Nothing_Playing()
    {
        var store = new PlaylistStore();
        store.Enqueue(["a.mp4", "b.mp4"]);

        Assert.Null(store.Current);
        Assert.Equal("a.mp4", store.Next(ended: false));
    }

    [Fact]
    public void Removing_The_Current_Item_Moves_Next_To_What_Followed_It()
    {
        PlaylistStore store = Loaded("b.mp4", "a.mp4", "b.mp4", "c.mp4");

        store.RemoveAt(1);

        Assert.Null(store.Current);
        Assert.Equal(["a.mp4", "c.mp4"], store.Items);
        Assert.Equal("c.mp4", store.Next(ended: true));
    }

    [Fact]
    public void Removing_Another_Item_Keeps_The_Current_One()
    {
        PlaylistStore store = Loaded("b.mp4", "a.mp4", "b.mp4", "c.mp4");

        store.RemoveAt(0);

        Assert.Equal("b.mp4", store.Current);
        Assert.Equal(0, store.CurrentIndex);
        Assert.False(store.CanGoPrevious);
    }

    [Fact]
    public void Move_Reorders_The_List_And_The_Play_Order()
    {
        PlaylistStore store = Loaded("a.mp4", "a.mp4", "b.mp4", "c.mp4");

        store.Move(2, 0);

        Assert.Equal(["c.mp4", "a.mp4", "b.mp4"], store.Items);
        Assert.Equal(1, store.CurrentIndex);
        Assert.Equal("c.mp4", store.Previous());
    }

    [Fact]
    public void Clear_Empties_The_Queue()
    {
        PlaylistStore store = Loaded("a.mp4", "a.mp4", "b.mp4");

        store.Clear();

        Assert.Empty(store.Items);
        Assert.Null(store.Current);
    }

    [Fact]
    public void Select_Plays_An_Item_From_The_List()
    {
        PlaylistStore store = Loaded("a.mp4", "a.mp4", "b.mp4", "c.mp4");

        Assert.Equal("c.mp4", store.Select(2));
        Assert.Equal("b.mp4", store.Previous());
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Select(3));
    }

    [Fact]
    public void Concurrent_Edits_And_Navigation_Keep_The_Queue_Whole()
    {
        string[] items = Enumerable.Range(0, 50).Select(i => $"{i}.mp4").ToArray();
        PlaylistStore store = Loaded("0.mp4", items);
        store.Repeat = RepeatMode.All;

        Parallel.For(0, 2000, i =>
        {
            switch (i % 4)
            {
                case 0: store.Next(ended: true); break;
                case 1: store.Previous(); break;
                case 2: store.Shuffle = i % 8 == 2; break;
                default: store.Move(i % items.Length, (i * 7) % items.Length); break;
            }
        });

        Assert.Equal(items.Order(), store.Items.Order());
        Assert.NotNull(store.Current);
    }
}

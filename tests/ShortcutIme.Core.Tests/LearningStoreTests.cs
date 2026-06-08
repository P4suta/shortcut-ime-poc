namespace ShortcutIme.Core.Tests;

public class LearningStoreTests
{
    [Fact]
    public void Record_MakesLatestSelectionRankHighest()
    {
        var store = new LearningStore();

        store.Record("共有");
        store.Record("許容");

        Assert.True(store.RecencyOf("許容") > store.RecencyOf("共有"));
        Assert.Equal(0, store.RecencyOf("未選択"));
    }

    [Fact]
    public void SaveLoad_RoundTripsRecency()
    {
        var path = Path.Combine(Path.GetTempPath(), $"learning-test-{Guid.NewGuid():N}.json");
        try
        {
            var store = new LearningStore();
            store.Record("共有");
            store.Record("許容");
            store.Save(path);

            var loaded = LearningStore.Load(path);

            Assert.Equal(store.RecencyOf("許容"), loaded.RecencyOf("許容"));
            Assert.True(loaded.RecencyOf("許容") > loaded.RecencyOf("共有"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

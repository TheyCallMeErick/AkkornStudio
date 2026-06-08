using System.Data;
using AkkornStudio.UI.Services.SqlEditor.Results;

namespace AkkornStudio.Tests.Unit.Services.SqlEditor;

public sealed class SqlResultColumnProfilingServiceTests
{
    [Fact]
    public async Task BuildProfilesAsync_WhenNumericColumn_ComputesMinMaxAverageAndNulls()
    {
        var table = new DataTable();
        table.Columns.Add("amount", typeof(double));
        table.Rows.Add(10d);
        table.Rows.Add(20d);
        table.Rows.Add(DBNull.Value);

        var sut = new SqlResultColumnProfilingService();
        IReadOnlyList<SqlResultColumnProfile> profiles = await sut.BuildProfilesAsync(table);

        SqlResultColumnProfile profile = Assert.Single(profiles);
        Assert.Equal(SqlResultColumnProfileKind.Numeric, profile.Kind);
        Assert.Equal(3, profile.RowCount);
        Assert.Equal(1, profile.NullCount);
        Assert.Equal(10d, profile.NumericMin);
        Assert.Equal(20d, profile.NumericMax);
        Assert.Equal(15d, profile.NumericAverage);
    }

    [Fact]
    public async Task BuildProfilesAsync_WhenTextColumn_ComputesNullsEmptiesDistinctAndTopValues()
    {
        var table = new DataTable();
        table.Columns.Add("name", typeof(string));
        table.Rows.Add("alice");
        table.Rows.Add("bob");
        table.Rows.Add("alice");
        table.Rows.Add(string.Empty);
        table.Rows.Add(DBNull.Value);

        var sut = new SqlResultColumnProfilingService();
        IReadOnlyList<SqlResultColumnProfile> profiles = await sut.BuildProfilesAsync(table);

        SqlResultColumnProfile profile = Assert.Single(profiles);
        Assert.Equal(SqlResultColumnProfileKind.Text, profile.Kind);
        Assert.Equal(1, profile.NullCount);
        Assert.Equal(1, profile.EmptyCount);
        Assert.Equal(3, profile.DistinctCount);
        Assert.Contains("alice (2)", profile.TopValuesSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildProfilesAsync_WhenTemporalColumn_ComputesMinMaxAndFutureSuspicion()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset yesterday = now.AddDays(-1);
        DateTimeOffset tomorrow = now.AddDays(1);

        var table = new DataTable();
        table.Columns.Add("created_at", typeof(DateTimeOffset));
        table.Rows.Add(yesterday);
        table.Rows.Add(tomorrow);
        table.Rows.Add(DBNull.Value);

        var sut = new SqlResultColumnProfilingService();
        IReadOnlyList<SqlResultColumnProfile> profiles = await sut.BuildProfilesAsync(table);

        SqlResultColumnProfile profile = Assert.Single(profiles);
        Assert.Equal(SqlResultColumnProfileKind.Temporal, profile.Kind);
        Assert.Equal(1, profile.NullCount);
        Assert.Equal(yesterday, profile.TemporalMin);
        Assert.Equal(tomorrow, profile.TemporalMax);
        Assert.Equal(1, profile.SuspectFutureValueCount);
    }

    [Fact]
    public async Task BuildProfilesAsync_WhenValuesContainNulls_TracksNullCountPerColumn()
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("description", typeof(string));
        table.Rows.Add(1, DBNull.Value);
        table.Rows.Add(DBNull.Value, "row-2");
        table.Rows.Add(3, "row-3");

        var sut = new SqlResultColumnProfilingService();
        IReadOnlyList<SqlResultColumnProfile> profiles = await sut.BuildProfilesAsync(table);

        SqlResultColumnProfile idProfile = Assert.Single(profiles.Where(profile => profile.ColumnName == "id"));
        SqlResultColumnProfile textProfile = Assert.Single(profiles.Where(profile => profile.ColumnName == "description"));
        Assert.Equal(1, idProfile.NullCount);
        Assert.Equal(1, textProfile.NullCount);
    }

    [Fact]
    public async Task BuildProfilesAsync_WhenDatasetIsLarge_ProcessesAllRows()
    {
        const int totalRows = 25000;
        var table = new DataTable();
        table.Columns.Add("value", typeof(int));
        for (int i = 0; i < totalRows; i++)
            table.Rows.Add(i % 1000);

        var sut = new SqlResultColumnProfilingService();
        IReadOnlyList<SqlResultColumnProfile> profiles = await sut.BuildProfilesAsync(table);

        SqlResultColumnProfile profile = Assert.Single(profiles);
        Assert.Equal(totalRows, profile.RowCount);
        Assert.Equal(0, profile.NullCount);
        Assert.Equal(0, profile.NumericMin);
        Assert.Equal(999, profile.NumericMax);
    }
}

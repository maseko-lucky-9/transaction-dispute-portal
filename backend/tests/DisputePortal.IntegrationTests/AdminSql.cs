using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>Superuser reads for assertions: what is really in the database, past any query filter.</summary>
public static class AdminSql
{
    public static async Task<List<T>> ListAsync<T>(string connectionString, string sql, Func<NpgsqlDataReader, T> read, params (string Name, object Value)[] parameters)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        List<T> rows = [];
        while (await reader.ReadAsync())
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    public static async Task<long> CountAsync(string connectionString, string sql, params (string Name, object Value)[] parameters) =>
        (await ListAsync(connectionString, sql, reader => reader.GetInt64(0), parameters)).Single();
}

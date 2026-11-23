using March7thHoney.Database;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Generated;
using March7thHoney.Database.Pet;
using March7thHoney.Database.Player;
using March7thHoney.GameServer.Game.Player;
using Microsoft.Data.Sqlite;

// A new player's rows are created in one transaction. With one commit per row, a failure part-way through left the
// rows written before it in the database, and a slow disk made the first login outlast the client's timeout.
internal static class NewPlayerBatchRegression
{
    public static async Task Run(string databaseFile, Action<string, bool> check)
    {
        const int uid = 60071;
        var connectionString = $"Data Source={databaseFile}";
        var avatarTable = EntityStores.Map[typeof(AvatarData)].Table;
        // The last row the wiring creates: PetManager is the last component PlayerInstance.WireManagers adds while the
        // 4.8-only activities are excluded. Every other new row is written before the insert fails.
        var failingTable = EntityStores.Map[typeof(PlayerPetData)].Table;

        Execute(connectionString,
            $"create trigger fail_new_player before insert on \"{failingTable}\" when new.\"Uid\" = {uid} " +
            "begin select raise(abort, 'regression: failing insert'); end");
        var failed = false;
        try
        {
            await new PlayerInstance(uid).InitializeAsync();
        }
        catch (SqliteException)
        {
            failed = true;
        }
        finally
        {
            Execute(connectionString, "drop trigger fail_new_player");
        }

        check("a new player whose row creation fails part-way reports the failure", failed);
        check("a failed new-player row creation leaves no rows from before the failure",
            CountRows(connectionString, avatarTable, uid) == 0);
        var cached = DatabaseHelper.UidInstanceMap.TryGetValue(uid, out var list) ? list.ToList() : [];
        check("a failed new-player row creation keeps only the player record in memory",
            cached is [PlayerData]);
        if (cached is not [PlayerData saved]) return;

        // The next login loads the saved player record and creates the rows again.
        var retry = new PlayerInstance(saved);
        await retry.InitializeAsync();
        check("the next login after a failed row creation writes the player's rows",
            CountRows(connectionString, avatarTable, uid) == 1 &&
            CountRows(connectionString, failingTable, uid) == 1);

        ResetWhileCreating(connectionString, avatarTable, check);
    }

    // A player reset (PlayerResetHelper) drops the player's rows from memory without a tombstone. Rows queued by a
    // login before the reset must not be inserted after it: nothing in memory would own them, and the next creation of
    // the same row would collide with them.
    private static void ResetWhileCreating(string connectionString, string avatarTable, Action<string, bool> check)
    {
        const int uid = 60072;
        var rejected = false;
        try
        {
            DatabaseHelper.CreateBatch(() =>
            {
                DatabaseHelper.Instance!.GetInstanceOrCreateNew<AvatarData>(uid);
                DatabaseHelper.Instance.DeleteUidGameplayData(uid);
            });
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        check("rows queued before a player reset are not inserted after it",
            rejected && CountRows(connectionString, avatarTable, uid) == 0);
        var recreated = false;
        try
        {
            DatabaseHelper.Instance!.GetInstanceOrCreateNew<AvatarData>(uid);
            recreated = CountRows(connectionString, avatarTable, uid) == 1;
        }
        catch (SqliteException)
        {
            // A row left behind by the reset collides with the new one; the check below reports it.
        }

        check("a reset player's rows can be created again", recreated);
    }

    private static void Execute(string connectionString, string sql)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long CountRows(string connectionString, string table, int uid)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"select count(*) from \"{table}\" where \"Uid\" = {uid}";
        return (long)command.ExecuteScalar()!;
    }
}

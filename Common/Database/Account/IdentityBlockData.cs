namespace March7thHoney.Database.Account;

// Negative IDs belong to rules, never to players. Deleting a player cannot remove a rule.
[DbTable("IdentityBlock")]
[NotPlayerData]
public class IdentityBlockData : BaseDatabaseDataHelper
{
    public string IdentityKey { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Value { get; set; } = "";
    public string Reason { get; set; } = "";
    public long CreatedAt { get; set; }
    public long ExpireAt { get; set; }

    public AccountBanStatus GetStatus() => new(
        (ExpireAt <= 0 || ExpireAt > Util.Extensions.GetUnixSec()) &&
        AccountBanHelper.ParseIdentityKeys(IdentityKey).Count == 1, 0, Reason, ExpireAt, CreatedAt, true)
    {
        RuleId = Uid,
        IdentityKey = this.IdentityKey
    };
}

// Keeps retired UIDs reserved and prevents delayed game writes from recreating deleted data.
[DbTable("DeletedAccount")]
[NotPlayerData]
public class DeletedAccountData : BaseDatabaseDataHelper
{
    public long DeletedAt { get; set; }
}

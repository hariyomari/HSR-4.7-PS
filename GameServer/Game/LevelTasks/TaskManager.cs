using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.LevelTasks.AvatarTask;

namespace March7thHoney.GameServer.Game.LevelTasks;

public class TaskManager(PlayerInstance player) : BasePlayerManager(player)
{
    public PerformanceTrigger PerformanceTrigger { get; } = new(player);
    public LevelTask LevelTask { get; } = new(player);
    public SummonUnitLevelTask SummonUnitLevelTask { get; } = new();
    public AbilityLevelTask AbilityLevelTask { get; } = new(player);
    public MissionTaskTrigger MissionTaskTrigger { get; } = new(player);
    public SceneTaskTrigger SceneTaskTrigger { get; } = new(player);
}

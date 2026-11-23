using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.Message;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Message;

public class MessageManager(PlayerInstance player) : BasePlayerManager<MessageData>(player)
{
    public List<MessageSectionData> PendingMessagePerformSectionList { get; private set; } = [];

    #region Get

    public MessageSectionData? GetMessageSectionData(int sectionId)
    {
        GameData.MessageSectionConfigData.TryGetValue(sectionId, out var sectionConfig);
        if (sectionConfig == null) return null;
        var groupId = sectionConfig.GroupID;
        if (!Data.Groups.TryGetValue(groupId, out var group)) return null;
        return group.Sections.FirstOrDefault(m => m.SectionId == sectionId);
    }

    public List<MessageGroup> GetMessageGroup(int contactId)
    {
        GameData.MessageContactsConfigData.TryGetValue(contactId, out var contactConfig);
        if (contactConfig == null) return [];
        var stubFinishUnseen = !Player.MissionEnabled;
        var result = new List<MessageGroup>();
        foreach (var item in contactConfig.Groups)
            if (Data.Groups.TryGetValue(item.ID, out var group))
            {
                var groupInfo = new MessageGroup
                {
                    Id = (uint)item.ID,
                    Status = group.Status,
                    RefreshTime = group.RefreshTime
                };
                foreach (var section in group.Sections)
                {
                    var sectionInfo = new MessageSection
                    {
                        Id = (uint)section.SectionId,
                        Status = section.Status
                    };
                    // Official wire shape (GetNpcMessageGroupScRsp): only a DOING section carries its offered
                    // list - shipping it for a finished section kept those bubbles selectable, so chats looked
                    // un-finished / re-opened.
                    if (section.Status == MessageSectionStatus.MessageSectionDoing)
                        sectionInfo.MessageItemList.AddRange(section.ToChooseItemId.Select(m => (uint)m));
                    // The history echoes only the LAST consumed item (official: one row, not the whole chain).
                    if (section.Items.Count > 0)
                        sectionInfo.ItemList.Add(new MessageItem
                        {
                            ItemId = (uint)section.Items[^1].ItemId
                        });
                    groupInfo.MessageSectionList.Add(sectionInfo);
                }

                groupInfo.MessageSectionId = (uint)group.CurrentSectionId;
                result.Add(groupInfo);
            }
            else if (stubFinishUnseen)
            {
                // Without missions, unseen message groups are unreachable. Report them as finished for client
                // unlock checks such as Forgotten Hall (Pom-Pom messages 4010105/4010134).
                var stub = new MessageGroup
                {
                    Id = (uint)item.ID,
                    Status = MessageGroupStatus.MessageGroupFinish,
                    RefreshTime = Extensions.GetUnixSec()
                };
                if (GameData.MessageGroupConfigData.TryGetValue(item.ID, out var groupConfig))
                {
                    foreach (var sid in groupConfig.MessageSectionIDList)
                        stub.MessageSectionList.Add(new MessageSection
                        {
                            Id = (uint)sid,
                            Status = MessageSectionStatus.MessageSectionFinish
                        });
                    if (groupConfig.MessageSectionIDList.Count > 0)
                        stub.MessageSectionId = (uint)groupConfig.MessageSectionIDList[^1];
                }
                result.Add(stub);
            }
            // Unstarted groups are NOT serialized: the official server ships only groups with real player
            // state (4 groups / 112 bytes on the prologue capture) and the client resolves the rest from its
            // own config. Sending a None row per config group pre-rendered every chat as already sent.

        return result;
    }

    #endregion

    #region Action

    public async ValueTask AddMessageSection(int sectionId)
    {
        GameData.MessageSectionConfigData.TryGetValue(sectionId, out var sectionConfig);
        if (sectionConfig == null) return;

        if (Data.Groups.TryGetValue(sectionConfig.GroupID, out var group) &&
            group.Sections.Find(x => x.SectionId == sectionId) != null)
            // already exist
            return;

        foreach (var item in sectionConfig.StartMessageItemIDList) await AddMessageItem(item);
    }

    public async ValueTask AddMessageItem(int itemId, bool sendPacket = true)
    {
        GameData.MessageItemConfigData.TryGetValue(itemId, out var itemConfig);
        if (itemConfig == null) return;
        var groupId = itemConfig.GroupID;
        var sectionId = itemConfig.SectionID;
        if (!Data.Groups.TryGetValue(groupId, out var group))
        {
            group = new MessageGroupData
            {
                GroupId = groupId,
                CurrentSectionId = sectionId,
                RefreshTime = Extensions.GetUnixSec(),
                Status = MessageGroupStatus.MessageGroupDoing
            };
            group.Sections.Add(new MessageSectionData
            {
                SectionId = sectionId,
                Status = MessageSectionStatus.MessageSectionDoing,
                ToChooseItemId = [.. itemConfig.NextItemIDList]
            });
            Data.Groups.Add(groupId, group);
        }
        else
        {
            group.CurrentSectionId = sectionId;
            group.RefreshTime = Extensions.GetUnixSec();
            group.Status = MessageGroupStatus.MessageGroupDoing;
            if (group.Sections.All(m => m.SectionId != sectionId)) // new section
            {
                group.Sections.Add(new MessageSectionData
                {
                    SectionId = sectionId,
                    Status = MessageSectionStatus.MessageSectionDoing,
                    ToChooseItemId = [.. itemConfig.NextItemIDList]
                });

                if (itemConfig.NextItemIDList.Count == 1)
                    await FinishMessageItem(itemConfig.NextItemIDList[0], false, true);
            }
            else // old
            {
                group.Sections.First(m => m.SectionId == sectionId).Status = MessageSectionStatus.MessageSectionDoing;
            }
        }

        MarkDirty();
        // sync
        if (sendPacket)
        {
            var notify = new PacketPlayerSyncScNotify(group, group.Sections.First(m => m.SectionId == sectionId));
            await Player.SendPacket(notify);
        }
    }

    public async ValueTask FinishSection(int sectionId, bool sendPacket = true)
    {
        GameData.MessageSectionConfigData.TryGetValue(sectionId, out var sectionConfig);
        if (sectionConfig == null) return;
        var groupId = sectionConfig.GroupID;
        if (!Data.Groups.TryGetValue(groupId, out var group)) return;
        var section = group.Sections.Find(m => m.SectionId == sectionId);
        if (section?.Status != MessageSectionStatus.MessageSectionDoing) return;
        section.Status = MessageSectionStatus.MessageSectionFinish;
        if (group.Sections.All(m => m.Status == MessageSectionStatus.MessageSectionFinish))
            group.Status = MessageGroupStatus.MessageGroupFinish;

        MarkDirty();
        // sync
        if (sendPacket)
        {
            var notify = new PacketPlayerSyncScNotify(group, section);
            await Player.SendPacket(notify);
        }

        // broadcast to mission system
        await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.MessagePerformSectionFinish);
        await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.MessageSectionFinish);
    }

    public async ValueTask FinishMessageItem(int itemId, bool sendPacket = true, bool trusted = false)
    {
        MessageGroupData? changedGroup = null;
        MessageSectionData? changedSection = null;
        // A single offered follow-up is consumed automatically; the visited set stops a config chain that loops.
        var visited = new HashSet<int>();
        while (visited.Add(itemId) && GameData.MessageItemConfigData.TryGetValue(itemId, out var itemConfig) &&
               Data.Groups.TryGetValue(itemConfig.GroupID, out var group))
        {
            var section = group.Sections.Find(m => m.SectionId == itemConfig.SectionID);
            if (section?.Status != MessageSectionStatus.MessageSectionDoing) break;
            // Reject client ids that aren't currently offered — stops arbitrary message-flow advancement.
            // Internal auto-advance passes trusted:true (it may legitimately chain across sections).
            if (!trusted && !section.ToChooseItemId.Contains(itemId)) break;

            changedGroup ??= group;
            changedSection ??= section;
            section.Items.Add(new MessageItemData
            {
                ItemId = itemId
            });
            // A copy: NextItemIDList belongs to the shared config, not to this player's section.
            section.ToChooseItemId = [.. itemConfig.NextItemIDList];
            group.RefreshTime = Extensions.GetUnixSec();

            if (section.ToChooseItemId.Count != 1) break; // only a single item is finished automatically
            itemId = section.ToChooseItemId[0];
            if (visited.Contains(itemId))
            {
                section.ToChooseItemId.Clear();
                break;
            }

            trusted = true;
        }

        if (changedGroup == null || changedSection == null) return;
        MarkDirty();
        if (sendPacket)
        {
            // sync
            var notify = new PacketPlayerSyncScNotify(changedGroup, changedSection);
            await Player.SendPacket(notify);
        }
    }

    #endregion
}

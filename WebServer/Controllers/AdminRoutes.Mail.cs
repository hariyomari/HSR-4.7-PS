using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Mail;
using March7thHoney.GameServer.Game.Mail;
using March7thHoney.GameServer.Server;
using March7thHoney.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static March7thHoney.WebServer.Http.WebResults;

namespace March7thHoney.WebServer.Controllers;

public static partial class AdminRoutes
{
    private const int MaxMailRecipients = 20000;
    private const int MaxMailAttachments = 50;

    private static void MapMailRoutes(IEndpointRouteBuilder api)
    {
        api.MapGet("/players/{uid:int}/mail", GetMailbox);
        api.MapDelete("/players/{uid:int}/mail/{mailId:int}", DeleteMail);
        api.MapPost("/mail/send", SendMail);
    }

    private static IResult GetMailbox(int uid)
    {
        return Success(BuildMailbox(uid));
    }

    private static IResult DeleteMail(int uid, int mailId)
    {
        var connection = Listener.GetActiveConnection(uid);
        if (connection?.Player?.MailManager != null)
        {
            connection.Player.MailManager.DeleteMail(mailId);
            DatabaseHelper.MarkDirty(uid);
            return Success(BuildMailbox(uid));
        }

        var mailData = DatabaseHelper.Instance?.GetInstance<MailData>(uid);
        if (mailData == null)
            return Error("Mailbox not found", statusCode: StatusCodes.Status404NotFound);

        var removed = mailData.MailList.RemoveAll(mail => mail.MailID == mailId);
        if (removed == 0)
            return Error("Mail not found", statusCode: StatusCodes.Status404NotFound);

        DatabaseHelper.SaveDatabaseType(mailData);
        return Success(BuildMailbox(uid));
    }

    private static async Task<IResult> SendMail(AdminSendMailRequest? request)
    {
        request ??= new AdminSendMailRequest();
        var welcome = request.welcome.GetValueOrDefault();
        var errors = new List<string>();

        // Recipients: every account, or an explicit uid list.
        List<AccountData> recipients;
        if (string.Equals(request.target, "all", StringComparison.OrdinalIgnoreCase))
        {
            recipients = MailDeliveryHelper.GetAllAccounts();
        }
        else
        {
            var uids = (request.uids ?? []).Where(uid => uid > 0).Distinct().ToList();
            if (uids.Count == 0)
                return Error("Choose at least one recipient");
            if (uids.Count > MaxMailRecipients)
                return Error($"At most {MaxMailRecipients} recipients per request");

            recipients = [];
            foreach (var uid in uids)
            {
                var account = AccountData.GetAccountByUid(uid);
                if (account == null)
                {
                    errors.Add($"UID {uid}: account not found");
                    continue;
                }

                recipients.Add(account);
            }
        }

        MailDraft? draft = null;
        if (!welcome)
        {
            var title = (request.title ?? string.Empty).Trim();
            var content = (request.content ?? string.Empty).Replace("\r\n", "\n").Trim();
            var sender = (request.sender ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sender))
                sender = ConfigManager.Config.GameServer.GameServerName;
            if (string.IsNullOrWhiteSpace(title))
                return Error("Title cannot be empty");
            if (string.IsNullOrWhiteSpace(content))
                return Error("Content cannot be empty");

            var templateId = request.template_id.GetValueOrDefault();
            if (templateId < 0)
                return Error("Template id cannot be negative");
            var expireDays = request.expire_days.GetValueOrDefault(30);
            if (expireDays is < 1 or > 3650)
                return Error("Expiry must be between 1 and 3650 days");

            if (!TryBuildAttachments(request.attachments, out var attachments, out var attachmentError))
                return Error(attachmentError);

            draft = new MailDraft(sender, title, content, templateId, expireDays, attachments,
                request.is_star.GetValueOrDefault());
        }

        var sent = 0;
        var failed = errors.Count;
        var delivered = new List<int>();
        foreach (var account in recipients)
        {
            try
            {
                var mail = draft ?? MailDeliveryHelper.BuildWelcomeDraft(account.Uid,
                    MailDeliveryHelper.GetRecipientName(account));
                if (await MailDeliveryHelper.SendToUidAsync(account.Uid, mail))
                {
                    sent++;
                    delivered.Add(account.Uid);
                }
                else
                {
                    failed++;
                    errors.Add($"UID {account.Uid}: database unavailable");
                }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"UID {account.Uid}: {ex.Message}");
            }
        }

        if (errors.Count > 20)
            errors = errors.Take(20).Append($"... {errors.Count - 20} more").ToList();

        return Success(new MailSendData(sent, failed, errors, delivered.Take(200).ToList()));
    }

    private static bool TryBuildAttachments(List<AdminMailItem>? items, out List<ItemData> attachments,
        out string error)
    {
        attachments = [];
        error = string.Empty;
        if (items == null || items.Count == 0)
            return true;

        if (items.Count > MaxMailAttachments)
        {
            error = $"At most {MaxMailAttachments} attachments per mail";
            return false;
        }

        foreach (var item in items)
        {
            if (item.item_id <= 0)
            {
                error = "Attachment item id must be positive";
                return false;
            }

            var count = item.count.GetValueOrDefault(1);
            if (count <= 0)
            {
                error = $"Item {item.item_id}: count must be positive";
                return false;
            }

            if (GameData.ItemConfigData.Count > 0 && !GameData.ItemConfigData.ContainsKey(item.item_id))
            {
                error = $"Item {item.item_id} does not exist";
                return false;
            }

            attachments.Add(new ItemData
            {
                ItemId = item.item_id,
                Count = count,
                Rank = MailDeliveryHelper.GetConfiguredAttachmentRank(item.item_id, Math.Max(0, item.rank.GetValueOrDefault())),
                Level = Math.Max(1, item.level.GetValueOrDefault(1)),
                Promotion = Math.Max(0, item.promotion.GetValueOrDefault()),
                TalentLevel = Math.Max(0, item.talent_level.GetValueOrDefault())
            });
        }

        return true;
    }

    private static MailboxData BuildMailbox(int uid)
    {
        var online = Listener.GetActiveConnection(uid) != null;
        var mailData = DatabaseHelper.Instance?.GetInstance<MailData>(uid);
        if (mailData == null)
            return new MailboxData(uid, [], 0, online);

        var mails = mailData.MailList
            .OrderByDescending(mail => mail.SendTime)
            .ThenByDescending(mail => mail.MailID)
            .Select(mail => new MailboxEntry(
                mail.MailID,
                mail.SenderName,
                mail.Title,
                mail.Content,
                mail.IsRead,
                mail.IsStar,
                mail.SendTime,
                mail.ExpireTime,
                mail.TemplateID,
                mail.Attachment.Items.Select(item => new MailAttachmentBrief(
                    item.ItemId, item.Count, item.Level, item.Rank, item.Promotion, item.TalentLevel)).ToList()))
            .ToList();

        return new MailboxData(uid, mails, mails.Count, online);
    }

    public class AdminSendMailRequest
    {
        public string? target { get; set; }
        public List<int>? uids { get; set; }
        public bool? welcome { get; set; }
        public string? sender { get; set; }
        public string? title { get; set; }
        public string? content { get; set; }
        public int? template_id { get; set; }
        public int? expire_days { get; set; }
        public bool? is_star { get; set; }
        public List<AdminMailItem>? attachments { get; set; }
    }

    public class AdminMailItem
    {
        public int item_id { get; set; }
        public int? count { get; set; }
        public int? level { get; set; }
        public int? rank { get; set; }
        public int? promotion { get; set; }
        public int? talent_level { get; set; }
    }
}

using March7thHoney.Data;
using March7thHoney.Data.Activity;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.MechCraft;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Activity.MechCraft;

public partial class MechCraftManager : BasePlayerManager
{
    private static readonly Logger Logger = new("MechCraft");
    private readonly HashSet<int> _availableOrders;
    private readonly HashSet<int> _availableChips;
    private readonly HashSet<int> _availableMechatrons;
    private readonly HashSet<int> _activatedTalentPoints;
    private readonly HashSet<int> _finishedReports;
    private readonly Dictionary<int, MechCraftDiyMechatron> _diyMechatrons = [];
    private readonly Dictionary<(int OrderId, int ExamId), List<MechCraftChipPiece>> _savedLayouts = [];
    private readonly HashSet<int> _completedOrders;
    private readonly HashSet<int> _completedExams;
    private (int OrderId, int ExamId)? _activeLevel;
    private bool _submitted;

    public uint Revenue { get; private set; }
    public uint Coin { get; private set; }
    public uint WorkshopLevel { get; }
    public uint DesignLevel { get; }
    public uint TotalDesignExp { get; private set; }
    public uint RunningExamId { get; private set; }
    public uint SelectedMechatronId { get; private set; }
    public IReadOnlySet<int> CompletedOrderIds => _completedOrders;
    public IReadOnlySet<int> CompletedExamIds => _completedExams;

    public MechCraftManager(PlayerInstance player) : base(player)
    {
        _availableOrders = GameData.MechCraftOrderData.Keys.ToHashSet();
        _availableChips = GameData.MechCraftChipData.Keys.ToHashSet();
        _availableMechatrons = GameData.MechCraftDiyMechatronData.Keys.ToHashSet();
        _activatedTalentPoints = GameData.MechCraftTalentPointData.Keys.ToHashSet();
        _finishedReports = GameData.MechCraftReportData.Keys.ToHashSet();
        _completedOrders = _availableOrders.ToHashSet();
        _completedExams = GameData.MechCraftExamData.Keys.ToHashSet();
        WorkshopLevel = (uint)GameData.MechCraftWorkshopLevelData.Keys.DefaultIfEmpty().Max();
        DesignLevel = (uint)GameData.MechCraftDesignLevelData.Keys.DefaultIfEmpty().Max();
        Revenue = (uint)GameData.MechCraftWorkshopLevelData.Values.Select(row => row.MinIncome).DefaultIfEmpty().Max();
        TotalDesignExp = (uint)GameData.MechCraftDesignLevelData.Values.Select(row => row.MinExp).DefaultIfEmpty().Max();

        var defaultPose = MechCraftResourceCatalog.IntConstant("Diy_Default_Pose_ID");
        foreach (var row in GameData.MechCraftDiyMechatronData.Values)
            _diyMechatrons[row.ID] = new MechCraftDiyMechatron
            {
                MechatronId = (uint)row.ID,
                PoseId = (uint)(row.PoseList.Contains(defaultPose) ? defaultPose : row.PoseList.FirstOrDefault()),
                PaintColorId = (uint)row.PaintColorList.FirstOrDefault(),
                EffectColorId = (uint)row.EffectColorList.FirstOrDefault()
            };
        var defaultMechatron = MechCraftResourceCatalog.IntConstant("Can_Diy_Effect_Mechatron_ID");
        SelectedMechatronId = (uint)(_availableMechatrons.Contains(defaultMechatron)
            ? defaultMechatron : _availableMechatrons.Order().FirstOrDefault());
        InitializeRewards();
    }

    public MechCraftGetDataScRsp ToProto()
    {
        var response = new MechCraftGetDataScRsp
        {
            Revenue = Revenue,
            Coin = Coin,
            WorkshopLevel = WorkshopLevel,
            DesignLevel = DesignLevel,
            TotalDesignExp = TotalDesignExp,
            RunningExamId = RunningExamId,
            SelectedMechatronId = SelectedMechatronId
        };
        response.AvailableOrderIdList.AddRange(_availableOrders.Order().Select(id => (uint)id));
        response.AvailableChipIdList.AddRange(_availableChips.Order().Select(id => (uint)id));
        response.AvailableMechatronIdList.AddRange(_availableMechatrons.Order().Select(id => (uint)id));
        response.ActivatedTalentPointIdList.AddRange(_activatedTalentPoints.Order().Select(id => (uint)id));
        response.FinishedReportIdList.AddRange(_finishedReports.Order().Select(id => (uint)id));
        response.DiyMechatronList.AddRange(_diyMechatrons.OrderBy(pair => pair.Key).Select(pair => pair.Value.Clone()));
        return response;
    }

    public bool IsCompletedSubMission(int missionId) => missionId > 0 &&
        (MechCraftResourceCatalog.BlockingSubMissionIDs.Contains(missionId) || missionId == MechCraftResourceCatalog.TutorialSubMissionID);

    public MechCraftActivateTalentPointScRsp ActivateTalentPoint(uint talentPointId)
    {
        return new MechCraftActivateTalentPointScRsp
        {
            Coin = Coin,
            Retcode = (uint)(talentPointId <= int.MaxValue && _activatedTalentPoints.Contains((int)talentPointId)
                ? Retcode.RetSucc : Retcode.RetReqParaInvalid)
        };
    }

    public Retcode SaveDiy(MechCraftSaveDiyCsReq request)
    {
        if (request.SelectedMechatronId > int.MaxValue || !_availableMechatrons.Contains((int)request.SelectedMechatronId))
            return Retcode.RetReqParaInvalid;
        var incoming = new Dictionary<int, MechCraftDiyMechatron>();
        foreach (var entry in request.DiyMechatronList)
        {
            if (entry.MechatronId > int.MaxValue || !GameData.MechCraftDiyMechatronData.TryGetValue((int)entry.MechatronId, out var row))
                return Retcode.RetReqParaInvalid;
            if (!IsAllowed(entry.PoseId, row.PoseList) || !IsAllowed(entry.PaintColorId, row.PaintColorList) ||
                !IsAllowed(entry.EffectColorId, row.EffectColorList) || !incoming.TryAdd(row.ID, entry.Clone()))
                return Retcode.RetReqParaInvalid;
        }

        foreach (var (id, value) in incoming) _diyMechatrons[id] = value;
        SelectedMechatronId = request.SelectedMechatronId;
        return Retcode.RetSucc;
    }

    private static bool IsAllowed(uint value, IReadOnlyList<int> allowed) => allowed.Count == 0
        ? value == 0 : value <= int.MaxValue && allowed.Contains((int)value);

    public Retcode FinishReport(uint reportId)
    {
        if (reportId > int.MaxValue || !GameData.MechCraftReportData.ContainsKey((int)reportId)) return Retcode.RetReqParaInvalid;
        _finishedReports.Add((int)reportId);
        return Retcode.RetSucc;
    }

    public Retcode SelectExam(int examId)
    {
        if (!GameData.MechCraftExamData.ContainsKey(examId)) return Retcode.RetStageConfigNotExist;
        RunningExamId = (uint)examId;
        _activeLevel = null;
        _submitted = false;
        return Retcode.RetSucc;
    }

    public Retcode ReportGameState(MechCraftReportGameStateCsReq request)
    {
        var orderId = request.BeginInfo?.OrderId ?? request.EndInfo?.OrderId ?? 0;
        var examId = request.BeginInfo?.ExamId ?? request.EndInfo?.ExamId ?? 0;
        if (!TryLevelKey(orderId, examId, out var key)) return Retcode.RetStageConfigNotExist;
        if (request.BeginInfo != null)
        {
            _activeLevel = key;
            _submitted = false;
            return Retcode.RetSucc;
        }

        if (request.EndInfo == null || !TryPieces(request.EndInfo.ChipPieceList, out _)) return Retcode.RetReqParaInvalid;
        if (request.EndInfo.Status == 2) _savedLayouts.Remove(key);
        else _savedLayouts[key] = request.EndInfo.ChipPieceList.Select(piece => piece.Clone()).ToList();
        if (_activeLevel == key)
        {
            _activeLevel = null;
            _submitted = false;
        }
        return Retcode.RetSucc;
    }

    private bool TryLevelKey(uint orderId, uint examId, out (int OrderId, int ExamId) key)
    {
        key = default;
        if (orderId > int.MaxValue || examId > int.MaxValue || (orderId == 0) == (examId == 0)) return false;
        key = ((int)orderId, (int)examId);
        return orderId != 0 ? _availableOrders.Contains(key.OrderId) : GameData.MechCraftExamData.ContainsKey(key.ExamId);
    }

    private bool TryPieces(IEnumerable<MechCraftChipPiece> values, out List<MechCraftEvaluationPiece> pieces)
    {
        pieces = [];
        foreach (var piece in values)
        {
            if (piece.ChipId > int.MaxValue || !_availableChips.Contains((int)piece.ChipId) ||
                piece.X > int.MaxValue || piece.Y > int.MaxValue || piece.Rotation > 3) return false;
            pieces.Add(new MechCraftEvaluationPiece((int)piece.ChipId, (int)piece.X, (int)piece.Y, (MechCraftRotationType)piece.Rotation, piece.IsLocked));
        }
        return true;
    }

    public MechCraftSubmitOrderScRsp SubmitOrder(MechCraftSubmitOrderCsReq request)
    {
        var response = new MechCraftSubmitOrderScRsp { DesignLevel = DesignLevel, RunningExamId = RunningExamId };
        if (!TryLevelKey(request.OrderId, 0, out var key))
        {
            response.Retcode = (uint)Retcode.RetStageConfigNotExist;
            return response;
        }
        if (!TryPieces(request.ChipPieceList, out var pieces))
        {
            response.Retcode = (uint)Retcode.RetReqParaInvalid;
            return response;
        }
        if (_activeLevel != key || _submitted)
        {
            response.Retcode = (uint)(_submitted && _activeLevel == key ? Retcode.RetRepeatedReq : Retcode.RetStageNotFound);
            return response;
        }
        var evaluation = MechCraftEvaluator.EvaluateOrder(key.OrderId, pieces, _activatedTalentPoints);
        if (!evaluation.LayoutValid || !evaluation.RequirementsMet)
        {
            Logger.Debug($"Order {request.OrderId} rejected: {evaluation.InvalidReason}");
            response.Retcode = (uint)Retcode.RetStageSettleError;
            return response;
        }

        var order = GameData.MechCraftOrderData[key.OrderId];
        var requirement = GameData.MechCraftRequirementData[order.RequirementID];
        response.GainBasicCoin = (uint)Math.Max(order.BaseIncome, 0);
        long experience = Math.Max(order.DesignExperience, 0);
        for (var index = 0; index < requirement.ConditionIDs.Count; index++)
        {
            var satisfied = evaluation.CompletedConditionIDs.Contains(requirement.ConditionIDs[index]);
            var bonus = satisfied && index < order.AdvancedIncomeBonuses.Count ? Math.Max(order.AdvancedIncomeBonuses[index], 0) : 0;
            response.GainBonusCoinList.Add((uint)bonus);
            if (satisfied && index < order.AdvancedExperienceBonuses.Count) experience += Math.Max(order.AdvancedExperienceBonuses[index], 0);
        }
        response.GainTotalCoin = (uint)Math.Min((long)response.GainBasicCoin + response.GainBonusCoinList.Sum(value => (long)value), uint.MaxValue);
        response.GainDesignExp = (uint)Math.Min(experience, uint.MaxValue - TotalDesignExp);
        Coin = AddSaturating(Coin, response.GainTotalCoin);
        Revenue = AddSaturating(Revenue, response.GainTotalCoin);
        TotalDesignExp += response.GainDesignExp;
        _savedLayouts[key] = request.ChipPieceList.Select(piece => piece.Clone()).ToList();
        _submitted = true;
        return response;
    }

    public MechCraftSubmitExamScRsp SubmitExam(MechCraftSubmitExamCsReq request)
    {
        var response = new MechCraftSubmitExamScRsp { WorkshopLevel = WorkshopLevel };
        if (!TryLevelKey(0, request.ExamId, out var key))
        {
            response.Retcode = (uint)Retcode.RetStageConfigNotExist;
            return response;
        }
        if (!TryPieces(request.ChipPieceList, out var pieces))
        {
            response.Retcode = (uint)Retcode.RetReqParaInvalid;
            return response;
        }
        if (_activeLevel != key || _submitted)
        {
            response.Retcode = (uint)(_submitted && _activeLevel == key ? Retcode.RetRepeatedReq : Retcode.RetStageNotFound);
            return response;
        }
        var evaluation = MechCraftEvaluator.EvaluateExam(key.ExamId, pieces, _activatedTalentPoints);
        if (!evaluation.LayoutValid || !evaluation.RequirementsMet)
        {
            Logger.Debug($"Exam {request.ExamId} rejected: {evaluation.InvalidReason}");
            response.Retcode = (uint)Retcode.RetStageSettleError;
            return response;
        }
        _savedLayouts[key] = request.ChipPieceList.Select(piece => piece.Clone()).ToList();
        RunningExamId = 0;
        _submitted = true;
        return response;
    }

    private static uint AddSaturating(uint left, uint right) => (uint)Math.Min((ulong)left + right, uint.MaxValue);
}

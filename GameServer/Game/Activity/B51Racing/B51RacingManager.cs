using March7thHoney.Data;
using March7thHoney.Data.Activity;
using March7thHoney.Data.Excel;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Activity.B51Racing;

public sealed partial class B51RacingManager : BasePlayerManager
{
    private static readonly Logger Logger = new("B51Racing");
    private static int _nextSessionId = Random.Shared.Next(1, int.MaxValue / 2);
    private readonly object _stateLock = new();
    private readonly Dictionary<uint, uint> _bestTimes = [];
    private readonly HashSet<int> _completedMainMissions = [];
    private readonly HashSet<int> _completedSubMissions = [];
    private B51RacingChallengeCarData _car;
    private uint _driverId;
    private RaceSession? _race;
    private B51RacingResourceCatalog Resources => GameData.B51RacingData;

    private sealed record RaceSession(uint Id, uint MatchId, uint TrackId, uint DriverId, B51RacingChallengeCarData Car)
    {
        public bool EnteredRaid { get; set; }
    }

    public B51RacingManager(PlayerInstance player) : base(player)
    {
        Resources.Validate();
        var carID = Resources.ChallengeCars.Keys.Min();
        _car = new B51RacingChallengeCarData
        {
            CarId = (uint)carID,
            Livery = new B51RacingCarLivery { CarNum = 1, PaintId = (uint)Resources.Paints.Keys.Min() }
        };
        _car.EquippedPartIdList.AddRange(Resources.Cars[carID].PartIDList.Select(id => (uint)id));
        _driverId = (uint)Resources.ChallengeDriverIDs.Min();
        InitializeMissions();
        InitializeRewards();
    }

    private void InitializeMissions()
    {
        if (!GameData.SubMissionInfoData.TryGetValue(Resources.UnlockSubMissionID, out var unlock)) return;
        var mainID = unlock.MainMissionId;
        while (mainID != 0 && _completedMainMissions.Add(mainID) && GameData.MainMissionData.TryGetValue(mainID, out var main))
        {
            _completedSubMissions.UnionWith(main.SubMissionIds);
            mainID = main.NextTrackMainMission;
        }
    }

    public bool IsCompletedMainMission(int id) => _completedMainMissions.Contains(id);
    public bool IsCompletedSubMission(int id) => _completedSubMissions.Contains(id);
    public IEnumerable<int> CompletedMainMissionIDs => _completedMainMissions;
    public IEnumerable<int> CompletedMissionIDs => _completedMainMissions.Concat(_completedSubMissions);

    public B51RacingGetDataScRsp ToProto()
    {
        lock (_stateLock)
        {
            var challenge = new B51RacingChallengeData { Car = _car.Clone(), DriverId = _driverId };
            foreach (var (trackID, time) in _bestTimes.OrderBy(pair => pair.Key))
                challenge.TrackBestTimeList.Add(new KVP { Key = trackID, Value = time });
            var display = new B51RacingCarDisplay
            {
                DriverId = _driverId, CarNum = _car.Livery.CarNum, PaintId = _car.Livery.PaintId
            };
            display.EquippedPartIdList.AddRange(_car.EquippedPartIdList);
            var response = new B51RacingGetDataScRsp
            {
                LastCycleId = (uint)Resources.Cycles.Keys.Max(),
                Cycle = new B51RacingCycleData
                {
                    Agenda = new B51RacingAgendaInfo { Match = new B51RacingMatchInfo(), Development = new B51RacingDevelopmentInfo() },
                    PartInfo = new B51RacingPartInfo(), StatInfo = new B51RacingStatInfo(), Livery = new B51RacingCarLivery()
                },
                CarDisplay = display, Challenge = challenge
            };
            response.UnlockedDriverIdList.AddRange(Resources.Drivers.Keys.Order().Select(id => (uint)id));
            return response;
        }
    }

    private bool IsValidLivery(B51RacingCarLivery? livery) => livery != null && livery.CarNum >= 1 &&
        livery.CarNum <= Resources.MaxCarNumber && Resources.Paints.ContainsKey((int)livery.PaintId);

    public B51RacingChallengeUpdateCarScRsp UpdateCar(B51RacingChallengeUpdateCarCsReq request)
    {
        lock (_stateLock)
        {
            if (!Resources.ChallengeCars.TryGetValue((int)request.CarId, out var challengeCar) ||
                !Resources.Cars.TryGetValue((int)request.CarId, out var car) || !IsValidLivery(request.Livery) ||
                request.EquippedPartIdList.Count != 3)
                return new B51RacingChallengeUpdateCarScRsp { Retcode = (uint)Retcode.RetReqParaInvalid, Car = _car.Clone() };
            var allowed = car.PartIDList.Concat(challengeCar.PartIDList).ToHashSet();
            var slots = new HashSet<B51RacingPartType>();
            foreach (var id in request.EquippedPartIdList)
                if (!allowed.Contains((int)id) || !Resources.Parts.TryGetValue((int)id, out var part) ||
                    part.Type == B51RacingPartType.None || !slots.Add(part.Type))
                    return new B51RacingChallengeUpdateCarScRsp { Retcode = (uint)Retcode.RetReqParaInvalid, Car = _car.Clone() };
            _car = new B51RacingChallengeCarData { CarId = request.CarId, Livery = request.Livery.Clone() };
            _car.EquippedPartIdList.AddRange(request.EquippedPartIdList);
            return new B51RacingChallengeUpdateCarScRsp { Car = _car.Clone() };
        }
    }

    public B51RacingSetDriverScRsp SetDriver(uint driverId)
    {
        lock (_stateLock)
        {
            var valid = Resources.ChallengeDriverIDs.Contains((int)driverId);
            if (valid) _driverId = driverId;
            return new B51RacingSetDriverScRsp { Retcode = valid ? 0 : (uint)Retcode.RetReqParaInvalid, DriverId = _driverId };
        }
    }

    public B51RacingCarLiveryScRsp SetLivery(B51RacingCarLivery? livery)
    {
        lock (_stateLock)
        {
            var valid = IsValidLivery(livery);
            if (valid) _car.Livery = livery!.Clone();
            return new B51RacingCarLiveryScRsp { Retcode = valid ? 0 : (uint)Retcode.RetReqParaInvalid, Livery = _car.Livery.Clone() };
        }
    }

    public B51RacingChallengeStartMatchScRsp StartMatch(B51RacingChallengeStartMatchCsReq request)
    {
        lock (_stateLock)
        {
            if (!Resources.ChallengeMatchIDs.Contains((int)request.MatchId) ||
                !Resources.Matches.TryGetValue((int)request.MatchId, out var match) ||
                !match.TrackIDList.Contains((int)request.TrackId) || !Resources.ChallengeDriverIDs.Contains((int)request.DriverId))
                return new B51RacingChallengeStartMatchScRsp { Retcode = (uint)Retcode.RetReqParaInvalid };
            uint sessionID;
            do { sessionID = unchecked((uint)Interlocked.Increment(ref _nextSessionId)); } while (sessionID == 0);
            _driverId = request.DriverId;
            _race = new RaceSession(sessionID, request.MatchId, request.TrackId, _driverId, _car.Clone()) { EnteredRaid = _raidScene != null };
            Logger.Debug($"Start match={request.MatchId} track={request.TrackId} session={sessionID}");
            return new B51RacingChallengeStartMatchScRsp { DriverId = _driverId, SessionId = sessionID };
        }
    }

    public B51RacingChallengeEndMatchScRsp EndMatch(B51RacingChallengeEndMatchCsReq request)
    {
        lock (_stateLock)
        {
            var response = new B51RacingChallengeEndMatchScRsp { MatchId = request.MatchId, TrackId = request.TrackId };
            if (_race == null || !_race.EnteredRaid || _race.Id != request.SessionId || _race.MatchId != request.MatchId ||
                _race.TrackId != request.TrackId || request.ResultType is < B51RacingMatchResultType.Completed or > B51RacingMatchResultType.Interrupted ||
                (request.ResultType == B51RacingMatchResultType.Completed && request.FinishTimeMs == 0))
            {
                response.Retcode = (uint)Retcode.RetReqParaInvalid;
                return response;
            }
            if (request.ResultType == B51RacingMatchResultType.Completed &&
                (!_bestTimes.TryGetValue(request.TrackId, out var best) || request.FinishTimeMs < best))
                _bestTimes[request.TrackId] = request.FinishTimeMs;
            response.BestTimeMs = _bestTimes.GetValueOrDefault(request.TrackId);
            Logger.Debug($"End match={request.MatchId} track={request.TrackId} session={request.SessionId} result={request.ResultType} time={request.FinishTimeMs} best={response.BestTimeMs}");
            _race = null;
            return response;
        }
    }

    public void ClearRace()
    {
        lock (_stateLock)
        {
            _race = null;
            _raidScene = null;
        }
    }
}

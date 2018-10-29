using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Perpetuum.Accounting.Characters;
using Perpetuum.ExportedTypes;
using Perpetuum.Groups.Corporations;
using Perpetuum.Log;
using Perpetuum.Players;
using Perpetuum.Timers;
using Perpetuum.Units;
using Perpetuum.Zones.Beams;

namespace Perpetuum.Zones.Intrusion
{
    public class SAPPlayerInfo
    {
        public readonly Character character;
        public readonly long corporationEid;
        public int score;

        public SAPPlayerInfo(Player player)
        {
            character = player.Character;
            corporationEid = player.CorporationEid;
        }

        public void IncrementScore(int value)
        {
            Interlocked.Add(ref score, value);
        }
    }
   
    /// <summary>
    /// SAP base class
    /// </summary>
    public abstract class SAP : Unit
    {
        public struct IntrusionCorporationScore
        {
            public readonly long corporationEid;
            public readonly int score;

            public IntrusionCorporationScore(long corporationEid, int score)
            {
                this.corporationEid = corporationEid;
                this.score = score;
            }
        }

        private static readonly TimeSpan _sapExpiry = 
#if (DEBUG)
        TimeSpan.FromMinutes(5);
#else
        TimeSpan.FromHours(1);
#endif

        private readonly TimeSpan _enterBeamDuration = TimeSpan.FromSeconds(3);
        private readonly TimeSpan _exitBeamDuration  = TimeSpan.FromSeconds(3);
        
        private static readonly TimeSpan _broadcastInfoInterval = TimeSpan.FromSeconds(2);
        private const int BROADCAST_INFO_TOP_SCORE_COUNT = 10;
        private const int BROADCAST_INFO_RANGE = 100;

        private readonly BeamType _enterBeamType;
        private readonly BeamType _exitBeamType;

        private readonly IntervalTimer _broadcastInfoTimer = new IntervalTimer(_broadcastInfoInterval);
        private readonly TimeTracker _expiryTimer = new TimeTracker(_sapExpiry);

        public event Action<SAP> TimeOut;
        public event Action<SAP> TakeOver;

        protected SAP(BeamType enterBeamType, BeamType exitBeamType)
        {
            _enterBeamType = enterBeamType;
            _exitBeamType  = exitBeamType;
        }

        public void AddToZone(IZone zone,Position spawnPosition)
        {
            var builder = Beam.NewBuilder().WithType(_enterBeamType).WithPosition(spawnPosition).WithDuration(_enterBeamDuration);
            zone.CreateBeam(builder);

            Task.Delay(_enterBeamDuration).ContinueWith(t => { base.AddToZone(zone,spawnPosition); });
        }

        protected override void OnUpdate(TimeSpan time)
        {
            base.OnUpdate(time);

            _broadcastInfoTimer.Update(time);

            if ( _broadcastInfoTimer.Passed )
            {
                _broadcastInfoTimer.Reset();
                BroadcastSAPInfoPacket();
            }

            _expiryTimer.Update(time);

            if (!_expiryTimer.Expired) 
                return;

            // lejart
            OnRemove();

            TimeOut?.Invoke(this);
        }

        private void OnRemove()
        {
            var exitBeamBuilder = Beam.NewBuilder().WithType(_exitBeamType).WithPosition(CurrentPosition).WithDuration(_exitBeamDuration);
            RemoveFromZone(exitBeamBuilder);
        }

        private void SendSAPPlayerInfoPacketToPlayer(Character character,int score)
        {
            Player player;
            if (Zone.TryGetPlayer(character, out player))
            {
                SendSAPPlayerInfoPacketToPlayer(player, score);
            }
        }

        private void SendSAPPlayerInfoPacketToPlayer(Player player, int score)
        {
            var packet = new Packet(ZoneCommand.SAPPlayerInfo);
            packet.AppendLong(Eid);

            packet.AppendInt(MaxScore);
            packet.AppendInt(score);

            player.Session.SendPacket(packet);
        }

        private void SendSAPPlayerInfoPacketToPlayer(Player player,SAPPlayerInfo info)
        {
            SendSAPPlayerInfoPacketToPlayer(player,info.score);
        }

        private ImmutableDictionary<Character,SAPPlayerInfo> _playerInfos = ImmutableDictionary<Character, SAPPlayerInfo>.Empty;

        public IEnumerable<SAPPlayerInfo> PlayerInfos => _playerInfos.Values;

        [CanBeNull]
        public SAPPlayerInfo GetPlayerInfo(Character character)
        {
            return _playerInfos.GetOrDefault(character);
        }

        protected int GetPlayerScore(Character character)
        {
            var info = GetPlayerInfo(character);
            return info?.score ?? 0;
        }
        
        protected void RemovePlayerInfo(Character character)
        {
            SAPPlayerInfo info;
            if (ImmutableInterlocked.TryRemove(ref _playerInfos,character,out info))
            {
                SendSAPPlayerInfoPacketToPlayer(character,0);
            }
        }


        protected void IncrementPlayerScore(Player player, int score)
        {
            player.ApplyPvPEffect();

            var info = ImmutableInterlocked.GetOrAdd(ref _playerInfos, player.Character, c => new SAPPlayerInfo(player));
            info.IncrementScore(score);

            SendSAPPlayerInfoPacketToPlayer(player,info);

            if (MaxScore > 0 && info.score >= MaxScore)
            {
                OnTakeOver();
            }

            Logger.Info($"Intrusion SAP score updated. sap = {Eid} ({ED.Name}) player = {info.character.Id} score = {info.score}");
        }

        public Outpost Site { get; set; }

        private int _takeOver;

        protected void OnTakeOver()
        {
            if ( Interlocked.CompareExchange(ref _takeOver,1,0) == 1)
                return;

            OnRemove();
            TakeOver?.Invoke(this);
        }

        private void BroadcastSAPInfoPacket()
        {
            var zone = Zone;
            if (zone == null) 
                return;

            Task.Run(() => SendSapInfoPacketToPlayers(zone));
        }

        private void SendSapInfoPacketToPlayers(IZone zone)
        {
            var characters = _playerInfos.Values.Select(i => i.character).ToArray();
            var playersWithScore = zone.Players.Where(player => characters.Contains(player.Character));
            var playersInRange = zone.Players.WithinRange(CurrentPosition,BROADCAST_INFO_RANGE);

            var players = playersWithScore.Concat(playersInRange).Distinct().ToArray();

            if (players.Length <= 0) 
                return;

            var sapInfoPacket = BuildSAPInfoPacket();
            players.ForEach(player =>
            {
                player.Session.SendPacket(sapInfoPacket);
            });
        }

        protected abstract int MaxScore { get; }
        protected abstract void AppendTopScoresToPacket(Packet packet,int count);

        private Packet BuildSAPInfoPacket()
        {
            var packet = new Packet(ZoneCommand.SAPInfo);

            packet.AppendInt(Definition);
            packet.AppendLong(Eid);

            Site.AppendSiteInfoToPacket(packet);

            packet.AppendInt((int) _expiryTimer.Duration.TotalMilliseconds);
            packet.AppendInt((int) _expiryTimer.Remaining.TotalMilliseconds);

            packet.AppendInt(MaxScore); // max score
            AppendTopScoresToPacket(packet,BROADCAST_INFO_TOP_SCORE_COUNT);
            return packet;
        }

        protected static void AppendPlayerTopScoresToPacket(SAP sap,Packet packet,int count)
        {
            var topScores = sap.GetPlayerTopScores(count);

            packet.AppendInt(topScores.Length);
            packet.AppendByte(sizeof(int));

            foreach (var score in topScores)
            {
                packet.AppendInt(score.character.Id);
                packet.AppendInt(score.score);
            }
        }

        public int StabilityChange
        {
            get
            {
                var change = ED.Options.Increase;
                if (change == 0)
                {
                    Logger.Error("consistency error, no SAP increase is defined for definition: " + Definition + " " + ED.Name);
                    change = 15;
                }

                return change;
            }
        }

        [CanBeNull]
        public Corporation GetWinnerCorporation()
        {
            var winnerCorpEid = GetWinnerCorporationEid();
            var corp = Corporation.Get(winnerCorpEid);
            return corp;
        }

        public virtual long GetWinnerCorporationEid()
        {
            var scores = GetPlayerTopScores(1);
            if (scores.Length > 0)
                return scores[0].corporationEid;

            return 0L;
        }

        public IEnumerable<SAPPlayerInfo> GetPlayersWithScore()
        {
            return PlayerInfos.Where(i => i.score > 0);
        }

        public SAPPlayerInfo[] GetPlayerTopScores(int count)
        {
            return PlayerInfos.OrderByDescending(playerInfo => playerInfo.score).Take(count).ToArray();
        }

        public IList<IntrusionCorporationScore> GetCorporationTopScores(int count)
        {
            var corporationScores = GetCorporationScores();
            var topScores = corporationScores.OrderByDescending(cs => cs.score).Take(count).ToArray();
            return topScores;
        }

        private IEnumerable<IntrusionCorporationScore> GetCorporationScores()
        {
            var corporationGroup = PlayerInfos.GroupBy(playerInfo => playerInfo.corporationEid);

            var result = new List<IntrusionCorporationScore>();

            foreach (var group in corporationGroup)
            {
                var sumScore = @group.Sum(playerInfo => playerInfo.score);
                result.Add(new IntrusionCorporationScore(@group.Key, sumScore));
            }

            return result;
        }
    }
}
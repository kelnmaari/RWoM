using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TorannMagic
{
    /// <summary>
    /// Optional integration with the Isekai RPG Leveling family
    /// (JellyCreative.IsekaiLeveling and its add-ons), wired purely through
    /// reflection so TorannMagic builds and runs with or without them.
    ///
    /// Synergy:
    ///  - raising a RWoM magic/might class level grants Isekai XP to the pawn
    ///  - an awakened pawn's Isekai level widens their RWoM mana pool
    ///  - (XML, see v1.7/IsekaiCompat) RWoM creatures appear as Isekai dungeon
    ///    rosters and RWoM-themed gates join the gate pool, guild contracts
    ///    and gate scrolls automatically
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TMPIsekai
    {
        private const string LevelingPackageId = "JellyCreative.IsekaiLeveling";

        public static readonly bool Active;

        private static MethodInfo _miGetCached;
        private static MethodInfo _miGainXP;
        private static FieldInfo _fiCurrentLevel;
        private static bool _reflectionReady;
        private static bool _warned;

        // pawnId -> (level, tick read at); Isekai levels change rarely,
        // no point reflecting into the comp every mana recompute.
        private static readonly Dictionary<int, int> LevelCache = new Dictionary<int, int>();
        private const int CacheRefreshInterval = 2000;

        static TMPIsekai()
        {
            Active = ModsConfig.IsActive(LevelingPackageId);
            if (Active)
            {
                try
                {
                    Type compType = AccessTools.TypeByName("IsekaiLeveling.IsekaiComponent");
                    _miGetCached = AccessTools.Method(compType, "GetCached");
                    _miGainXP = AccessTools.Method(compType, "GainXP");
                    _fiCurrentLevel = AccessTools.Field(compType, "currentLevel");
                    _reflectionReady = compType != null && _miGetCached != null && _miGainXP != null && _fiCurrentLevel != null;
                    if (!_reflectionReady)
                    {
                        Log.Warning("[TM] Isekai Leveling detected but its API could not be resolved; integration disabled.");
                        Active = false;
                    }
                    else if (Prefs.DevMode)
                    {
                        Log.Message("[TM] Isekai Leveling integration active.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[TM] Isekai Leveling integration failed to initialize: " + ex.Message);
                    Active = false;
                }
            }
        }

        /// <summary>Current Isekai level of the pawn, 0 when not awakened/absent.</summary>
        public static int GetIsekaiLevel(Pawn pawn)
        {
            if (!Active || pawn == null)
            {
                return 0;
            }
            int now = Find.TickManager.TicksGame;
            if (LevelCache.TryGetValue(pawn.thingIDNumber, out int level)
                && LevelCache.TryGetValue(pawn.thingIDNumber ^ int.MinValue, out int stamp)
                && now - stamp < CacheRefreshInterval)
            {
                return level;
            }
            try
            {
                object comp = _miGetCached.Invoke(null, new object[] { pawn });
                level = comp == null ? 0 : (int)_fiCurrentLevel.GetValue(comp);
            }
            catch
            {
                level = 0;
            }
            LevelCache[pawn.thingIDNumber] = level;
            LevelCache[pawn.thingIDNumber ^ int.MinValue] = now;
            return level;
        }

        /// <summary>Mana pool multiplier from the pawn's Isekai level: +0.5% per level, capped at +50%.</summary>
        public static float ManaPoolMultiplier(Pawn pawn)
        {
            int level = GetIsekaiLevel(pawn);
            return level <= 0 ? 1f : 1f + Math.Min(level * 0.005f, 0.5f);
        }

        /// <summary>Grants Isekai XP for advancing a RWoM class level.</summary>
        public static void OnClassLevelUp(Pawn pawn, int newLevel, bool magic)
        {
            if (!Active || !_reflectionReady || pawn == null || newLevel <= 0)
            {
                return;
            }
            try
            {
                object comp = _miGetCached.Invoke(null, new object[] { pawn });
                if (comp == null)
                {
                    // Unawakened pawns have no Isekai comp; nothing to sync.
                    if (!_warned && Prefs.DevMode)
                    {
                        _warned = true;
                        Log.Message("[TM] Isekai: pawn has no leveling component (not awakened); XP not granted.");
                    }
                    return;
                }
                // Modest but compounding: a full 0..10 RWoM class climb is worth
                // roughly one early Isekai level, so the systems overlay without
                // either one trivializing the other.
                int xp = 20 * newLevel;
                _miGainXP.Invoke(comp, new object[] { xp, magic ? "RWoM magic" : "RWoM might" });
                LevelCache.Remove(pawn.thingIDNumber);
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    Log.Warning("[TM] Isekai XP grant failed: " + ex.Message);
                }
            }
        }
    }
}

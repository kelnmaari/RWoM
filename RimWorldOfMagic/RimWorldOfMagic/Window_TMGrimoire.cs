using System;
using System.Collections.Generic;
using System.Linq;
using AbilityUser;
using RimWorld;
using UnityEngine;
using Verse;

namespace TorannMagic
{
    /// <summary>
    /// Full character grimoire window, Isekai-styled: class levels, XP and mana at
    /// the top, then one card per spell/skill with its description, upgrade button
    /// and per-skill upgrade rows with visible explanations — the whole progression
    /// UI of the Magic/Might tabs in a single centered, scrolling window.
    /// </summary>
    public class Window_TMGrimoire : Window
    {
        private const float IconSize = 46f;

        private static readonly Color BarBg = new Color(0.15f, 0.15f, 0.18f);
        private static readonly Color BarMp = new Color(0.30f, 0.45f, 0.90f);
        private static readonly Color BarXp = new Color(0.90f, 0.75f, 0.25f);
        private static readonly Color TextGray = new Color(0.60f, 0.60f, 0.66f);
        private static readonly Color Purple = new Color(0.65f, 0.40f, 0.85f);
        private static readonly Color ResStamina = new Color(0.30f, 0.75f, 0.40f);
        private static readonly Color ResPsionic = new Color(0.65f, 0.40f, 0.85f);
        private static readonly Color ResHate = new Color(0.85f, 0.30f, 0.35f);
        private static readonly Color ResBlood = new Color(0.60f, 0.10f, 0.12f);
        private static readonly Color ResChi = new Color(0.95f, 0.60f, 0.25f);
        private static readonly Color ResSpirit = new Color(0.95f, 0.90f, 0.55f);

        private readonly Pawn pawn;
        private readonly CompAbilityUserMagic magic;
        private readonly CompAbilityUserMight might;
        private Vector2 scrollPos;

        public override Vector2 InitialSize => new Vector2(880f, 720f);

        public Window_TMGrimoire(Pawn pawn)
        {
            this.pawn = pawn;
            magic = pawn.GetCompAbilityUserMagic();
            might = pawn.GetCompAbilityUserMight();
            forcePause = false;
            doCloseX = true;
            absorbInputAroundWindow = false;
            closeOnAccept = false;
            closeOnCancel = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float cur = 0f;

            Text.Font = GameFont.Medium;
            Label(new Rect(0f, cur, inRect.width, 32f), "Grimoire", TextAnchor.MiddleLeft,
                TorannMagic.Utils.TMIsekaiUI.HeaderGold);
            Text.Font = GameFont.Small;
            float nameW = Text.CalcSize(pawn.LabelShortCap).x + 8f;
            Label(new Rect(0f, cur + 30f, nameW, 18f),
                pawn.LabelShortCap, TextAnchor.MiddleLeft, Color.white);
            Label(new Rect(nameW, cur + 30f, inRect.width - nameW, 18f),
                "— " + ClassLabel(), TextAnchor.MiddleLeft, TorannMagic.Utils.TMIsekaiUI.LabelGold);
            cur += 50f;

            // class description pulled from the class trait
            string classDesc = ClassDescription();
            if (!classDesc.NullOrEmpty())
            {
                Text.Font = GameFont.Tiny;
                float dh = Text.CalcHeight(classDesc, inRect.width);
                Label(new Rect(0f, cur, inRect.width, dh), classDesc, TextAnchor.UpperLeft, TextGray);
                cur += dh + 8f;
            }

            cur += 4f;
            EnsureFilters();

            Rect scrollRect = new Rect(0f, cur, inRect.width, inRect.height - cur);
            float viewWidth = scrollRect.width - 20f;
            float viewHeight = MeasureViewHeight(viewWidth);
            Rect view = new Rect(0f, 0f, viewWidth, Mathf.Max(viewHeight, scrollRect.height));
            Widgets.BeginScrollView(scrollRect, ref scrollPos, view);

            float y = 0f;

            if (TMPIsekai.Active)
            {
                y = DrawIsekaiBlock(view, y);
            }

            y = DrawResourcesBlock(view, y);

            if (magic != null && magic.MagicData != null)
            {
                y = DrawMagicSection(view, y);
            }

            if (might != null && might.MightData != null)
            {
                y = DrawMightSection(view, y);
            }

            Widgets.EndScrollView();
        }

        #region Magic

        private float DrawMagicSection(Rect view, float y)
        {
            Text.Font = GameFont.Medium;
            Label(new Rect(0f, y, view.width, 26f), "TM_Header".Translate().CapitalizeFirst(),
                TextAnchor.MiddleLeft, TorannMagic.Utils.TMIsekaiUI.HeaderGold);
            y += 30f;

            Rect info = new Rect(0f, y, view.width, 76f);
            TorannMagic.Utils.TMIsekaiUI.Panel(info);
            int level = magic.MagicData.MagicUserLevel;
            int points = magic.MagicData.MagicAbilityPoints;
            Text.Font = GameFont.Small;
            Label(new Rect(info.x + 12f, info.y + 8f, 260f, 20f),
                "TM_Level".Translate().CapitalizeFirst() + ": " + level, TextAnchor.MiddleLeft, Color.white);
            Label(new Rect(info.x + 280f, info.y + 8f, 260f, 20f),
                "TM_PointsAvail".Translate() + ": " + points, TextAnchor.MiddleLeft, TorannMagic.Utils.TMIsekaiUI.LabelGold);

            int xpForLevel = magic.GetXPForLevel(Mathf.Max(0, level));
            int xp = magic.MagicData.MagicUserXP;
            float xpFrac = xpForLevel <= 0 ? 0f : Mathf.Clamp01((float)xp / xpForLevel);
            Rect xpBar = new Rect(info.x + 12f, info.y + 32f, info.width - 24f, 10f);
            Widgets.DrawBoxSolid(xpBar, BarBg);
            Widgets.DrawBoxSolid(new Rect(xpBar.x, xpBar.y, xpBar.width * xpFrac, xpBar.height), BarXp);
            Text.Font = GameFont.Tiny;
            Label(new Rect(info.x + 12f, info.y + 44f, info.width - 24f, 14f),
                "XP " + xp + " / " + xpForLevel, TextAnchor.MiddleLeft, TextGray);

            if (DebugSettings.godMode)
            {
                if (Widgets.ButtonText(new Rect(info.xMax - 90f, info.y + 8f, 78f, 22f), "+1", true, false, true))
                {
                    magic.LevelUp(true);
                }
                if (Widgets.ButtonText(new Rect(info.xMax - 90f, info.y + 36f, 78f, 22f), "Reset Class", true, false, true))
                {
                    magic.ResetSkills();
                }
            }

            y += info.height + 10f;

            // global stat skills (Clarity / Focus / Spirit), 1 point per level
            y = DrawStatBlock(view, y, "TM_Stats", magicStats);

            foreach (MagicPower power in magic.MagicData.AllMagicPowersWithSkills)
            {
                TMAbilityDef ability = power.abilityDef as TMAbilityDef;
                if (ability == null || !magicFilter.Contains(ability))
                {
                    continue;
                }
                y = DrawMagicPowerCard(view, y, power, points);
            }
            return y + 12f;
        }

        private float DrawMagicPowerCard(Rect view, float y, MagicPower power, int points)
        {
            TMAbilityDef ability = (TMAbilityDef)power.abilityDef;
            string desc = power.abilityDescDef?.description;
            if (power.level > 0 && power.level < power.maxLevel && power.nextLevelAbilityDescDef?.description != null)
            {
                desc += "\n\n" + "TM_MCU_NextLevel".Translate() + "\n" + power.nextLevelAbilityDescDef.description;
            }
            float descWidth = view.width - 24f - IconSize - 10f;
            float descHeight = PowerDescriptionHeight(power, view.width);
            List<MagicPowerSkill> skills = GetMagicSkills(ability);
            float cardHeight = Mathf.Max(IconSize, 32f + descHeight) + skills.Count * 32f + 16f + 8f;

            Rect card = new Rect(0f, y, view.width, cardHeight);
            TorannMagic.Utils.TMIsekaiUI.Panel(card);
            Text.Font = GameFont.Small;

            Rect icon = new Rect(card.x + 10f, card.y + 10f, IconSize, IconSize);
            if (power.Icon != null)
            {
                GUI.color = power.learned ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                Widgets.DrawTextureFitted(icon, power.Icon, 1f);
                GUI.color = Color.white;
            }

            float titleX = icon.xMax + 10f;
            Label(new Rect(titleX, card.y + 8f, card.width - (titleX - card.x) - 150f, 22f),
                ability.LabelCap + "   Lv " + power.level + " / " + power.maxLevel,
                TextAnchor.MiddleLeft, TorannMagic.Utils.TMIsekaiUI.LabelGold);

            Rect action = new Rect(card.xMax - 142f, card.y + 8f, 132f, 24f);
            DrawMagicPowerAction(action, power, points);

            if (!string.IsNullOrEmpty(desc))
            {
                Rect descRect = new Rect(titleX, card.y + 32f, descWidth, descHeight);
                Text.Font = GameFont.Tiny;
                Widgets.Label(descRect, desc);
            }

            float sy = card.y + Mathf.Max(IconSize, 32f + descHeight) + 6f;
            foreach (MagicPowerSkill skill in skills)
            {
                bool canUp = power.learned && skill.level < skill.levelMax && points >= skill.costToLevel;
                Rect skillLabel = new Rect(card.x + 16f, sy, card.width - 16f - 50f, 18f);
                Text.Font = GameFont.Tiny;
                Label(skillLabel, skill.label.Translate() + ": " + skill.level + " / " + skill.levelMax +
                    "  —  " + skill.desc.Translate(), TextAnchor.UpperLeft, canUp ? Color.white : TextGray);
                if (canUp && Widgets.ButtonText(new Rect(card.xMax - 52f, sy - 2f, 36f, 20f), "+", true, false, true))
                {
                    skill.level++;
                    magic.MagicData.MagicAbilityPoints -= skill.costToLevel;
                }
                else if (power.learned && skill.level >= skill.levelMax)
                {
                    Label(new Rect(card.xMax - 52f, sy - 2f, 36f, 20f), "max", TextAnchor.MiddleCenter, TextGray);
                }
                sy += 32f;
            }

            return y + cardHeight + 10f;
        }

        private void DrawMagicPowerAction(Rect rect, MagicPower power, int points)
        {
            if (!power.learned)
            {
                if (points < power.learnCost)
                {
                    Label(rect, "TM_MCU_PointsToLearn".Translate(power.learnCost) + "TM_Learn".Translate(),
                        TextAnchor.MiddleCenter, TextGray);
                    return;
                }
                if (Widgets.ButtonText(rect, "TM_Learn".Translate() + " (" + power.learnCost + ")", true, false, true))
                {
                    power.learned = true;
                    TMAbilityDef ability = (TMAbilityDef)power.abilityDef;
                    if (!(power.abilityDef.defName == "TM_TechnoBit") && ability.shouldInitialize)
                    {
                        magic.AddPawnAbility(power.abilityDef);
                    }
                    if (power.abilityDef.defName == "TM_TechnoWeapon")
                    {
                        magic.AddPawnAbility(TorannMagicDefOf.TM_NanoStimulant);
                        magic.MagicData.MagicPowersStandalone
                            .FirstOrDefault((MagicPower x) => x.abilityDef == TorannMagicDefOf.TM_NanoStimulant).learned = true;
                    }
                    if (ability.childAbilities != null)
                    {
                        foreach (AbilityUser.AbilityDef childDef in ability.childAbilities)
                        {
                            if (childDef is TMAbilityDef child && child.shouldInitialize)
                            {
                                magic.AddPawnAbility(child);
                            }
                        }
                    }
                    magic.MagicData.MagicAbilityPoints -= power.learnCost;
                }
                return;
            }

            if (power.level >= power.maxLevel)
            {
                Label(rect, "max", TextAnchor.MiddleCenter, TextGray);
                return;
            }
            if (points < power.costToLevel)
            {
                Label(rect, "TM_MCU_PointsToLearn".Translate(power.costToLevel), TextAnchor.MiddleCenter, TextGray);
                return;
            }
            if (Widgets.ButtonText(rect, "TM_LevelUp".Translate() + " (" + power.costToLevel + ")", true, false, true))
            {
                magic.LevelUpPower(power);
                magic.MagicData.MagicAbilityPoints -= power.costToLevel;
            }
        }

        private List<MagicPowerSkill> GetMagicSkills(TMAbilityDef ability)
        {
            List<MagicPowerSkill> list = new List<MagicPowerSkill>();
            MagicPowerSkill s = magic.MagicData.GetSkill_Power(ability);
            if (s != null) list.Add(s);
            s = magic.MagicData.GetSkill_Efficiency(ability);
            if (s != null) list.Add(s);
            s = magic.MagicData.GetSkill_Versatility(ability);
            if (s != null) list.Add(s);
            return list;
        }

        private List<MagicPower> GetMagicClassGroup()
        {
            MagicData data = magic.MagicData;
            if (pawn.story == null || pawn.story.traits == null)
            {
                return null;
            }
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.InnerFire)) return data.MagicPowersIF;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.HeartOfFrost)) return data.MagicPowersHoF;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.StormBorn)) return data.MagicPowersSB;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Arcanist)) return data.MagicPowersA;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Paladin)) return data.MagicPowersP;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Summoner)) return data.MagicPowersS;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Druid)) return data.MagicPowersD;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Necromancer) || pawn.story.traits.HasTrait(TorannMagicDefOf.Lich)) return data.MagicPowersN;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Priest)) return data.MagicPowersPR;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.TM_Bard)) return data.MagicPowersB;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Succubus)) return data.MagicPowersSD;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Warlock)) return data.MagicPowersWD;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Geomancer)) return data.MagicPowersG;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Technomancer)) return data.MagicPowersT;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.BloodMage)) return data.MagicPowersBM;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Enchanter)) return data.MagicPowersE;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Chronomancer)) return data.MagicPowersC;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.TM_Wanderer)) return data.MagicPowersW;
            return null;
        }

        #endregion

        #region Might

        private float DrawMightSection(Rect view, float y)
        {
            Text.Font = GameFont.Medium;
            Label(new Rect(0f, y, view.width, 26f), "TM_HeaderMight".Translate().CapitalizeFirst(),
                TextAnchor.MiddleLeft, TorannMagic.Utils.TMIsekaiUI.HeaderGold);
            y += 30f;

            Rect info = new Rect(0f, y, view.width, 64f);
            TorannMagic.Utils.TMIsekaiUI.Panel(info);
            int level = might.MightData.MightUserLevel;
            int points = might.MightData.MightAbilityPoints;
            Text.Font = GameFont.Small;
            Label(new Rect(info.x + 12f, info.y + 8f, 260f, 20f),
                "TM_Level".Translate().CapitalizeFirst() + ": " + level, TextAnchor.MiddleLeft, Color.white);
            Label(new Rect(info.x + 280f, info.y + 8f, 260f, 20f),
                "TM_PointsAvail".Translate() + ": " + points, TextAnchor.MiddleLeft, TorannMagic.Utils.TMIsekaiUI.LabelGold);

            int xpForLevel = might.GetXPForLevel(Mathf.Max(0, level));
            int xp = might.MightData.MightUserXP;
            float xpFrac = xpForLevel <= 0 ? 0f : Mathf.Clamp01((float)xp / xpForLevel);
            Rect xpBar = new Rect(info.x + 12f, info.y + 34f, info.width - 24f, 10f);
            Widgets.DrawBoxSolid(xpBar, BarBg);
            Widgets.DrawBoxSolid(new Rect(xpBar.x, xpBar.y, xpBar.width * xpFrac, xpBar.height), BarXp);
            Text.Font = GameFont.Tiny;
            Label(new Rect(info.x + 12f, info.y + 46f, info.width - 24f, 14f),
                "XP " + xp + " / " + xpForLevel, TextAnchor.MiddleLeft, TextGray);

            if (DebugSettings.godMode)
            {
                if (Widgets.ButtonText(new Rect(info.xMax - 90f, info.y + 8f, 78f, 22f), "+1", true, false, true))
                {
                    might.LevelUp(true);
                }
                if (Widgets.ButtonText(new Rect(info.xMax - 90f, info.y + 36f, 78f, 22f), "Reset Class", true, false, true))
                {
                    might.ResetSkills();
                }
            }

            y += info.height + 10f;

            y = DrawStatBlock(view, y, "TM_StatsMight", mightStats);

            foreach (MightPower power in might.MightData.AllMightPowersWithSkills)
            {
                TMAbilityDef ability = power.abilityDef as TMAbilityDef;
                if (ability == null || !mightFilter.Contains(ability))
                {
                    continue;
                }
                y = DrawMightPowerCard(view, y, power, points);
            }
            return y + 12f;
        }

        private float DrawMightPowerCard(Rect view, float y, MightPower power, int points)
        {
            TMAbilityDef ability = (TMAbilityDef)power.abilityDef;
            string desc = power.abilityDescDef?.description;
            if (power.level > 0 && power.level < power.maxLevel && power.nextLevelAbilityDescDef?.description != null)
            {
                desc += "\n\n" + "TM_MCU_NextLevel".Translate() + "\n" + power.nextLevelAbilityDescDef.description;
            }
            float descWidth = view.width - 24f - IconSize - 10f;
            float descHeight = PowerDescriptionHeight(power, view.width);
            List<MightPowerSkill> skills = GetMightSkills(ability);
            float cardHeight = Mathf.Max(IconSize, 32f + descHeight) + skills.Count * 32f + 16f + 8f;

            Rect card = new Rect(0f, y, view.width, cardHeight);
            TorannMagic.Utils.TMIsekaiUI.Panel(card);
            Text.Font = GameFont.Small;

            Rect icon = new Rect(card.x + 10f, card.y + 10f, IconSize, IconSize);
            if (power.Icon != null)
            {
                GUI.color = power.learned ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                Widgets.DrawTextureFitted(icon, power.Icon, 1f);
                GUI.color = Color.white;
            }

            float titleX = icon.xMax + 10f;
            Label(new Rect(titleX, card.y + 8f, card.width - (titleX - card.x) - 150f, 22f),
                ability.LabelCap + "   Lv " + power.level + " / " + power.maxLevel,
                TextAnchor.MiddleLeft, TorannMagic.Utils.TMIsekaiUI.LabelGold);

            Rect action = new Rect(card.xMax - 142f, card.y + 8f, 132f, 24f);
            DrawMightPowerAction(action, power, points);

            if (!string.IsNullOrEmpty(desc))
            {
                Rect descRect = new Rect(titleX, card.y + 32f, descWidth, descHeight);
                Text.Font = GameFont.Tiny;
                Widgets.Label(descRect, desc);
            }

            float sy = card.y + Mathf.Max(IconSize, 32f + descHeight) + 6f;
            foreach (MightPowerSkill skill in skills)
            {
                bool canUp = power.learned && skill.level < skill.levelMax && points >= skill.costToLevel;
                Rect skillLabel = new Rect(card.x + 16f, sy, card.width - 16f - 50f, 18f);
                Text.Font = GameFont.Tiny;
                Label(skillLabel, skill.label.Translate() + ": " + skill.level + " / " + skill.levelMax +
                    "  —  " + skill.desc.Translate(), TextAnchor.UpperLeft, canUp ? Color.white : TextGray);
                if (canUp && Widgets.ButtonText(new Rect(card.xMax - 52f, sy - 2f, 36f, 20f), "+", true, false, true))
                {
                    skill.level++;
                    might.MightData.MightAbilityPoints -= skill.costToLevel;
                }
                else if (power.learned && skill.level >= skill.levelMax)
                {
                    Label(new Rect(card.xMax - 52f, sy - 2f, 36f, 20f), "max", TextAnchor.MiddleCenter, TextGray);
                }
                sy += 32f;
            }

            return y + cardHeight + 10f;
        }

        private void DrawMightPowerAction(Rect rect, MightPower power, int points)
        {
            if (!power.learned)
            {
                if (points < power.learnCost)
                {
                    Label(rect, "TM_MCU_PointsToLearn".Translate(power.learnCost) + "TM_Learn".Translate(),
                        TextAnchor.MiddleCenter, TextGray);
                    return;
                }
                if (Widgets.ButtonText(rect, "TM_Learn".Translate() + " (" + power.learnCost + ")", true, false, true))
                {
                    power.learned = true;
                    might.AddPawnAbility((TMAbilityDef)power.abilityDef);
                    might.MightData.MightAbilityPoints -= power.learnCost;
                }
                return;
            }

            if (power.level >= power.maxLevel)
            {
                Label(rect, "max", TextAnchor.MiddleCenter, TextGray);
                return;
            }
            if (points < power.costToLevel)
            {
                Label(rect, "TM_MCU_PointsToLearn".Translate(power.costToLevel), TextAnchor.MiddleCenter, TextGray);
                return;
            }
            if (Widgets.ButtonText(rect, "TM_LevelUp".Translate() + " (" + power.costToLevel + ")", true, false, true))
            {
                might.LevelUpPower(power);
                might.MightData.MightAbilityPoints -= power.costToLevel;
            }
        }

        private List<MightPowerSkill> GetMightSkills(TMAbilityDef ability)
        {
            List<MightPowerSkill> list = new List<MightPowerSkill>();
            MightPowerSkill s = might.MightData.GetSkill_Power(ability);
            if (s != null) list.Add(s);
            s = might.MightData.GetSkill_Efficiency(ability);
            if (s != null) list.Add(s);
            s = might.MightData.GetSkill_Versatility(ability);
            if (s != null) list.Add(s);
            return list;
        }

        private List<MightPower> GetMightClassGroup()
        {
            MightData data = might.MightData;
            if (pawn.story == null || pawn.story.traits == null)
            {
                return null;
            }
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Gladiator)) return data.MightPowersG;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.TM_Sniper)) return data.MightPowersS;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Bladedancer)) return data.MightPowersB;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Ranger)) return data.MightPowersR;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.Faceless)) return data.MightPowersF;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.TM_Psionic)) return data.MightPowersP;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.DeathKnight)) return data.MightPowersDK;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.TM_Monk)) return data.MightPowersM;
            if (pawn.story.traits.HasTrait(TorannMagicDefOf.TM_Wayfarer)) return data.MightPowersW;
            return null;
        }

        #endregion

        private float DrawStatBlock(Rect view, float y, string headerKey, List<MagicPowerSkill> stats)
        {
            Text.Font = GameFont.Small;
            Label(new Rect(0f, y, view.width, 22f), headerKey.Translate().CapitalizeFirst(),
                TextAnchor.MiddleLeft, TorannMagic.Utils.TMIsekaiUI.HeaderGold);
            y += 26f;
            Rect panel = new Rect(0f, y, view.width, stats.Count * 30f + 14f);
            TorannMagic.Utils.TMIsekaiUI.Panel(panel);
            int points = magic != null && magic.MagicData != null ? magic.MagicData.MagicAbilityPoints : 0;
            float sy = panel.y + 7f;
            foreach (MagicPowerSkill stat in stats)
            {
                bool canUp = stat.level < stat.levelMax && points >= 1;
                Text.Font = GameFont.Tiny;
                Rect row = new Rect(panel.x + 14f, sy, panel.width - 14f - 50f, 18f);
                Label(row, stat.label.Translate() + ": " + stat.level + " / " + stat.levelMax +
                    "  —  " + stat.desc.Translate(), TextAnchor.UpperLeft, canUp ? Color.white : TextGray);
                if (canUp && Widgets.ButtonText(new Rect(panel.xMax - 52f, sy - 2f, 36f, 20f), "+", true, false, true))
                {
                    stat.level++;
                    magic.MagicData.MagicAbilityPoints -= 1;
                    points -= 1;
                }
                else if (stat.level >= stat.levelMax)
                {
                    Label(new Rect(panel.xMax - 52f, sy - 2f, 36f, 20f), "max", TextAnchor.MiddleCenter, TextGray);
                }
                sy += 30f;
            }
            return y + panel.height + 12f;
        }

        private float DrawStatBlock(Rect view, float y, string headerKey, List<MightPowerSkill> stats)
        {
            Text.Font = GameFont.Small;
            Label(new Rect(0f, y, view.width, 22f), headerKey.Translate().CapitalizeFirst(),
                TextAnchor.MiddleLeft, TorannMagic.Utils.TMIsekaiUI.HeaderGold);
            y += 26f;
            Rect panel = new Rect(0f, y, view.width, stats.Count * 30f + 14f);
            TorannMagic.Utils.TMIsekaiUI.Panel(panel);
            int points = might != null && might.MightData != null ? might.MightData.MightAbilityPoints : 0;
            float sy = panel.y + 7f;
            foreach (MightPowerSkill stat in stats)
            {
                bool canUp = stat.level < stat.levelMax && points >= 1;
                Text.Font = GameFont.Tiny;
                Rect row = new Rect(panel.x + 14f, sy, panel.width - 14f - 50f, 18f);
                Label(row, stat.label.Translate() + ": " + stat.level + " / " + stat.levelMax +
                    "  —  " + stat.desc.Translate(), TextAnchor.UpperLeft, canUp ? Color.white : TextGray);
                if (canUp && Widgets.ButtonText(new Rect(panel.xMax - 52f, sy - 2f, 36f, 20f), "+", true, false, true))
                {
                    stat.level++;
                    might.MightData.MightAbilityPoints -= 1;
                    points -= 1;
                }
                else if (stat.level >= stat.levelMax)
                {
                    Label(new Rect(panel.xMax - 52f, sy - 2f, 36f, 20f), "max", TextAnchor.MiddleCenter, TextGray);
                }
                sy += 30f;
            }
            return y + panel.height + 12f;
        }

        private List<(string label, float cur, float max, string regen, Color color)> GetResourceBars()
        {
            // Same sources the vanilla energy gizmo uses: the comp's own need
            // instances and the *100 display scale, plus the real regen rate.
            List<(string label, float cur, float max, string regen, Color color)> bars =
                new List<(string, float, float, string, Color)>();

            if (might != null && might.Stamina != null)
            {
                bars.Add(("Stamina", might.Stamina.CurInstantLevel * 100f, might.maxSP * 100f,
                    RegenString(might.Stamina.lastGainPct), ResStamina));
            }
            if (magic != null && magic.Mana != null)
            {
                bars.Add(("Mana", magic.Mana.CurInstantLevel * 100f, magic.maxMP * 100f,
                    RegenString(magic.Mana.lastGainPct), BarMp));
            }
            AddHediffResource(bars, "TM_PsionicHD", "Psionic", ResPsionic);
            AddHediffResource(bars, "TM_HateHD", "Hate", ResHate);
            AddHediffResource(bars, "TM_BloodHD", "Blood", ResBlood);
            AddHediffResource(bars, "TM_ChiHD", "Chi", ResChi);
            Need_Spirit spirit = pawn.needs?.TryGetNeed(TorannMagicDefOf.TM_SpiritND) as Need_Spirit;
            if (spirit != null)
            {
                bars.Add(("Spirit", spirit.CurLevel, spirit.MaxLevel, "", ResSpirit));
            }
            if (magic != null && magic.customClass != null && magic.customClass.showHediffOnGizmo && magic.customClass.classHediff != null)
            {
                AddHediffResource(bars, magic.customClass.classHediff.defName,
                    magic.customClass.classTrait?.LabelCap ?? magic.customClass.classHediff.label,
                    ResCustom(magic.customClass.classIconColor));
            }

            return bars;
        }

        private float DrawResourcesBlock(Rect view, float y)
        {
            var bars = GetResourceBars();
            if (bars.Count == 0)
            {
                return y;
            }

            Text.Font = GameFont.Medium;
            Label(new Rect(0f, y, view.width, 26f), "TM_GrimoireResources".Translate().CapitalizeFirst(),
                TextAnchor.MiddleLeft, TorannMagic.Utils.TMIsekaiUI.HeaderGold);
            y += 30f;
            Rect panel = new Rect(0f, y, view.width, bars.Count * 30f + 14f);
            TorannMagic.Utils.TMIsekaiUI.Panel(panel);
            float sy = panel.y + 7f;
            foreach ((string label, float cur, float max, string regen, Color color) in bars)
            {
                float frac = max <= 0f ? 0f : Mathf.Clamp01(cur / max);
                Text.Font = GameFont.Tiny;
                Label(new Rect(panel.x + 14f, sy, 120f, 14f), label, TextAnchor.MiddleLeft, TorannMagic.Utils.TMIsekaiUI.LabelGold);
                Rect bar = new Rect(panel.x + 140f, sy, panel.width - 320f, 12f);
                Widgets.DrawBoxSolid(bar, BarBg);
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * frac, bar.height), color);
                Label(new Rect(bar.xMax + 6f, sy, 80f, 14f),
                    Mathf.RoundToInt(cur) + " / " + Mathf.RoundToInt(max), TextAnchor.MiddleLeft, TextGray);
                if (!regen.NullOrEmpty())
                {
                    Label(new Rect(panel.xMax - 158f, sy, 150f, 14f), regen, TextAnchor.MiddleRight, TextGray);
                }
                sy += 30f;
            }
            return y + panel.height + 12f;
        }

        private static string RegenString(float lastGainPct)
        {
            // lastGainPct is the smoothed per-tick fraction; *200 matches the rate the
            // legacy tabs show as "X% per second".
            return (lastGainPct >= 0f ? "+" : "") + (lastGainPct * 200f).ToString("0.###") + "%/s";
        }

        private void AddHediffResource(List<(string label, float cur, float max, string regen, Color color)> bars, string hediffDefName, string label, Color color)
        {
            Hediff hediff = pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDef.Named(hediffDefName), false);
            if (hediff != null)
            {
                bars.Add((label, hediff.Severity, 100f, "", color));
            }
        }

        private Color ResCustom(Color iconColor)
        {
            return Color.Lerp(iconColor, Color.white, 0.25f);
        }

        #region shared

        private List<TMAbilityDef> MagicAbilityFilter(List<MagicPower> magicGroup, List<MightPower> mightGroup)
        {
            List<TMAbilityDef> list = new List<TMAbilityDef>();
            if (magicGroup != null)
            {
                foreach (MagicPower p in magicGroup)
                {
                    foreach (AbilityUser.AbilityDef def in p.TMabilityDefs)
                    {
                        if (def is TMAbilityDef tm) list.Add(tm);
                    }
                }
            }
            if (mightGroup != null)
            {
                foreach (MightPower p in mightGroup)
                {
                    foreach (AbilityUser.AbilityDef def in p.TMabilityDefs)
                    {
                        if (def is TMAbilityDef tm) list.Add(tm);
                    }
                }
            }
            if (magic != null)
            {
                if (magic.customClass != null)
                {
                    list.AddRange(magic.customClass.classMageAbilities);
                }
                if (magic.AdvancedClasses != null)
                {
                    foreach (TMDefs.TM_CustomClass cc in magic.AdvancedClasses)
                    {
                        list.AddRange(cc.classMageAbilities);
                    }
                }
            }
            if (might != null && might.customClass != null)
            {
                list.AddRange(might.customClass.classFighterAbilities);
            }
            return list;
        }

        private float DrawIsekaiBlock(Rect view, float y)
        {
            int isoLevel = TMPIsekai.GetIsekaiLevel(pawn);
            string rank = IsekaiRank(isoLevel);
            Rect panel = new Rect(0f, y, view.width, 52f);
            TorannMagic.Utils.TMIsekaiUI.Panel(panel);
            Text.Font = GameFont.Small;
            Label(new Rect(panel.x + 12f, panel.y + 6f, panel.width - 24f, 20f),
                "Awakened  Lv " + isoLevel + (rank.NullOrEmpty() ? "" : "   [" + rank + "]"),
                TextAnchor.MiddleLeft, Purple);
            Text.Font = GameFont.Tiny;
            Label(new Rect(panel.x + 12f, panel.y + 28f, panel.width - 24f, 14f),
                "TM_GrimoireIsekaiHint".Translate(), TextAnchor.MiddleLeft, TextGray);
            return y + panel.height + 12f;
        }

        // Height helpers mirror the drawing code exactly; the scroll view needs the
        // total up front or the bottom of the list is unreachable.
        private float MeasureViewHeight(float width)
        {
            float h = 90f;
            if (TMPIsekai.Active)
            {
                h += 64f;
            }
            h += ResourcesPanelHeight();
            if (magic != null && magic.MagicData != null)
            {
                h += 30f + 76f + 10f + 22f;                       // header + info panel + gap + section label
                h += StatBlockHeight(magicStats);
                foreach (MagicPower power in magic.MagicData.AllMagicPowersWithSkills)
                {
                    TMAbilityDef ability = power.abilityDef as TMAbilityDef;
                    if (ability == null || !magicFilter.Contains(ability))
                    {
                        continue;
                    }
                    h += MagicPowerCardHeight(width, power) + 10f;
                }
                h += 12f;
            }
            if (might != null && might.MightData != null)
            {
                h += 30f + 64f + 10f + 22f;
                h += StatBlockHeight(mightStats);
                foreach (MightPower power in might.MightData.AllMightPowersWithSkills)
                {
                    TMAbilityDef ability = power.abilityDef as TMAbilityDef;
                    if (ability == null || !mightFilter.Contains(ability))
                    {
                        continue;
                    }
                    h += MightPowerCardHeight(width, power) + 10f;
                }
                h += 12f;
            }
            return h;
        }

        private float ResourcesPanelHeight()
        {
            if (magic == null && might == null)
            {
                return 0f;
            }
            int bars = 0;
            if (might != null && might.Stamina != null) bars++;
            if (magic != null && magic.Mana != null) bars++;
            if (pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDef.Named("TM_PsionicHD"), false) != null) bars++;
            if (pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDef.Named("TM_HateHD"), false) != null) bars++;
            if (pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDef.Named("TM_BloodHD"), false) != null) bars++;
            if (pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDef.Named("TM_ChiHD"), false) != null) bars++;
            Need_Spirit spirit = pawn.needs?.TryGetNeed(TorannMagicDefOf.TM_SpiritND) as Need_Spirit;
            if (spirit != null) bars++;
            if (magic != null && magic.customClass != null && magic.customClass.showHediffOnGizmo && magic.customClass.classHediff != null) bars++;
            return bars == 0 ? 0f : 30f + bars * 30f + 14f + 12f;
        }

        private List<MagicPowerSkill> magicStats;
        private List<MightPowerSkill> mightStats;
        private List<TMAbilityDef> magicFilter;
        private List<TMAbilityDef> mightFilter;
        private bool filtersBuilt;

        private void EnsureFilters()
        {
            if (filtersBuilt)
            {
                return;
            }
            filtersBuilt = true;
            magicStats = magic != null && magic.MagicData != null
                ? new List<MagicPowerSkill>
                {
                    magic.MagicData.MagicPowerSkill_global_regen[0],
                    magic.MagicData.MagicPowerSkill_global_eff[0],
                    magic.MagicData.MagicPowerSkill_global_spirit[0],
                }
                : new List<MagicPowerSkill>();
            mightStats = might != null && might.MightData != null
                ? new List<MightPowerSkill>
                {
                    might.MightData.MightPowerSkill_global_refresh[0],
                    might.MightData.MightPowerSkill_global_seff[0],
                    might.MightData.MightPowerSkill_global_strength[0],
                    might.MightData.MightPowerSkill_global_endurance[0],
                }
                : new List<MightPowerSkill>();
            magicFilter = MagicAbilityFilter(GetMagicClassGroup(), null);
            mightFilter = MagicAbilityFilter(null, GetMightClassGroup());
        }

        private float StatBlockHeight(List<MagicPowerSkill> stats)
        {
            return stats.Count == 0 ? 0f : 26f + stats.Count * 30f + 14f + 12f;
        }

        private float StatBlockHeight(List<MightPowerSkill> stats)
        {
            return stats.Count == 0 ? 0f : 26f + stats.Count * 30f + 14f + 12f;
        }

        private float MagicPowerCardHeight(float width, MagicPower power)
        {
            TMAbilityDef ability = (TMAbilityDef)power.abilityDef;
            float descHeight = PowerDescriptionHeight(power, width);
            int skillCount = 0;
            MagicPowerSkill s = magic.MagicData.GetSkill_Power(ability);
            if (s != null) skillCount++;
            s = magic.MagicData.GetSkill_Efficiency(ability);
            if (s != null) skillCount++;
            s = magic.MagicData.GetSkill_Versatility(ability);
            if (s != null) skillCount++;
            return Mathf.Max(IconSize, 32f + descHeight) + skillCount * 32f + 16f + 8f;
        }

        private float MightPowerCardHeight(float width, MightPower power)
        {
            TMAbilityDef ability = (TMAbilityDef)power.abilityDef;
            float descHeight = PowerDescriptionHeight(power, width);
            int skillCount = 0;
            MightPowerSkill s = might.MightData.GetSkill_Power(ability);
            if (s != null) skillCount++;
            s = might.MightData.GetSkill_Efficiency(ability);
            if (s != null) skillCount++;
            s = might.MightData.GetSkill_Versatility(ability);
            if (s != null) skillCount++;
            return Mathf.Max(IconSize, 32f + descHeight) + skillCount * 32f + 16f + 8f;
        }

        private float PowerDescriptionHeight(MagicPower power, float width)
        {
            string desc = power.abilityDescDef?.description;
            if (power.level > 0 && power.level < power.maxLevel && power.nextLevelAbilityDescDef?.description != null)
            {
                desc += "\n\n" + "TM_MCU_NextLevel".Translate() + "\n" + power.nextLevelAbilityDescDef.description;
            }
            if (string.IsNullOrEmpty(desc))
            {
                return 0f;
            }
            Text.Font = GameFont.Tiny;
            float h = Text.CalcHeight(desc, width - 24f - IconSize - 10f);
            return h;
        }

        private float PowerDescriptionHeight(MightPower power, float width)
        {
            string desc = power.abilityDescDef?.description;
            if (power.level > 0 && power.level < power.maxLevel && power.nextLevelAbilityDescDef?.description != null)
            {
                desc += "\n\n" + "TM_MCU_NextLevel".Translate() + "\n" + power.nextLevelAbilityDescDef.description;
            }
            if (string.IsNullOrEmpty(desc))
            {
                return 0f;
            }
            Text.Font = GameFont.Tiny;
            return Text.CalcHeight(desc, width - 24f - IconSize - 10f);
        }

        private string ClassLabel()
        {
            if (pawn.story?.traits == null)
            {
                return "unawakened";
            }
            List<TraitDef> magicTraits = TM_Data.MagicTraits;
            for (int i = 0; i < magicTraits.Count; i++)
            {
                if (pawn.story.traits.HasTrait(magicTraits[i]))
                {
                    return magicTraits[i].degreeDatas[0].label;
                }
            }
            List<TraitDef> mightTraits = TM_Data.MightTraits;
            for (int i = 0; i < mightTraits.Count; i++)
            {
                if (pawn.story.traits.HasTrait(mightTraits[i]))
                {
                    return mightTraits[i].degreeDatas[0].label;
                }
            }
            return "unawakened";
        }

        private string ClassDescription()
        {
            // full vanilla trait tip: description + stat offsets/effects, same text
            // the trait tooltip shows
            Trait trait = ClassTraitOnPawn();
            return trait?.TipString(pawn);
        }

        private Trait ClassTraitOnPawn()
        {
            if (pawn.story?.traits == null)
            {
                return null;
            }
            if (magic?.customClass?.classTrait != null && pawn.story.traits.HasTrait(magic.customClass.classTrait))
            {
                return pawn.story.traits.GetTrait(magic.customClass.classTrait, magic.customClass.traitDegree);
            }
            if (might?.customClass?.classTrait != null && pawn.story.traits.HasTrait(might.customClass.classTrait))
            {
                return pawn.story.traits.GetTrait(might.customClass.classTrait, might.customClass.traitDegree);
            }
            foreach (TraitDef td in TM_Data.MagicTraits)
            {
                if (pawn.story.traits.HasTrait(td))
                {
                    return pawn.story.traits.GetTrait(td);
                }
            }
            foreach (TraitDef td in TM_Data.MightTraits)
            {
                if (pawn.story.traits.HasTrait(td))
                {
                    return pawn.story.traits.GetTrait(td);
                }
            }
            return null;
        }

        private static void Label(Rect rect, string text, TextAnchor anchor, Color color)
        {
            GUI.color = color;
            GUIStyle style = new GUIStyle(GUI.skin.label) { alignment = anchor, wordWrap = true };
            // match vanilla font metrics, otherwise Text.CalcHeight underestimates
            // and IMGUI labels clip the text
            switch (Text.Font)
            {
                case GameFont.Tiny: style.fontSize = 12; break;
                case GameFont.Small: style.fontSize = 14; break;
                case GameFont.Medium: style.fontSize = 16; break;
            }
            GUI.Label(rect, text, style);
            GUI.color = Color.white;
        }

        private static string IsekaiRank(int level)
        {
            if (level <= 0)
            {
                return null;
            }
            string[] ranks = { "F", "E", "D", "C", "B", "A", "S", "SS", "SSS", "Nation" };
            return ranks[Mathf.Min(level / 10, 9)];
        }

        #endregion
    }
}

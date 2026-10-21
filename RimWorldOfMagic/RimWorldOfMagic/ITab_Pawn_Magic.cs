using RimWorld;
using UnityEngine;
using Verse;
using System.Linq;

namespace TorannMagic
{
    /// <summary>
    /// Inspect-pane tab variant of the Grimoire button. Enabled by turning off the
    /// "grimoireOnAbilityBar" mod setting: the tab shows for magic class holders
    /// and opens the Grimoire window on click instead of stacking another gizmo
    /// onto the already crowded ability bar.
    /// </summary>
    public class ITab_Pawn_Magic : ITab
    {
        public override bool IsVisible
        {
            get
            {
                if (ModOptions.Settings.Instance.grimoireOnAbilityBar)
                {
                    return false;
                }
                Pawn pawn = base.SelPawn;
                if (pawn == null || !pawn.IsColonist)
                {
                    return false;
                }
                CompAbilityUserMagic comp = pawn.GetCompAbilityUserMagic();
                return comp != null && comp.IsMagicUser;
            }
        }

        public ITab_Pawn_Magic()
        {
            this.size = new Vector2(360f, 130f);
            this.labelKey = "TM_Grimoire";
        }

        public override void OnOpen()
        {
            base.OnOpen();
            OpenGrimoire();
        }

        protected override void FillTab()
        {
            Rect buttonRect = new Rect(12f, 12f, this.size.x - 24f, 32f);
            if (Widgets.ButtonText(buttonRect, "TM_GrimoireOpen".Translate()))
            {
                OpenGrimoire();
            }
            Rect hintRect = new Rect(16f, 52f, this.size.x - 32f, this.size.y - 64f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(hintRect, "TM_GrimoireTabHint".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void OpenGrimoire()
        {
            Pawn pawn = base.SelPawn;
            if (pawn == null)
            {
                return;
            }
            Window_TMGrimoire open = Find.WindowStack.Windows.OfType<Window_TMGrimoire>().FirstOrDefault();
            if (open != null)
            {
                open.Close();
            }
            Find.WindowStack.Add(new Window_TMGrimoire(pawn));
        }
    }
}

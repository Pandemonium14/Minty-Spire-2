using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MintySpire2.MintySpire2.src.util;

namespace MintySpire2.MintySpire2.src;

/// <summary>
///     Adds a small text label to the Right of the health bar when the health bar is visible
///     and the owner creature is the player.
/// </summary>
[HarmonyPatch(typeof(NHealthBar))]
public static class SummedIncomingDamageRender
{
    private const string RightTextNodeName = "ModIncomingDamageText";
    private const float RightPadding = 6f;

    private static readonly FieldInfo CreatureField = AccessTools.Field(typeof(NHealthBar), "_creature");
    private static readonly WeakNodeRegistry<NHealthBar> ValidBars = new();

    /// <summary>
    ///     After a creature is assigned, create label node if it doesn't exist.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NHealthBar.SetCreature))]
    public static void SetCreature_Postfix(NHealthBar __instance)
    {
        CreateLabelIfNotExist(__instance);
    }
    
    /// <summary>
    ///     Refresh labels when a creature death is fired to recalculate incoming damage immediately.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Creature), nameof(Creature.InvokeDiedEvent))]
    public static void InvokeDiedEvent_Postfix()
    {
        ValidBars.ForEachLive(RefreshVisibilityAndText);
    }


    /// <summary>
    ///     Whenever the bar is updated, update the text display (this is overkill)
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NHealthBar.RefreshValues))]
    public static void RefreshValues_Postfix(NHealthBar __instance)
    {
        ValidBars.Register(__instance);
        RefreshVisibilityAndText(__instance);
    }

    /// <summary>
    ///     When the container size is about to change, reposition the label
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(NHealthBar), "SetHpBarContainerSizeWithOffsets")]
    public static void SetHpBarContainerSizeWithOffsets_Postfix(NHealthBar __instance, Vector2 size)
    {
        RepositionLabel(__instance, size);
    }

    /// <summary>
    ///     Creates the label once and attach it near the HP bar container.
    /// </summary>
    /// <returns>bool: Was label created</returns>
    private static bool CreateLabelIfNotExist(NHealthBar bar)
    {
        if (bar.HasNode(RightTextNodeName))
            return false;

        // Parent to the same node that holds the bar so coordinates are consistent.
        var container = bar.HpBarContainer;

        var label = new Label
        {
            Name = RightTextNodeName,
            Text = "",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        
        var font = GD.Load<Font>("res://fonts/kreon_bold.ttf");
        if (font != null)
            label.AddThemeFontOverride((StringName)"font", font);
        label.HorizontalAlignment = HorizontalAlignment.Left;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeColorOverride("font_color", Colors.Salmon);
        label.AddThemeFontSizeOverride("font_size", 14);

        // Add to the same parent as the bar container so it's position relative to it.
        var parent = container.GetParent() as Control ?? bar;
        parent.AddChild(label);
        RepositionLabel(bar, container.Size);
        return true;
    }

    /// <summary>
    ///     Positions the label to the Right of the HP bar container.
    /// </summary>
    private static void RepositionLabel(NHealthBar bar, Vector2 newSize)
    {
        var label = bar.GetNode(RightTextNodeName) as Label;
        if (label == null) return;

        var container = bar.HpBarContainer;

        // Positioning for the label.
        var labelWidth = 20f;
        var labelHeight = newSize.Y;

        label.Size = new Vector2(labelWidth, labelHeight);
        label.Position = new Vector2(
            container.Position.X + newSize.X + RightPadding,
            container.Position.Y - labelHeight / 4
        );
    }

    /// <summary>
    ///     Shows/hides the label and sets its text.
    ///     Only visible when the health bar is visible, the creature is the player and its their turn.
    /// </summary>
    private static void RefreshVisibilityAndText(NHealthBar bar)
    {
        var label = bar.GetNode(RightTextNodeName) as Label;
        if (label == null)
            return;

        if (!bar.Visible || CombatManager.Instance.IsEnemyTurnStarted)
        {
            label.Visible = false;
            return;
        }

        // Only show for the player-owned health bar.
        var creature = CreatureField?.GetValue(bar) as Creature;
        if (creature?.Player == null)
        {
            label.Visible = false;
            return;
        }

        var incomingDamage = CalculateIncomingDamage(creature);
        if (incomingDamage > 0)
        {
            label.Text = $"←{incomingDamage}";
            label.Visible = true;
            return;
        }

        label.Visible = false;
    }

    /// <summary>
    ///     Calculate the incoming damage from common sources such as monsters and powers.
    /// </summary>
    /// <param name="creature">The Player creature that we'll calculate the incoming damage for.</param>
    private static int CalculateIncomingDamage(Creature creature)
    {
        // Collect incoming damage from all hittable monsters (can untargetable monsters attack?).
        var incomingDamage = 0;
        foreach (var hittableEnemy in creature.CombatState.HittableEnemies)
        {
            foreach (var intent in hittableEnemy.Monster.NextMove.Intents)
            {
                if (intent.IntentType is IntentType.Attack or IntentType.DeathBlow)
                    incomingDamage += ((AttackIntent)intent).GetTotalDamage(null, hittableEnemy); // Is null alright here?
            }
        }

        // Knowledge demon end of turn damage
        incomingDamage += creature.Player.Creature.GetPower<DisintegrationPower>()?.Amount ?? 0;

        return incomingDamage;
    }
}
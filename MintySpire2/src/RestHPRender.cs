#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Rooms;

namespace MintySpire2.MintySpire2.src;

/// <summary>
///     Harmony patches for NRestSiteButton.
///     Adds an extra label above the button visuals, and for HealRestSiteOption shows:
///     current HP -> HP after healing (including relic modifiers).
/// </summary>
[HarmonyPatch]
public static class RestHPRender
{
    private const string HealLabelNodeName = "ModHealPreviewLabel";

    private static readonly PropertyInfo OwnerProperty = AccessTools.Property(typeof(RestSiteOption), "Owner");

    private static readonly List<WeakReference<NRestSiteButton>> ValidButtons = new();

    /// <summary>
    ///     After NRestSiteButton is ready, inject our label and immediately populate it.
    /// </summary>
    [HarmonyPatch(typeof(NRestSiteButton), nameof(NRestSiteButton._Ready))]
    [HarmonyPostfix]
    public static void Ready_Postfix(NRestSiteButton __instance)
    {
        if (__instance.Option is not HealRestSiteOption) return;

        CreateLabelIfNotExists(__instance);
        UpdateExtraLabel(__instance);
    }

    /// <summary>
    ///     After the button reloads (typically when Option is assigned), refresh the preview text.
    /// </summary>
    [HarmonyPatch(typeof(NRestSiteButton), "Reload")]
    [HarmonyPostfix]
    public static void Reload_Postfix(NRestSiteButton __instance)
    {
        if (__instance.Option is not HealRestSiteOption) return;

        CreateLabelIfNotExists(__instance);
        UpdateExtraLabel(__instance);
    }

    /// <summary>
    ///     Creates the label and applies layout/styling. Uses %Visuals as the parent when available.
    /// </summary>
    private static void CreateLabelIfNotExists(NRestSiteButton button)
    {
        // Avoid adding duplicates if the node is reused or _Ready is run more than once.
        if (button.HasNode(HealLabelNodeName))
            return;

        // Attach to %Visuals so it follows the button's visuals scaling/layout.
        var parent = button.GetNodeOrNull<Control>("%Visuals") ?? button;

        var label = new Label
        {
            Name = HealLabelNodeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        
        var font = GD.Load<Font>("res://fonts/kreon_bold.ttf");
        if (font != null)
            label.AddThemeFontOverride((StringName)"font", font);
        label.AddThemeColorOverride("font_color", Colors.LightGreen);
        label.AddThemeFontSizeOverride("font_size", 16);

        // full width strip anchored at top, slightly above button.
        label.AnchorLeft = 0;
        label.AnchorRight = 1;
        label.AnchorTop = 0;
        label.AnchorBottom = 0;

        label.OffsetLeft = 12;
        label.OffsetRight = -12;
        label.OffsetTop = -18;
        label.OffsetBottom = -4;
        label.HorizontalAlignment = HorizontalAlignment.Left;

        parent.AddChild(label);
    }

    /// <summary>
    ///     Updates the label text/visibility depending on the current RestSiteOption.
    /// </summary>
    private static void UpdateExtraLabel(NRestSiteButton button)
    {
        var extra = button.FindChild(HealLabelNodeName, true, false) as Label;
        if (extra == null) return;

        // RestSiteOption.Owner is protected, using cached field
        var ownerObj = OwnerProperty?.GetValue(button.Option);
        if (ownerObj is not Player player)
        {
            extra.Visible = false;
            return;
        }

        // Calculate current HP and the projected HP after healing.
        var currentHp = player.Creature.CurrentHp;
        var maxHp = player.Creature.MaxHp;

        var healAmount = HealRestSiteOption.GetBaseHealAmount(player.Creature);
        healAmount = ApplyRelicHealModifiers(player, healAmount);

        var healInt = (int)Math.Floor(healAmount);
        var healedHp = Math.Min(maxHp, currentHp + Math.Max(0, healInt));

        extra.Text = $"HP: {currentHp} → {healedHp}";
        extra.Visible = true;

        RegisterValidButton(button);
    }

    /// <summary>
    ///     Runs all relic modifiers that affect rest site healing.
    /// </summary>
    private static decimal ApplyRelicHealModifiers(Player player, decimal baseAmount)
    {
        var amount = baseAmount;
        var relics = player.Relics;

        // Basically regal pillow
        foreach (var relic in relics) amount = relic.ModifyRestSiteHealAmount(player.Creature, amount);

        return amount;
    }

    /// <summary>
    ///     Catch HP changes to dynamically update the label (in case of Eternal Feather)
    /// </summary>
    [HarmonyPatch(typeof(Creature))]
    public static class CatchHPChange
    {
        // Patch the property setter: Creature.set_CurrentHp(int)
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertySetter(typeof(Creature), nameof(Creature.CurrentHp));
        }

        private static void Postfix(Creature __instance, int value)
        {
            // Can't check for RestSite because it's null at time of healing
            if (__instance.CombatState?.RunState.CurrentRoom?.RoomType == RoomType.Monster) return;

            for (var i = ValidButtons.Count - 1; i >= 0; i--)
            {
                if (!ValidButtons[i].TryGetTarget(out var btn) || !GodotObject.IsInstanceValid(btn))
                {
                    ValidButtons.RemoveAt(i);
                    continue;
                }

                UpdateExtraLabel(btn);
            }
        }
    }
    
    private static void RegisterValidButton(NRestSiteButton button)
    {
        //Cleanup Dead Buttons
        for (var i = ValidButtons.Count - 1; i >= 0; i--)
            if (!ValidButtons[i].TryGetTarget(out var b) || !GodotObject.IsInstanceValid(b))
                ValidButtons.RemoveAt(i);
            else if (ReferenceEquals(b, button)) return; // Avoid duplicates

        ValidButtons.Add(new WeakReference<NRestSiteButton>(button));
    }
}
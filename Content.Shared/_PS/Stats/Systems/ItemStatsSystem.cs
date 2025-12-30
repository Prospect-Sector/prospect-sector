using Content.Shared._PS.Stats.Components;
using Content.Shared._PS.Stats.Prototypes;
using Content.Shared.Examine;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._PS.Stats.Systems;

/// <summary>
/// Handles item stats display in examine tooltip and stat effect calculations.
/// </summary>
public sealed class ItemStatsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemStatsComponent, GetVerbsEvent<ExamineVerb>>(OnExamine);
    }

    private void OnExamine(EntityUid uid, ItemStatsComponent component, ref GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract)
            return;

        var message = GetStatsExamineMessage(component);
        var rarity = _proto.Index(component.Rarity);

        _examine.AddHoverExamineVerb(
            args,
            component,
            Loc.GetString("item-stats-examine-verb"),
            message.ToMarkup(),
            "/Textures/Interface/VerbIcons/dot.svg.192dpi.png");
    }

    /// <summary>
    /// Generates the formatted examine message for item stats.
    /// </summary>
    public FormattedMessage GetStatsExamineMessage(ItemStatsComponent component)
    {
        var msg = new FormattedMessage();
        var rarity = _proto.Index(component.Rarity);

        // Rarity header with color
        var colorHex = rarity.Color.ToHex();
        msg.AddMarkupOrThrow($"[color={colorHex}][bold]{Loc.GetString(rarity.Name)}[/bold][/color]");
        msg.PushNewline();

        // Weapon damage multiplier (if applicable)
        if (component.WeaponDamageMultiplier > 1f)
        {
            var dmgPercent = (component.WeaponDamageMultiplier - 1f) * 100f;
            msg.AddMarkupOrThrow(Loc.GetString("item-stats-weapon-damage", ("value", dmgPercent.ToString("F1"))));
            msg.PushNewline();
        }

        // Armor bonuses (if applicable)
        if (component.SoftcritBonus > 0 || component.DeathThresholdBonus > 0)
        {
            if (component.SoftcritBonus > 0)
            {
                msg.AddMarkupOrThrow(Loc.GetString("item-stats-softcrit-bonus", ("value", component.SoftcritBonus)));
                msg.PushNewline();
            }
            if (component.DeathThresholdBonus > 0)
            {
                msg.AddMarkupOrThrow(Loc.GetString("item-stats-death-bonus", ("value", component.DeathThresholdBonus)));
                msg.PushNewline();
            }
        }

        // Core stat bonuses
        if (component.StatBonuses.Count > 0)
        {
            msg.PushNewline();
            msg.AddMarkupOrThrow($"[bold]{Loc.GetString("item-stats-header-stats")}[/bold]");
            msg.PushNewline();

            foreach (var (stat, value) in component.StatBonuses)
            {
                var statName = GetStatDisplayName(stat);
                var color = GetStatColor(stat);
                msg.AddMarkupOrThrow($"[color={color}]+{value} {statName}[/color]");
                msg.PushNewline();
            }
        }

        // Affixes
        if (component.Affixes.Count > 0)
        {
            msg.PushNewline();
            msg.AddMarkupOrThrow($"[bold]{Loc.GetString("item-stats-header-affixes")}[/bold]");
            msg.PushNewline();

            foreach (var affix in component.Affixes)
            {
                if (!_proto.TryIndex(affix.AffixId, out var affixProto))
                    continue;

                var affixName = Loc.GetString(affixProto.Name);
                msg.AddMarkupOrThrow($"[color=#a0a0ff]+{affix.Value:F1}% {affixName}[/color]");
                msg.PushNewline();
            }
        }

        return msg;
    }

    /// <summary>
    /// Gets the display name for a stat type.
    /// </summary>
    private string GetStatDisplayName(StatType stat)
    {
        return stat switch
        {
            StatType.Strength => Loc.GetString("stat-strength"),
            StatType.Dexterity => Loc.GetString("stat-dexterity"),
            StatType.Agility => Loc.GetString("stat-agility"),
            StatType.Fortitude => Loc.GetString("stat-fortitude"),
            StatType.Intelligence => Loc.GetString("stat-intelligence"),
            StatType.Luck => Loc.GetString("stat-luck"),
            _ => stat.ToString()
        };
    }

    /// <summary>
    /// Gets the display color for a stat type.
    /// </summary>
    private string GetStatColor(StatType stat)
    {
        return stat switch
        {
            StatType.Strength => "#ff6b6b",     // Red
            StatType.Dexterity => "#ffd93d",    // Yellow
            StatType.Agility => "#6bcb77",      // Green
            StatType.Fortitude => "#4d96ff",    // Blue
            StatType.Intelligence => "#c9b1ff", // Purple
            StatType.Luck => "#ffc107",         // Gold
            _ => "#ffffff"
        };
    }
}

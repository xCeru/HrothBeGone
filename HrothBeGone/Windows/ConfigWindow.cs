using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace HrothBeGone.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("HrothBeGone")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new System.Numerics.Vector2(360, 240),
            MaximumSize = new System.Numerics.Vector2(700, 500),
        };
    }

    public override void Draw()
    {
        bool enabled = plugin.IsEnabled;
        if (ImGui.Checkbox("Enable HrothBeGone", ref enabled))
            plugin.SetEnabled(enabled);

        ImGui.Separator();

        bool random = plugin.Configuration.RandomRace;
        if (ImGui.Checkbox("Random target race", ref random))
            plugin.SetRandomRace(random);

        if (!random)
        {
            byte current = plugin.Configuration.TargetRace;
            string preview = Plugin.RaceName(current);

            if (ImGui.BeginCombo("Target race", preview))
            {
                foreach (byte race in Plugin.AvailableRaces)
                {
                    bool selected = race == current;
                    if (ImGui.Selectable(Plugin.RaceName(race), selected))
                        plugin.SetTargetRace(race);

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }
        else
        {
            ImGui.TextWrapped("Each loaded Hrothgar actor is assigned a random non-Hrothgar race when first detected.");
        }

        bool includeSelf = plugin.Configuration.IncludeLocalPlayer;
        if (ImGui.Checkbox("Include my character", ref includeSelf))
            plugin.SetIncludeLocalPlayer(includeSelf);

        ImGui.Separator();
        ImGui.TextWrapped("Changes are client-side only. They are not sent to the server.");
        ImGui.TextWrapped("Use /hrothbegone to open this window.");
    }
}

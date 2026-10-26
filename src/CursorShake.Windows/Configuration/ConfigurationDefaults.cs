using System.Globalization;
using CursorShake.Core.Shake;
namespace CursorShake.Windows.Configuration;

public static class ConfigurationDefaults
{
    public static string Text
    {
        get
        {
            var p = new GestureProfile(); var g = p.Growth;
            return FormattableString.Invariant($"""
                # CursorShake configuration. Saving valid changes applies them at normal cursor size.
                version = 1

                [appearance]
                theme = "windows" # windows, bibata-ice, nordzy-white, or a declared personal theme

                [gesture]
                count = {p.CountToActivate}
                maximum_interval_ms = {p.MaximumInterval.TotalMilliseconds}
                minimum_travel = {p.MinimumTravel} # logical pixels at 96 DPI
                minimum_speed = {p.MinimumSpeed} # logical pixels per second

                [growth]
                maximum_scale = {g.MaximumScale}
                growth_duration_ms = {g.GrowthDuration.TotalMilliseconds}
                inactivity_delay_ms = {g.InactivityDelay.TotalMilliseconds} # includes the stroke grace interval
                return_duration_ms = {g.ReturnDuration.TotalMilliseconds}
                maximum_cadence_multiplier = {g.MaximumCadenceMultiplier}

                # Personal themes live in themes/<id> next to this file. Only CUR/ANI/VCUR/VANI.
                # [themes.my-theme]
                # default = "pointer.vcur"
                # [themes.my-theme.roles]
                # arrow = "pointer.vcur"
                # wait = "busy.vani"
                """) + "\n";
        }
    }
}

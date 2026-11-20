using Microsoft.JSInterop;
using MudBlazor;

namespace ChatApp.Services
{
    public enum ThemeMode { System, Light, Dark }

    public record AppTheme(string Key, string Name, string Swatch, MudTheme Theme);

    /// <summary>
    /// Holds the selected color theme and light/dark mode, and remembers them in localStorage.
    /// </summary>
    public class ThemeService
    {
        private const string ThemeKeyStorage = "theme";
        private const string ModeKeyStorage = "themeMode";

        private readonly IJSRuntime _js;

        public ThemeService(IJSRuntime js) => _js = js;

        public IReadOnlyList<AppTheme> Themes { get; } = new List<AppTheme>
        {
            Create("indigo", "Indigo", "#594AE2", "#FF4081", "#7E6FFF", "#FF80AB"),
            Create("ocean",  "Ocean",  "#0277BD", "#00ACC1", "#4FC3F7", "#4DD0E1"),
            Create("forest", "Forest", "#2E7D32", "#8D6E63", "#66BB6A", "#BCAAA4"),
            Create("sunset", "Sunset", "#E65100", "#AD1457", "#FFA726", "#F06292"),
            Create("rose",   "Rose",   "#C2185B", "#7B1FA2", "#F48FB1", "#CE93D8"),
        };

        public AppTheme Current { get; private set; } = default!;
        public ThemeMode Mode { get; private set; } = ThemeMode.System;

        public event Action? Changed;

        private bool _loaded;

        public async Task LoadAsync()
        {
            Current ??= Themes[0];
            if (_loaded) return;
            _loaded = true;
            try
            {
                var key = await _js.InvokeAsync<string?>("localStorage.getItem", ThemeKeyStorage);
                var mode = await _js.InvokeAsync<string?>("localStorage.getItem", ModeKeyStorage);
                Current = Themes.FirstOrDefault(t => t.Key == key) ?? Themes[0];
                if (Enum.TryParse<ThemeMode>(mode, out var m)) Mode = m;
            }
            catch
            {
                // localStorage can be unavailable (private mode etc.) - fall back to defaults.
            }
        }

        public async Task SetThemeAsync(AppTheme theme)
        {
            Current = theme;
            Changed?.Invoke();
            await SaveAsync(ThemeKeyStorage, theme.Key);
        }

        public async Task SetModeAsync(ThemeMode mode)
        {
            Mode = mode;
            Changed?.Invoke();
            await SaveAsync(ModeKeyStorage, mode.ToString());
        }

        private async Task SaveAsync(string key, string value)
        {
            try { await _js.InvokeVoidAsync("localStorage.setItem", key, value); }
            catch { /* ignore storage failures */ }
        }

        private static AppTheme Create(string key, string name,
            string primaryLight, string secondaryLight, string primaryDark, string secondaryDark)
        {
            var theme = new MudTheme
            {
                PaletteLight = new PaletteLight
                {
                    Primary = primaryLight,
                    Secondary = secondaryLight,
                    AppbarBackground = primaryLight,
                },
                PaletteDark = new PaletteDark
                {
                    Primary = primaryDark,
                    Secondary = secondaryDark,
                    PrimaryContrastText = "#111111",
                    AppbarBackground = "#1E1E2D",
                },
            };
            return new AppTheme(key, name, primaryLight, theme);
        }
    }
}

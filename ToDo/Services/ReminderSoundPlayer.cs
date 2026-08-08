using System.IO;
using System.Media;
using System.Windows;

namespace ToDo.Services;

/// <summary>
/// Plays the reminder sound. The default is a short chime bundled into the app
/// (Resources/reminder-chime.wav), which plays unconditionally through the default
/// audio device — unlike SystemSounds.Exclamation, which is silent whenever the
/// Windows sound scheme is set to "No Sounds" (the most common cause of "no reminder
/// sound"). A user-chosen WAV (Settings.ReminderSoundPath) overrides the built-in one;
/// a missing/unreadable custom file falls back to the chime instead of failing.
/// </summary>
public static class ReminderSoundPlayer
{
    /// <summary>Custom-file player; recreated when the configured path changes.</summary>
    private static SoundPlayer? _player;

    /// <summary>Built-in chime, loaded once from the embedded resource.</summary>
    private static SoundPlayer? _builtIn;

    public static void Play()
    {
        var path = SettingsService.Current.ReminderSoundPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                if (_player == null ||
                    !string.Equals(_player.SoundLocation, path, StringComparison.OrdinalIgnoreCase))
                {
                    _player?.Dispose();
                    _player = new SoundPlayer(path);
                }
                _player.Load();      // synchronous load: a missing/corrupt file throws here, never fails silently
                _player.Play();
                return;
            }
            catch
            {
                // Bad or unreadable file: never let the reminder fail — fall back to the built-in chime.
                _player?.Dispose();
                _player = null;
            }
        }

        try
        {
            BuiltIn().Play();
        }
        catch
        {
            // Nothing else to fall back to; log so a silent reminder is never a mystery.
            DiagnosticLog.Warn("sound", "failed to play the built-in reminder chime");
        }
    }

    private static SoundPlayer BuiltIn()
    {
        if (_builtIn == null)
        {
            var stream = Application.GetResourceStream(
                new Uri("pack://application:,,,/Resources/reminder-chime.wav"))?.Stream
                ?? throw new IOException("embedded reminder-chime.wav not found");
            _builtIn = new SoundPlayer(stream);
            _builtIn.Load();        // preload so the first Play() is instant, not a delayed pop
        }
        return _builtIn;
    }
}

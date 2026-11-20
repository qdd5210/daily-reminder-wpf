using System.IO;
using System.Text;
using System.Text.Json;

namespace DailyReminder;

internal static class AppPaths
{
    internal static string DefaultDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DailyReminder");

    internal static string ResolveDataDirectory(string[] arguments)
    {
        var configured = Environment.GetEnvironmentVariable("DAILY_REMINDER_DATA_DIR");
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], "--data-dir", StringComparison.OrdinalIgnoreCase))
            {
                configured = arguments[index + 1];
                break;
            }
        }
        return string.IsNullOrWhiteSpace(configured) ? DefaultDataDirectory : Path.GetFullPath(configured);
    }

    internal static void Initialize(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!string.Equals(Path.GetFullPath(directory), Path.GetFullPath(DefaultDataDirectory), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        MigrateLegacyData(directory, new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DailyReminder")
        });
    }

    internal static void MigrateLegacyData(string destinationDirectory, IEnumerable<string> legacyDirectories)
    {
        var destinationFile = Path.Combine(destinationDirectory, "tasks.json");
        if (File.Exists(destinationFile))
        {
            return;
        }

        foreach (var legacyDirectory in legacyDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var legacyFile = Path.Combine(legacyDirectory, "tasks.json");
            if (!File.Exists(legacyFile) || string.Equals(Path.GetFullPath(legacyFile), Path.GetFullPath(destinationFile), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var json = File.ReadAllText(legacyFile, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<ReminderData>(json)
                ?? throw new InvalidDataException($"旧记录无法读取：{legacyFile}");
            data.Settings ??= new ReminderSettings();
            Directory.CreateDirectory(destinationDirectory);
            var image = data.Settings.BackgroundImage;
            if (!string.IsNullOrWhiteSpace(image))
            {
                var sourceImage = Path.IsPathRooted(image) ? image : Path.Combine(legacyDirectory, image);
                if (File.Exists(sourceImage))
                {
                    var imageName = $"background-imported-{Guid.NewGuid():N}{Path.GetExtension(sourceImage)}";
                    File.Copy(sourceImage, Path.Combine(destinationDirectory, imageName), false);
                    data.Settings.BackgroundImage = imageName;
                }
            }

            var temporary = Path.Combine(destinationDirectory, $".migration-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temporary, destinationFile, false);
            return;
        }
    }
}

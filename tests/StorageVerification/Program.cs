using System.Text.Json;
using DailyReminder;

var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var previousEnvironment = Environment.GetEnvironmentVariable("DAILY_REMINDER_DATA_DIR");
var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
    Console.WriteLine("PASS " + description);
}

try
{
    Environment.SetEnvironmentVariable("DAILY_REMINDER_DATA_DIR", null);
    Check(AppPaths.ResolveDataDirectory(Array.Empty<string>()) == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DailyReminder"), "default data is LocalAppData, independent of executable folder");
    var environmentDirectory = Path.Combine(fixture, "environment");
    Environment.SetEnvironmentVariable("DAILY_REMINDER_DATA_DIR", environmentDirectory);
    Check(AppPaths.ResolveDataDirectory(Array.Empty<string>()) == environmentDirectory, "environment override remains supported");
    var explicitDirectory = Path.Combine(fixture, "explicit");
    Check(AppPaths.ResolveDataDirectory(new[] { "--data-dir", explicitDirectory }) == explicitDirectory, "explicit directory overrides environment");

    var legacy = Path.Combine(fixture, "legacy");
    var destination = Path.Combine(fixture, "data");
    Directory.CreateDirectory(legacy);
    Directory.CreateDirectory(destination);
    var original = JsonSerializer.Serialize(new ReminderData
    {
        Settings = new ReminderSettings { BackgroundImage = "background.png", ReminderStyle = "jelly" },
        Tasks = new List<ReminderTask>
        {
            new() { Id = "scheduled", Title = "保留已设置提醒的待办", Time = "18:30" },
            new() { Id = "untimed", Title = "保留无提醒待办", Time = "" }
        }
    });
    File.WriteAllText(Path.Combine(legacy, "tasks.json"), original);
    var imageBytes = new byte[] { 1, 2, 3, 4, 5 };
    File.WriteAllBytes(Path.Combine(legacy, "background.png"), imageBytes);
    File.WriteAllText(Path.Combine(destination, "background.png"), "pre-existing orphan");
    AppPaths.MigrateLegacyData(destination, new[] { legacy });
    var migratedFile = Path.Combine(destination, "tasks.json");
    var migrated = JsonSerializer.Deserialize<ReminderData>(File.ReadAllText(migratedFile))!;
    Check(migrated.Tasks.Count == 2 && migrated.Tasks[0].Time == "18:30" && migrated.Tasks[1].Time == "", "legacy tasks and optional reminder times migrate unchanged");
    Check(migrated.Settings.ReminderStyle == "jelly", "reminder style migrates");
    Check(File.ReadAllBytes(Path.Combine(destination, migrated.Settings.BackgroundImage)).SequenceEqual(imageBytes), "custom background is copied into data directory");
    Check(File.ReadAllText(Path.Combine(destination, "background.png")) == "pre-existing orphan", "migration does not overwrite an unrelated image");
    Check(File.ReadAllText(Path.Combine(legacy, "tasks.json")) == original, "legacy records remain intact");
    var alreadyMigrated = File.ReadAllText(migratedFile);
    File.WriteAllText(Path.Combine(legacy, "tasks.json"), "invalid legacy file after migration");
    AppPaths.MigrateLegacyData(destination, new[] { legacy });
    Check(File.ReadAllText(migratedFile) == alreadyMigrated, "existing user data is never overwritten on later launches");
    Console.WriteLine("All " + checks + " storage checks passed.");
}
finally
{
    Environment.SetEnvironmentVariable("DAILY_REMINDER_DATA_DIR", previousEnvironment);
}

using MemoryPack;
using March7thHoney.Internationalization;
using March7thHoney.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace March7thHoney.Data.Config.Rogue;

[MemoryPackable]
public partial class RogueDialogueBaseConfigInfo
{
    public string OptionPath { get; set; } = string.Empty;
    public string DialoguePath { get; set; } = string.Empty;

    [JsonIgnore] public LevelGraphConfigInfo? DialogueInfo { get; set; }
    [JsonIgnore] public RogueDialogueEventConfigInfo? OptionInfo { get; set; }

    /// <summary>
    ///     Loads the option and dialogue graphs. A missing file leaves its graph unset; a file that fails to parse is
    ///     logged and marks the resource cache incomplete, so a partial load is not saved as a complete cache.
    /// </summary>
    public void Loaded()
    {
        if (!string.IsNullOrEmpty(OptionPath))
            LoadFile(OptionPath, text => OptionInfo = JsonConvert.DeserializeObject<RogueDialogueEventConfigInfo>(text));

        if (!string.IsNullOrEmpty(DialoguePath))
            LoadFile(DialoguePath,
                text => DialogueInfo =
                    LevelGraphConfigInfo.LoadFromJsonObject(JObject.Parse(text.Replace("$type", "Type"))));
    }

    private static void LoadFile(string relativePath, Action<string> load)
    {
        var file = new FileInfo(Path.Combine(ConfigManager.Config.Path.ResourcePath, relativePath));
        if (!file.Exists) return;
        try
        {
            load(File.ReadAllText(file.FullName));
        }
        catch (Exception ex)
        {
            ResourceCache.IsComplete = false;
            new Logger(nameof(RogueDialogueBaseConfigInfo)).Error(
                I18NManager.Translate("Server.ServerInfo.FailedToReadItem", file.Name,
                    I18NManager.Translate("Word.Error")), ex);
        }
    }
}
